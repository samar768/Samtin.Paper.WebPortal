using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Techno.Paper.Api.Infrastructure;
using Techno.Paper.Api.Middleware;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Techno.Paper.Api.Tests")]

namespace Techno.Paper.Api.Services;

public interface IEnvironmentService
{
    Task<EnvironmentFormResponse> FormAsync(SessionResponse session, CancellationToken ct);
    Task<MasterRecordResponse> SaveAsync(SessionResponse session, SaveMasterRequest request, CancellationToken ct);
    Task<MasterAuditResponse> AuditAsync(SessionResponse session, int page, CancellationToken ct);
}

public sealed class EnvironmentService(Database database, PocOptions options, ILookupService lookups,
    IWriteCheckpoint checkpoint) : IEnvironmentService
{
    private static ApiFault ConfigurationError() => new(409, "environment_configuration",
        "Environment settings require administrator review. No settings were changed.");
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    private static string Identifier(string name)
    {
        if (!Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_]{0,127}$")) throw ConfigurationError();
        return $"[{name}]";
    }
    private static int? Integer(Dictionary<string, string?> row, string key) => row[key] is null ? null :
        int.Parse(row[key]!, CultureInfo.InvariantCulture);

    internal async Task<EnvironmentFormResponse> LoadAsync(SqlConnection connection, SqlTransaction? transaction,
        SessionResponse session, CancellationToken ct)
    {
        var capabilities = await MasterAccess.ReadAsync(database, options, session, "Master", "MnuEnvironmentSetting", ct);
        if (!capabilities.View) throw new ApiFault(403, "environment_denied", "Environment settings are not permitted.");
        var registrations = await Database.QueryAsync(connection, transaction,
            """
            SELECT d.*,r.CaptureAuditValues,(SELECT COUNT(*) FROM webpoc.MasterField f
                WHERE f.MasterId=d.MasterId AND f.Revision=d.Revision) AS FieldCount
            FROM webpoc.MasterRegistration r JOIN webpoc.MasterDefinition d
            ON d.MasterId=r.Id AND d.Revision=r.ActiveRevision WHERE r.Id='environment' AND r.Enabled=1
            """, ct);
        if (registrations.Count != 1) throw ConfigurationError();
        var registration = registrations[0];
        if (registration["Handler"] != "environment" || registration["TableName"] != "Enviro" ||
            registration["KeyColumn"] != "ID" || registration["Scope"] != "site" ||
            registration["MenuModule"] != "Master" || registration["MenuName"] != "MnuEnvironmentSetting") throw ConfigurationError();
        var configured = await Database.QueryAsync(connection, transaction,
            """
            SELECT f.*,l.ControlName,l.TabName,l.GroupName,l.LookupId,l.ChoiceSet,l.Multiple,
                l.IntegerDigits,l.FractionDigits,l.Maximum
            FROM webpoc.MasterField f JOIN webpoc.EnvironmentFieldLayout l ON l.MasterId=f.MasterId
                AND l.Revision=f.Revision AND l.ColumnName=f.ColumnName
            WHERE f.MasterId='environment' AND f.Revision=@revision ORDER BY f.Position
            """, ct, ("@revision", int.Parse(registration["Revision"]!, CultureInfo.InvariantCulture)));
        var columns = await Database.QueryAsync(connection, transaction,
            """
            SELECT c.name AS ColumnName,t.name AS SqlType,c.max_length AS Length,c.precision AS Precision,
                c.scale AS Scale,c.is_identity AS IdentityColumn,c.is_computed AS Computed
            FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id WHERE c.object_id=OBJECT_ID('dbo.Enviro')
            """, ct);
        if (columns.SingleOrDefault(c => c["ColumnName"] == "ID") is not { } id ||
            id["SqlType"] != "varchar" || id["Length"] != "2" ||
            columns.SingleOrDefault(c => c["ColumnName"] == "Site_Code")?["Length"] != "2" ||
            columns.SingleOrDefault(c => c["ColumnName"] == "WebPocVersion")?["SqlType"] != "timestamp" ||
            configured.Count is < 1 or > 80 || configured.Count != int.Parse(registration["FieldCount"]!, CultureInfo.InvariantCulture))
            throw ConfigurationError();
        var choices = await Database.QueryAsync(connection, transaction,
            "SELECT * FROM webpoc.EnvironmentChoice ORDER BY ChoiceSet,Position", ct);
        var fields = new List<FieldResponse>();
        var layout = new List<EnvironmentLayoutResponse>();
        foreach (var row in configured)
        {
            var key = row["ColumnName"]!;
            _ = Identifier(key);
            var shape = columns.SingleOrDefault(c => c["ColumnName"] == key) ?? throw ConfigurationError();
            if (Regex.IsMatch(key, @"password|secret|token", RegexOptions.IgnoreCase) ||
                key is "ID" or "Site_Code" or "Transfered" or "WebPocVersion" ||
                shape["IdentityColumn"] != "False" || shape["Computed"] != "False" ||
                shape["SqlType"] is not ("varchar" or "float" or "tinyint")) throw ConfigurationError();
            var physicalLength = int.Parse(shape["Length"]!, CultureInfo.InvariantCulture);
            var length = Integer(row, "MaxLength") ?? physicalLength;
            if (shape["SqlType"] == "varchar" && (length is < 1 or > 4000 || physicalLength != -1 && length > physicalLength))
                throw ConfigurationError();
            var fieldChoices = choices.Where(c => c["ChoiceSet"] == row["ChoiceSet"] && row["ChoiceSet"] is not null)
                .Select(c => new EnvironmentChoiceResponse(c["Code"]!, c["Label"]!, c["Selectable"] == "True")).ToList();
            if (row["ChoiceSet"] is not null && fieldChoices.Count == 0) throw ConfigurationError();
            fields.Add(new(key, "master", row["Label"]!, shape["SqlType"]!, row["Required"] == "True", length,
                int.Parse(shape["Precision"]!, CultureInfo.InvariantCulture), int.Parse(shape["Scale"]!, CultureInfo.InvariantCulture),
                null, true, row["LookupId"]));
            layout.Add(new(key, row["ControlName"]!, row["TabName"]!, row["GroupName"]!, row["LookupId"],
                row["Multiple"] == "True", Integer(row, "IntegerDigits"), Integer(row, "FractionDigits"),
                Integer(row, "Maximum"), fieldChoices));
        }
        var definition = new MasterDefinitionResponse("environment", registration["Title"]!,
            int.Parse(registration["Revision"]!, CultureInfo.InvariantCulture), Hash(new { registration, configured, columns, choices }),
            fields, configured.ToDictionary(f => f["ColumnName"]!, f => f["DefaultValue"]),
            capabilities with { Delete = false, Print = false }, [], "site", registration["CaptureAuditValues"] == "True");
        var records = await Database.QueryAsync(connection, transaction,
            $"""
            SELECT {string.Join(",", fields.Select(f => Identifier(f.Key)))},ID AS __Id,WebPocVersion AS __Version,
              (SELECT TOP(1) Username FROM webpoc.MasterAudit a WHERE a.MasterId='environment'
                AND a.RecordId=e.ID AND a.Operation='create' ORDER BY a.Id) AS __CreatedBy,
              (SELECT TOP(1) Username FROM webpoc.MasterAudit a WHERE a.MasterId='environment'
                AND a.RecordId=e.ID AND a.Operation='edit' ORDER BY a.Id DESC) AS __ModifiedBy
            FROM dbo.Enviro e WHERE Site_Code=@site
            """, ct, ("@site", session.Site));
        if (records.Count > 1) throw ConfigurationError();
        var record = records.SingleOrDefault();
        return new(definition, record is null ? null : new(record["__Id"]!, record["__Version"]!, definition.Version,
            fields.ToDictionary(f => f.Key, f => record[f.Key]), new(record["__CreatedBy"], record["__ModifiedBy"], null, null)), layout);
    }

    public async Task<EnvironmentFormResponse> FormAsync(SessionResponse session, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(false, ct);
        return await LoadAsync(connection, null, session, ct);
    }

    internal static string[] SelectionKeys(string? value)
    {
        if (string.IsNullOrEmpty(value)) return [];
        if (!Regex.IsMatch(value, @"^\|[^|,]+\|(?:,\s*\|[^|,]+\|)*$"))
            throw new ApiFault(422, "invalid_environment_selection", "Select codes using the configured lookup.");
        var keys = Regex.Matches(value, @"\|([^|,]+)\|").Select(match => match.Groups[1].Value).ToArray();
        if (keys.Length > 100 || keys.Distinct().Count() != keys.Length)
            throw new ApiFault(422, "invalid_environment_selection", "Select at most 100 distinct reference codes.");
        return keys;
    }

    private async Task ValidateReferencesAsync(EnvironmentFormResponse form, Dictionary<string, string?> values,
        SessionResponse session, CancellationToken ct)
    {
        foreach (var group in form.Layout.Where(f => f.LookupId is not null).GroupBy(f => f.LookupId!))
        {
            var selected = new Dictionary<string, string[]>();
            foreach (var field in group)
            {
                try
                {
                    selected[field.Key] = field.Multiple ? SelectionKeys(values[field.Key]) :
                        string.IsNullOrEmpty(values[field.Key]) ? [] : [values[field.Key]!];
                }
                catch (ApiFault fault) when (fault.Code == "invalid_environment_selection")
                {
                    throw InvalidField(field.Key, fault.Message);
                }
            }
            var missing = new HashSet<string>();
            foreach (var batch in selected.Values.SelectMany(keys => keys).Distinct().Chunk(100))
                missing.UnionWith((await lookups.ResolveAsync(session, group.Key, new(batch.ToList()), ct)).MissingKeys);
            var invalid = selected.FirstOrDefault(f => f.Value.Any(missing.Contains));
            if (invalid.Key is not null) throw InvalidField(invalid.Key, "A selected reference no longer exists or is not eligible.");
        }
    }
    private static ApiFault InvalidField(string key, string message) =>
        new(422, "invalid_environment_field", message, new Dictionary<string, string[]> { [$"values.{key}"] = [message] });

    public async Task<MasterRecordResponse> SaveAsync(SessionResponse session, SaveMasterRequest request, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(false, ct);
        await Database.ExecuteAsync(connection, null, "SET XACT_ABORT ON; SET LOCK_TIMEOUT 10000;", ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var saved = await SaveInTransactionAsync(connection, transaction, session, request, ct);
            checkpoint.Reached("environment-before-commit");
            await transaction.CommitAsync(ct);
            return saved;
        }
        catch (SqlException exception) when (exception.Number is 1205 or 1222)
        {
            throw new ApiFault(409, "master_busy", "Another session is updating settings. Retry after it completes.");
        }
    }

    internal async Task<MasterRecordResponse> SaveInTransactionAsync(SqlConnection connection, SqlTransaction transaction,
        SessionResponse session, SaveMasterRequest request, CancellationToken ct)
    {
        if (!Guid.TryParse(request.IdempotencyKey, out _) || request.Values.Count > 80)
            throw new ApiFault(422, "invalid_environment_request", "A valid command identifier and bounded settings are required.");
        var form = await LoadAsync(connection, transaction, session, ct);
        if (!(request.Version is null ? form.Definition.Capabilities.Add : form.Definition.Capabilities.Edit))
            throw new ApiFault(403, "environment_denied", "Saving settings is not permitted in this workspace.");
        var commandKey = Hash(new { session.Username, session.Company, session.Site, Master = "environment", request.IdempotencyKey });
        var payloadHash = Hash(request);
        var commands = await Database.QueryAsync(connection, transaction,
            "SELECT PayloadHash,ResponseJson FROM webpoc.MasterCommand WITH(UPDLOCK,HOLDLOCK) WHERE CommandKey=@key",
            ct, ("@key", commandKey));
        if (commands.Count != 0)
        {
            if (commands[0]["PayloadHash"] != payloadHash)
                throw new ApiFault(409, "master_command_reused", "This command was already used for different settings.");
            return JsonSerializer.Deserialize<MasterRecordResponse>(commands[0]["ResponseJson"]!) ?? throw ConfigurationError();
        }
        if (request.MetadataVersion != form.Definition.Version)
            throw new ApiFault(409, "master_metadata_changed", "Settings fields changed. Preserve your draft and reopen the form.");
        if (form.Record?.Version != request.Version)
            throw new ApiFault(409, "master_conflict", "Settings changed in another session. Reopen them before saving.");
        if (request.Values.Keys.Any(k => !form.Definition.Fields.Any(f => f.Key == k)))
            throw new ApiFault(422, "unknown_master_field", "The request contains an unapproved or server-owned setting.");
        var values = new Dictionary<string, string?>(form.Record?.Values ?? form.Definition.Defaults);
        foreach (var pair in request.Values) values[pair.Key] = pair.Value;
        var parameters = new List<(string Name, object? Value)>();
        foreach (var field in form.Definition.Fields)
        {
            var value = values[field.Key];
            var layout = form.Layout.Single(l => l.Key == field.Key);
            if (field.Required && string.IsNullOrWhiteSpace(value)) throw InvalidField(field.Key, $"{field.Label} is required.");
            if (value is not null && layout.Choices.Count > 0 &&
                !layout.Choices.Any(c => c.Code == value && (c.Selectable || form.Record?.Values[field.Key] == value)))
                throw InvalidField(field.Key, "Choose a supported setting value.");
            object? parameter = value;
            if (field.Type is "float" or "tinyint" && value is not null)
            {
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
                    !double.IsFinite(number) || number < 0 ||
                    field.Type == "tinyint" && (number > 255 || number != Math.Truncate(number)) ||
                    layout.Maximum is { } maximum && number > maximum ||
                    layout.IntegerDigits is { } digits && !Regex.IsMatch(value,
                        $@"^\d{{1,{digits}}}{(layout.FractionDigits > 0 ? $@"(\.\d{{1,{layout.FractionDigits}}})?" : "")}$"))
                    throw InvalidField(field.Key, $"{field.Label} has an invalid number or exceeds its supported range.");
                parameter = field.Type == "tinyint" ? (object)(int)number : number;
            }
            else if (field.Type == "varchar") parameter = MetadataService.Validate(field, value);
            parameters.Add(($"@value{parameters.Count}", parameter));
        }
        await ValidateReferencesAsync(form, values, session, ct);
        var id = form.Record?.Id;
        if (id is null)
        {
            var keys = await Database.QueryAsync(connection, transaction,
                """
                    SELECT ID FROM dbo.Enviro WITH(UPDLOCK,HOLDLOCK)
                    UNION SELECT RecordId FROM webpoc.MasterAudit WHERE MasterId='environment'
                    """, ct);
            if (keys.Any(k => !Regex.IsMatch(k["ID"]!, @"^\d{1,2}$"))) throw ConfigurationError();
            var next = keys.Select(k => int.Parse(k["ID"]!, CultureInfo.InvariantCulture)).DefaultIfEmpty(0).Max() + 1;
            if (next > 99) throw new ApiFault(409, "environment_numbering", "The environment ID range is exhausted.");
            id = next.ToString(CultureInfo.InvariantCulture);
            await Database.ExecuteAsync(connection, transaction,
                "INSERT dbo.Enviro(ID,Site_Code) VALUES(@id,@site)", ct, ("@id", id), ("@site", session.Site));
        }
        parameters.Add(("@id", id)); parameters.Add(("@site", session.Site));
        var changed = form.Definition.Fields.Where(f => form.Record is null || form.Record.Values[f.Key] != values[f.Key])
            .Select(f => $"{Identifier(f.Key)}=@value{form.Definition.Fields.ToList().IndexOf(f)}").ToList();
        if (changed.Count > 0)
        {
            var versionFilter = "";
            if (form.Record is not null)
            {
                parameters.Add(("@version", Convert.FromBase64String(form.Record.Version)));
                versionFilter = " AND WebPocVersion=@version";
            }
            var update = await Database.ExecuteAsync(connection, transaction,
                $"UPDATE dbo.Enviro SET {string.Join(",", changed)} WHERE ID=@id AND Site_Code=@site{versionFilter}", ct, parameters.ToArray());
            if (update != 1) throw new ApiFault(409, "master_conflict", "Settings changed in another session.");
        }
        var saved = (await LoadAsync(connection, transaction, session, ct)).Record ?? throw ConfigurationError();
        await MasterAuditLog.WriteAsync(connection, transaction, form.Definition, session, id,
            form.Record is null ? "create" : "edit", form.Record?.Values, saved.Values, ct);
        saved = (await LoadAsync(connection, transaction, session, ct)).Record ?? throw ConfigurationError();
        await Database.ExecuteAsync(connection, transaction,
            "INSERT webpoc.MasterCommand(CommandKey,MasterId,RecordId,PayloadHash,ResponseJson) VALUES(@command,'environment',@id,@hash,@response)",
            ct, ("@command", commandKey), ("@id", id), ("@hash", payloadHash), ("@response", JsonSerializer.Serialize(saved)));
        return saved;
    }

    public async Task<MasterAuditResponse> AuditAsync(SessionResponse session, int page, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(false, ct);
        var form = await LoadAsync(connection, null, session, ct);
        return await MasterAuditLog.ReadAsync(connection, form.Definition, session, null, page, ct);
    }
}

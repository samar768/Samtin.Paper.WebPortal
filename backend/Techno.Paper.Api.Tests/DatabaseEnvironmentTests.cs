using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Techno.Paper.Api.Infrastructure;
using Techno.Paper.Api.Middleware;
using Techno.Paper.Api.Services;

namespace Techno.Paper.Api.Tests;

[Collection("Development database")]
public sealed class DatabaseEnvironmentTests
{
    private static async Task<(Database Database, EnvironmentService Settings, LookupService Lookups,
        SessionResponse Session, PocOptions Options)> Services(IWriteCheckpoint? checkpoint = null)
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("PAPER_POC_INTEGRATION") == "1", "Approved development database only.");
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "Techno.Paper.Api", "appsettings.Local.json"))).Build();
        var options = configuration.GetSection("Poc").Get<PocOptions>()!;
        Assert.True(options.DevelopmentWritesApproved);
        var database = new Database(options);
        var identity = new IdentityService(database, options, new CompanyContextService(database, options));
        var session = await identity.DescribeAsync("SA", "26", "HO", TestContext.Current.CancellationToken);
        var lookups = new LookupService(database, [new SalesLookupAccessPolicy(), new EnvironmentLookupAccessPolicy(database, options)],
            new MasterService(database, options));
        return (database, new EnvironmentService(database, options, lookups, checkpoint ?? new WriteCheckpoint()), lookups, session, options);
    }

    private static async Task<Dictionary<string, string?>> ValidValues(EnvironmentFormResponse form,
        LookupService lookups, SessionResponse session, CancellationToken ct)
    {
        var values = new Dictionary<string, string?>();
        foreach (var field in form.Definition.Fields)
        {
            var layout = form.Layout.Single(l => l.Key == field.Key);
            if (layout.Choices.Count > 0) values[field.Key] = layout.Choices.First(c => c.Selectable).Code;
            else if (layout.LookupId is { } lookup)
            {
                var candidates = (await lookups.SearchAsync(session, lookup, null, 0, ct)).Options;
                if (candidates.Count == 0)
                {
                    Assert.False(field.Required, $"Required lookup {lookup} needs development fixture data.");
                    values[field.Key] = null;
                    continue;
                }
                var option = candidates[0];
                values[field.Key] = layout.Multiple ? $"|{option.Key}|" : option.Key;
            }
            else values[field.Key] = field.Type is "float" or "tinyint" ? "1" : "environment-test-rollback";
        }
        values["InchKG"] = "1.23456789123456";
        values["Sl_Order_Weight_round_to_places"] = "6";
        values["SheetConversion"] = "500.25";
        return values;
    }
    private static SaveMasterRequest Request(EnvironmentFormResponse form, Dictionary<string, string?> values) =>
        new(form.Definition.Version, form.Record?.Version, Guid.NewGuid().ToString(), values);

    [Fact]
    public async Task Master_form_contains_all_Windows_fields_tabs_and_not_hidden_credentials()
    {
        var (_, settings, _, session, _) = await Services();
        var form = await settings.FormAsync(session, TestContext.Current.CancellationToken);
        Assert.Equal(62, form.Definition.Fields.Count);
        Assert.Equal(62, form.Layout.Count);
        Assert.Equal(["Gate", "Sales", "Excise", "Purchase", "CostSheet", "Stores"], form.Layout.Select(l => l.Tab).Distinct());
        Assert.Equal(62, form.Layout.Select(l => l.ControlName).Distinct().Count());
        Assert.DoesNotContain(form.Definition.Fields, f => f.Key.StartsWith("WS_") || f.Key.Contains("Password") || f.Key == "Site_Code");
        Assert.NotNull(form.Record);
        Assert.False(form.Definition.Capabilities.Delete);
        Assert.False(form.Definition.Capabilities.Print);
        Assert.Equal(6, form.Layout.Single(l => l.Key == "Sl_Order_Weight_round_to_places").Maximum);
    }

    [Fact]
    public async Task Environment_and_its_lookups_use_own_live_menu_not_Sales_permissions()
    {
        var (_, settings, lookups, session, _) = await Services();
        var ct = TestContext.Current.CancellationToken;
        var noSales = session with { Capabilities = session.Capabilities with { View = false } };
        Assert.True((await settings.FormAsync(noSales, ct)).Definition.Capabilities.View);
        Assert.NotEmpty((await lookups.SearchAsync(noSales, "env-accounts", null, 0, ct)).Options);
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiFault>(() => lookups.DefinitionAsync(noSales, "parties", ct))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiFault>(() => settings.FormAsync(session with { Username = "NOENVTEST" }, ct))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiFault>(() => lookups.DefinitionAsync(
            session with { Username = "NOENVTEST" }, "env-groups", ct))).Status);
    }

    [Fact]
    public async Task Readonly_site_and_nonadministrator_cannot_write_or_read_other_site_audit()
    {
        var (_, settings, _, session, options) = await Services();
        var ct = TestContext.Current.CancellationToken;
        var form = await settings.FormAsync(session, ct);
        var readOnly = session with { ReadOnly = true };
        Assert.False((await settings.FormAsync(readOnly, ct)).Definition.Capabilities.Edit);
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiFault>(() => settings.SaveAsync(readOnly, Request(form, []), ct))).Status);
        var otherSite = await settings.FormAsync(readOnly with { Site = "D1" }, ct);
        Assert.Null(otherSite.Record);
        Assert.Empty((await settings.AuditAsync(readOnly with { Site = "D1" }, 0, ct)).Entries);
        options.MetadataAdministrator = "OTHER";
        Assert.True((await settings.FormAsync(session, ct)).Definition.Capabilities.View);
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiFault>(() => settings.AuditAsync(session, 0, ct))).Status);
    }

    [Theory]
    [InlineData("Sl_Order_Weight_round_to_places", "7")]
    [InlineData("Sl_Order_Weight_round_to_places", "1.5")]
    [InlineData("WeightConvRate", "10000000000")]
    [InlineData("WeightConvRate", "1.5")]
    [InlineData("SheetConversion", "500.123")]
    [InlineData("InchKG", "NaN")]
    [InlineData("CMKG", "-1")]
    [InlineData("PORateEditable", "Yes")]
    [InlineData("WGTEXEPath", null)]
    [InlineData("SalaryAc", "|ZZZ|")]
    [InlineData("SalaryAc", "Group descriptions")]
    public async Task Invalid_values_references_and_exact_numeric_limits_never_change_settings(string key, string? value)
    {
        var (_, settings, lookups, session, _) = await Services();
        var ct = TestContext.Current.CancellationToken;
        var form = await settings.FormAsync(session, ct);
        var values = await ValidValues(form, lookups, session, ct);
        values[key] = value;
        var fault = await Assert.ThrowsAsync<ApiFault>(() => settings.SaveAsync(session, Request(form, values), ct));
        Assert.Equal(422, fault.Status);
        Assert.Contains($"values.{key}", fault.Errors!.Keys);
        Assert.Equal(form.Record!.Version, (await settings.FormAsync(session, ct)).Record!.Version);
    }

    [Fact]
    public async Task Full_settings_roundtrip_audit_replay_and_conflicts_are_verified_inside_rollback_transaction()
    {
        var (database, settings, lookups, session, _) = await Services();
        var ct = TestContext.Current.CancellationToken;
        var form = await settings.FormAsync(session, ct);
        var request = Request(form, await ValidValues(form, lookups, session, ct));
        await using var connection = await database.OpenAsync(false, ct);
        await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct))
        {
            var saved = await settings.SaveInTransactionAsync(connection, transaction, session, request, ct);
            Assert.NotEqual(form.Record!.Version, saved.Version);
            Assert.Equal(1.23456789123456, double.Parse(saved.Values["InchKG"]!, CultureInfo.InvariantCulture));
            Assert.Equal("6", saved.Values["Sl_Order_Weight_round_to_places"]);
            Assert.Equal(62, saved.Values.Count);
            foreach (var field in form.Definition.Fields.Where(f => f.Type == "varchar"))
                Assert.Equal(request.Values[field.Key], saved.Values[field.Key]);
            Assert.Equal(session.Username, saved.RecordInfo.ModifiedBy);
            var events = await Database.QueryAsync(connection, transaction,
                "SELECT TOP(1) * FROM webpoc.MasterAudit WHERE MasterId='environment' AND RecordId=@id ORDER BY Id DESC",
                ct, ("@id", saved.Id));
            var entry = Assert.Single(events);
            Assert.Equal("26", entry["CompanyCode"]); Assert.Equal("HO", entry["SiteCode"]);
            Assert.Equal("True", entry["ValuesCaptured"]);
            Assert.Equal(form.Record.Values["InchKG"], JsonSerializer.Deserialize<Dictionary<string, string?>>(entry["BeforeJson"]!)!["InchKG"]);
            Assert.Equal(saved.Values["InchKG"], JsonSerializer.Deserialize<Dictionary<string, string?>>(entry["AfterJson"]!)!["InchKG"]);
            Assert.Equal(saved.Version, (await settings.SaveInTransactionAsync(connection, transaction, session, request, ct)).Version);
            var reused = request with { Values = new(request.Values) { ["InchKG"] = "2" } };
            Assert.Equal("master_command_reused", (await Assert.ThrowsAsync<ApiFault>(() =>
                settings.SaveInTransactionAsync(connection, transaction, session, reused, ct))).Code);
            Assert.Equal("master_conflict", (await Assert.ThrowsAsync<ApiFault>(() =>
                settings.SaveInTransactionAsync(connection, transaction, session, request with { IdempotencyKey = Guid.NewGuid().ToString() }, ct))).Code);
            var hidden = request with { IdempotencyKey = Guid.NewGuid().ToString(), Version = saved.Version,
                Values = new(request.Values) { ["WS_SMSPassword"] = "must-not-be-written" } };
            Assert.Equal("unknown_master_field", (await Assert.ThrowsAsync<ApiFault>(() =>
                settings.SaveInTransactionAsync(connection, transaction, session, hidden, ct))).Code);
            await transaction.RollbackAsync(ct);
        }
        var after = await settings.FormAsync(session, ct);
        Assert.Equal(form.Record!.Version, after.Record!.Version);
        Assert.Equal(JsonSerializer.Serialize(form.Record.Values), JsonSerializer.Serialize(after.Record.Values));
    }

    private sealed class InjectedFailure : Exception;

    [Fact]
    public async Task Detailed_audit_can_be_disabled_and_resumed_without_losing_operational_history()
    {
        var (database, settings, lookups, session, _) = await Services();
        var ct = TestContext.Current.CancellationToken;
        var original = await settings.FormAsync(session, ct);
        var values = await ValidValues(original, lookups, session, ct);
        await using var connection = await database.OpenAsync(false, ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await Database.ExecuteAsync(connection, transaction,
            "UPDATE webpoc.MasterRegistration SET CaptureAuditValues=0 WHERE Id='environment'", ct);
        var off = await settings.LoadAsync(connection, transaction, session, ct);
        Assert.NotEqual(original.Definition.Version, off.Definition.Version);
        var saved = await settings.SaveInTransactionAsync(connection, transaction, session, Request(off, values), ct);
        var events = await Database.QueryAsync(connection, transaction,
            "SELECT TOP(1) * FROM webpoc.MasterAudit WHERE MasterId='environment' ORDER BY Id DESC", ct);
        var minimal = Assert.Single(events);
        Assert.Equal("False", minimal["ValuesCaptured"]); Assert.Null(minimal["BeforeJson"]); Assert.Null(minimal["AfterJson"]);
        Assert.Equal("SA", minimal["Username"]); Assert.Equal("HO", minimal["SiteCode"]);
        await Database.ExecuteAsync(connection, transaction,
            "UPDATE webpoc.MasterRegistration SET CaptureAuditValues=1 WHERE Id='environment'", ct);
        var on = await settings.LoadAsync(connection, transaction, session, ct);
        var resumed = new Dictionary<string, string?>(saved.Values) { ["InchKG"] = "2.5" };
        await settings.SaveInTransactionAsync(connection, transaction, session, Request(on, resumed), ct);
        var detailed = Assert.Single(await Database.QueryAsync(connection, transaction,
            "SELECT TOP(1) * FROM webpoc.MasterAudit WHERE MasterId='environment' ORDER BY Id DESC", ct));
        Assert.Equal("True", detailed["ValuesCaptured"]);
        Assert.NotNull(detailed["BeforeJson"]); Assert.NotNull(detailed["AfterJson"]);
        await transaction.RollbackAsync(ct);
        Assert.Equal(original.Record!.Version, (await settings.FormAsync(session, ct)).Record!.Version);
    }

    [Fact]
    public async Task Missing_site_creation_preserves_numeric_ID_rules_and_rolls_back_entire_configuration()
    {
        var (database, settings, lookups, session, _) = await Services();
        var ct = TestContext.Current.CancellationToken;
        var original = await settings.FormAsync(session, ct);
        var values = await ValidValues(original, lookups, session, ct);
        await using var connection = await database.OpenAsync(false, ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await Database.ExecuteAsync(connection, transaction, "DELETE dbo.Enviro WHERE ID=@id AND Site_Code=@site",
            ct, ("@id", original.Record!.Id), ("@site", session.Site));
        var absent = await settings.LoadAsync(connection, transaction, session, ct);
        Assert.Null(absent.Record);
        var saved = await settings.SaveInTransactionAsync(connection, transaction, session, Request(absent, values), ct);
        Assert.InRange(int.Parse(saved.Id, CultureInfo.InvariantCulture), 1, 99);
        Assert.Equal("SA", saved.RecordInfo.CreatedBy);
        var site = Assert.Single(await Database.QueryAsync(connection, transaction,
            "SELECT Site_Code FROM dbo.Enviro WHERE ID=@id", ct, ("@id", saved.Id)));
        Assert.Equal(session.Site, site["Site_Code"]);
        await transaction.RollbackAsync(ct);
        Assert.Equal(original.Record.Version, (await settings.FormAsync(session, ct)).Record!.Version);
    }

    private sealed class FailingCheckpoint : IWriteCheckpoint
    {
        public void Reached(string stage) { if (stage == "environment-before-commit") throw new InjectedFailure(); }
    }
    [Fact]
    public async Task Public_save_rolls_back_values_audit_and_command_on_failure_before_commit()
    {
        var (_, settings, lookups, session, _) = await Services(new FailingCheckpoint());
        var ct = TestContext.Current.CancellationToken;
        var form = await settings.FormAsync(session, ct);
        var history = await settings.AuditAsync(session, 0, ct);
        var request = Request(form, await ValidValues(form, lookups, session, ct));
        await Assert.ThrowsAsync<InjectedFailure>(() => settings.SaveAsync(session,
            request, ct));
        Assert.Equal(form.Record!.Version, (await settings.FormAsync(session, ct)).Record!.Version);
        Assert.Equal(history.Entries.Count, (await settings.AuditAsync(session, 0, ct)).Entries.Count);
    }
}

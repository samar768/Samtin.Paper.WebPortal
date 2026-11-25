using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Techno.Paper.Api.Endpoints;
using Techno.Paper.Api.Infrastructure;
using Techno.Paper.Api.Middleware;
using Techno.Paper.Api.Services;

namespace Techno.Paper.Api.Tests;

[Collection("Development database")]
public sealed class DatabaseMasterTests
{
    [Fact]
    public async Task Master_delete_endpoint_builds_with_explicit_request_body()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddScoped<IIdentityService>(_ => throw new InvalidOperationException("Metadata validation must not execute services."));
        builder.Services.AddScoped<IMasterService>(_ => throw new InvalidOperationException("Metadata validation must not execute services."));
        await using var app = builder.Build();
        app.MapMasters();
        Assert.Single(((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>(), endpoint =>
                endpoint.RoutePattern.RawText == "/api/masters/{id}/records/{recordId}" &&
                endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("DELETE"));
    }

    private static async Task<(Database Database, MasterService Masters, SessionResponse Session)> Services()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("PAPER_POC_INTEGRATION") == "1",
            "Only run against the explicitly approved development database.");
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "Techno.Paper.Api", "appsettings.Local.json"))).Build();
        var options = configuration.GetSection("Poc").Get<PocOptions>()!;
        Assert.True(options.DevelopmentWritesApproved);
        var database = new Database(options);
        var identity = new IdentityService(database, options, new CompanyContextService(database, options));
        return (database, new MasterService(database, options),
            await identity.DescribeAsync("SA", "26", "HO", TestContext.Current.CancellationToken));
    }

    private static SaveMasterRequest Request(MasterFormResponse form, string name, string? command = null) =>
        new(form.Definition.Version, form.Record?.Version, command ?? Guid.NewGuid().ToString(),
            new() { [form.Definition.Fields[0].Key] = name });

    private static async Task Cleanup(Database database, string id, string table, IEnumerable<string> keys,
        string keyColumn = "Code", string nameColumn = "Name")
    {
        await using var connection = await database.OpenAsync(false, CancellationToken.None);
        foreach (var key in keys)
            await Database.ExecuteAsync(connection, null,
                $"""
                DELETE FROM dbo.[{table}] WHERE [{keyColumn}]=@key AND [{nameColumn}] LIKE 'master-test-%';
                DELETE FROM webpoc.MasterCommand WHERE MasterId=@master AND RecordId=@key;
                DELETE FROM webpoc.MasterAudit WHERE MasterId=@master AND RecordId=@key;
                """, CancellationToken.None, ("@key", key), ("@master", id));
    }

    [Theory]
    [InlineData("bf", "BFMast", "Code", "Name", 35, "global")]
    [InlineData("gsm", "GSMMast", "Code", "Name", 35, "global")]
    [InlineData("varity", "VarityMast", "Code", "Name", 25, "global")]
    [InlineData("godowns", "GodownMast", "GodCode", "GodName", 20, "site")]
    [InlineData("departments", "DepartmentMast", "Code", "Name", 35, "site")]
    [InlineData("classifications", "ClassificationMast", "Code", "Name", 30, "global")]
    [InlineData("item-makes", "ItemMakeMast", "Code", "Name", 35, "global")]
    [InlineData("quality-specifications", "SpecificationsMast", "Code", "Name", 35, "global")]
    [InlineData("rejection-reasons", "RejectionReasonMast", "Code", "Reason", 50, "site")]
    [InlineData("deductions", "DeductionMast", "Code", "Name", 35, "global")]
    [InlineData("machines", "MachineMast", "MachineCode", "Name", 35, "global")]
    public async Task Windows_master_rules_roundtrip_audit_idempotency_and_conflict(string id, string table,
        string keyColumn, string nameColumn, int length, string scope)
    {
        var (database, masters, session) = await Services();
        var ct = TestContext.Current.CancellationToken;
        var keys = new List<string>();
        var form = await masters.FormAsync(session, id, null, ct);
        Assert.Equal(length, Assert.Single(form.Definition.Fields).Length);
        Assert.Equal(scope, form.Definition.Scope);
        Assert.True(form.Definition.Fields[0].Required);
        Assert.Equal("invalid_field", (await Assert.ThrowsAsync<ApiFault>(() =>
            masters.SaveAsync(session, id, null, Request(form, new string('x', length + 1)), ct))).Code);
        var name = "master-test-" + Guid.NewGuid().ToString("N")[..5] + " x";
        var request = Request(form, $"  {name.Replace(" x", "  x")}  ");
        try
        {
            var saved = await masters.SaveAsync(session, id, null, request, ct);
            keys.Add(saved.Id);
            Assert.Matches(@"^HO\d{1,4}$", saved.Id);
            Assert.Equal(name, saved.Values[nameColumn]);
            Assert.Equal("SA", saved.RecordInfo.CreatedBy);
            Assert.Null(saved.RecordInfo.ModifiedBy);
            var replay = await masters.SaveAsync(session, id, null, request, ct);
            Assert.Equal(saved.Id, replay.Id); Assert.Equal(saved.Version, replay.Version);
            var duplicate = await Assert.ThrowsAsync<ApiFault>(() =>
                masters.SaveAsync(session, id, null, Request(form, name), ct));
            Assert.Equal("master_duplicate", duplicate.Code);
            var reused = await Assert.ThrowsAsync<ApiFault>(() =>
                masters.SaveAsync(session, id, null, request with { Values = new() { [nameColumn] = name + "x" } }, ct));
            Assert.Equal("master_command_reused", reused.Code);
            var editForm = await masters.FormAsync(session, id, saved.Id, ct);
            var edited = await masters.SaveAsync(session, id, saved.Id, Request(editForm, name + "x"), ct);
            Assert.NotEqual(saved.Version, edited.Version);
            Assert.Equal("SA", edited.RecordInfo.CreatedBy); Assert.Equal("SA", edited.RecordInfo.ModifiedBy);
            var conflict = await Assert.ThrowsAsync<ApiFault>(() =>
                masters.SaveAsync(session, id, saved.Id, Request(editForm, name + "y"), ct));
            Assert.Equal("master_conflict", conflict.Code);
            await using (var connection = await database.OpenAsync(false, ct))
            {
                var row = Assert.Single(await Database.QueryAsync(connection, null,
                    $"SELECT U_Name,U_AE,Transfered FROM dbo.[{table}] WHERE [{keyColumn}]=@key", ct, ("@key", saved.Id)));
                Assert.Equal("SA", row["U_Name"]); Assert.Equal("E", row["U_AE"]); Assert.Equal("N", row["Transfered"]);
            }
            Assert.Single((await masters.PrintAsync(session, id, name, ct)).Records);
            var staleDelete = await Assert.ThrowsAsync<ApiFault>(() => masters.DeleteAsync(session, id, saved.Id, new(saved.Version), ct));
            Assert.Equal("master_conflict", staleDelete.Code);
            await masters.DeleteAsync(session, id, saved.Id, new(edited.Version), ct);
            Assert.Empty((await masters.ListAsync(session, id, name, 0, ct)).Records);
            var after = await masters.SaveAsync(session, id, null, Request(form, name), ct);
            keys.Add(after.Id);
            Assert.NotEqual(saved.Id, after.Id);
            await using var auditConnection = await database.OpenAsync(false, ct);
            var audit = await Database.QueryAsync(auditConnection, null,
                "SELECT Operation FROM webpoc.MasterAudit WHERE MasterId=@master AND RecordId=@key ORDER BY Id",
                ct, ("@master", id), ("@key", saved.Id));
            Assert.Equal(new[] { "create", "edit", "delete" }, audit.Select(row => row["Operation"]));
        }
        finally { await Cleanup(database, id, table, keys, keyColumn, nameColumn); }
    }

    [Theory]
    [InlineData("godowns", "GodownMast", "GodCode", "GodName")]
    [InlineData("departments", "DepartmentMast", "Code", "Name")]
    [InlineData("rejection-reasons", "RejectionReasonMast", "Code", "Reason")]
    public async Task Site_masters_isolate_reads_prints_updates_deletes_and_duplicate_names(string id, string table,
        string keyColumn, string nameColumn)
    {
        var (database, masters, session) = await Services();
        var ct = TestContext.Current.CancellationToken;
        var form = await masters.FormAsync(session, id, null, ct);
        var name = "master-test-" + Guid.NewGuid().ToString("N")[..6];
        var keys = new List<string>();
        var foreignKey = "D1" + Random.Shared.Next(1000, 9999).ToString();
        try
        {
            var saved = await masters.SaveAsync(session, id, null, Request(form, name), ct);
            keys.Add(saved.Id);
            await using var connection = await database.OpenAsync(false, ct);
            await Database.ExecuteAsync(connection, null,
                $"INSERT INTO dbo.[{table}]([{keyColumn}],[{nameColumn}],Site_Code) VALUES(@key,@name,'D1')",
                ct, ("@key", foreignKey), ("@name", name));
            keys.Add(foreignKey);
            var row = Assert.Single(await Database.QueryAsync(connection, null,
                $"SELECT Site_Code FROM dbo.[{table}] WHERE [{keyColumn}]=@key", ct, ("@key", saved.Id)));
            Assert.Equal("HO", row["Site_Code"]);
            Assert.Equal(saved.Id, Assert.Single((await masters.ListAsync(session, id, name, 0, ct)).Records).Id);
            Assert.Equal(saved.Id, Assert.Single((await masters.PrintAsync(session, id, name, ct)).Records).Id);
            Assert.Equal(404, (await Assert.ThrowsAsync<ApiFault>(() => masters.FormAsync(session, id, foreignKey, ct))).Status);
            Assert.Equal(404, (await Assert.ThrowsAsync<ApiFault>(() =>
                masters.SaveAsync(session, id, foreignKey, Request(form, name + "x") with { Version = saved.Version }, ct))).Status);
            Assert.Equal(404, (await Assert.ThrowsAsync<ApiFault>(() =>
                masters.DeleteAsync(session, id, foreignKey, new(saved.Version), ct))).Status);
            Assert.Equal("master_duplicate", (await Assert.ThrowsAsync<ApiFault>(() =>
                masters.SaveAsync(session, id, null, Request(form, name), ct))).Code);
            var foreign = session with { Site = "D1", ReadOnly = true };
            Assert.Equal(foreignKey, Assert.Single((await masters.ListAsync(foreign, id, name, 0, ct)).Records).Id);
            Assert.False((await masters.ListAsync(foreign, id, name, 0, ct)).Definition.Capabilities.Add);
            Assert.Empty((await masters.AuditAsync(foreign, id, saved.Id, 0, ct)).Entries);
        }
        finally { await Cleanup(database, id, table, keys, keyColumn, nameColumn); }
    }

    [Fact]
    public async Task Configurable_audit_preserves_history_actor_time_and_key_history_without_logging_disabled_values()
    {
        var (database, masters, session) = await Services();
        var ct = TestContext.Current.CancellationToken;
        var keys = new List<string>();
        await using var connection = await database.OpenAsync(false, ct);
        var originalPolicy = Assert.Single(await Database.QueryAsync(connection, null,
            "SELECT CaptureAuditValues FROM webpoc.MasterRegistration WHERE Id='bf'", ct))["CaptureAuditValues"];
        var form = await masters.FormAsync(session, "bf", null, ct);
        var name = "master-test-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var saved = await masters.SaveAsync(session, "bf", null, Request(form, name), ct);
            keys.Add(saved.Id);
            var created = Assert.Single((await masters.AuditAsync(session with { ReadOnly = true }, "bf", saved.Id, 0, ct)).Entries);
            Assert.True(created.ValuesCaptured);
            Assert.Null(created.Before); Assert.Equal(name, created.After!["Name"]);
            Assert.Equal("SA", created.Username); Assert.Equal("26", created.CompanyCode); Assert.Equal("HO", created.SiteCode);
            Assert.EndsWith("Z", created.OccurredAt);
            var nonAdministrator = new MasterService(database, new PocOptions
            {
                Company = session.Company, Site = session.Site, DevelopmentWritesApproved = true, MetadataAdministrator = "OTHER"
            });
            Assert.True((await nonAdministrator.ListAsync(session, "bf", null, 0, ct)).Definition.Capabilities.View);
            Assert.Equal(403, (await Assert.ThrowsAsync<ApiFault>(() =>
                nonAdministrator.AuditAsync(session, "bf", saved.Id, 0, ct))).Status);
            await Database.ExecuteAsync(connection, null, "UPDATE webpoc.MasterRegistration SET CaptureAuditValues=0 WHERE Id='bf'", ct);
            var off = await masters.FormAsync(session, "bf", saved.Id, ct);
            var offRequest = Request(off, name + "x");
            var edited = await masters.SaveAsync(session, "bf", saved.Id, offRequest, ct);
            var offEvents = (await masters.AuditAsync(session, "bf", saved.Id, 0, ct)).Entries;
            Assert.False(offEvents[0].ValuesCaptured); Assert.Null(offEvents[0].Before); Assert.Null(offEvents[0].After);
            Assert.True(offEvents[1].ValuesCaptured); Assert.Equal("SA", edited.RecordInfo.CreatedBy);
            var replay = await masters.SaveAsync(session, "bf", saved.Id, offRequest, ct);
            Assert.Equal(edited.Version, replay.Version);
            Assert.Equal(2, (await masters.AuditAsync(session, "bf", saved.Id, 0, ct)).Entries.Count);
            await masters.DeleteAsync(session, "bf", saved.Id, new(replay.Version), ct);
            var afterDelete = await masters.SaveAsync(session, "bf", null, Request(await masters.FormAsync(session, "bf", null, ct), name), ct);
            keys.Add(afterDelete.Id);
            Assert.NotEqual(saved.Id, afterDelete.Id);
            await Database.ExecuteAsync(connection, null, "UPDATE webpoc.MasterRegistration SET CaptureAuditValues=1 WHERE Id='bf'", ct);
            var on = await masters.FormAsync(session, "bf", afterDelete.Id, ct);
            await masters.SaveAsync(session, "bf", afterDelete.Id, Request(on, name + "y"), ct);
            var resumed = (await masters.AuditAsync(session, "bf", afterDelete.Id, 0, ct)).Entries[0];
            Assert.True(resumed.ValuesCaptured); Assert.Equal(name, resumed.Before!["Name"]); Assert.Equal(name + "y", resumed.After!["Name"]);
        }
        finally
        {
            await Cleanup(database, "bf", "BFMast", keys);
            await Database.ExecuteAsync(connection, null, "UPDATE webpoc.MasterRegistration SET CaptureAuditValues=@policy WHERE Id='bf'",
                CancellationToken.None, ("@policy", originalPolicy == "True"));
        }
    }

    [Theory]
    [InlineData("bf")]
    [InlineData("gsm")]
    public async Task Master_permissions_are_independent_of_Sales_and_protect_fields_and_read_only_contexts(string id)
    {
        var (_, masters, session) = await Services();
        var ct = TestContext.Current.CancellationToken;
        var withoutSales = session with { Capabilities = new(false, false, false, false, false, false, false) };
        var form = await masters.FormAsync(withoutSales, id, null, ct);
        Assert.True(form.Definition.Capabilities.Add);
        Assert.NotNull(await masters.LookupCreateAsync(withoutSales, id, ct));
        Assert.Null(await masters.LookupCreateAsync(session, "order-types", ct));
        var denied = await Assert.ThrowsAsync<ApiFault>(() => masters.FormAsync(session with { Username = "NO-MASTER" }, id, null, ct));
        Assert.Equal(403, denied.Status);
        var readOnly = session with { ReadOnly = true };
        Assert.False((await masters.ListAsync(readOnly, id, null, 0, ct)).Definition.Capabilities.Add);
        Assert.Null(await masters.LookupCreateAsync(readOnly, id, ct));
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiFault>(() => masters.FormAsync(readOnly, id, null, ct))).Status);
        Assert.Equal("required_field", (await Assert.ThrowsAsync<ApiFault>(() =>
            masters.SaveAsync(session, id, null, Request(form, "   "), ct))).Code);
        Assert.Equal("invalid_field", (await Assert.ThrowsAsync<ApiFault>(() =>
            masters.SaveAsync(session, id, null, Request(form, new string('x', 36)), ct))).Code);
        var forged = Request(form, "master-test-forged") with { Values = new() { ["Name"] = "master-test-forged", ["Code"] = "HO9999" } };
        Assert.Equal("unknown_master_field", (await Assert.ThrowsAsync<ApiFault>(() =>
            masters.SaveAsync(session, id, null, forged, ct))).Code);
    }

    [Theory]
    [InlineData("bf", "BFMast")]
    [InlineData("gsm", "GSMMast")]
    public async Task Concurrent_master_creates_do_not_duplicate_names_or_partially_write_audit(string id, string table)
    {
        var (database, masters, session) = await Services();
        var ct = TestContext.Current.CancellationToken;
        var form = await masters.FormAsync(session, id, null, ct);
        var name = "master-test-" + Guid.NewGuid().ToString("N")[..12];
        var keys = new List<string>();
        try
        {
            var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
            {
                try { return (Record: await masters.SaveAsync(session, id, null, Request(form, name), ct), Fault: (ApiFault?)null); }
                catch (ApiFault fault) { return (Record: (MasterRecordResponse?)null, Fault: fault); }
            }));
            var saved = Assert.Single(results, result => result.Record is not null).Record!;
            keys.Add(saved.Id);
            Assert.Equal(409, Assert.Single(results, result => result.Fault is not null).Fault!.Status);
            Assert.Single((await masters.ListAsync(session, id, name, 0, ct)).Records);
            await using var connection = await database.OpenAsync(false, ct);
            Assert.Single(await Database.QueryAsync(connection, null,
                "SELECT Id FROM webpoc.MasterAudit WHERE MasterId=@master AND RecordId=@key",
                ct, ("@master", id), ("@key", saved.Id)));
        }
        finally { await Cleanup(database, id, table, keys); }
    }

    [Theory]
    [InlineData("bf", "BFMast", "BF")]
    [InlineData("gsm", "GSMMast", "GSM")]
    public async Task Referenced_masters_cannot_be_deleted_and_no_audit_is_written_on_failure(string id, string table, string field)
    {
        var (database, masters, session) = await Services();
        var ct = TestContext.Current.CancellationToken;
        await using var connection = await database.OpenAsync(false, ct);
        var referenced = Assert.Single(await Database.QueryAsync(connection, null,
            $"SELECT TOP (1) m.Code FROM dbo.[{table}] m WHERE EXISTS(SELECT 1 FROM dbo.SOrdDet d WHERE d.[{field}]=m.Code)", ct));
        var key = referenced["Code"]!;
        var form = await masters.FormAsync(session, id, key, ct);
        var before = await Database.QueryAsync(connection, null,
            "SELECT Id FROM webpoc.MasterAudit WHERE MasterId=@master AND RecordId=@key", ct, ("@master", id), ("@key", key));
        Assert.Equal("master_in_use", (await Assert.ThrowsAsync<ApiFault>(() =>
            masters.DeleteAsync(session, id, key, new(form.Record!.Version), ct))).Code);
        Assert.Equal(form.Record!.Version, (await masters.FormAsync(session, id, key, ct)).Record!.Version);
        var after = await Database.QueryAsync(connection, null,
            "SELECT Id FROM webpoc.MasterAudit WHERE MasterId=@master AND RecordId=@key", ct, ("@master", id), ("@key", key));
        Assert.Equal(before.Count, after.Count);
    }

    [Fact]
    public async Task Reviewed_scalar_revisions_persist_new_fields_reject_stale_forms_and_preserve_hidden_values()
    {
        var (database, masters, session) = await Services();
        var ct = TestContext.Current.CancellationToken;
        var keys = new List<string>();
        var original = await masters.FormAsync(session, "bf", null, ct);
        var revision = original.Definition.Revision + 1;
        var name = "master-test-" + Guid.NewGuid().ToString("N")[..12];
        await using var connection = await database.OpenAsync(false, ct);
        var shape = Assert.Single(await Database.QueryAsync(connection, null,
            "SELECT COL_LENGTH('dbo.BFMast','WebPocMasterTestNote') AS Length", ct));
        Assert.Null(shape["Length"]);
        try
        {
            await Database.ExecuteAsync(connection, null,
                """
                SET XACT_ABORT ON;
                BEGIN TRAN;
                ALTER TABLE dbo.BFMast ADD WebPocMasterTestNote varchar(60) NULL;
                INSERT INTO webpoc.MasterDefinition(MasterId,Revision,Title,TableName,KeyColumn,NameColumn,MenuModule,MenuName,Handler,PublishedBy,Scope)
                  SELECT MasterId,@revision,Title,TableName,KeyColumn,NameColumn,MenuModule,MenuName,Handler,'integration-test',Scope
                  FROM webpoc.MasterDefinition WHERE MasterId='bf' AND Revision=@original;
                INSERT INTO webpoc.MasterField(MasterId,Revision,ColumnName,Label,Position,Required,DefaultValue,MaxLength)
                  SELECT MasterId,@revision,ColumnName,Label,Position,Required,DefaultValue,MaxLength FROM webpoc.MasterField
                  WHERE MasterId='bf' AND Revision=@original;
                INSERT INTO webpoc.MasterField(MasterId,Revision,ColumnName,Label,Position,Required,DefaultValue)
                  VALUES('bf',@revision,'WebPocMasterTestNote','Pilot note',20,0,'Default note');
                UPDATE webpoc.MasterRegistration SET ActiveRevision=@revision WHERE Id='bf';
                COMMIT;
                """, ct, ("@revision", revision), ("@original", original.Definition.Revision));
            var updated = await masters.FormAsync(session, "bf", null, ct);
            Assert.Equal("Default note", updated.Definition.Defaults["WebPocMasterTestNote"]);
            Assert.Equal("master_metadata_changed", (await Assert.ThrowsAsync<ApiFault>(() =>
                masters.SaveAsync(session, "bf", null, Request(original, name), ct))).Code);
            var request = Request(updated, name);
            request.Values["WebPocMasterTestNote"] = "Stored value";
            var saved = await masters.SaveAsync(session, "bf", null, request, ct);
            keys.Add(saved.Id);
            Assert.Equal("Stored value", saved.Values["WebPocMasterTestNote"]);
            await Database.ExecuteAsync(connection, null,
                """
                BEGIN TRAN;
                INSERT INTO webpoc.MasterDefinition(MasterId,Revision,Title,TableName,KeyColumn,NameColumn,MenuModule,MenuName,Handler,PublishedBy,Scope)
                  SELECT MasterId,@next,Title,TableName,KeyColumn,NameColumn,MenuModule,MenuName,Handler,'integration-test',Scope
                  FROM webpoc.MasterDefinition WHERE MasterId='bf' AND Revision=@revision;
                INSERT INTO webpoc.MasterField(MasterId,Revision,ColumnName,Label,Position,Required,DefaultValue,MaxLength)
                  SELECT MasterId,@next,ColumnName,Label,Position,Required,DefaultValue,MaxLength FROM webpoc.MasterField
                  WHERE MasterId='bf' AND Revision=@revision AND ColumnName<>'WebPocMasterTestNote';
                UPDATE webpoc.MasterRegistration SET ActiveRevision=@next WHERE Id='bf';
                COMMIT;
                """, ct, ("@revision", revision), ("@next", revision + 1));
            var hidden = await masters.FormAsync(session, "bf", saved.Id, ct);
            Assert.DoesNotContain(hidden.Definition.Fields, field => field.Key == "WebPocMasterTestNote");
            await masters.SaveAsync(session, "bf", saved.Id, Request(hidden, name + "x"), ct);
            var stored = Assert.Single(await Database.QueryAsync(connection, null,
                "SELECT WebPocMasterTestNote FROM dbo.BFMast WHERE Code=@key", ct, ("@key", saved.Id)));
            Assert.Equal("Stored value", stored["WebPocMasterTestNote"]);
        }
        finally
        {
            await Cleanup(database, "bf", "BFMast", keys);
            await Database.ExecuteAsync(connection, null,
                """
                SET XACT_ABORT ON;
                BEGIN TRAN;
                UPDATE webpoc.MasterRegistration SET ActiveRevision=@original WHERE Id='bf';
                DELETE FROM webpoc.MasterField WHERE MasterId='bf' AND Revision IN (@revision,@next);
                DELETE FROM webpoc.MasterDefinition WHERE MasterId='bf' AND Revision IN (@revision,@next) AND PublishedBy='integration-test';
                IF COL_LENGTH('dbo.BFMast','WebPocMasterTestNote') IS NOT NULL
                    ALTER TABLE dbo.BFMast DROP COLUMN WebPocMasterTestNote;
                COMMIT;
                """, CancellationToken.None, ("@original", original.Definition.Revision),
                ("@revision", revision), ("@next", revision + 1));
        }
    }
}

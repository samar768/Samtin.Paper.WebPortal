using System.Globalization;
using Microsoft.Extensions.Configuration;
using Techno.Paper.Api.Domain;
using Techno.Paper.Api.Infrastructure;
using Techno.Paper.Api.Middleware;
using Techno.Paper.Api.Services;

namespace Techno.Paper.Api.Tests;

[CollectionDefinition("Development database", DisableParallelization = true)]
public sealed class DevelopmentDatabaseCollection;

[Collection("Development database")]
public sealed class DatabaseWorkflowTests
{
    private static (Database Database, MetadataService Metadata, OrderService Orders, IdentityService Identity) Services(IWriteCheckpoint? checkpoint = null)
    {
        var options = ApprovedOptions();
        var database = new Database(options);
        var metadata = new MetadataService(database);
        return (database, metadata, new OrderService(database, metadata, options, checkpoint ?? new WriteCheckpoint(), new TaskService(database, options, [new SalesOrderTaskTarget(database)])),
            new IdentityService(database, options, new CompanyContextService(database, options)));
    }

    private static PocOptions ApprovedOptions()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("PAPER_POC_INTEGRATION") == "1",
            "Set PAPER_POC_INTEGRATION=1 only for the explicitly approved development database.");
        var apiPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Techno.Paper.Api"));
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.Combine(apiPath, "appsettings.json"))
            .AddJsonFile(Path.Combine(apiPath, "appsettings.Local.json")).Build();
        var options = configuration.GetSection("Poc").Get<PocOptions>()!;
        Assert.True(options.DevelopmentWritesApproved);
        return options;
    }

    private static SaveOrderRequest Request(OrderResponse order, SchemaResponse schema, string? key = null)
    {
        Dictionary<string, string?> Fields(Dictionary<string, string?> values, string section) =>
            values.Where(p => schema.Fields.Any(f => f.Section == section && f.Key == p.Key))
                .ToDictionary(p => p.Key, p => p.Value);
        static List<Dictionary<string, string?>> Inputs(List<Dictionary<string, string?>> rows) =>
            rows.Select(row =>
            {
                var input = new Dictionary<string, string?> { ["SNo"] = row["SNo"] };
                if (row["PerEditable"] == "Y") input["Per"] = row["Per"];
                if (row["AmtEditable"] == "Y") input["Amount"] = row["Amount"];
                return input;
            }).ToList();
        return new SaveOrderRequest(order.MetadataVersion, order.Version, key ?? Guid.NewGuid().ToString(),
            Fields(order.Header, "header"), order.Lines.Select(line =>
            {
                var projected = Fields(line, "lines");
                if (order.Id is not null && line.GetValueOrDefault("V_SNo") is { } number && number != "0")
                    projected["V_SNo"] = number;
                return projected;
            }).ToList(), Inputs(order.HeaderExpenses), order.LineExpenses.Select(Inputs).ToList(),
            order.Terms.Select(t => t.GetValueOrDefault("LookupId") is not null
                ? new Dictionary<string, string?> { ["Code"] = t["Code"], ["ValueCode"] = t.GetValueOrDefault("ValueCode") }
                : new Dictionary<string, string?> { ["Code"] = t["Code"], ["Value"] = t["Value"] }).ToList());
    }

    private static async Task<(SessionResponse Session, SchemaResponse Schema, SaveOrderRequest Request)> Fixture(
        OrderService orders, IdentityService identity, MetadataService metadata)
    {
        var session = await identity.DescribeAsync("SA", "26", "HO", TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;
        var schema = await metadata.GetAsync(session, ct);
        var order = await orders.DefaultsAsync(session, ct);
        var template = await orders.LineDefaultsAsync(session, ct);
        var parties = await orders.LookupAsync(session, "parties", null, ct);
        var forms = await orders.LookupAsync(session, "forms", null, ct);
        var items = await orders.LookupAsync(session, "items", null, ct);
        var bf = await orders.LookupAsync(session, "bf", null, ct);
        var gsm = await orders.LookupAsync(session, "gsm", null, ct);
        Assert.NotEmpty(parties); Assert.NotEmpty(forms); Assert.NotEmpty(items); Assert.NotEmpty(bf); Assert.NotEmpty(gsm);
        foreach (var term in order.Terms.Where(t => t.GetValueOrDefault("LookupId") is not null))
        {
            var options = ApprovedOptions();
            var database = new Database(options);
            var lookup = new LookupService(database, [new SalesLookupAccessPolicy()], new MasterService(database, options));
            var page = await lookup.SearchAsync(session, term["LookupId"]!, null, 0, ct);
            term["ValueCode"] = page.Options.First().Key;
        }
        order.Header["PartyCode"] = parties[0]["code"]; order.Header["DelAt"] = parties[0]["code"];
        order.Header["FormCode"] = forms[0]["code"]; order.Header["PartyOrdNo"] = "WEBPOC-TEST";
        order.Header["RecId"] = "POC" + Guid.NewGuid().ToString("N")[..7];
        template.Values["ItemCode"] = items[0]["code"]; template.Values["BF"] = bf[0]["code"];
        template.Values["GSM"] = gsm[0]["code"]; template.Values["SizeReel"] = "20";
        template.Values["Qty"] = "100"; template.Values["Rate"] = "2";
        order.Lines.Add(template.Values); order.LineExpenses.Add(template.Expenses);
        return (session, schema, Request(order, schema));
    }

    [Fact]
    public async Task Configured_terms_lookup_honors_top_and_module_grants_with_no_committed_changes()
    {
        var (database, _, _, _) = Services();
        var ct = TestContext.Current.CancellationToken;
        var scriptPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "Techno.Paper.Api", "sql", "016-voucher-term-lookup-permissions.sql"));
        await using var connection = await database.OpenAsync(false, ct);
        await using var transaction = connection.BeginTransaction();
        var script = await File.ReadAllTextAsync(scriptPath, ct);
        await Database.ExecuteAsync(connection, transaction, script, ct);
        var bindings = new Dictionary<string, TermLookupSource>();
        var terms = await VoucherTerms.LoadAsync(connection, transaction, "SORD", ct, bindings, "sales-orders", "SORD");
        var term = Assert.Single(terms, row => row.GetValueOrDefault("Editor") == "lookup");
        Assert.False(term.ContainsKey("DMHGQuery"));
        var source = bindings[Assert.IsType<string>(term.GetValueOrDefault("LookupId"))];
        var configuredRows = Assert.Single(await Database.QueryAsync(connection, transaction,
            "SELECT DMHGQuery FROM dbo.Term_Cond_Det WHERE VType=@voucher AND Code=@code",
            ct, ("@voucher", "SORD"), ("@code", term["Code"])));
        var configured = Assert.IsType<VoucherTermLookup.Definition>(VoucherTermLookup.Parse(configuredRows["DMHGQuery"]));
        Assert.Equal(configured.Limit, source.Limit);
        if (configured.Limit is { } limit)
        {
            var count = Assert.Single(await Database.QueryAsync(connection, transaction,
                $"SELECT COUNT(*) AS LookupCount FROM {source.Rows} s", ct, ("@termLimit", limit)));
            Assert.InRange(int.Parse(count["LookupCount"]!, CultureInfo.InvariantCulture), 1, limit);
        }
        var query = new VoucherTermLookup.Definition("dbo", "ItemMast", "Code", "Name", 1, false, false);
        Assert.Null(await VoucherTermLookup.BindAsync(connection, transaction, query, "unrelated-test-resource", ct));

        var rows = await Database.QueryAsync(connection, transaction,
            "SELECT TOP(2) Code FROM dbo.ItemMast ORDER BY Code", ct);
        var capped = source with { Limit = 1, SortByLabel = false, Descending = false };
        var first = rows[0]["Code"]!;
        var second = rows[1]["Code"]!;
        Assert.Equal(first, (await VoucherTerms.ResolveAsync(connection, transaction, capped, first, ct)).Code);
        Assert.Null((await VoucherTerms.ResolveAsync(connection, transaction, capped, second, ct)).Code);
        Assert.Equal(second, (await VoucherTerms.ResolveAsync(connection, transaction, capped, second, ct, retained: true)).Code);
        await transaction.RollbackAsync(ct);
    }

    [Fact]
    public async Task Distance_value_help_and_order_validation_use_live_configuration_without_saving()
    {
        var (database, metadata, orders, identity) = Services();
        var (session, _, request) = await Fixture(orders, identity, metadata);
        var ct = TestContext.Current.CancellationToken;
        var lookups = new LookupService(database, [new SalesLookupAccessPolicy()], new MasterService(database, ApprovedOptions()));
        var draft = await orders.DefaultsAsync(session, ct);
        var definition = Assert.Single(draft.Terms, row => row.GetValueOrDefault("Editor") == "lookup");
        Assert.False(definition.ContainsKey("DMHGQuery"));
        var lookupId = Assert.IsType<string>(definition.GetValueOrDefault("LookupId"));
        var result = await lookups.SearchAsync(session, lookupId, null, 0, ct);
        Assert.Equal("Name", Assert.Single(result.Definition.Columns).Label);
        var option = result.Options[0];
        var resolved = await lookups.ResolveAsync(session, lookupId, new([option.Key]), ct);
        Assert.Equal(option.Label, Assert.Single(resolved.Options).Label);
        var input = Assert.Single(request.Terms, t => t.ContainsKey("ValueCode"));
        input["ValueCode"] = null;
        var missing = await Assert.ThrowsAsync<ApiFault>(() => orders.SaveAsync(session, null, request, true, ct));
        Assert.Equal("term_required", missing.Code);
        input["ValueCode"] = option.Key;
        var preview = await orders.SaveAsync(session, null, request, true, ct);
        var term = Assert.Single(preview.Terms, t => t.GetValueOrDefault("LookupId") == lookupId);
        Assert.Equal(option.Key, term["ValueCode"]);
        Assert.Equal(option.Label, term["Value"]?.Trim());
    }

    [Fact]
    public async Task Buyer_value_help_is_opt_in_and_persists_only_the_existing_party_key()
    {
        var (database, metadata, orders, identity) = Services();
        var (session, schema, request) = await Fixture(orders, identity, metadata);
        var ct = TestContext.Current.CancellationToken;
        var binding = Assert.Single(schema.Fields, field => field.Key == "PartyCode");
        Assert.Equal("PartyCode", binding.Key);
        var lookups = new LookupService(database, [new SalesLookupAccessPolicy()], new MasterService(database, ApprovedOptions()));
        var result = await lookups.SearchAsync(session, binding.ValueHelp!, null, 0, ct);
        var selected = result.Options[0];
        Assert.NotEqual(selected.Label, selected.Key);
        Assert.Equal("parties", result.Definition.Id);
        Assert.Equal(
            new[] { ("Name", "Party Name", 180), ("ManualCode", "Code", 70), ("Address", "Address", 250),
                ("City", "City", 100), ("DName", "Distributor", 200), ("DISCity", "D.City", 100) },
            result.Definition.Columns.Select(column => (column.Key, column.Label, column.Width)));
        Assert.DoesNotContain("Code", selected.Cells.Keys);
        await using (var connection = await database.OpenAsync(false, ct))
        {
            var source = Assert.Single(await Database.QueryAsync(connection, null,
                """
                SELECT ISNULL(p.Add1,'')+ISNULL(p.Add2,'') AS Address,
                       ISNULL(d.Name,'') AS Distributor,ISNULL(c.CityName,'') AS DistributorCity
                FROM dbo.SubGroup p LEFT JOIN dbo.SubGroup d ON d.SubCode=p.Distributor
                LEFT JOIN dbo.City c ON c.CityCode=d.CityCode WHERE p.SubCode=@code
                """, ct, ("@code", selected.Key)));
            Assert.Equal(source["Address"], selected.Cells["Address"]);
            Assert.Equal(source["Distributor"], selected.Cells["DName"]);
            Assert.Equal(source["DistributorCity"], selected.Cells["DISCity"]);
        }
        var resolved = await lookups.ResolveAsync(session, "parties", new([selected.Key]), ct);
        Assert.Equal(selected.Label, Assert.Single(resolved.Options).Label);
        request.Header["PartyCode"] = selected.Key;
        try
        {
            var saved = await orders.SaveAsync(session, null, request, false, ct);
            Assert.Equal(selected.Key, saved.Header["PartyCode"]);
            var updated = Request(saved, schema);
            updated.Header["PartyCode"] = selected.Label;
            var fault = await Assert.ThrowsAsync<ApiFault>(() => orders.SaveAsync(session, saved.Id, updated, true, ct));
            Assert.Equal(422, fault.Status);
            Assert.Equal(selected.Key, (await orders.GetAsync(session, saved.Id!, ct)).Header["PartyCode"]);
        }
        finally { await CleanupAsync(database, request.Header["RecId"]!, session); }
    }

    [Fact]
    public async Task All_existing_sales_lookups_use_Windows_columns_and_resolve_the_existing_codes()
    {
        var (database, metadata, orders, identity) = Services();
        var ct = TestContext.Current.CancellationToken;
        var session = await identity.DescribeAsync("SA", "26", "HO", ct);
        var schema = await metadata.GetAsync(session, ct);
        var service = new LookupService(database, [new SalesLookupAccessPolicy()], new MasterService(database, ApprovedOptions()));
        var expected = new[] {
            ("header", "PartyCode", "parties"), ("header", "Consignee", "consignees"),
            ("header", "DelAt", "delivery-parties"), ("header", "FormCode", "forms"),
            ("header", "OrderType", "order-types"), ("header", "Exciseable", "yes-no"),
            ("lines", "ItemCode", "items"), ("lines", "BF", "bf"), ("lines", "GSM", "gsm"),
            ("lines", "SizeUnit", "size-units"), ("lines", "Grain", "grain")
        };
        Assert.Equal(expected.Length, schema.Fields.Count(field => field.ValueHelp is not null));
        foreach (var (section, key, id) in expected)
        {
            var field = Assert.Single(schema.Fields, field => field.Section == section && field.Key == key);
            Assert.Equal(id, field.ValueHelp);
            var previous = await orders.LookupAsync(session, field.Lookup!, null, ct);
            Assert.NotEmpty(previous);
            var code = previous[0]["code"]!;
            var resolved = await service.ResolveAsync(session, id, new([code]), ct);
            Assert.Empty(resolved.MissingKeys);
            var option = Assert.Single(resolved.Options);
            Assert.Equal(code, option.Key);
            Assert.Equal(previous[0]["label"]!.Trim(), option.Label);
            Assert.DoesNotContain("Code", option.Cells.Keys);
        }
        var items = await service.SearchAsync(session, "items", null, 0, ct);
        Assert.Equal(
            new[] { ("Name", 200), ("Code", 175), ("SKU", 80), ("Varity", 140), ("GSM", 100),
                ("Length", 100), ("Width", 100), ("BFName", 60), ("Grain", 40) },
            items.Definition.Columns.Select(column => (column.Label, column.Width)));
        var selected = items.Options[0];
        await using var connection = await database.OpenAsync(false, ct);
        var source = Assert.Single(await Database.QueryAsync(connection, null,
            """
            SELECT i.ItemType,i.ManualCode,i.SKU,v.Name AS Varity,g.Name AS GSM,b.Name AS BFName
            FROM dbo.ItemMast i LEFT JOIN dbo.VarityMast v ON v.Code=i.Varity
            LEFT JOIN dbo.GSMMast g ON g.Code=i.GSM LEFT JOIN dbo.BFMast b ON b.Code=i.BFCode
            WHERE i.Code=@code
            """, ct, ("@code", selected.Key)));
        Assert.Equal("FM", source["ItemType"]);
        foreach (var key in new[] { "ManualCode", "SKU", "Varity", "GSM", "BFName" })
            Assert.Equal(source[key], selected.Cells[key]);
        var nonFinished = await Database.QueryAsync(connection, null,
            "SELECT TOP(1) Code FROM dbo.ItemMast WHERE ItemType<>'FM' ORDER BY Code", ct);
        Assert.NotEmpty(nonFinished);
        var rejected = await service.ResolveAsync(session, "items", new([nonFinished[0]["Code"]!]), ct);
        Assert.Empty(rejected.Options);
        Assert.Single(rejected.MissingKeys);
    }

    [Fact]
    public async Task Sql_lookup_configuration_controls_columns_labels_paging_and_exact_scoped_resolution()
    {
        var (database, _, _, identity) = Services();
        var ct = TestContext.Current.CancellationToken;
        var session = await identity.DescribeAsync("SA", "26", "HO", ct);
        var service = new LookupService(database, [new SalesLookupAccessPolicy()], new MasterService(database, ApprovedOptions()));
        var suffix = Guid.NewGuid().ToString("N");
        var id = "lookup-probe-" + suffix;
        var view = "LookupProbe" + suffix;
        await using var connection = await database.OpenAsync(false, ct);
        var data = string.Join(" UNION ALL ", Enumerable.Range(1, 25).Select(index =>
            $"SELECT CAST('K{index:00}' AS varchar(10)) AS Code,CAST('Same name' AS varchar(100)) AS Name," +
            $"CAST({(index == 1 ? "NULL" : "'Town'")} AS varchar(25)) AS City," +
            $"CAST({(index == 1 ? "NULL" : index == 2 ? "'|D1|'" : "'|HO|'")} AS varchar(500)) AS SiteList"));
        try
        {
            await Database.ExecuteAsync(connection, null, "EXEC sys.sp_executesql @ddl", ct,
                ("@ddl", $"CREATE VIEW webpoc.[{view}] AS {data}"));
            await Database.ExecuteAsync(connection, null,
                """
                INSERT webpoc.LookupDefinition(Id,Resource,Title,SourceView,KeyColumn,ScopeMode,ScopeColumn,PageSize)
                VALUES(@id,'sales-orders','Probe',@view,'Code','list','SiteList',10);
                INSERT webpoc.LookupColumn(LookupId,ColumnName,Caption,Position,Width,Visible,Searchable,LabelOrder)
                VALUES(@id,'Name','Name',1,180,1,1,1),(@id,'City','City',2,100,1,1,NULL),(@id,'Code','Code',3,100,1,1,NULL);
                """, ct, ("@id", id), ("@view", view));
            var first = await service.SearchAsync(session, id, null, 0, ct);
            var second = await service.SearchAsync(session, id, null, 1, ct);
            Assert.Equal(10, first.Options.Count); Assert.True(first.HasMore);
            Assert.Equal("K01", first.Options[0].Key);
            Assert.Equal(first.Options.Select(option => option.Key),
                (await service.SearchAsync(session, id, null, 0, ct)).Options.Select(option => option.Key));
            Assert.Empty(first.Options.Select(option => option.Key).Intersect(second.Options.Select(option => option.Key)));
            var resolved = await service.ResolveAsync(session, id, new(["K25", "K01", "k03", "K02"]), ct);
            Assert.Equal(3, resolved.Options.Count);
            Assert.Contains(resolved.Options, option => option.Key == "k03");
            Assert.Equal("K02", Assert.Single(resolved.MissingKeys));
            var otherSite = await service.ResolveAsync(session with { Site = "D1", ReadOnly = true }, id, new(["K02", "K25"]), ct);
            Assert.Equal("K02", Assert.Single(otherSite.Options).Key);
            Assert.Equal("K25", Assert.Single(otherSite.MissingKeys));
            Assert.Empty((await service.SearchAsync(session, id, "' OR 1=1--", 0, ct)).Options);
            Assert.Equal(403, (await Assert.ThrowsAsync<ApiFault>(() => service.SearchAsync(
                session with { Capabilities = session.Capabilities with { View = false } }, id, null, 0, ct))).Status);
            await Database.ExecuteAsync(connection, null,
                "UPDATE webpoc.LookupColumn SET LabelOrder=2 WHERE LookupId=@id AND ColumnName='City'; UPDATE webpoc.LookupColumn SET Caption='Party description',Width=240 WHERE LookupId=@id AND ColumnName='Name';",
                ct, ("@id", id));
            var changed = await service.SearchAsync(session, id, null, 0, ct);
            Assert.NotEqual(first.Definition.Version, changed.Definition.Version);
            Assert.Equal("Party description", changed.Definition.Columns[0].Label);
            Assert.Equal("Same name", changed.Options[0].Label);
            Assert.Equal("Same name - Town", changed.Options[1].Label);
            await Database.ExecuteAsync(connection, null, "UPDATE webpoc.LookupDefinition SET SourceView='dbo.SubGroup; invalid' WHERE Id=@id", ct, ("@id", id));
            Assert.Equal("lookup_configuration", (await Assert.ThrowsAsync<ApiFault>(() =>
                service.DefinitionAsync(session, id, ct))).Code);
        }
        finally
        {
            await Database.ExecuteAsync(connection, null,
                "DELETE webpoc.LookupColumn WHERE LookupId=@id; DELETE webpoc.LookupDefinition WHERE Id=@id;", ct, ("@id", id));
            await Database.ExecuteAsync(connection, null, $"DROP VIEW IF EXISTS webpoc.[{view}]", ct);
        }
    }

    [Fact]
    public async Task Voucher_selection_loads_a_preview_template_but_does_not_widen_save_scope()
    {
        var (_, metadata, orders, identity) = Services();
        var (session, _, request) = await Fixture(orders, identity, metadata);
        var ct = TestContext.Current.CancellationToken;
        var types = await orders.VoucherTypesAsync(session, ct);
        Assert.Contains(types, voucher => voucher.Code == "SORD" && voucher.CanSave);
        Assert.Contains(types, voucher => voucher.Code == "SODRL" && !voucher.CanSave);
        var defaults = await orders.DefaultsAsync(session, ct, "SODRL");
        var line = await orders.LineDefaultsAsync(session, ct, "SODRL");
        Assert.Equal("SODRL", defaults.Header["V_Type"]);
        Assert.NotEmpty(defaults.HeaderExpenses);
        Assert.NotEmpty(line.Expenses);
        Assert.All(defaults.HeaderExpenses.Concat(line.Expenses), row => Assert.Equal("SODRL", row["VType"]?.Trim()));
        var calculated = await orders.CalculateDraftAsync(session,
            new(request.MetadataVersion, request.Lines, request.HeaderExpenses, request.LineExpenses, VoucherType: "SODRL"), ct);
        Assert.All(calculated.LineExpenses.SelectMany(expenses => expenses), row => Assert.Equal("SODRL", row["VType"]?.Trim()));
        Assert.Equal(defaults.Numbers, (await orders.DefaultsAsync(session, ct, "SODRL")).Numbers);
        Assert.Equal(422, (await Assert.ThrowsAsync<ApiFault>(() => orders.DefaultsAsync(session, ct, "L-GST"))).Status);
        Assert.Equal(422, (await Assert.ThrowsAsync<ApiFault>(() =>
            orders.SaveAsync(session, null, request with { VoucherType = "SODRL" }, false, ct))).Status);
    }

    [Fact]
    public async Task Voucher_expense_reader_loads_distinct_live_voucher_templates()
    {
        var (database, _, _, _) = Services();
        var ct = TestContext.Current.CancellationToken;
        await using var connection = await database.OpenAsync(false, ct);
        var orderTypes = new[] { "SORD", "SODRL" };
        foreach (var voucher in orderTypes)
        {
            var header = await VoucherExpenseStore.LoadTemplateAsync(connection, null, voucher, false, ct);
            var lines = await VoucherExpenseStore.LoadTemplateAsync(connection, null, voucher, true, ct);
            Assert.NotEmpty(header);
            Assert.NotEmpty(lines);
            Assert.All(header.Concat(lines), row =>
                Assert.Equal(voucher, row.GetValueOrDefault("VType")?.Trim()));
        }
        var gst = await VoucherExpenseStore.LoadTemplateAsync(connection, null, "L-GST", true, ct);
        Assert.Contains(gst, row => row["ExpShortCode"]?.Trim() == "CGST" && row["Amt_SaveIn"]?.Trim() == "CGSTAmt");

        var fixture = Assert.Single(await Database.QueryAsync(connection, null,
            """
            SELECT TOP (1) i.Code AS ItemCode,d.ExpCode,d.Per
            FROM dbo.ItemMast i JOIN dbo.TaxBandDet d ON d.Code=i.TaxBandCode
            JOIN dbo.ExpenseLine e ON e.ExpCode=d.ExpCode AND e.VType='L-GST'
            WHERE i.ItemType='FM' AND d.Per>0
              AND (NULLIF(i.SiteList,'') IS NULL OR CHARINDEX('|HO|','|' + i.SiteList + '|')>0)
            ORDER BY i.Code,d.ExpCode
            """, ct));
        var defaults = await VoucherExpenseStore.LoadItemDefaultsAsync(connection, null, fixture["ItemCode"]!, "HO", ct);
        var taxed = VoucherExpenseStore.ApplyItemDefaults(gst, defaults);
        var matching = Assert.Single(taxed, row => row["ExpCode"] == fixture["ExpCode"]);
        Assert.Equal(ExpenseCalculator.Round(ExpenseCalculator.Number(fixture, "Per"), 3), ExpenseCalculator.Number(matching, "Per"));
        var order = await VoucherExpenseStore.LoadTemplateAsync(connection, null, "SORD", true, ct);
        var unchanged = VoucherExpenseStore.ApplyItemDefaults(order, defaults);
        Assert.Equal(order, unchanged);
    }

    [Fact]
    public async Task Incomplete_draft_calculation_matches_full_preview_without_reserving_numbers()
    {
        var (_, metadata, orders, identity) = Services();
        var (session, _, request) = await Fixture(orders, identity, metadata);
        var ct = TestContext.Current.CancellationToken;
        var before = await orders.DefaultsAsync(session, ct);
        var full = await orders.SaveAsync(session, null, request, true, ct);
        var partial = new DraftCalculationRequest(request.MetadataVersion, request.Lines, request.HeaderExpenses, request.LineExpenses);
        var result = await orders.CalculateDraftAsync(session, partial, ct);
        Assert.Equal(full.Lines[0]["Amount"], result.Lines[0]["Amount"]);
        Assert.Equal(full.Header["GAmount"], result.Header["GAmount"]);
        Assert.DoesNotContain("DocId", result.Lines[0].Keys);
        Assert.Equal(before.Numbers, (await orders.DefaultsAsync(session, ct)).Numbers);
        partial.Lines[0]["ItemCode"] = null; partial.Lines[0]["BF"] = null; partial.Lines[0]["GSM"] = null;
        partial.Lines[0]["SizeReel"] = "0"; partial.Lines[0]["Qty"] = "0";
        var incomplete = await orders.CalculateDraftAsync(session, partial, ct);
        Assert.Equal(0m, ExpenseCalculator.Number(incomplete.Lines[0], "Amount"));
        Assert.Equal(409, (await Assert.ThrowsAsync<ApiFault>(() =>
            orders.CalculateDraftAsync(session, partial with { MetadataVersion = "stale" }, ct))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiFault>(() =>
            orders.CalculateDraftAsync(session with { ReadOnly = true }, partial, ct))).Status);
    }

    [Fact]
    public async Task Nonunit_secondary_conversion_and_tolerance_are_shared_by_preview_and_save()
    {
        var (database, metadata, orders, identity) = Services();
        var (session, schema, request) = await Fixture(orders, identity, metadata);
        var ct = TestContext.Current.CancellationToken;
        await using (var connection = await database.OpenAsync(false, ct))
        {
            var items = await Database.QueryAsync(connection, null,
                """
                SELECT TOP(1) Code,ConversionRate FROM dbo.ItemMast WHERE ItemType='FM' AND ConversionRate>0 AND ConversionRate<>1
                  AND (NULLIF(SiteList,'') IS NULL OR CHARINDEX('|' + @site + '|','|' + SiteList + '|')>0)
                """, ct, ("@site", session.Site));
            Assert.NotEmpty(items);
            request.Lines[0]["ItemCode"] = items[0]["Code"];
            request.Lines[0]["QtySDU"] = "3";
            request.Lines[0]["Rate"] = "2.125";
            request.Lines[0]["Tl_ExcessQty"] = "0.125"; request.Lines[0]["TL_ShortageQty"] = "0.5";
            var result = await orders.CalculateDraftAsync(session, new(request.MetadataVersion, request.Lines, request.HeaderExpenses, request.LineExpenses), ct);
            var expected = 3m * ExpenseCalculator.Number(items[0], "ConversionRate");
            Assert.Equal(expected, ExpenseCalculator.Number(result.Lines[0], "Qty"));
            Assert.Equal(ExpenseCalculator.Round(expected * 2.125m, 2), ExpenseCalculator.Number(result.Lines[0], "Amount"));
        }
        try
        {
            var preview = await orders.SaveAsync(session, null, request, true, ct);
            var saved = await orders.SaveAsync(session, null, request, false, ct);
            Assert.Equal(preview.Lines[0]["Qty"], saved.Lines[0]["Qty"]);
            Assert.Equal(preview.Lines[0]["Amount"], saved.Lines[0]["Amount"]);
            Assert.Equal(0.125m, ExpenseCalculator.Number(saved.Lines[0], "Tl_ExcessQty"));
            var invalid = Request(saved, schema);
            invalid.Lines[0]["TL_ShortageQty"] = "999999999";
            var failure = await Assert.ThrowsAsync<ApiFault>(() => orders.SaveAsync(session, saved.Id, invalid, false, ct));
            Assert.Equal("invalid_tolerance", failure.Code);
        }
        finally { await CleanupAsync(database, request.Header["RecId"]!, session); }
    }

    [Fact]
    public async Task Create_calculate_retry_edit_conflict_delete_roundtrip()
    {
        var (database, metadata, orders, identity) = Services();
        var (session, schema, request) = await Fixture(orders, identity, metadata);
        var ct = TestContext.Current.CancellationToken;
        string? id = null;
        try
        {
            var preview = await orders.SaveAsync(session, null, request, true, ct);
            Assert.Equal(200m, ExpenseCalculator.Number(preview.Header, "GAmount"));
            Assert.Equal(0m, ExpenseCalculator.Number(preview.Header, "NAmount"));
            var created = await orders.SaveAsync(session, null, request, false, ct);
            id = created.Id;
            Assert.NotNull(id); Assert.Equal(21, id.Length);
            Assert.Equal(request.Header["RecId"], created.Header["RecId"]);
            Assert.Equal(200m, ExpenseCalculator.Number(created.Header, "GAmount"));
            Assert.True((await orders.ListAsync(session, request.Header["RecId"], 0, ct)).Count <= 25);
            var retry = await orders.SaveAsync(session, null, request, false, ct);
            Assert.Equal(id, retry.Id);
            var update = Request(created, schema);
            update.Header["PartyOrdNo"] = "WEBPOC-EDIT";
            var edited = await orders.SaveAsync(session, id, update, false, ct);
            Assert.Equal("WEBPOC-EDIT", edited.Header["PartyOrdNo"]);
            Assert.NotEqual(created.Version, edited.Version);
            var stale = update with { IdempotencyKey = Guid.NewGuid().ToString() };
            Assert.Equal(409, (await Assert.ThrowsAsync<ApiFault>(() => orders.SaveAsync(session, id, stale, false, ct))).Status);
            await orders.DeleteAsync(session, id, new DeleteOrderRequest(edited.Version!), ct);
            Assert.Equal(404, (await Assert.ThrowsAsync<ApiFault>(() => orders.GetAsync(session, id, ct))).Status);
            id = null;
        }
        finally { await CleanupAsync(database, request.Header["RecId"]!, session); }
    }

    [Fact]
    public async Task Add_previews_do_not_reserve_and_stale_automatic_references_allocate_on_save()
    {
        var (database, metadata, orders, identity) = Services();
        var (session, _, fixture) = await Fixture(orders, identity, metadata);
        var ct = TestContext.Current.CancellationToken;
        var firstDefaults = await orders.DefaultsAsync(session, ct);
        var secondDefaults = await orders.DefaultsAsync(session, ct);
        Assert.NotNull(firstDefaults.Numbers);
        Assert.Equal(firstDefaults.Numbers, secondDefaults.Numbers);
        Assert.Equal(firstDefaults.Numbers.Reference, firstDefaults.Header["RecId"]);
        Assert.Null(firstDefaults.Id);
        var request = fixture with
        {
            Header = new Dictionary<string, string?>(fixture.Header) { ["RecId"] = firstDefaults.Numbers.Reference },
            AutomaticReference = true
        };
        var references = new List<string>();
        try
        {
            var calculated = await orders.SaveAsync(session, null, request, true, ct);
            Assert.Null(calculated.Id);
            Assert.Equal(firstDefaults.Numbers, calculated.Numbers);
            Assert.Equal(firstDefaults.Numbers, (await orders.DefaultsAsync(session, ct)).Numbers);
            var first = await orders.SaveAsync(session, null, request, false, ct);
            references.Add(first.Header["RecId"]!);
            Assert.Equal(firstDefaults.Numbers.VoucherNumber, first.Header["V_No"]);
            var second = await orders.SaveAsync(session, null,
                request with { IdempotencyKey = Guid.NewGuid().ToString() }, false, ct);
            references.Add(second.Header["RecId"]!);
            Assert.NotEqual(first.Id, second.Id);
            Assert.Equal(long.Parse(first.Header["V_No"]!, CultureInfo.InvariantCulture) + 1,
                long.Parse(second.Header["V_No"]!, CultureInfo.InvariantCulture));
            Assert.Equal(long.Parse(first.Header["RecId"]!, CultureInfo.InvariantCulture) + 1,
                long.Parse(second.Header["RecId"]!, CultureInfo.InvariantCulture));
            Assert.NotEqual(request.Header["RecId"], second.Header["RecId"]);
            var retry = await orders.SaveAsync(session, null, request, false, ct);
            Assert.Equal(first.Id, retry.Id);
            Assert.Equal(first.Header["RecId"], retry.Header["RecId"]);
            Assert.Equal(409, (await Assert.ThrowsAsync<ApiFault>(() => orders.SaveAsync(session, null,
                request with { AutomaticReference = false, IdempotencyKey = Guid.NewGuid().ToString() }, false, ct))).Status);
        }
        finally
        {
            foreach (var reference in references) await CleanupAsync(database, reference, session);
        }
    }

    [Fact]
    public async Task Finished_product_lookup_and_new_item_validation_match_the_item_master()
    {
        var (database, metadata, orders, identity) = Services();
        var (session, _, request) = await Fixture(orders, identity, metadata);
        var ct = TestContext.Current.CancellationToken;
        await using var connection = await database.OpenAsync(false, ct);
        var eligible = await Database.QueryAsync(connection, null,
            """
            SELECT TOP(50) Code AS code,Name AS label FROM dbo.ItemMast
            WHERE ItemType='FM' AND (NULLIF(SiteList,'') IS NULL OR CHARINDEX('|' + @site + '|','|' + SiteList + '|')>0)
            ORDER BY Name
            """, ct, ("@site", session.Site));
        var items = await orders.LookupAsync(session, "items", null, ct);
        Assert.NotEmpty(eligible);
        Assert.Equal(eligible.Select(row => row["code"]), items.Select(row => row["code"]));
        Assert.Equal(eligible.Select(row => row["code"]).Where(code => code!.Contains("HO5135")),
            (await orders.LookupAsync(session, "items", "HO5135", ct)).Select(row => row["code"]));
        Assert.Empty(await orders.LookupAsync(session, "items", "HO5926", ct));
        request.Lines[0]["ItemCode"] = "HO5926";
        foreach (var preview in new[] { true, false })
        {
            var failure = await Assert.ThrowsAsync<ApiFault>(() => orders.SaveAsync(session, null, request, preview, ct));
            Assert.Equal("finished_product_required", failure.Code);
            Assert.Contains("lines[0].ItemCode", failure.Errors!.Keys);
        }
    }

    [Fact]
    public async Task Finished_product_rule_preserves_only_the_same_item_on_an_existing_line()
    {
        var (database, metadata, orders, identity) = Services();
        var (session, schema, request) = await Fixture(orders, identity, metadata);
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var created = await orders.SaveAsync(session, null, request, false, ct);
            // Simulate a historical order only on this test's exact marked aggregate.
            await using (var connection = await database.OpenAsync(false, ct))
                Assert.Equal(1, await Database.ExecuteAsync(connection, null,
                    """
                    UPDATE d SET ItemCode='HO5926' FROM dbo.SOrdDet d
                    JOIN dbo.SOrd s ON s.DocId=d.DocId
                    WHERE d.DocId=@id AND s.RecId=@reference AND s.PartyOrdNo='WEBPOC-TEST' AND s.PreparedBy=@user
                    """, ct, ("@id", created.Id), ("@reference", request.Header["RecId"]), ("@user", session.Username)));
            var historical = await orders.GetAsync(session, created.Id!, ct);
            var unchanged = Request(historical, schema);
            unchanged.Lines[0]["Qty"] = "200";
            await orders.SaveAsync(session, historical.Id, unchanged, true, ct);
            var saved = await orders.SaveAsync(session, historical.Id, unchanged, false, ct);
            Assert.Equal("HO5926", saved.Lines[0]["ItemCode"]);
            var replaced = Request(saved, schema);
            replaced.Lines[0]["ItemCode"] = "HO5928";
            var replacementFailure = await Assert.ThrowsAsync<ApiFault>(() => orders.SaveAsync(session, saved.Id, replaced, true, ct));
            Assert.Equal("finished_product_required", replacementFailure.Code);
            var copied = Request(saved, schema);
            copied.Lines.Add(copied.Lines[0].Where(p => p.Key != "V_SNo").ToDictionary(p => p.Key, p => p.Value));
            copied.LineExpenses.Add(copied.LineExpenses[0].Select(row => new Dictionary<string, string?>(row)).ToList());
            var copiedFailure = await Assert.ThrowsAsync<ApiFault>(() => orders.SaveAsync(session, saved.Id, copied, true, ct));
            Assert.Equal("finished_product_required", copiedFailure.Code);
            Assert.Contains("lines[1].ItemCode", copiedFailure.Errors!.Keys);
            var finished = Request(saved, schema);
            finished.Lines[0]["ItemCode"] = request.Lines[0]["ItemCode"];
            var updated = await orders.SaveAsync(session, saved.Id, finished, false, ct);
            Assert.Equal(request.Lines[0]["ItemCode"], updated.Lines[0]["ItemCode"]);
        }
        finally { await CleanupAsync(database, request.Header["RecId"]!, session); }
    }

    [Fact]
    public async Task Conversion_and_required_field_errors_identify_the_correct_fields_and_row()
    {
        var (_, metadata, orders, identity) = Services();
        var (session, _, request) = await Fixture(orders, identity, metadata);
        var ct = TestContext.Current.CancellationToken;
        request.Header["PartyOrdNo"] = null;
        var required = await Assert.ThrowsAsync<ApiFault>(() => orders.SaveAsync(session, null, request, true, ct));
        Assert.Contains("header.PartyOrdNo", required.Errors!.Keys);
        request.Header["PartyOrdNo"] = "WEBPOC-TEST";
        var secondLine = new Dictionary<string, string?>(request.Lines[0]) { ["ItemCode"] = "HO5135", ["QtySDU"] = "100" };
        request.Lines.Add(secondLine);
        request.LineExpenses.Add(request.LineExpenses[0].Select(row => new Dictionary<string, string?>(row)).ToList());
        var conversion = await Assert.ThrowsAsync<ApiFault>(() => orders.SaveAsync(session, null, request, true, ct));
        Assert.Equal("invalid_conversion", conversion.Code);
        Assert.Contains("lines[1].ItemCode", conversion.Errors!.Keys);
        Assert.Contains("lines[1].QtySDU", conversion.Errors.Keys);
        Assert.Contains("Row 2", conversion.Message);
        Assert.Contains("set Secondary weight to 0", conversion.Message);
    }

    [Theory]
    [InlineData("header")]
    [InlineData("details")]
    [InlineData("expenses")]
    [InlineData("terms")]
    [InlineData("tasks")]
    public async Task Every_write_stage_rolls_back(string stage)
    {
        var (database, metadata, orders, identity) = Services(new FailingCheckpoint(stage));
        var (session, _, request) = await Fixture(orders, identity, metadata);
        var ct = TestContext.Current.CancellationToken;
        try
        {
            await Assert.ThrowsAsync<InjectedFailure>(() => orders.SaveAsync(session, null, request, false, ct));
            await using var connection = await database.OpenAsync(false, ct);
            var rows = await Database.QueryAsync(connection, null,
                "SELECT DocId FROM dbo.SOrd WHERE RecId=@reference", ct, ("@reference", request.Header["RecId"]));
            Assert.Empty(rows);
            var tasks = await Database.QueryAsync(connection, null,
                "SELECT Id FROM webpoc.erpTaskInstance WHERE Title=@title", ct,
                ("@title", $"SORD {request.Header["RecId"]} created"));
            Assert.Empty(tasks);
        }
        finally { await CleanupAsync(database, request.Header["RecId"]!, session); }
    }

    [Fact]
    public async Task Delete_failure_restores_the_complete_aggregate()
    {
        var (database, metadata, orders, identity) = Services(new FailingCheckpoint("delete"));
        var (session, _, request) = await Fixture(orders, identity, metadata);
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var saved = await orders.SaveAsync(session, null, request, false, ct);
            await Assert.ThrowsAsync<InjectedFailure>(() =>
                orders.DeleteAsync(session, saved.Id!, new(saved.Version!), ct));
            var restored = await orders.GetAsync(session, saved.Id!, ct);
            Assert.Equal(saved.Version, restored.Version);
            Assert.Equal(saved.Lines.Count, restored.Lines.Count);
            Assert.Equal(saved.Terms.Count, restored.Terms.Count);
            Assert.Equal(saved.HeaderExpenses.Count, restored.HeaderExpenses.Count);
        }
        finally { await CleanupAsync(database, request.Header["RecId"]!, session); }
    }

    [Fact]
    public async Task Twenty_concurrent_creates_have_distinct_legacy_numbers()
    {
        var (database, metadata, orders, identity) = Services();
        var (session, _, template) = await Fixture(orders, identity, metadata);
        var ct = TestContext.Current.CancellationToken;
        var requests = Enumerable.Range(0, 20).Select(_ => template with
        {
            IdempotencyKey = Guid.NewGuid().ToString(),
            Header = new Dictionary<string, string?>(template.Header) { ["RecId"] = "POC" + Guid.NewGuid().ToString("N")[..7] }
        }).ToArray();
        try
        {
            var created = await Task.WhenAll(requests.Select(request => orders.SaveAsync(session, null, request, false, ct)));
            Assert.Equal(20, created.Select(o => o.Id).Distinct().Count());
            Assert.All(created, order => Assert.Equal(21, order.Id!.Length));
        }
        finally
        {
            foreach (var request in requests) await CleanupAsync(database, request.Header["RecId"]!, session);
        }
    }

    [Fact]
    public async Task Activated_header_and_line_fields_roundtrip_and_preserve_hidden_values()
    {
        var (database, metadata, orders, identity) = Services();
        var ct = TestContext.Current.CancellationToken;
        var session = await identity.DescribeAsync("SA", "26", "HO", ct);
        Assert.Contains(await metadata.DiscoverAsync(ct), c => c.Name == "WebPocReference");
        Assert.Contains(await metadata.DiscoverAsync(ct), c => c.Name == "WebPocMeasure");
        await metadata.ActivateAsync(session, new("header", "WebPocReference", "Web reference", "A"), ct);
        await metadata.ActivateAsync(session, new("lines", "WebPocMeasure", "Web measurement", "A"), ct);
        var (_, schema, request) = await Fixture(orders, identity, metadata);
        Assert.True(request.Header.ContainsKey("WebPocReference"));
        Assert.True(request.Lines[0].ContainsKey("WebPocMeasure"));
        request.Header["WebPocReference"] = "Generic field round-trip";
        request.Lines[0]["WebPocMeasure"] = "1234.125";
        try
        {
            var created = await orders.SaveAsync(session, null, request, false, ct);
            Assert.Equal("Generic field round-trip", created.Header["WebPocReference"]);
            Assert.Equal(1234.125m, ExpenseCalculator.Number(created.Lines[0], "WebPocMeasure"));
            await metadata.ActivateAsync(session, new("header", "WebPocReference", "Web reference", "M"), ct);
            await metadata.ActivateAsync(session, new("lines", "WebPocMeasure", "Web measurement", "M"), ct);
            var restricted = session with { Capabilities = session.Capabilities with { MetadataAdministration = false } };
            var restrictedSchema = await metadata.GetAsync(restricted, ct);
            var hidden = await orders.GetAsync(restricted, created.Id!, ct);
            Assert.False(hidden.Header.ContainsKey("WebPocReference"));
            Assert.False(hidden.Lines[0].ContainsKey("WebPocMeasure"));
            var permittedUpdate = Request(hidden, restrictedSchema);
            permittedUpdate.Header["PartyOrdNo"] = "WEBPOC-EDIT";
            await orders.SaveAsync(restricted, created.Id, permittedUpdate, false, ct);
            var preserved = await orders.GetAsync(session, created.Id!, ct);
            Assert.Equal("Generic field round-trip", preserved.Header["WebPocReference"]);
            Assert.Equal(1234.125m, ExpenseCalculator.Number(preserved.Lines[0], "WebPocMeasure"));
            var forbidden = Request(hidden, restrictedSchema);
            forbidden.Header["WebPocReference"] = "forged";
            Assert.Equal(422, (await Assert.ThrowsAsync<ApiFault>(() =>
                orders.SaveAsync(restricted, created.Id, forbidden, false, ct))).Status);
            await metadata.ActivateAsync(session, new("header", "WebPocReference", "Web reference", "A"), ct);
            await metadata.ActivateAsync(session, new("lines", "WebPocMeasure", "Web measurement", "A"), ct);
            var latest = await orders.GetAsync(session, created.Id!, ct);
            schema = await metadata.GetAsync(session, ct);
            var clear = Request(latest, schema);
            clear.Header["WebPocReference"] = null; clear.Lines[0]["WebPocMeasure"] = null;
            var cleared = await orders.SaveAsync(session, latest.Id, clear, false, ct);
            Assert.Null(cleared.Header["WebPocReference"]); Assert.Null(cleared.Lines[0]["WebPocMeasure"]);
            await orders.DeleteAsync(session, cleared.Id!, new(cleared.Version!), ct);
        }
        finally { await CleanupAsync(database, request.Header["RecId"]!, session); }
    }

    [Fact]
    public async Task Deleted_document_numbers_are_not_reused_by_later_requests()
    {
        var (database, metadata, orders, identity) = Services();
        var (session, _, request) = await Fixture(orders, identity, metadata);
        var second = request with
        {
            IdempotencyKey = Guid.NewGuid().ToString(),
            Header = new(request.Header) { ["RecId"] = "POC" + Guid.NewGuid().ToString("N")[..7] }
        };
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var first = await orders.SaveAsync(session, null, request, false, ct);
            await orders.DeleteAsync(session, first.Id!, new(first.Version!), ct);
            var next = await orders.SaveAsync(session, null, second, false, ct);
            Assert.NotEqual(first.Id, next.Id);
            var failure = await Assert.ThrowsAsync<ApiFault>(() => orders.SaveAsync(session, null, request, false, ct));
            Assert.Equal("idempotency_deleted", failure.Code);
            await orders.DeleteAsync(session, next.Id!, new(next.Version!), ct);
        }
        finally
        {
            await CleanupAsync(database, request.Header["RecId"]!, session);
            await CleanupAsync(database, second.Header["RecId"]!, session);
        }
    }

    [Fact]
    public async Task Missing_approved_column_is_an_explicit_schema_conflict()
    {
        var (database, metadata, _, identity) = Services();
        var ct = TestContext.Current.CancellationToken;
        var session = await identity.DescribeAsync("SA", "26", "HO", ct);
        var column = "WebPocMissing_" + Guid.NewGuid().ToString("N");
        await using var connection = await database.OpenAsync(false, ct);
        try
        {
            await Database.ExecuteAsync(connection, null,
                "INSERT webpoc.Field(Section,ColumnName,Label,Permission) VALUES('header',@column,'POC schema regression','A')",
                ct, ("@column", column));
            var failure = await Assert.ThrowsAsync<ApiFault>(() => metadata.GetAsync(session, ct));
            Assert.Equal(409, failure.Status);
            Assert.Equal("active_field_missing", failure.Code);
        }
        finally
        {
            await Database.ExecuteAsync(connection, null,
                "DELETE webpoc.Field WHERE Section='header' AND ColumnName=@column AND Label='POC schema regression'",
                CancellationToken.None, ("@column", column));
        }
    }

    [Fact]
    public async Task Desktop_navigation_uses_all_authorized_module_placements()
    {
        var (database, _, _, identity) = Services();
        var session = await identity.DescribeAsync("SA", "26", "HO", TestContext.Current.CancellationToken);
        var navigation = await new NavigationService(database)
            .GetAsync(session, TestContext.Current.CancellationToken);
        Assert.True(navigation.Modules.Count > 1);
        var sales = Assert.Single(navigation.Modules, module => module.Key == "Sales");
        Assert.Contains(sales.Items, item => item.Key == "Sales:MnuSaleOrder" && item.Implemented);
        Assert.Contains(sales.Items, item => item.Key.StartsWith("Customized:"));
    }

    [Fact]
    public async Task Separate_line_expenses_survive_save_reload_and_removing_an_earlier_row()
    {
        var (database, metadata, orders, identity) = Services();
        var (session, schema, request) = await Fixture(orders, identity, metadata);
        var ct = TestContext.Current.CancellationToken;
        var secondLine = new Dictionary<string, string?>(request.Lines[0]) { ["Qty"] = "200" };
        request.Lines.Add(secondLine);
        request.LineExpenses.Add(request.LineExpenses[0].Select(row => new Dictionary<string, string?>(row)).ToList());
        var firstExpense = request.LineExpenses[0].First(row => row.ContainsKey("Per"));
        var secondExpense = request.LineExpenses[1].Single(row => row["SNo"] == firstExpense["SNo"]);
        firstExpense["Per"] = "5"; secondExpense["Per"] = "7";
        try
        {
            var saved = await orders.SaveAsync(session, null, request, false, ct);
            var reopened = await orders.GetAsync(session, saved.Id!, ct);
            Assert.Equal(5m, ExpenseCalculator.Number(reopened.LineExpenses[0].Single(row => row["SNo"] == firstExpense["SNo"]), "Per"));
            Assert.Equal(7m, ExpenseCalculator.Number(reopened.LineExpenses[1].Single(row => row["SNo"] == firstExpense["SNo"]), "Per"));
            var removedFirst = reopened with { Lines = [reopened.Lines[1]], LineExpenses = [reopened.LineExpenses[1]] };
            var editRequest = Request(removedFirst, schema);
            var preview = await orders.CalculateDraftAsync(session,
                new(editRequest.MetadataVersion, editRequest.Lines, editRequest.HeaderExpenses, editRequest.LineExpenses, saved.Id), ct);
            Assert.Equal(7m, ExpenseCalculator.Number(preview.LineExpenses[0].Single(row => row["SNo"] == firstExpense["SNo"]), "Per"));
            var edited = await orders.SaveAsync(session, saved.Id, editRequest, false, ct);
            Assert.Single(edited.Lines);
            Assert.Equal(7m, ExpenseCalculator.Number(edited.LineExpenses[0].Single(row => row["SNo"] == firstExpense["SNo"]), "Per"));
            await orders.DeleteAsync(session, edited.Id!, new(edited.Version!), ct);
        }
        finally { await CleanupAsync(database, request.Header["RecId"]!, session); }
    }

    [Fact]
    public async Task Denied_action_and_forged_fields_are_rejected()
    {
        var (_, metadata, orders, identity) = Services();
        var (session, _, request) = await Fixture(orders, identity, metadata);
        var denied = session with { Capabilities = session.Capabilities with { Add = false } };
        var ct = TestContext.Current.CancellationToken;
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiFault>(() => orders.SaveAsync(denied, null, request, false, ct))).Status);
        request.Header["ApprovedBy"] = "SA";
        Assert.Equal(422, (await Assert.ThrowsAsync<ApiFault>(() => orders.SaveAsync(session, null, request, false, ct))).Status);
    }

    [Fact]
    public async Task Listing_settings_roundtrip_isolates_users_and_companies_and_resets_only_the_owner()
    {
        var (database, _, _, identity) = Services();
        var ct = TestContext.Current.CancellationToken;
        var real = await identity.DescribeAsync("SA", "26", "HO", ct);
        var first = real with { Username = "LP" + Guid.NewGuid().ToString("N")[..8] };
        var second = first with { Username = "LP" + Guid.NewGuid().ToString("N")[..8] };
        var otherCompany = first with { Company = "WEBPOC-TEST" };
        var service = new ListingPreferenceService(database, new PocOptions { DevelopmentWritesApproved = true });
        var defaults = ListingPolicy.Defaults();
        var custom = defaults with
        {
            Columns = defaults.Columns.Reverse().Select(c => c with { Visible = c.Id != "state", Width = 220 }).ToArray(),
            Density = "comfortable", PageSize = 10, SortColumn = "gross", SortDirection = "asc"
        };
        try
        {
            Assert.Equal(25, (await service.GetAsync(first, "sales-orders", ct)).PageSize);
            await Task.WhenAll(service.SaveAsync(first, "sales-orders", custom, ct), service.SaveAsync(first, "sales-orders", custom, ct));
            var reopened = await service.GetAsync(first, "sales-orders", ct);
            Assert.Equal(custom.Columns.ToArray(), reopened.Columns.ToArray());
            Assert.Equal("comfortable", reopened.Density);
            Assert.Equal("gross", reopened.SortColumn);
            Assert.Equal("asc", reopened.SortDirection);
            Assert.Equal(10, reopened.PageSize);
            Assert.Equal(25, (await service.GetAsync(second, "sales-orders", ct)).PageSize);
            Assert.Equal(25, (await service.GetAsync(otherCompany, "sales-orders", ct)).PageSize);
            await service.SaveAsync(otherCompany, "sales-orders", custom with { PageSize = 50 }, ct);
            await service.ResetAsync(first, "sales-orders", ct);
            Assert.Equal(25, (await service.GetAsync(first, "sales-orders", ct)).PageSize);
            Assert.Equal(50, (await service.GetAsync(otherCompany, "sales-orders", ct)).PageSize);
            var denied = first with { Capabilities = first.Capabilities with { View = false } };
            Assert.Equal(403, (await Assert.ThrowsAsync<ApiFault>(() => service.GetAsync(denied, "sales-orders", ct))).Status);
            Assert.Equal(403, (await Assert.ThrowsAsync<ApiFault>(() => service.SaveAsync(denied, "sales-orders", custom, ct))).Status);
            Assert.Equal(403, (await Assert.ThrowsAsync<ApiFault>(() => service.ResetAsync(denied, "sales-orders", ct))).Status);
            Assert.Equal(404, (await Assert.ThrowsAsync<ApiFault>(() => service.GetAsync(first, "unknown", ct))).Status);
            Assert.Equal(400, (await Assert.ThrowsAsync<ApiFault>(() => service.SaveAsync(first, "sales-orders", custom with { PageSize = 10000 }, ct))).Status);
            var writesDisabled = new ListingPreferenceService(database, new PocOptions());
            Assert.Equal(403, (await Assert.ThrowsAsync<ApiFault>(() => writesDisabled.SaveAsync(first, "sales-orders", custom, ct))).Status);
            Assert.Equal(403, (await Assert.ThrowsAsync<ApiFault>(() => writesDisabled.ResetAsync(first, "sales-orders", ct))).Status);
        }
        finally
        {
            await using var connection = await database.OpenAsync(true, CancellationToken.None);
            await Database.ExecuteAsync(connection, null,
                "DELETE webpoc.ListingPreference WHERE Username IN (@first,@second) AND ListingKey='sales-orders'",
                CancellationToken.None, ("@first", first.Username), ("@second", second.Username));
        }
    }

    [Fact]
    public async Task Listing_sort_and_paging_cover_the_complete_filtered_result_not_only_the_first_page()
    {
        var (database, metadata, orders, identity) = Services();
        var (session, _, template) = await Fixture(orders, identity, metadata);
        var ct = TestContext.Current.CancellationToken;
        var prefix = "LP" + Guid.NewGuid().ToString("N")[..6];
        var references = new List<string>();
        try
        {
            for (var index = 1; index <= 12; index++)
            {
                var reference = prefix + index.ToString("00", CultureInfo.InvariantCulture);
                references.Add(reference);
                var request = template with
                {
                    IdempotencyKey = Guid.NewGuid().ToString(),
                    Header = new(template.Header) { ["RecId"] = reference },
                    Lines = [new(template.Lines[0]) { ["Rate"] = index.ToString(CultureInfo.InvariantCulture) }]
                };
                await orders.SaveAsync(session, null, request, false, ct);
            }
            var first = await orders.ListAsync(session, prefix, 0, ct, 10, "gross", "asc");
            var second = await orders.ListAsync(session, prefix, 1, ct, 10, "gross", "asc");
            Assert.Equal(10, first.Count);
            Assert.Equal(2, second.Count);
            Assert.Empty(first.Select(r => r["DocId"]).Intersect(second.Select(r => r["DocId"])));
            Assert.Equal(100m, ExpenseCalculator.Number(first[0], "GAmount"));
            Assert.Equal(1000m, ExpenseCalculator.Number(first[^1], "GAmount"));
            Assert.Equal(1100m, ExpenseCalculator.Number(second[0], "GAmount"));
            Assert.Equal(1200m, ExpenseCalculator.Number(second[^1], "GAmount"));
            foreach (var size in new[] { 25, 50, 100 })
            {
                var descending = await orders.ListAsync(session, prefix, 0, ct, size, "gross", "desc");
                Assert.Equal(12, descending.Count);
                Assert.Equal(1200m, ExpenseCalculator.Number(descending[0], "GAmount"));
            }
            foreach (var sort in new[] { "date", "reference", "buyer", "state" })
                Assert.Equal(12, (await orders.ListAsync(session, prefix, 0, ct, 25, sort, "asc")).Count);
            var denied = session with { Capabilities = session.Capabilities with { View = false } };
            Assert.Equal(403, (await Assert.ThrowsAsync<ApiFault>(() => orders.ListAsync(denied, prefix, 0, ct, 100, "gross", "asc"))).Status);
        }
        finally
        {
            foreach (var reference in references) await CleanupAsync(database, reference, session);
        }
    }

    [Fact]
    public async Task Company_selection_requires_context_and_prevents_stale_cross_year_requests()
    {
        var (database, _, orders, identity) = Services();
        var ct = TestContext.Current.CancellationToken;
        var sid = Guid.NewGuid().ToString("N");
        await using var control = await database.OpenAsync(true, ct);
        await Database.ExecuteAsync(control, null,
            "INSERT webpoc.Session(Id,Username,ExpiresAt) VALUES(@id,'SA',DATEADD(hour,1,SYSUTCDATETIME()))", ct, ("@id", sid));
        try
        {
            Assert.Null((await identity.GetAuthenticationAsync(sid, "SA", ct)).Context);
            Assert.Equal("context_required", (await Assert.ThrowsAsync<ApiFault>(() => identity.RequireContextAsync(sid, "SA", null, ct))).Code);
            var choices = await identity.CompanySelectionAsync(sid, "SA", ct);
            Assert.Contains(choices.Companies, c => c.Company == "26" && c.FinancialYear == "2026-2027");
            Assert.Contains(choices.Companies, c => c.Company == "25" && c.ReadOnly);
            var sites = choices.Companies.Single(c => c.Company == "26").Sites;
            if (sites.Count > 1)
            {
                Assert.Equal("site_required", (await Assert.ThrowsAsync<ApiFault>(() =>
                    identity.SelectCompanyAsync(sid, "SA", new("26", null, null), ct))).Code);
                Assert.Null((await identity.GetAuthenticationAsync(sid, "SA", ct)).Context);
            }
            var current = await identity.SelectCompanyAsync(sid, "SA", new("26", sites.Count == 1 ? null : "HO", null), ct);
            Assert.Equal("HO", current.Site);
            Assert.Equal("Head Office", current.SiteName);
            Assert.False(current.ReadOnly);
            Assert.True(current.Capabilities.Add);
            Assert.NotNull(current.CompanyInformation);
            Assert.Equal(current.ContextVersion, (await identity.RequireContextAsync(sid, "SA", current.ContextVersion, ct)).ContextVersion);
            Assert.Equal("context_changed", (await Assert.ThrowsAsync<ApiFault>(() => identity.RequireContextAsync(sid, "SA", null, ct))).Code);
            Assert.Equal(403, (await Assert.ThrowsAsync<ApiFault>(() => identity.SelectCompanyAsync(sid, "SA", new("26", "XX", current.ContextVersion), ct))).Status);
            Assert.Equal(current.ContextVersion, (await identity.GetAuthenticationAsync(sid, "SA", ct)).Context!.ContextVersion);
            var history = await identity.SelectCompanyAsync(sid, "SA", new("25", "HO", current.ContextVersion), ct);
            Assert.True(history.ReadOnly);
            Assert.False(history.Capabilities.Add || history.Capabilities.Edit || history.Capabilities.Delete || history.Capabilities.MetadataAdministration);
            Assert.Equal("2021", history.Prefix);
            Assert.Equal("2021-2022", history.FinancialYear);
            Assert.Equal("context_changed", (await Assert.ThrowsAsync<ApiFault>(() => identity.RequireContextAsync(sid, "SA", current.ContextVersion, ct))).Code);
            Assert.Equal(409, (await Assert.ThrowsAsync<ApiFault>(() => identity.SelectCompanyAsync(sid, "SA", new("26", "HO", current.ContextVersion), ct))).Status);
            var currentRows = await orders.ListAsync(current, null, 0, ct);
            var historyRows = await orders.ListAsync(history, null, 0, ct);
            Assert.NotEmpty(currentRows); Assert.NotEmpty(historyRows);
            foreach (var site in sites.Where(site => site.Code != "HO"))
            {
                var alternate = await identity.DescribeAsync("SA", "26", site.Code, ct);
                Assert.Equal(site.Code, alternate.Site);
                Assert.True(alternate.ReadOnly);
                Assert.False(alternate.Capabilities.Add || alternate.Capabilities.Edit || alternate.Capabilities.Delete);
                Assert.Equal(404, (await Assert.ThrowsAsync<ApiFault>(() => orders.GetAsync(alternate, currentRows[0]["DocId"]!, ct))).Status);
            }
            Assert.All(historyRows, row => Assert.InRange(DateOnly.Parse(row["V_Date"]!, CultureInfo.InvariantCulture),
                DateOnly.Parse(history.StartDate, CultureInfo.InvariantCulture), DateOnly.Parse(history.EndDate, CultureInfo.InvariantCulture)));
            Assert.Equal(404, (await Assert.ThrowsAsync<ApiFault>(() => orders.GetAsync(history, currentRows[0]["DocId"]!, ct))).Status);
            Assert.Equal(404, (await Assert.ThrowsAsync<ApiFault>(() => orders.GetAsync(current, historyRows[0]["DocId"]!, ct))).Status);
            Assert.Empty(await orders.ListAsync(current with { Site = "XX" }, null, 0, ct));
            Assert.Equal(404, (await Assert.ThrowsAsync<ApiFault>(() => orders.GetAsync(current with { Site = "XX" }, currentRows[0]["DocId"]!, ct))).Status);
            var forged = history with { Capabilities = history.Capabilities with { Add = true, Edit = true, Delete = true } };
            var empty = new SaveOrderRequest("", null, Guid.NewGuid().ToString(), [], [], [], [], []);
            Assert.Equal("read_only_context", (await Assert.ThrowsAsync<ApiFault>(() => orders.SaveAsync(forged, null, empty, true, ct))).Code);
            Assert.Equal("read_only_context", (await Assert.ThrowsAsync<ApiFault>(() => orders.DeleteAsync(forged, historyRows[0]["DocId"]!, new("invalid"), ct))).Code);
            await identity.RevokeAsync(sid, ct);
            Assert.False(await identity.IsSessionValidAsync(sid, "SA", ct));
        }
        finally { await identity.RevokeAsync(sid, CancellationToken.None); }
    }

    [Fact]
    public async Task Ordinary_users_only_receive_UserSite_companies_and_exact_authorized_sites()
    {
        var options = ApprovedOptions();
        var database = new Database(options);
        var companies = new CompanyContextService(database, options);
        var ct = TestContext.Current.CancellationToken;
        await using var control = await database.OpenAsync(true, ct);
        var grants = await Database.QueryAsync(control, null,
            """
            SELECT s.User_Name,s.CompCode,s.SiteList FROM dbo.UserSite s
            JOIN dbo.UserMast u ON u.User_Name=s.User_Name
            WHERE UPPER(s.User_Name) NOT IN ('SA','SUPER') ORDER BY s.User_Name,s.CompCode
            """, ct);
        Assert.NotEmpty(grants);
        var username = grants[0]["User_Name"]!;
        var authorized = grants.Where(g => g["User_Name"] == username).ToArray();
        var choices = await companies.OptionsAsync(username, ct);
        Assert.NotEmpty(choices);
        Assert.All(choices, choice =>
        {
            var records = authorized.Where(g => g["CompCode"] == choice.Company).ToArray();
            Assert.NotEmpty(records);
            var allowed = records.SelectMany(g => CompanyContextRules.ParseSites(g["SiteList"] ?? "")).ToArray();
            Assert.All(choice.Sites, site => Assert.Contains(site.Code, allowed));
        });
        var denied = await Database.QueryAsync(control, null,
            """
            SELECT TOP(1) c.Comp_Code FROM dbo.Company c WHERE NOT EXISTS(
              SELECT 1 FROM dbo.UserSite s WHERE s.CompCode=c.Comp_Code AND s.User_Name=@user)
            """, ct, ("@user", username));
        Assert.NotEmpty(denied);
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiFault>(() => companies.ResolveAsync(username, denied[0]["Comp_Code"]!, "HO", ct))).Status);
    }

    [Fact]
    public async Task Task_memos_enforce_groups_scope_idempotency_and_per_user_read_receipts()
    {
        var (database, _, _, identity) = Services();
        var tasks = new TaskService(database, ApprovedOptions(), [new SalesOrderTaskTarget(database)]);
        var ct = TestContext.Current.CancellationToken;
        var session = await identity.DescribeAsync("SA", "26", "HO", ct);
        Guid? id = null;
        try
        {
            var input = new PublishTaskRequest("MEMO", "POC memo " + Guid.NewGuid(), "Plain text, not executable markup: <b>example</b>", Guid.NewGuid());
            var memo = await tasks.PublishAsync(session, input, ct);
            id = memo.Id;
            Assert.Null(memo.Href); Assert.Equal("Memo", memo.Category); Assert.False(memo.Read);
            Assert.Equal(id, (await tasks.PublishAsync(session, input, ct)).Id);
            Assert.Equal(409, (await Assert.ThrowsAsync<ApiFault>(() => tasks.PublishAsync(session, input with { Body = "changed" }, ct))).Status);
            Assert.Equal(403, (await Assert.ThrowsAsync<ApiFault>(() => tasks.PublishAsync(session, input with { Type = "SORD_APPROVAL" }, ct))).Status);
            Assert.Equal(404, (await Assert.ThrowsAsync<ApiFault>(() => tasks.GetAsync(session with { Username = "NO-GRANT" }, id.Value, ct))).Status);
            Assert.Equal(404, (await Assert.ThrowsAsync<ApiFault>(() => tasks.GetAsync(session with { Site = "D1" }, id.Value, ct))).Status);
            Assert.Equal(403, (await Assert.ThrowsAsync<ApiFault>(() => tasks.PublishAsync(session with { ReadOnly = true }, input, ct))).Status);
            await tasks.ReadAsync(session, id.Value, ct);
            await tasks.ReadAsync(session, id.Value, ct);
            Assert.True((await tasks.GetAsync(session, id.Value, ct)).Read);
            var other = await identity.DescribeAsync("SUPER", "26", "HO", ct);
            Assert.False((await tasks.GetAsync(other, id.Value, ct)).Read);
            Assert.Contains((await tasks.ListAsync(session, 0, ct)).Items, item => item.Id == id);
        }
        finally
        {
            if (id is not null)
            {
                await using var c = await database.OpenAsync(false, CancellationToken.None);
                await Database.ExecuteAsync(c, null, """
                    SET XACT_ABORT ON; BEGIN TRAN;
                    DELETE webpoc.erpTaskReceipt WHERE TaskId=@id;
                    DELETE webpoc.erpTaskAudience WHERE TaskId=@id;
                    DELETE webpoc.erpTaskHistory WHERE TaskId=@id;
                    DELETE webpoc.erpTaskInstance WHERE Id=@id AND CreatedBy='SA' AND SourceDocId IS NULL;
                    COMMIT;
                    """, CancellationToken.None, ("@id", id.ToString()));
            }
        }
    }

    [Fact]
    public async Task Task_order_rejection_resubmission_and_approval_enforce_versions_and_permissions()
    {
        var (database, metadata, orders, identity) = Services();
        var (session, schema, request) = await Fixture(orders, identity, metadata);
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var created = await orders.SaveAsync(session, null, request, false, ct);
            var state = await orders.ApprovalAsync(session, created.Id!, ct);
            Assert.Equal("Pending", state.State); Assert.True(state.CanAct);
            await using (var c = await database.OpenAsync(false, ct))
            {
                await Database.ExecuteAsync(c, null, "UPDATE webpoc.erpApprovalRequest SET AllowSelf=0 WHERE Id=@id", ct, ("@id", state.RequestId.ToString()));
                var blocked = await orders.ApprovalAsync(session, created.Id!, ct);
                Assert.False(blocked.CanAct);
                Assert.Equal(403, (await Assert.ThrowsAsync<ApiFault>(() => orders.DecideAsync(session, created.Id!,
                    new(created.Version!, blocked.RequestId!.Value, blocked.Version!, "Approve", null), ct))).Status);
                await Database.ExecuteAsync(c, null, "UPDATE webpoc.erpApprovalRequest SET AllowSelf=1 WHERE Id=@id", ct, ("@id", state.RequestId.ToString()));
            }
            state = await orders.ApprovalAsync(session, created.Id!, ct);
            var decision = new OrderDecisionRequest(created.Version!, state.RequestId!.Value, state.Version!, "Reject", "");
            Assert.Equal(422, (await Assert.ThrowsAsync<ApiFault>(() => orders.DecideAsync(session, created.Id!, decision, ct))).Status);
            Assert.Equal(403, (await Assert.ThrowsAsync<ApiFault>(() => orders.DecideAsync(
                session with { Capabilities = session.Capabilities with { Approve = false } }, created.Id!, decision, ct))).Status);
            Assert.Equal(403, (await Assert.ThrowsAsync<ApiFault>(() => orders.DecideAsync(
                session with { Username = "NO-GRANT" }, created.Id!, decision with { Comment = "No permission" }, ct))).Status);
            Assert.Equal(409, (await Assert.ThrowsAsync<ApiFault>(() => orders.DecideAsync(session, created.Id!,
                decision with { RequestVersion = "old", Comment = "stale" }, ct))).Status);
            Assert.Equal(404, (await Assert.ThrowsAsync<ApiFault>(() => orders.DecideAsync(session with { Site = "D1" }, created.Id!,
                decision with { Comment = "wrong site" }, ct))).Status);
            var rejected = await orders.DecideAsync(session, created.Id!, decision with { Comment = "Please correct the customer reference." }, ct);
            Assert.False(rejected.Locked);
            Assert.Equal("Rejected", (await orders.ApprovalAsync(session, created.Id!, ct)).State);
            var revision = Request(rejected, schema);
            revision.Header["PartyOrdNo"] = "WEBPOC-EDIT";
            var revised = await orders.SaveAsync(session, created.Id!, revision, false, ct);
            var replacement = await orders.ApprovalAsync(session, created.Id!, ct);
            Assert.Equal("Pending", replacement.State); Assert.NotEqual(state.RequestId, replacement.RequestId);
            Assert.Equal(409, (await Assert.ThrowsAsync<ApiFault>(() => orders.DecideAsync(session, created.Id!,
                decision with { Version = revised.Version!, Comment = "old request" }, ct))).Status);
            var tasks = new TaskService(database, ApprovedOptions(), [new SalesOrderTaskTarget(database)]);
            var inbox = await tasks.ListAsync(session, 0, ct);
            Assert.Contains(inbox.Items, task => task.Category == "Approval" && task.Href == $"/dashboard/sale-order/{Uri.EscapeDataString(created.Id!)}");
            Assert.Empty((await tasks.ListAsync(session with { Site = "D1" }, 0, ct)).Items);
        }
        finally { await CleanupAsync(database, request.Header["RecId"]!, session); }
    }

    [Fact]
    public async Task Task_configured_sequential_stages_snapshot_and_only_final_approval_changes_legacy_order()
    {
        var (database, metadata, orders, identity) = Services();
        var (session, _, request) = await Fixture(orders, identity, metadata);
        var tasks = new TaskService(database, ApprovedOptions(), [new SalesOrderTaskTarget(database)]);
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var created = await orders.SaveAsync(session, null, request, false, ct);
            await using (var c = await database.OpenAsync(false, ct))
            await using (var tx = (Microsoft.Data.SqlClient.SqlTransaction)await c.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct))
            {
                var config = await Database.QueryAsync(c, tx, """
                    SELECT f.Id,MAX(st.Step)+1 AS NextStep FROM webpoc.erpApprovalFlow f
                    JOIN webpoc.erpApprovalFlowStage st ON st.FlowId=f.Id
                    WHERE f.Company='26' AND f.Site='HO' AND f.VoucherType='SORD' GROUP BY f.Id
                    """, ct);
                var next = int.Parse(config[0]["NextStep"]!, CultureInfo.InvariantCulture);
                await Database.ExecuteAsync(c, tx, """
                    INSERT webpoc.erpApprovalFlowStage(FlowId,Step,Name,GroupCode)
                    SELECT FlowId,@step,'Second-level POC verification',GroupCode
                    FROM webpoc.erpApprovalFlowStage WHERE FlowId=@flow AND Step=1
                    """, ct, ("@flow", config[0]["Id"]), ("@step", next));
                await tasks.SubmitDocumentAsync(c, tx, session, "SORD", created.Id!, created.Header["RecId"]!, created.Version!,
                    "SA", Guid.NewGuid().ToString(), false, ct);
                await Database.ExecuteAsync(c, tx, "DELETE webpoc.erpApprovalFlowStage WHERE FlowId=@flow AND Step=@step",
                    ct, ("@flow", config[0]["Id"]), ("@step", next));
                await tx.CommitAsync(ct);
            }
            var state = await orders.ApprovalAsync(session, created.Id!, ct);
            Assert.True(state.Stages.Count >= 2);
            var current = created;
            for (var index = 0; index < state.Stages.Count; index++)
            {
                var snapshot = await orders.ApprovalAsync(session, created.Id!, ct);
                Assert.True(snapshot.CanAct);
                var decision = new OrderDecisionRequest(current.Version!, snapshot.RequestId!.Value, snapshot.Version!, "Approve", $"Step {index + 1}");
                current = await orders.DecideAsync(session, created.Id!, decision, ct);
                Assert.Equal(index == state.Stages.Count - 1, current.Locked);
                if (!current.Locked)
                {
                    Assert.Equal(409, (await Assert.ThrowsAsync<ApiFault>(() => orders.DecideAsync(session, created.Id!, decision, ct))).Status);
                    await using var c = await database.OpenAsync(false, ct);
                    var stored = await Database.QueryAsync(c, null, "SELECT ApprovedBy FROM dbo.SOrd WHERE DocId=@id", ct, ("@id", created.Id));
                    Assert.True(string.IsNullOrEmpty(stored[0]["ApprovedBy"]));
                }
            }
            Assert.Equal("Approved", (await orders.ApprovalAsync(session, created.Id!, ct)).State);
            await using (var c = await database.OpenAsync(false, ct))
            {
                var stored = await Database.QueryAsync(c, null, "SELECT ApprovedBy FROM dbo.SOrd WHERE DocId=@id", ct, ("@id", created.Id));
                Assert.Equal("SA", stored[0]["ApprovedBy"]);
            }
            Assert.Equal(409, (await Assert.ThrowsAsync<ApiFault>(() => orders.DeleteAsync(session, current.Id!, new(current.Version!), ct))).Status);
        }
        finally { await CleanupAsync(database, request.Header["RecId"]!, session); }
    }

    [Fact]
    public async Task Copy_returns_clean_unsaved_authorized_draft_with_current_defaults_and_independent_rows()
    {
        var (database, metadata, orders, identity) = Services();
        var (session, schema, request) = await Fixture(orders, identity, metadata);
        var ct = TestContext.Current.CancellationToken;
        string? copiedReference = null;
        request.Header["V_Date"] = session.StartDate;
        request.Header["DelDate"] = session.StartDate;
        request.Header["PartyOrdDt"] = session.StartDate;
        try
        {
            var source = await orders.SaveAsync(session, null, request, false, ct);
            var state = await orders.ApprovalAsync(session, source.Id!, ct);
            var approved = await orders.DecideAsync(session, source.Id!, new(source.Version!, state.RequestId!.Value, state.Version!, "Approve", null), ct);
            var defaults = await orders.DefaultsAsync(session, ct);
            var copied = await orders.CopyAsync(session, source.Id!, ct);
            Assert.Null(copied.Id); Assert.Null(copied.Version); Assert.Null(copied.RecordInfo);
            Assert.False(copied.Locked); Assert.Null(copied.LockReason);
            Assert.NotEqual(source.Header["RecId"], copied.Header["RecId"]);
            Assert.Equal(defaults.Header["V_Date"], copied.Header["V_Date"]);
            Assert.Equal(defaults.Header["DelDate"], copied.Header["DelDate"]);
            Assert.Equal("", copied.Header["PartyOrdNo"]);
            Assert.Equal(source.Header["PartyCode"], copied.Header["PartyCode"]);
            Assert.Equal(source.Lines[0]["ItemCode"], copied.Lines[0]["ItemCode"]);
            Assert.Equal(source.Lines[0]["Qty"], copied.Lines[0]["Qty"]);
            Assert.Equal(source.Lines[0]["Rate"], copied.Lines[0]["Rate"]);
            Assert.DoesNotContain("V_SNo", copied.Lines[0].Keys);
            Assert.DoesNotContain("DocId", copied.Header.Keys);
            Assert.DoesNotContain("ApprovedBy", copied.Header.Keys);
            Assert.Equal("SA", approved.RecordInfo!.CreatedBy);
            Assert.Equal("SA", approved.RecordInfo.ModifiedBy);
            Assert.Equal("SA", approved.RecordInfo.ApprovedBy);
            copied.Lines[0]["Qty"] = "999";
            Assert.Equal(source.Lines[0]["Qty"], (await orders.GetAsync(session, source.Id!, ct)).Lines[0]["Qty"]);
            Assert.Equal(403, (await Assert.ThrowsAsync<ApiFault>(() => orders.CopyAsync(
                session with { Capabilities = session.Capabilities with { Add = false } }, source.Id!, ct))).Status);
            Assert.Equal(403, (await Assert.ThrowsAsync<ApiFault>(() => orders.CopyAsync(session with { ReadOnly = true }, source.Id!, ct))).Status);
            Assert.Equal(404, (await Assert.ThrowsAsync<ApiFault>(() => orders.CopyAsync(session with { Site = "D1" }, source.Id!, ct))).Status);
            Assert.Equal(approved.Version, (await orders.GetAsync(session, source.Id!, ct)).Version);
            Assert.Equal(422, (await Assert.ThrowsAsync<ApiFault>(() => orders.SaveAsync(session, null, Request(copied, schema), true, ct))).Status);
            copiedReference = "POC" + Guid.NewGuid().ToString("N")[..7];
            copied.Header["RecId"] = copiedReference; copied.Header["PartyOrdNo"] = "WEBPOC-TEST";
            var savedCopy = await orders.SaveAsync(session, null, Request(copied, schema), false, ct);
            Assert.NotEqual(source.Id, savedCopy.Id); Assert.Null(savedCopy.RecordInfo!.ApprovedBy);
            var copyApproval = await orders.ApprovalAsync(session, savedCopy.Id!, ct);
            Assert.Equal("Pending", copyApproval.State); Assert.NotEqual(state.RequestId, copyApproval.RequestId);
        }
        finally
        {
            if (copiedReference is not null) await CleanupAsync(database, copiedReference, session);
            await CleanupAsync(database, request.Header["RecId"]!, session);
        }
    }

    [Fact]
    public async Task Unapproval_requires_dedicated_grant_reason_versions_and_unlocked_source_then_resubmits_from_stage_one()
    {
        var (database, metadata, orders, identity) = Services();
        var (session, schema, request) = await Fixture(orders, identity, metadata);
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var created = await orders.SaveAsync(session, null, request, false, ct);
            var pending = await orders.ApprovalAsync(session, created.Id!, ct);
            var approved = await orders.DecideAsync(session, created.Id!, new(created.Version!, pending.RequestId!.Value, pending.Version!, "Approve", "Checked"), ct);
            var state = await orders.ApprovalAsync(session, created.Id!, ct);
            Assert.True(state.CanReverse);
            var reversal = new UnapproveOrderRequest(approved.Version!, state.RequestId, state.Version, "Correct the buyer reference.");
            Assert.Equal(422, (await Assert.ThrowsAsync<ApiFault>(() => orders.UnapproveAsync(session, created.Id!, reversal with { Reason = " " }, ct))).Status);
            Assert.Equal(409, (await Assert.ThrowsAsync<ApiFault>(() => orders.UnapproveAsync(session, created.Id!, reversal with { Version = "old" }, ct))).Status);
            Assert.Equal(409, (await Assert.ThrowsAsync<ApiFault>(() => orders.UnapproveAsync(session, created.Id!, reversal with { RequestVersion = "old" }, ct))).Status);
            Assert.Equal(403, (await Assert.ThrowsAsync<ApiFault>(() => orders.UnapproveAsync(session with { Username = "NO-GRANT" }, created.Id!, reversal, ct))).Status);
            Assert.Equal(403, (await Assert.ThrowsAsync<ApiFault>(() => orders.UnapproveAsync(
                session with { Capabilities = session.Capabilities with { Approve = false } }, created.Id!, reversal, ct))).Status);
            Assert.Equal(403, (await Assert.ThrowsAsync<ApiFault>(() => orders.UnapproveAsync(session with { ReadOnly = true }, created.Id!, reversal, ct))).Status);
            Assert.Equal(404, (await Assert.ThrowsAsync<ApiFault>(() => orders.UnapproveAsync(session with { Site = "D1" }, created.Id!, reversal, ct))).Status);
            await using (var c = await database.OpenAsync(false, ct))
            {
                var taskService = new TaskService(database, ApprovedOptions(), [new SalesOrderTaskTarget(database)]);
                await using (var tx = (Microsoft.Data.SqlClient.SqlTransaction)await c.BeginTransactionAsync(ct))
                {
                    await Database.ExecuteAsync(c, tx,
                        "UPDATE webpoc.erpTaskTypeGroup SET CanReverse=0 WHERE TypeCode='SORD_APPROVAL' AND GroupCode='SALES_APPROVERS_26_HO'", ct);
                    Assert.False(await taskService.CanReverseAsync(c, tx, session, "SORD", ct));
                    await tx.RollbackAsync(ct);
                }
                await Database.ExecuteAsync(c, null, "UPDATE dbo.SOrd SET Closed='Y' WHERE DocId=@id", ct, ("@id", created.Id));
                Assert.False((await orders.ApprovalAsync(session, created.Id!, ct)).CanReverse);
                var closed = await orders.GetAsync(session, created.Id!, ct);
                Assert.Equal(409, (await Assert.ThrowsAsync<ApiFault>(() => orders.UnapproveAsync(session, created.Id!, reversal with { Version = closed.Version! }, ct))).Status);
                await Database.ExecuteAsync(c, null, "UPDATE dbo.SOrd SET Closed=NULL WHERE DocId=@id", ct, ("@id", created.Id));
                await Database.ExecuteAsync(c, null,
                    "INSERT dbo.Sord_adjustment(SordDocID,Sord_VSNO,ReelNo,ReelDocID) VALUES(@id,1,'REVTEST',@id)", ct, ("@id", created.Id));
                var allocated = await orders.GetAsync(session, created.Id!, ct);
                Assert.False((await orders.ApprovalAsync(session, created.Id!, ct)).CanReverse);
                Assert.Equal(409, (await Assert.ThrowsAsync<ApiFault>(() => orders.UnapproveAsync(session, created.Id!, reversal with { Version = allocated.Version! }, ct))).Status);
                await Database.ExecuteAsync(c, null,
                    "DELETE dbo.Sord_adjustment WHERE SordDocID=@id AND ReelDocID=@id AND ReelNo='REVTEST'", ct, ("@id", created.Id));
                await Database.ExecuteAsync(c, null, """
                    INSERT dbo.WOrderDetail(DocId,V_Type,RecId,V_SNo,V_Date,Site_Code,SODocId)
                    SELECT DocId,V_Type,RecId,1,V_Date,Site_Code,DocId FROM dbo.SOrd WHERE DocId=@id
                    """, ct, ("@id", created.Id));
                var used = await orders.GetAsync(session, created.Id!, ct);
                Assert.False((await orders.ApprovalAsync(session, created.Id!, ct)).CanReverse);
                Assert.Equal(409, (await Assert.ThrowsAsync<ApiFault>(() => orders.UnapproveAsync(session, created.Id!, reversal with { Version = used.Version! }, ct))).Status);
                await Database.ExecuteAsync(c, null, "DELETE dbo.WOrderDetail WHERE DocId=@id AND SODocId=@id AND V_Type='SORD'", ct, ("@id", created.Id));
            }
            approved = await orders.GetAsync(session, created.Id!, ct);
            reversal = reversal with { Version = approved.Version! };
            var failing = Services(new FailingCheckpoint("unapproval")).Orders;
            await Assert.ThrowsAsync<InjectedFailure>(() => failing.UnapproveAsync(session, created.Id!, reversal, ct));
            Assert.Equal("Approved", (await orders.ApprovalAsync(session, created.Id!, ct)).State);
            var editable = await orders.UnapproveAsync(session, created.Id!, reversal, ct);
            Assert.False(editable.Locked); Assert.Null(editable.RecordInfo!.ApprovedBy);
            var reversed = await orders.ApprovalAsync(session, created.Id!, ct);
            Assert.Equal("Reversed", reversed.State); Assert.Equal("SA", reversed.ReversedBy);
            Assert.Equal(reversal.Reason, reversed.ReversalReason); Assert.False(reversed.CanReverse);
            Assert.Equal(409, (await Assert.ThrowsAsync<ApiFault>(() => orders.UnapproveAsync(session, created.Id!, reversal, ct))).Status);
            var saved = await orders.SaveAsync(session, created.Id!, Request(editable, schema), false, ct);
            var restarted = await orders.ApprovalAsync(session, saved.Id!, ct);
            Assert.Equal("Pending", restarted.State); Assert.NotEqual(state.RequestId, restarted.RequestId);
            Assert.Equal("Active", restarted.Stages[0].State); Assert.Null(restarted.Stages[0].Actor);
            await using var verify = await database.OpenAsync(false, ct);
            var history = await Database.QueryAsync(verify, null,
                "SELECT Actor,Reason,PreviousApprovedBy FROM webpoc.erpApprovalReversal WHERE DocId=@id", ct, ("@id", created.Id));
            Assert.Single(history); Assert.Equal("SA", history[0]["Actor"]); Assert.Equal("SA", history[0]["PreviousApprovedBy"]);
        }
        finally { await CleanupAsync(database, request.Header["RecId"]!, session); }
    }

    private sealed class InjectedFailure : Exception;
    private sealed class FailingCheckpoint(string stage) : IWriteCheckpoint
    {
        public void Reached(string current) { if (current == stage) throw new InjectedFailure(); }
    }

    private static async Task CleanupAsync(Database database, string reference, SessionResponse session)
    {
        // Only this test's exact random reference and marked buyer-order value can qualify.
        await using var connection = await database.OpenAsync(false, CancellationToken.None);
        await Database.ExecuteAsync(connection, null,
            """
            SET XACT_ABORT ON; BEGIN TRAN;
            DECLARE @id varchar(21)=(SELECT DocId FROM dbo.SOrd WHERE RecId=@reference
              AND PartyOrdNo IN ('WEBPOC-TEST','WEBPOC-EDIT') AND PreparedBy=@user AND Site_Code=@site AND V_Prefix=@prefix AND V_Type='SORD');
            IF @id IS NOT NULL
            BEGIN
              DELETE h FROM webpoc.erpTaskHistory h JOIN webpoc.erpTaskInstance t ON t.Id=h.TaskId WHERE t.SourceDocId=@id;
              DELETE r FROM webpoc.erpTaskReceipt r JOIN webpoc.erpTaskInstance t ON t.Id=r.TaskId WHERE t.SourceDocId=@id;
              DELETE a FROM webpoc.erpTaskAudience a JOIN webpoc.erpTaskInstance t ON t.Id=a.TaskId WHERE t.SourceDocId=@id;
              DELETE webpoc.erpTaskInstance WHERE SourceDocId=@id;
              DELETE webpoc.erpApprovalReversal WHERE DocId=@id;
              DELETE st FROM webpoc.erpApprovalRequestStage st JOIN webpoc.erpApprovalRequest r ON r.Id=st.RequestId WHERE r.DocId=@id;
              DELETE webpoc.erpApprovalRequest WHERE DocId=@id;
              DELETE dbo.Sord_adjustment WHERE SordDocID=@id AND ReelDocID=@id AND ReelNo='REVTEST';
              DELETE dbo.WOrderDetail WHERE DocId=@id AND SODocId=@id AND V_Type='SORD';
              DELETE dbo.Term_Cond_Trans WHERE Ref_DocId=@id;
              DELETE dbo.SPExpenseLine WHERE DocId=@id; DELETE dbo.SPExpense WHERE DocId=@id;
              DELETE dbo.SOrdDet WHERE DocId=@id; DELETE dbo.SOrd WHERE DocId=@id;
            END;
            COMMIT;
            """, CancellationToken.None, ("@reference", reference), ("@user", session.Username),
            ("@site", session.Site), ("@prefix", session.Prefix));
    }
}

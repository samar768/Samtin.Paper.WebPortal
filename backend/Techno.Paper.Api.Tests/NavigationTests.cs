using Techno.Paper.Api.Domain;
using Techno.Paper.Api.Middleware;
using Techno.Paper.Api.Services;

namespace Techno.Paper.Api.Tests;

public sealed class NavigationTests
{
    private static readonly HashSet<string> NoRestrictions = new(StringComparer.OrdinalIgnoreCase);
    private static MenuGrant Menu(string name, string parent = "", bool active = true,
        string module = "Sales", string placement = "Sales", int order = 1, string permission = "AEDP") =>
        new(module, name, name, parent, placement, order, permission, active);

    [Fact]
    public void Placement_and_parent_relationships_match_desktop_not_execution_dll()
    {
        var result = NavigationRules.Build([
            Menu("Order"), Menu("MnuSaleOrder", "Order", order: 2),
            Menu("Extra", "Order", module: "Customized", order: 3)], NoRestrictions);
        var module = Assert.Single(result.Modules);
        Assert.Equal("Sales", module.Key);
        Assert.Equal("Sales:Order", module.Items[2].ParentKey);
        Assert.Equal("/dashboard/sale-order", module.Items[1].Href);
        Assert.True(module.Items[1].Implemented);
        Assert.False(module.Items[2].Implemented);
        Assert.Contains("menu=Customized%3AExtra", module.Items[2].Href);
    }

    [Fact]
    public void Inactive_or_system_restricted_ancestors_hide_their_descendants()
    {
        var rows = new[] { Menu("Order"), Menu("MnuSaleOrder", "Order") };
        Assert.Empty(NavigationRules.Build(rows, new HashSet<string> { "Sales:Order" }).Modules);
        Assert.Empty(NavigationRules.Build([Menu("Order", active: false), rows[1]], NoRestrictions).Modules);
    }

    [Fact]
    public void View_only_menu_is_visible_but_no_ungranted_module_is_invented()
    {
        var result = NavigationRules.Build([Menu("MnuSaleOrder", permission: "")], NoRestrictions);
        Assert.Single(result.Modules);
        Assert.Single(result.Modules[0].Items);
    }

    [Fact]
    public void Registered_master_routes_reuse_existing_menu_visibility_and_ancestor_restrictions()
    {
        var rows = new[] { Menu("General", module: "Master", placement: "Master"),
            Menu("MnuBFMaster", "General", module: "Master", placement: "Master") };
        var routes = new Dictionary<string, string> { ["Master:MnuBFMaster"] = "bf" };
        var item = Assert.Single(NavigationRules.Build(rows, NoRestrictions, routes).Modules[0].Items,
            item => item.Key == "Master:MnuBFMaster");
        Assert.True(item.Implemented);
        Assert.Equal("/dashboard/masters/bf", item.Href);
        Assert.Empty(NavigationRules.Build(rows, new HashSet<string> { "Master:General" }, routes).Modules);
    }

    [Fact]
    public void Conflicting_grants_are_not_merged_or_given_admin_bypass()
    {
        Assert.Throws<PolicyConflictException>(() =>
            NavigationRules.Build([Menu("Order"), Menu("Order", permission: "")], NoRestrictions));
    }

    [Fact]
    public void Orphan_is_hidden_with_warning_and_cycle_is_an_explicit_failure()
    {
        var orphan = NavigationRules.Build([Menu("Child", "Missing")], NoRestrictions);
        Assert.Empty(orphan.Modules);
        Assert.Single(orphan.Warnings);
        Assert.Throws<ApiFault>(() => NavigationRules.Build([Menu("One", "Two"), Menu("Two", "One")], NoRestrictions));
    }
}

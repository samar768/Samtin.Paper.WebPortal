using Techno.Paper.Api.Domain;
using Techno.Paper.Api.Infrastructure;
using Techno.Paper.Api.Middleware;

namespace Techno.Paper.Api.Services;

internal static class MasterAccess
{
    public static async Task<MasterCapabilitiesResponse> ReadAsync(Database database, PocOptions options,
        SessionResponse session, string module, string menu, CancellationToken ct)
    {
        var access = await NavigationService.ReadAccessAsync(database, session, ct);
        try
        {
            var grant = SalesRules.ResolveGrant(access.Grants.Where(row =>
                row.Module.Equals(module, StringComparison.OrdinalIgnoreCase) &&
                row.Name.Equals(menu, StringComparison.OrdinalIgnoreCase))
                .Select(row => row.Active ? row.Permission : "!inactive"));
            var view = grant is not null && NavigationRules.Build(access.Grants, access.Restricted).Modules
                .SelectMany(item => item.Items).Any(item => item.Key.Equals($"{module}:{menu}", StringComparison.OrdinalIgnoreCase));
            var write = view && !session.ReadOnly && options.DevelopmentWritesApproved &&
                session.Company == options.Company && session.Site == options.Site;
            return new(view, write && grant!.Contains('A'), write && grant!.Contains('E'),
                write && grant!.Contains('D'), view && grant!.Contains('P'),
                view && session.Username.Equals(options.MetadataAdministrator, StringComparison.OrdinalIgnoreCase));
        }
        catch (PolicyConflictException)
        {
            throw new ApiFault(409, "master_configuration", "Conflicting master permissions require administrator review.");
        }
    }
}

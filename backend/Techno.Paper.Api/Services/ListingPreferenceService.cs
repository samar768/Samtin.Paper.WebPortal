using System.Text.Json;
using Techno.Paper.Api.Domain;
using Techno.Paper.Api.Infrastructure;
using Techno.Paper.Api.Middleware;

namespace Techno.Paper.Api.Services;

public interface IListingPreferenceService
{
    Task<ListingPreferences> GetAsync(SessionResponse session, string listing, CancellationToken ct);
    Task<ListingPreferences> SaveAsync(SessionResponse session, string listing, ListingPreferences settings, CancellationToken ct);
    Task ResetAsync(SessionResponse session, string listing, CancellationToken ct);
}

public sealed class ListingPreferenceService(Database database, PocOptions options) : IListingPreferenceService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static void Authorize(SessionResponse session, string listing)
    {
        if (listing != "sales-orders")
            throw new ApiFault(404, "listing_not_found", "This listing does not support personalization.");
        if (!session.Capabilities.View)
            throw new ApiFault(403, "action_denied", "Viewing this listing is not permitted.");
    }

    private void RequireWrites()
    {
        if (!options.DevelopmentWritesApproved)
            throw new ApiFault(403, "writes_disabled", "Development database writes are not approved.");
    }

    public async Task<ListingPreferences> GetAsync(SessionResponse session, string listing, CancellationToken ct)
    {
        Authorize(session, listing);
        await using var connection = await database.OpenAsync(true, ct);
        var rows = await Database.QueryAsync(connection, null,
            """
            SELECT Settings FROM webpoc.ListingPreference
            WHERE Username=@user AND Company=@company AND ListingKey=@listing
            """, ct, ("@user", session.Username), ("@company", session.Company), ("@listing", listing));
        if (rows.Count == 0) return ListingPolicy.Defaults();
        var settings = JsonSerializer.Deserialize<ListingPreferences>(rows[0]["Settings"]!, JsonOptions)
            ?? throw new ApiFault(500, "invalid_listing_settings", "Saved settings are invalid. Reset the listing defaults.");
        ListingPolicy.Validate(settings);
        return settings;
    }

    public async Task<ListingPreferences> SaveAsync(SessionResponse session, string listing, ListingPreferences settings, CancellationToken ct)
    {
        Authorize(session, listing);
        RequireWrites();
        ListingPolicy.Validate(settings);
        await using var connection = await database.OpenAsync(true, ct);
        await using var transaction = connection.BeginTransaction();
        // Range locking makes first-time saves from two devices a single scoped upsert.
        await Database.ExecuteAsync(connection, transaction,
            """
            UPDATE webpoc.ListingPreference WITH (UPDLOCK,SERIALIZABLE)
            SET Settings=@settings,UpdatedAt=SYSUTCDATETIME()
            WHERE Username=@user AND Company=@company AND ListingKey=@listing;
            IF @@ROWCOUNT=0
                INSERT webpoc.ListingPreference(Username,Company,ListingKey,Settings)
                VALUES(@user,@company,@listing,@settings);
            """, ct, ("@user", session.Username), ("@company", session.Company), ("@listing", listing),
            ("@settings", JsonSerializer.Serialize(settings, JsonOptions)));
        await transaction.CommitAsync(ct);
        return settings;
    }

    public async Task ResetAsync(SessionResponse session, string listing, CancellationToken ct)
    {
        Authorize(session, listing);
        RequireWrites();
        await using var connection = await database.OpenAsync(true, ct);
        await Database.ExecuteAsync(connection, null,
            """
            DELETE FROM webpoc.ListingPreference
            WHERE Username=@user AND Company=@company AND ListingKey=@listing
            """, ct, ("@user", session.Username), ("@company", session.Company), ("@listing", listing));
    }
}

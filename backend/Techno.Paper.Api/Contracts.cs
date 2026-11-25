namespace Techno.Paper.Api;

/// <summary>ERP username/password verification request. Passwords are never returned.</summary>
public sealed record LoginRequest(string Username, string Password);
/// <summary>Current authenticated ERP identity and effective Sales capabilities.</summary>
public sealed record SessionResponse(string Username, string Company, string CompanyName, string Site,
    string FinancialYear, string Prefix, string StartDate, string EndDate, CapabilitiesResponse Capabilities,
    string SiteName = "", string ContextVersion = "", bool ReadOnly = false, CompanyInformationResponse? CompanyInformation = null);
/// <summary>Verified identity, optionally with a selected authorized workspace.</summary>
public sealed record AuthenticationResponse(string Username, SessionResponse? Context);
/// <summary>Authorized company/year and its permitted sites. Unsupported database routes are explicitly unavailable.</summary>
public sealed record CompanyOptionResponse(string Company, string CompanyName, string FinancialYear, string StartDate,
    string EndDate, IReadOnlyList<SiteOptionResponse> Sites, bool Available, string? UnavailableReason, bool ReadOnly);
/// <summary>One authorized site; a single option can be selected automatically.</summary>
public sealed record SiteOptionResponse(string Code, string Name, bool HeadOffice);
/// <summary>Explicit workspace selection with optimistic session-context concurrency.</summary>
public sealed record SelectCompanyRequest(string Company, string? Site, string? ExpectedVersion);
/// <summary>Authenticated company-selection options and the revision needed to switch safely.</summary>
public sealed record CompanySelectionResponse(string Username, string? Company, string? Site, string? ContextVersion,
    IReadOnlyList<CompanyOptionResponse> Companies);
/// <summary>Whitelisted current company information with Windows-style site address/tax overrides.</summary>
public sealed record CompanyInformationResponse(string RegisteredName, string DisplayName, string Address1, string Address2,
    string City, string Country, string Pin, string Phone, string Email, string Pan, string Tin, bool HeadOffice);
/// <summary>Server-evaluated capabilities; record state can further restrict actions.</summary>
public sealed record CapabilitiesResponse(bool View, bool Add, bool Edit, bool Delete, bool Print,
    bool SelectedPriceList, bool MetadataAdministration, bool Approve = false);
/// <summary>CSRF request token for subsequent session and business commands.</summary>
public sealed record CsrfResponse(string Token);
/// <summary>Approved typed or extension field exposed by the Sales metadata contract.</summary>
public sealed record FieldResponse(string Key, string Section, string Label, string Type, bool Required,
    int Length, int Precision, int Scale, string? Lookup, bool Writable, string? ValueHelp = null);
/// <summary>Versioned field contract for the Sales aggregate.</summary>
public sealed record SchemaResponse(string Version, IReadOnlyList<FieldResponse> Fields);
/// <summary>Server-defined values and expense definitions for a new line.</summary>
public sealed record LineDefaultsResponse(Dictionary<string, string?> Values, List<Dictionary<string, string?>> Expenses);
/// <summary>Active Sales Order voucher and whether portal persistence supports it.</summary>
public sealed record SalesOrderVoucher(string Code, string Label, bool CanSave);
/// <summary>Item tax-band defaults, keyed by the canonical expense master code.</summary>
public sealed record ItemExpenseDefault(string ExpCode, string Per, string Amount);
/// <summary>Provisional scoped document numbers, not reserved until an atomic save.</summary>
public sealed record DocumentNumbersResponse(string Reference, string VoucherNumber);
/// <summary>A versioned order aggregate; scalar values use lossless strings or explicit null.</summary>
public sealed record OrderResponse(string? Id, string? Version, string MetadataVersion,
    Dictionary<string, string?> Header, List<Dictionary<string, string?>> Lines,
    List<Dictionary<string, string?>> HeaderExpenses, List<List<Dictionary<string, string?>>> LineExpenses,
    List<Dictionary<string, string?>> Terms, bool Locked, string? LockReason, DocumentNumbersResponse? Numbers = null,
    RecordInfoResponse? RecordInfo = null, bool CanPrintPdf = false);
/// <summary>Server-owned ERP actor identities and non-approval change restrictions.</summary>
public sealed record RecordInfoResponse(string? CreatedBy, string? ModifiedBy, string? ApprovedBy, string? ChangeBlockedReason);
/// <summary>Atomic create/update payload and concurrency/idempotency information.</summary>
public sealed record SaveOrderRequest(string MetadataVersion, string? Version, string IdempotencyKey,
    Dictionary<string, string?> Header, List<Dictionary<string, string?>> Lines,
    List<Dictionary<string, string?>> HeaderExpenses, List<List<Dictionary<string, string?>>> LineExpenses,
    List<Dictionary<string, string?>> Terms, bool AutomaticReference = false,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? VoucherType = null);
/// <summary>Optimistic concurrency token required for deletion.</summary>
public sealed record DeleteOrderRequest(string Version);
/// <summary>Bounded, nonpersisting calculation of incomplete order lines and editable expenses.</summary>
public sealed record DraftCalculationRequest(string MetadataVersion, List<Dictionary<string, string?>> Lines,
    List<Dictionary<string, string?>> HeaderExpenses, List<List<Dictionary<string, string?>>> LineExpenses, string? DocumentId = null,
    string? VoucherType = null);
/// <summary>Calculated fields and expenses only; no identities, workflow changes or numbering reservations.</summary>
public sealed record DraftCalculationResponse(Dictionary<string, string?> Header, List<Dictionary<string, string?>> Lines,
    List<Dictionary<string, string?>> HeaderExpenses, List<List<Dictionary<string, string?>>> LineExpenses);
/// <summary>Lossless quantity tolerance input; null percentage means use the explicit quantity.</summary>
public sealed record ToleranceRequest(string? Quantity, string? Excess, string? Shortage, string? ExcessPercent, string? ShortagePercent);
/// <summary>Calculated tolerance quantities and upper/lower fulfillment limits.</summary>
public sealed record ToleranceResponse(string Excess, string Shortage, string UpperQuantity, string LowerQuantity);
/// <summary>Single metadata activation decision; changes are audited.</summary>
public sealed record ActivateFieldRequest(string Section, string Column, string Label, string Permission);
public sealed record NavigationResponse(IReadOnlyList<NavigationModuleResponse> Modules, IReadOnlyList<string> Warnings);
public sealed record NavigationModuleResponse(string Key, string Label, IReadOnlyList<NavigationItemResponse> Items);
public sealed record NavigationItemResponse(string Key, string Label, string? ParentKey, int Order,
    string Href, bool Implemented);
/// <summary>Presentation of one allowlisted column; array order defines display order.</summary>
public sealed record ListingColumnPreference(string Id, bool Visible, int Width);
/// <summary>Versioned per-user listing settings used by preference GET and PUT.</summary>
public sealed record ListingPreferences(int SchemaVersion, IReadOnlyList<ListingColumnPreference> Columns,
    string Density, int PageSize, string SortColumn, string SortDirection);

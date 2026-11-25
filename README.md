# PaperSoft Sales Order POC

Next.js/React frontend and a modular ASP.NET Core 10 API. The POC uses real ERP credentials, a protected dashboard, permission-aware Sales routes, SQL-backed orders and approved metadata-driven fields.

The compact workspace pairs a scrollable charcoal desktop module rail with a white collapsible submenu, pale-blue selected pages and a blue active-module accent. Small screens retain the nine-dot module launcher and drawer rather than consuming entry space with an extra rail. The top-right profile menu uses a local sample avatar. Navigation follows `User_Permission.ReportFor` placement and `Parent` relationships, including Customized menus placed inside Sales. All active granted modules are shown; unimplemented transaction menus open an explicit informational page, not a simulated editor. Missing-parent entries remain hidden and surface a configuration warning.

**Company workspace:** verified credentials lead to company/financial-year selection before any modules load. Choices follow the Windows `Company`/`UserSite` rules: exact `SA`/`SUPER` identities see all companies; other users see only their company and site grants. One authorized site is automatic; multiple sites require selection. Existing company records represent company/year pairs; missing years are not invented. Only company 26 / FY 2026-27 / HO is write-approved; other available contexts are read-only.

The header company button opens current company/year dates, site, access mode and whitelisted company details. Nonblank site trading-name/address/PAN/TIN fields override registered details as in Windows. Change company/year/site is also available in the profile menu. Leaving a dirty order uses the existing unsaved-changes guard. Selection persists in the server session, while each browser tab retains a context revision: another tab's switch rejects subsequent stale requests with a visible warning instead of applying an old draft to a different context. Navigation, grants, listing preferences, lookups and order state reload on workspace entry. Sales queries enforce selected site, financial-year prefix and actual start/end dates server-side.

Successful sign-in, workspace selection and sign-out intentionally use full-document navigation rather than cached client routing. This clears retained page/provider state at identity and workspace boundaries. Selection/sign-out also clear their busy state in `finally`, so returning to the selector cannot leave every control disabled after a previous successful operation.

Green plus shortcuts in the Sales submenu/header and order toolbar open the same Add New workflow; they appear only for an authorized, implemented transaction with Add rights. `RecordCreateLink` shares their styling and accessible labels, while `menuCreateHref` keeps create-route eligibility explicit. The list toolbar provides working search, clear-search and refresh icons, clickable order references and previous/next navigation. Refresh preserves the applied search/page; clear-search returns to page one. Unimplemented export, bulk actions or other module creation flows are not represented by decorative buttons.

**Personal listing settings:** the toolbar settings icon opens column visibility/order/width, compact/comfortable spacing, rows per page and default sorting. Apply saves to `EcoTech_Comp.webpoc.ListingPreference`, keyed by authenticated ERP username + company + listing; Cancel makes no changes. Reset defaults removes only your preference. Settings follow the same account/company on another browser/device on its next list opening/reload; simultaneous edits use last explicit save wins, not live synchronization. Open remains available even when Reference is hidden. At least one data column must stay visible; widths are bounded to 80–600 px.

The shared listing settings component, schema/helpers and loading hook can serve future listing adapters. Preferences load once per listing mount with one indexed lookup, not on search/refresh, and save only on Apply or explicit Reset. Table presentation stays client-side; sorting and paging remain in the authorized SQL query, returning only 10/25/50/100 rows. Defaults remain 25 rows and date descending, with a stable DocId tiebreaker. Reference sorts as text, gross as SQL numeric, state as unapproved/approved. Unknown columns/sort keys and invalid settings are rejected, not silently applied. A load failure offers Retry/Reset rather than overwriting a user's settings with defaults. No business-table DDL, full aggregate downloads, column-level permission changes or persisted search filters are involved. Larger-data sort/index tuning still requires representative workload measurements.

Order items use a metadata-driven editable table. Each item owns its values and expenses under a stable client key; **Tax** opens that row's expense dialog. **Apply to row** updates the draft, **Cancel** discards dialog changes, and **Save order** commits the complete aggregate atomically. Removing an earlier row cannot reassign its expenses to another item. Lookup, lossless numeric and native date controls share the existing metadata contract; horizontal grid scrolling keeps the page contained.

**Item-table screen fit:** only the Sales Order item table uses compact controls, wrapped headings and icon-only tolerance/Tax actions with accessible labels and tooltips. A metadata-driven `colgroup` reserves fixed space for row/action cells and shares the remaining width proportionally, giving Item/Description more space than numeric/choice fields. All columns remain present; the table fills its container above its calculated readable minimum and scrolls locally below it. Additional approved fields automatically participate in sizing and increase that minimum. Item and Tax/action sticky offsets stay aligned at every width. Other grids, order details, totals, lookup result grids and navigation density are unchanged.

**Quantity tolerance:** one per-row slider icon (**Edit / View quantity tolerance**) replaces the excess/shortage grid columns. The reusable `QuantityToleranceEditor` shows order quantity, excess/shortage percent or quantity and upper/lower limits. Editing a quantity switches that side to quantity mode; entering a percentage (including zero) uses percentage mode, rounded to 3 decimal places. Apply updates only the selected stable row's existing `Tl_ExcessQty` / `TL_ShortageQty`; Cancel changes nothing. No Apply-all behavior was requested. Locked/read-only records remain view-only, and dialog edits participate in unsaved-change guards. Negative tolerances and shortages above weight are rejected by both popup calculation and Save; Save errors reopen the affected popup.
**Item totals:** a compact strip immediately below the grid shows **Total quantity (reels)** and **Total weight**, matching Windows `LblQtyAmt`/`LblWeightQty`. It stays outside the grid's scroll area and updates from current active draft rows, including subsequent server-calculated weights/reels. Exact decimal arithmetic preserves the Windows two-place running-total display rounding without altering stored values or money calculations. Untouched entry rows do not count; invalid/negative edits explicitly show Check values rather than a misleading total. Web reference (`WebPocReference`) and Web measurement (`WebPocMeasure`) are hidden only in the Sales Order editor; metadata, existing values and save/calculation payloads remain intact.

**Quantity tolerance:** one per-row slider icon (**Edit / View quantity tolerance**) replaces the excess/shortage grid columns. The reusable `QuantityToleranceEditor` shows order quantity, excess/shortage percent or quantity and upper/lower limits. Editing a quantity switches that side to quantity mode; entering a percentage (including zero) uses percentage mode, rounded to 3 decimal places. Apply updates only the selected stable row's existing `Tl_ExcessQty` / `TL_ShortageQty`; Cancel changes nothing. No Apply-all behavior was requested. Locked/read-only records remain view-only, and dialog edits participate in unsaved-change guards. Negative tolerances and shortages above weight are rejected by both popup calculation and Save; Save errors reopen the affected popup.

**Automatic calculations:** after 400 ms without another draft edit, `/api/orders/draft-calculation` calculates incomplete lines and editable expenses without requiring a party, PO or complete header. It never reserves numbers, writes business data or publishes tasks. Abort/stale-input guards preserve newer edits, stable item keys, focus and untouched entry rows. The Tax dialog previews edits through the same endpoint; Apply uses the current result, and Cancel leaves the order draft unchanged. Errors are visible and field-bound. Save/Validate still perform full authorization, lookup, date, metadata, quantity and mandatory-expense checks.

**Sales Order tax:** the shared calculation reads `ExpenseLine` and `ExpenseDet` rules for `SORD`. Rules can calculate tax on a discounted line, roll up rounded line tax into matching header heads, or calculate a separate header tax. Only allowlisted Sales Order rate/amount targets can be written. The current development configuration has no `SORD` tax heads; do not activate tax without reviewed accounting rules and fixtures. This change does not enable ledger posting.

**Voucher type:** new Sales Orders list active vouchers in category `SORD`. Switching type loads the selected voucher's provisional numbers, expense rules and terms; a dirty draft requires confirmation before it is replaced. `SORD` is save-enabled and `SODRL` is preview-only. A saved order's voucher type cannot be changed.

If New Order opens with voucher selection unavailable after updating the portal, rebuild and restart the local API. Next.js hot reload does not update an already-running .NET process.

`SalesLineCalculator` is the shared server-authoritative decimal rule used by draft previews and Save. It follows archived Windows Sales behavior: MM/CM/INCH conversion, zero-counterpart-only weight/reel derivation, secondary weight **multiplied** by item conversion (3 decimal places), weight times manual rate (2 decimal places), and `Enviro.Sl_Order_Weight_round_to_places` for reel-derived weight. VB Format-style midpoint rounding is away from zero, also used by the configured expense calculator. Discounts/AgreedRate remain driven by existing allowlisted expense definitions; enabled or selected price-list workflows are still deferred rather than approximated. The reusable decimal tolerance rule and debounced-request helper can serve other transaction adapters; no JavaScript floating-point money formulas, dependency additions or SQL schema changes are involved.

The shared `components/expense-editor.tsx` renders both order expenses/totals and item Tax dialogs. Expense labels take the flexible column; percentage/value and amount columns stay compact and right-aligned (80/112 px on small screens, 96/128 px on larger screens, including cell padding). Numeric values remain lossless editable strings, with existing per-field editability and validation highlighting. Calculated values remain readable when disabled; no totals, rounding or financial rules are changed. Future modules can pass the same expense-definition rows, change callback and optional validation-path/error mapping without importing Sales Order code.

Thin native scrollbars are styled globally in `app/globals.css` for the page, menus, tables, dialogs and other scrollable controls, with stronger hover/keyboard-focus feedback. Shared color variables adapt to dark mode and the charcoal module rail; high-contrast mode retains system colors. The item table additionally uses `components/grid-scroll-viewport.tsx` for stronger feedback during scrolling and left/right shadows indicating remaining horizontal content. Shadows disappear at the corresponding boundary; resize/content measurements keep them current. Native mouse, touch, keyboard and scrollbar dragging remain intact. Fixed Item/Tax columns, validation focus and automatic entry rows are unchanged. New scroll containers inherit the global styling without a module-specific class.

Sales Order item searches include only `ItemMast.ItemType='FM'` (Finished Mtrl./Finished Product), retaining existing site restrictions and bounded search results. Validate/Save enforce this for new or replaced items and return a field error on the affected Item cell. As approved, historical non-FM items may remain only when unchanged on the same persisted document/line identity; copying one to a new line or replacing it with another non-FM item is rejected. Existing conversion, quantity, permissions and other lookup checks still apply. Item master data and existing orders are not migrated automatically.

**Add New numbering:** the defaults include the next order reference and voucher number for the current site/voucher/FY prefix. These are visibly **provisional**, not reserved. Automatic references are recalculated under the existing transaction/numbering lock on Save; concurrent changes are reported to the user. Editing the reference selects manual mode, with the existing duplicate check; clearing it restores automatic mode. The voucher number is server-owned and read-only, including after reopen/calculation. No numbering reservation schema or legacy changes are required.

Writable item grids start with one trailing blank entry row. Editing any of its fields activates it and creates exactly one successor, without moving focus. Untouched entries are omitted from calculation/save, while partially edited rows remain subject to normal validation. Applying changed expenses also activates an entry; cancelling its dialog does not. The 100-line limit applies to actual items. **Add line** is retained as a shortcut to focus the existing entry. The generic entry-row helpers are in `lib/entry-rows.ts`; `DocumentNumbering` centralizes the server-owned table/site/voucher/prefix query so other aggregate adapters can reuse the behavior.

Validation responses include structured `errors` paths alongside the existing diagnostic reference. The shared field controls show a compact red border/help text and accessible descriptions; Validate/Save focuses and reveals the first affected control. Line-index errors bind to stable item keys, not blank entry rows. Expense errors open the relevant row's Tax dialog. Secondary-weight conversion errors highlight both Item and Secondary weight: select an item with a configured conversion, or use zero Secondary weight and enter Weight directly. No item-master values are changed automatically.

See the [PRD and acceptance record](docs/prd/sales-order-poc.md). This is a **development-only POC, not production-ready or full Sales parity**.

## Tasks, notifications and configurable approval

The dashboard Tasks table and header notification bell share a permission-filtered inbox. Memo/Note tasks open plain-text dialogs; transaction tasks open the existing record component through registered server adapters, never a database-supplied executable URL. Both task and record access are rechecked on open. The inbox is scoped to the selected company/year/site, returns 25 rows per page, and records read receipts separately for each user. One shared provider refreshes the inbox/type catalog every 45 seconds while visible, on return to the tab, and after local order/task actions; it is not push messaging or email.

The initial group is SA/SUPER for company 26 / HO. Database tables `webpoc.erpTaskGroup`, `erpTaskGroupMember` and `erpTaskTypeGroup` configure audiences, manual publishing and action rights. Direct creator notifications supplement group audiences without bypassing company/site or source-record View/date restrictions. Memo/Note publishing uses `/api/tasks`; transaction and approval tasks are published by trusted transaction services, not arbitrary client-supplied target URLs, recipients or approval states.

New web Sales Orders atomically create a creation notification and a pending approval task. Unapproved edits cancel outstanding tasks/requests and submit a new approval revision; rejection requires a reason, notifies the creator and leaves the order editable for resubmission. Existing orders are not bulk-backfilled, and the Windows application has not been changed to publish events.

`erpApprovalFlow` selects the workflow by company/site/voucher type; `erpApprovalFlowStage` defines 1-20 contiguous sequential group stages. A request snapshots stage names, groups and self/repeat-approver policy, so later definition edits do not rewrite in-flight decisions. Any eligible member may decide the active stage; future stages are not actionable. Membership/action grants are rechecked at decision time, alongside the existing ERP View and **Approved By** permissions. Only the final stage updates `SOrd.ApprovedBy`; intermediate stages never unlock downstream approval-dependent processing. Final approval preserves the existing edit/delete lock. Controlled unapproval is described below; parallel/unanimous approval, amount routing, reminders and escalation remain excluded.

The default is **one stage**, SA/SUPER, with self-approval allowed for this POC. [Optional two-stage configuration example](backend/Techno.Paper.Api/sql/006-sord-two-stage-example.sql) previews changes by default; explicitly enabling it configures SA sales review followed by SUPER finance review and disallows repeating an actor. It has **not** been applied automatically. Configuration changes require reviewed SQL and are not an end-user administration screen.

`ITaskService.SubmitDocumentAsync` participates in the caller's SQL transaction, while `ITaskTarget` isolates each module's visibility/date rules and safe deep-link generation. Sales Order is the first implemented adapter. Future Sales Invoice/Dispatch integrations must register their own adapters and invoke the shared publisher from their transaction services; merely adding a task-type row does not grant record access or implement another module.

## Reusable record actions, copy and controlled unapproval

The Sales Order editor uses a shared sticky `RecordActionBar`: Save remains primary; Options groups Edit (focus the first editable field), Copy to new order, Validate/calculate, New blank order, eligible approval decisions and Delete. Callers supply typed action definitions, permissions, disabled states and callbacks, so future modules can reuse the control without importing Sales logic. It uses the existing accessible dropdown/popover primitives, blue accents and wrapping mobile layout. Item-row/Tax-dialog controls remain local to their rows.

Record details shows the server-recorded Created by, Modified by and Approved by ERP usernames. Missing legacy values say **Not recorded**; full personal names or historical timestamps are not invented. Modified by means the last document save, while approval/reversal actors remain in workflow history. These values never enter the editable save payload.

Copy requires View/Add and the selected writable workspace. It returns an unsaved draft using current authorized metadata, expense definitions and terms. Business values, items, editable taxes and terms are copied; document/line identities, allocations, derived totals and audit/workflow identities are not. Numbers are fresh and provisional, date fields use fresh defaults, and the customer PO reference is cleared and must be entered before Save. Source approval is not inherited. Normal FM-item, lookup, calculation and field-permission checks apply on validation/save. Copy does not reserve a number, save a record, publish a task, or cross company/year/site boundaries.

Unapprove requires a separate active scoped `erpTaskTypeGroup.CanReverse` grant **and** ERP approval rights, source View/save-date access, a writable workspace, the latest document/workflow versions and a reason (1-2000 characters). Closed headers/lines, downstream use/ledger postings and reel allocations remain blocked. It clears only `SOrd.ApprovedBy`, cancels outstanding tasks and the old request, retains stage decisions, appends an immutable `erpApprovalReversal` history row, notifies recipients and audits the command atomically. No new approval is actionable until the next edit/save submits a new request from stage one. Approved legacy orders without web requests use the same permission/safety gates and nullable request versions.

Deploy [007-approval-reversal.sql](backend/Techno.Paper.Api/sql/007-approval-reversal.sql) after 005. The initial SA/SUPER group receives CanReverse only when the column is introduced; rerunning the migration does not restore revoked grants. Other groups default to denied. Reviewed SQL remains the configuration surface; PDF/email/attachments/journal actions are intentionally absent rather than decorative placeholders.

## Approved environment

- SQL Server: use your approved development instance (`YOUR_SQL_SERVER` in the examples).
- `EcoTech_Comp` and `EcoTech_main` were explicitly designated disposable development databases by the owner. Authentication, synthetic CRUD and the reviewed support/demo schema changes were approved there; this is an exception to the original clone-only plan.
- Write-approved context: company 26, FY 2026-27, HO, SORD. Other authorized company/year records are selectable read-only. The nine existing records currently share `EcoTech_main`. Sites are HO and the owner-requested `D1` / `D2` demo sites (Demo Site 1/2 (POC)); SA/SUPER can select all three, while ordinary users retain their existing site grants. Demo sites are read-only and have no seeded orders. Future records pointing at an unconfigured database are shown unavailable, never connected dynamically.
- ERP user `SA` is the approved metadata administrator; it is **not** a SQL login requirement. Never enter an ERP password in source or chat.
- The legacy ERP and shared-library source trees are unchanged.

Do not point this application or its tests at a production/shared database. The API refuses non-Development startup; write commands additionally require explicit configuration approval. Those checks do not replace operator review of connection strings and SQL permissions.

## Setup

Prerequisites: .NET 10 SDK, Node.js (validated with 26.5), npm and SQL Server access. SQL compatibility level 100 is supported.

From the repository root:

```powershell
npm ci
dotnet restore .\backend\Techno.Paper.Api.Tests\Techno.Paper.Api.Tests.csproj
$api = '.\backend\Techno.Paper.Api'
if (!(Test-Path "$api\appsettings.Local.json")) {
    Copy-Item "$api\appsettings.Example.json" "$api\appsettings.Local.json"
}
```

Edit the ignored local configuration: approved connection targets, Windows integrated authentication, write-approved company/site, browser origin and metadata administrator. `Poc.Company`/`Poc.Site` define the write approval, not an implicit selection after login. Set `DevelopmentWritesApproved` to `true` only after review. Keep credentials server-side. The example validates SQL certificates; `TrustServerCertificate=True` was used only for the approved local SQL server's self-signed development certificate.

If npm fails with a corporate certificate-chain error, use the Windows trust store for that process: `$env:NODE_USE_SYSTEM_CA='1'`. Do not disable TLS validation.

### Explicit database deployment

Review targets and scripts first. These commands were already applied in the approved environment; startup never runs migrations:

```powershell
$server = 'YOUR_SQL_SERVER'
sqlcmd -S $server -d EcoTech_Comp -E -C -b -i .\backend\Techno.Paper.Api\sql\001-support.sql
sqlcmd -S $server -d EcoTech_main -E -C -b -i .\backend\Techno.Paper.Api\sql\001-support.sql
sqlcmd -S $server -d EcoTech_Comp -E -C -b -i .\backend\Techno.Paper.Api\sql\003-listing-preferences.sql
sqlcmd -S $server -d EcoTech_Comp -E -C -b -i .\backend\Techno.Paper.Api\sql\004-session-company-context.sql
sqlcmd -S $server -d EcoTech_main -E -C -b -i .\backend\Techno.Paper.Api\sql\005-tasks-and-approvals.sql
sqlcmd -S $server -d EcoTech_main -E -C -I -b -m-1 -i .\backend\Techno.Paper.Api\sql\007-approval-reversal.sql
sqlcmd -S $server -d EcoTech_main -E -C -I -b -m-1 -i .\backend\Techno.Paper.Api\sql\008-value-help.sql
sqlcmd -S $server -d EcoTech_main -E -C -I -b -m-1 -i .\backend\Techno.Paper.Api\sql\009-buyer-windows-layout.sql
sqlcmd -S $server -d EcoTech_main -E -C -I -b -m-1 -i .\backend\Techno.Paper.Api\sql\010-sales-order-value-help.sql
sqlcmd -S $server -d EcoTech_main -E -C -I -b -m-1 -i .\backend\Techno.Paper.Api\sql\011-simple-masters.sql
sqlcmd -S $server -d EcoTech_main -E -C -I -b -m-1 -i .\backend\Techno.Paper.Api\sql\013-master-family-and-audit.sql
sqlcmd -S $server -d EcoTech_main -E -C -I -b -m-1 -i .\backend\Techno.Paper.Api\sql\015-environment-settings.sql
sqlcmd -S $server -d EcoTech_main -E -C -I -b -m-1 -i .\backend\Techno.Paper.Api\sql\016-voucher-term-lookup-permissions.sql
# Optional demonstration, separately approved and already applied here:
sqlcmd -S $server -d EcoTech_main -E -C -b -i .\backend\Techno.Paper.Api\sql\002-extension-demo.sql
```

`-C` is the local self-signed-certificate exception. The scripts add namespaced session/metadata/idempotency/audit/preference/task/approval tables; script 002 adds two nullable demonstration extension columns. The preference, session-context and task deployments are additive, serialized, transactional and safe to rerun; they never run at startup. Script 004 adds nullable company/site/revision fields to the support session table; existing sessions must select a workspace. Script 005 adds the normalized task/approval schema and explicitly matches the legacy document-key collation. No broad VB maintenance, credential changes or existing business-record repairs are performed. There is intentionally no destructive down migration.

## Run

Voucher terms use the existing Windows ERP tables. No additional terms table or app setting is required. Apply [016-voucher-term-lookup-permissions.sql](backend/Techno.Paper.Api/sql/016-voucher-term-lookup-permissions.sql) before deploying the API; it sets up column-level exposure permissions, not term configuration. See the [Sales Order technical design](../.github/docs/technical-design/sales-order.md#voucher-terms) for supported lookups.

API, terminal 1:

```powershell
dotnet build .\backend\Techno.Paper.Api\Techno.Paper.Api.csproj
Set-Location .\backend\Techno.Paper.Api
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet .\bin\Debug\net10.0\Techno.Paper.Api.dll --urls http://localhost:5080
```

Portal, terminal 2, from repository root:

```powershell
npm run dev -- --hostname localhost --port 3000
```

For a compiled preview instead, run `npm run build`, then `npm run start -- --hostname localhost --port 3000`.

Open [localhost:3000](http://localhost:3000), sign in with an authorized ERP username/password, select a company/year and site if needed, then open Sales orders. The same-origin `/api/poc` proxy reaches the API; do not expose port 5080 publicly. Plain HTTP/cookies are a localhost development exception. A production design must use HTTPS, secure cookies, least-privilege SQL access, hardened hosting and a separate readiness review.

Session idle timeout is 30 minutes, maximum server lifetime eight hours. Logout revokes the SQL session. The retired unauthenticated `/rpc` adapter returns 410.

`GET/POST /api/session` returns `{username,context}` with null context until selection. `GET /api/companies` supplies authorized choices; `PUT /api/session/context` uses the catalog's `contextVersion` as `expectedVersion` (null initially). All module endpoints require `X-ERP-Context` with the selected revision, in addition to the session cookie and CSRF token for writes. Revision conflicts return 409. Historical mode disables business writes and metadata administration, not personal listing preferences.

API contracts: [OpenAPI](http://localhost:5080/openapi/v1.json), [HTTP requests](backend/Techno.Paper.Api/Techno.Paper.Api.http), [DTOs](backend/Techno.Paper.Api/Contracts.cs).

## New fields without deployment

Only approved optional scalar columns on `SOrd`/`SOrdDet` are eligible. Newly added columns are **not** automatically public.

1. Apply reviewed database DDL outside the application.
2. As the metadata administrator, GET `/api/poc/metadata/discover`.
3. POST `/api/poc/metadata/activate` with a current CSRF token and, for example:

   ```json
   {"section":"header","column":"WebPocReference","label":"Web reference","permission":"A"}
   ```

4. Repeat for `{"section":"lines","column":"WebPocMeasure","label":"Web measurement","permission":"A"}`.
5. Reopen the editor. Defaults, rendering, validation, GET, create and update share the active contract. Old open forms receive a metadata conflict rather than silently dropping fields.

`A` means authorized Sales users, still subject to action rights; `M` restricts the field to the metadata administrator. Supported extension types: nullable SQL integer types, bit, date/datetime/datetime2, bounded character columns up to 4,000 storage bytes (2,000 Unicode characters), and decimal/numeric up to precision 28/scale 8. Values are scalar strings or explicit null; SQL Unicode lengths, decimal scale and legacy code-page representability are validated. Only literal defaults are interpreted, never SQL expressions.

Keys, relationships, identity/computed/generated fields, original technical columns and sensitive names cannot be activated. Required new fields, relationships, formulas, renames/drops/type narrowing and workflow changes require a separate design/migration. Generic persistence does not infer business meaning.

## SQL-driven Sales Order value help

Following approval of the Buyer pilot, all **11 existing Sales Order lookup fields** use the shared [ErpLookup](components/erp-lookup.tsx). Header fields are Buyer, Consignee, Delivery To, Against Form, Order Type and Excisable. Item-row fields are Item, BF, GSM, Size Unit and Grain. Descriptions are displayed, while the existing save contract retains only the master/enumeration codes. Dates, amounts and other inputs remain unchanged. Missing Windows workflows (city, voucher selection, price lists, amendments and contractual orders) are not added.

F4/Arrow Down opens a searchable table; arrow keys navigate rows and Enter selects. Search text is separate from the selected value, so Cancel/Escape cannot replace a key. Clear removes only the draft selection and normal required-field validation still applies. Desktop uses a viewport-bounded popover; small screens use a dialog with local table scrolling. Exact-key resolution hydrates saved descriptions independently of the first search page, including disabled/read-only fields. Missing references and request failures are visible with Retry; saved keys are never silently cleared.

[008-value-help.sql](backend/Techno.Paper.Api/sql/008-value-help.sql) adds three configuration tables and the reviewed `webpoc.LookupPartySource` view in the company database, without altering legacy tables:

| Configuration | Responsibility |
| --- | --- |
| `LookupDefinition` | Lookup identity/resource, reviewed source view, textual key, site-scope mode, page size and label separator |
| `LookupColumn` | Column order, caption, width, searchability and optional `LabelOrder` |
| `LookupField` | Opt-in binding of an authorized metadata field to a lookup |

After [009-buyer-windows-layout.sql](backend/Techno.Paper.Api/sql/009-buyer-windows-layout.sql), the Buyer table mirrors the Windows Sales Order `FHP_Party` visible columns and pixel widths:

| Windows caption | Source column | Width |
| --- | --- | --- |
| Party Name | Name | 180 px |
| Code | ManualCode | 70 px |
| Address | Add1 + Add2 (null parts become empty strings) | 250 px |
| City | Buyer's CityName | 100 px |
| Distributor | Distributor's Name | 200 px |
| D.City | Distributor's CityName | 100 px |

The displayed **Code is ManualCode**, not the persisted SubCode. SubCode remains the hidden lookup key. Windows' hidden Zone/DZone/CityCode fields are not exposed or used to add new selection side effects. Only Name participates in the default selected description. For a description such as `Name - City`, assign City the second `LabelOrder` and configure the separator; empty parts are omitted. This does not change the persisted code.

Configured widths are retained exactly, totalling 900 px. The generic desktop popup sizes to the configured table plus web padding rather than retaining the old 680 px limit or copying Windows window chrome. Smaller viewports retain these column widths and scroll inside the table; page and footer remain contained.

[010-sales-order-value-help.sql](backend/Techno.Paper.Api/sql/010-sales-order-value-help.sql) adds the remaining definitions/views and field bindings. Consignee and Delivery To share the reviewed Party source and six-column presentation but have their own titles. The other Windows column mappings are:

| Lookup | Visible columns and pixel widths |
| --- | --- |
| Item | Name 200, Code 175, SKU 80, Varity 140, GSM 100, Length 100, Width 100, BFName 60, Grain 40 |
| Against Form | Name 200, Per 100 |
| BF / GSM / Order Type / Excisable | Name 100 |
| Size Unit | Unit 100 |
| Grain | Grain 100 |

The Item Code column displays ManualCode; its hidden key is ItemMast.Code. Varity/GSM/BF labels come from existing master joins; dimensions/SKU/grain are display-only. Finished products remain `ItemType='FM'` under the existing site scope. Form display uses TaxForms and Tax_per, preserving the previous form eligibility. Enumeration views preserve existing codes: R/S/O, Y/N, CM/MM/INCH and L/S; Inch/Long/Short are labels, not replacement stored values. Lookup selection does not add Windows automatic BF/GSM/dimension filling, price-list effects or Buyer-dependent party filtering.

The support-table width check now permits 40-400 px for the exact Windows item Grain width. Single-column popovers have a 280 px minimum to fit search/paging/Cancel without stretching their 100 px table column. Selected labels truncate within their existing grid cell, so long item names cannot expand the frozen Item column and cover adjacent controls. Add line and validation focus support the new combobox buttons.

**Adding an ordinary lookup:** deploy a reviewed `webpoc` source view, a definition, column rows and a binding to an already-authorized field. Reuse the same control/service: no duplicate React lookup or endpoint is needed. Select the appropriate reviewed scope (`list`, `code` or `none`); a definition never grants module access. A new module needs its access-policy adapter once and must expose the binding in its metadata. New business filtering or foreign-key rules require deliberate implementation, not a configuration bypass.

The browser sends lookup IDs, search/page values and keys, **never SQL**. The server checks registered views/columns, parameterizes values, enforces Sales View/current workspace access and bounds search results. The Party source deliberately preserves the existing `SiteList` rule; it does not introduce Windows Nature/Active/distributor filters or change order validation.

Search waits 200 ms and uses server paging (25 rows by default). Description resolution batches up to 100 keys; its 30-second cache is scoped by username/workspace/lookup and bounded to 500 entries. Failures are not cached; Retry invalidates the cache without allowing older in-flight results to overwrite newer descriptions. Configuration edits are reflected by fresh requests/reopening or refreshing the page, not live push updates; cached descriptions may remain for their TTL.

Controls do not fetch a separate definition on mount: search responses already carry table metadata, and exact-key responses supply selected descriptions. Empty controls issue no lookup request until opened. Saved rows use the shared batched resolver, including repeated enumeration keys, instead of a separate metadata request per item field. This bounds client/network work; representative database/concurrency benchmarks and scan/sort tuning are still required before production.

Deploy 008, 009 and 010 in order after the support schema. They use a transaction/application lock and bounded lock wait. Initial 008 seeds do not overwrite later configuration edits, but its old Party source-view definition is reapplied: always run 009 afterwards. Reapplying 009 deliberately restores the approved Buyer columns/widths; review it before replacing customized Buyer configuration. Initial 010 seeds preserve later definition/column edits, while its reviewed source views are redeployed. Conflicting existing field bindings are rejected rather than silently replaced. To withdraw a lookup, remove its field binding through reviewed SQL and reopen the editor; do not drop configuration or business data. Configuration administration and production least-privilege certification remain outside this POC.

## Verification

### BF/GSM master pilot

[011-simple-masters.sql](backend/Techno.Paper.Api/sql/011-simple-masters.sql) deploys the approved first master slice. Existing BF/GSM menus now open `/dashboard/masters/bf` and `/dashboard/masters/gsm`; the same [workspace](components/erp-master-workspace.tsx), [right-side form](components/erp-master-drawer.tsx) and [API service](backend/Techno.Paper.Api/Services/MasterService.cs) serve both. Existing lookup grids/Windows widths stay unchanged. Only BF/GSM lookups offer **Add new**, only with the corresponding master Add grant in a writable POC context. Item, Party and fixed enumeration creation remain deliberately unavailable.

Windows rules retained: internal six-character keys use the two-character site prefix plus an unpadded numeric suffix; Name is required and limited to 35 characters even though GSM's SQL column remains nullable; duplicate names are rejected under the table's SQL collation. Add writes `U_Name/U_EntDt/U_AE='A'`; Edit preserves Code, updates these fields and resets `Transfered='N'`. Referenced masters cannot be deleted. BF/GSM are global within the business database, not restricted by financial year/site. The confirmed future Party-all-sites and Item-current-site policies are not implemented in this slice.

Explicit web improvements: trim surrounding whitespace and collapse repeated spaces; allocate keys transactionally with range locks and retain portal-audited key history to avoid recycling deleted portal keys. Database unique-name indexes protect both web and legacy writers; deployment aborts on pre-existing duplicate names rather than repairing them. Additive `WebPocVersion` rowversion columns detect edits made by either Windows or web clients; audit timestamps are not concurrency tokens. Old VB source, existing master values/keys/nullability and transaction formulas are not rewritten. Index/rowversion compatibility must be reviewed before any production deployment.

Master authorization reuses the existing menu visibility/ancestor/system-restriction rules and the master's own Add/Edit/Delete/Print grants, never Sales Add permission or Windows' missing-grant `AEDP` fallback. Commands capture the tab's context revision and the selected POC write restrictions. Actor identities are server-owned; portal create/edit/delete events retain approved before/after scalar values. Saves are atomic and idempotent; the drawer retains an uncertain request unchanged for retry. Save is independent from the order: cancelling an order does not delete its newly created master.

Successful lookup creation re-resolves the saved key through the originating authorized lookup before selection. Missing/ineligible results are explicitly reported as **already saved but not applied**, with retry rather than a second creation. Scoped cache invalidation refreshes the affected lookup's mounted descriptions without clearing other lookups/workspaces; stale responses cannot repopulate old labels. Cancel preserves lookup search/page, the prior code and the unsaved order. Drawers support keyboard focus, guarded cancellation and viewport-local scrolling.

Registry: `MasterRegistration` selects an enabled active revision; immutable-by-convention `MasterDefinition`/`MasterField` revisions describe reviewed tables/fields/menu mappings; `LookupMaster` binds creation entry points. `MasterCommand` and `MasterAudit` hold command results/history. The API validates physical columns, supported scalar types, defaults, primary/unique indexes and rowversion. No browser SQL/table identifiers, automatic column exposure, generic FK writes or metadata-administration UI are provided.

**Adding fields:** deploy a nullable supported scalar column, copy the active definition/fields into a new revision, add the approved field and atomically activate that revision. [012-master-field-example.sql](backend/Techno.Paper.Api/sql/012-master-field-example.sql) is an optional BF Remark example, **not deployed by default**. Reopen forms after activation: incompatible old metadata is rejected on save. Removing a field means publishing a revision without it, not dropping the column; omitted/unexposed stored values survive edits. New dependencies, scope fields, calculations and complex aggregates need deliberate handler work, not metadata overrides. Never mutate published revisions or run schema deployment on application startup.

View/Add/Edit/Delete/Print/Exit use the shared record-action control. Print offers an authorized filtered list preview and browser Print/Save PDF, capped at 1000 records with an explicit filter-required error above the cap. This is a web template, not Crystal Reports pixel parity. Listing search is debounced, parameterized and paged at 25 rows; performance validation is functional/bounded, not production load certification.

Focused master verification:

### Expanded Windows single-field family and audit history

[013-master-family-and-audit.sql](backend/Techno.Paper.Api/sql/013-master-family-and-audit.sql) adds nine reviewed Master-module registrations to BF/GSM, using the same API/workspace/drawer:

| Route suffix under `/dashboard/masters/` | Description limit | Business scope |
| --- | ---: | --- |
| `bf`, `gsm` | 35 | Global |
| `varity` | 25 | Global |
| `godowns` | 20 | Current site |
| `departments` | 35 | Current site |
| `classifications` | 30 | Global |
| `item-makes` | 35 | Global |
| `quality-specifications` | 35 | Global |
| `rejection-reasons` | 50 | Current site |
| `deductions` | 35 | Global |
| `machines` | 35 | Global; basic Master-module form only |

Windows' shorter Godown/Classification/Machine input limits are configured separately from physical SQL column lengths using `MasterField.MaxLength`. New revisions preserve these limits and `MasterDefinition.Scope`. Site-specific list/get/print/edit/delete/history queries enforce the selected site; Name uniqueness is per site. Site is server-owned and inserted automatically. Existing rows with missing sites are not reassigned or exposed across sites. Machine's existing 10-character primary key is retained; other registered keys remain six characters. Audit/command record-ID storage is widened to ten characters without changing nullability, data or primary keys.

Unit is intentionally not registered: `UnitMast.Unit`, not its generated `Code`, is the physical primary key, so description edits are relationship changes. Designation, Bin, Tariff, VAT, Item Category/Group, Policy, Item and Party remain deliberate future handlers; they have additional fields, validations, relationships or dependent writes. Their existing menus remain marked unavailable. No Machine-module full equipment screen is replaced by the basic Master-module registration.

**Audit:** detailed portal old/new-value capture defaults **on for all 11 masters**. [014-master-audit-policy.sql](backend/Techno.Paper.Api/sql/014-master-audit-policy.sql) is a reviewed per-master configuration template: select `@MasterId` and `@CaptureValues`. This is administrator SQL configuration, not a new browser permissions/configuration screen. Changes apply to subsequent commands and stale form metadata must be refreshed. Existing history is never removed by disabling capture. Minimum actor/time/operation history remains on for reliable creator attribution and key allocation; normal records and idempotent response snapshots still contain business values.

The Options menu offers **Audit selected master** or **Audit trail** to the configured `Poc:MetadataAdministrator` when that user also has the master's View access. `GET /api/masters/{id}/audit?recordId=&page=0` repeats authorization server-side, pages 25 newest events and scopes site masters. The shared audit drawer shows approved field old/new values, timestamp (stored UTC/displayed locally), changed-by, operation, company/site and metadata revision. Value-capture-off events are explicitly identified. Read-only contexts can inspect authorized history but cannot write.

This captures **portal commands only**, not all Windows/SQL changes, historical backfill, deleted hidden-column values, unrestricted row snapshots or tamper-proof regulatory auditing. Hidden approved values remain stored but are not rewritten by unrelated edits. Menu/action grants are enforced; full field-level legacy UCP parity remains outside this simple-master slice. Retention, cross-client capture and additional auditor roles need a separate approved design.

[AGENTS.md](AGENTS.md) records the implementation/maintenance requirements. The optional [012 field example](backend/Techno.Paper.Api/sql/012-master-field-example.sql) now requires 013 and preserves scope/limits while publishing a revision.

### Environment Setting (Windows Master form)

Open **Master → General → Environment Setting**, or `/dashboard/masters/environment`. This is a dedicated singleton settings handler, not name-only master CRUD. Its **Gate, Sales, Excise, Purchase, CostSheet and Stores** tabs cover all **62 persisted controls and 48 required controls** from `Master.FrmEnviro`, plus the disabled, nonpersisted Kanta “Value” display and conversion example.

- Gate: EXE/data/background-image paths, weight conversion/operator/unit, negative-stock and party-required rules.
- Sales: Inch/CM conversion, invoice notification/reel/billing/book settings, sheet/rim/order rounding, pricing, email/SMS, credit/policy checks, three production-designation selections and invoice/Kanta excess/shortage.
- Excise: all twelve second-half-of-capital, input and capital Excise/Cess/EduCess/SHCess account selections.
- Purchase: PO/GRN/bill/unit/indent rules, transporter/unloader posting, impurity formula, Form 38, open-rate vouchers and last-rate categories.
- CostSheet: Salary/Freight/Interest/Tax group multi-selections. Empty/“All” does not satisfy required costing groups.
- Stores: stock-issue department-mandatory setting.

The existing City, Unit, SubGroup, AcGroup, Designation and Voucher tables drive the SQL lookup controls. Environment multi-selects open one searchable checkbox grid, pre-check existing codes and retain draft picks across pages. The header checkbox affects only the current results page. Apply changes the form draft, Cancel leaves it untouched, and Save settings persists canonical `|code|,|code|` values while the field shows comma-separated descriptions. The controls use Windows-derived desktop widths and stack or shrink to the available width on smaller screens. Open-rate vouchers retain the Windows `PORD`/`PORDR` filter. No lookup master is fabricated when an optional source is empty. Existing unavailable references/formats remain explicit and are not silently replaced.

[015-environment-settings.sql](backend/Techno.Paper.Api/sql/015-environment-settings.sql) publishes the reviewed definition, field/layout/choice metadata, seven resource-authorized lookups, Enviro rowversion and unique non-null site index. Duplicate sites abort deployment without repair. It was applied twice successfully. The portal exposes only reviewed Master-form columns: unrelated email/payroll credentials and other modules’ environment tables are never read into the form or written.

Site and numeric two-character ID are server-owned; new settings use the Windows numeric ID rule with a 1–99 bound. The form provides View/Edit/Save/Cancel/Exit and administrator audit, not fake Add/Delete/Print operations over a singleton. Server permissions use **Master:MnuEnvironmentSetting** and its ancestors/restrictions, independently of Sales, with current approved development-company/site gating.

All Windows-required settings are checked across tabs before save. The web intentionally rejects malformed/nonfinite numbers instead of VB `Val` coercion, does not display misleading Yes/No defaults for stored nulls, and preserves untouched nulls/hidden columns. Existing incomplete configurations therefore require explicit completion. Approved adaptations: paths are editable configuration strings (no browser upload, local browsing or executable invocation), order rounding is **0–6** to match the current Sales calculator, and the existing email-criteria `W` value displays/persists if unchanged but cannot be newly selected (the Windows picker offers A/M only). Unit's help width uses the shared 60px minimum instead of Windows 50px.

Save uses a serializable transaction, parameterized values, record rowversion, metadata fingerprint and stable idempotency key. Old/new snapshots and command response commit with settings; per-master audit policy is configured with 014 using `@MasterId='environment'`. Audit remains administrator-plus-master-View and site-scoped. None of the live settings were changed for verification: successful save/create/replay/audit tests ran inside rolled-back transactions, with a public pre-commit failure test confirming rollback.

Verified **49 targeted backend cases**, **34 frontend cases**, zero-warning backend build, TypeScript, targeted lint and production build. Exact source comparison checked every Windows saved column/control and all 48 required controls. Final authenticated browser acceptance needs the owner's private sign-in if the session has expired.

```powershell
node --test .\tests\erp-masters.test.mjs .\tests\master-audit.test.mjs .\tests\lookup-resolver.test.mjs .\tests\poc-api.test.mjs .\tests\order-draft.test.mjs .\tests\order-calculation.test.mjs
$env:PAPER_POC_INTEGRATION = '1'
dotnet .\backend\Techno.Paper.Api.Tests\bin\Debug\net10.0\Techno.Paper.Api.Tests.dll -method 'Techno.Paper.Api.Tests.DatabaseMasterTests.*' -method 'Techno.Paper.Api.Tests.NavigationTests.*'
```

Do not mix class and method filters for this runner: different filter types intersect and can select zero tests. Master tests create uniquely named `master-test-*` records and clean their own rows/command/audit fixtures. The field-revision case temporarily adds one specifically named nullable test column, publishes test revisions, verifies persistence/stale-form rejection/hidden-value preservation and restores the original definition/schema. Run that case without another master acceptance session.

```powershell
node --test .\tests\poc-api.test.mjs .\tests\erp-masters.test.mjs .\tests\order-draft.test.mjs .\tests\order-calculation.test.mjs .\tests\lookup-resolver.test.mjs .\tests\listing-preferences.test.mjs .\tests\workspace-context.test.mjs .\tests\erp-tasks.test.mjs
npx tsc --noEmit
npm run build
dotnet build .\backend\Techno.Paper.Api.Tests\Techno.Paper.Api.Tests.csproj --no-restore
# Unit tests; database tests skip unless explicitly enabled:
dotnet .\backend\Techno.Paper.Api.Tests\bin\Debug\net10.0\Techno.Paper.Api.Tests.dll
# Opt in ONLY against the approved disposable development databases:
$env:PAPER_POC_INTEGRATION = '1'
dotnet .\backend\Techno.Paper.Api.Tests\bin\Debug\net10.0\Techno.Paper.Api.Tests.dll
Remove-Item Env:\PAPER_POC_INTEGRATION
```

Stop the API before rebuilding its binaries on Windows. The xUnit v3 managed DLL runner is intentional: native VSTest/test apphost startup hung on this machine. Frontend tests use Node's native TypeScript stripping.

Live integration tests require the two demonstration columns and the preference/session-context/task/reversal/value-help deployments (003-005 and 007-010). They use random `POC...`/`LP...` references and `WEBPOC-TEST`/`WEBPOC-EDIT` markers, clean their own business aggregates and retain audit/idempotency tombstones. They verify rollback checkpoints, 20 concurrent creates, CRUD, retries/conflicts, no document-number reuse after deletion, extension null clearing and hidden-value preservation. Listing coverage crosses the 10-row boundary with 12 synthetic orders, verifies SQL sorting across pages, preference first-save concurrency, per-user/company isolation, denied access, disabled writes and scoped reset. Context coverage includes pending authentication, explicit selection, stale revisions, unauthorized companies/sites, historical read-only enforcement and site/year data isolation. Task coverage includes audience/type/action rights, receipts, publication retries, workspace isolation, atomic rollback, rejection/resubmission, self-approval policy, stale decisions and multi-stage snapshots/final-only ERP approval. Value-help coverage verifies all 11 bindings and original code/label compatibility, exact Windows item columns/widths and related master values, finished-product exclusion, Buyer code-only persistence, configurable columns/labels/paging, duplicate descriptions, exact resolution beyond the first page, site scope, denied View, parameterized search and explicit malformed-configuration failures; its uniquely named probe view/configuration is removed afterwards. Pure rule tests cover multiple-site selection without creating site master data. Synthetic preference/session identities are removed afterwards. Tests also temporarily vary the approved demo fields' metadata permissions and restore `A`; run without other acceptance activity.

## Deliberate limits

- Manual-price configuration only. Selected-price-list orders, approved/closed orders and orders with downstream use/allocations are conservatively read-only.
- Configured sequential Sales Order approval and controlled unapproval are included; closure, printing, allocation editing and other transaction modules are excluded.
- Existing configuration intentionally produces Gross Amount but zero Bill/Net Amount; this surprising desktop behavior was verified and explicitly approved, not silently corrected.
- Missing voucher-permission rows impose no additional date restriction, matching the approved desktop behavior. Conflicting distinct grant rows fail explicitly instead of guessing precedence; there is no administrator action bypass.
- Full multi-persona browser permission acceptance, complete calculation parity and product-owner sign-off remain required; see the PRD.
- Existing dependencies still reported 11 npm audit findings during installation. No production security certification or unrelated blanket dependency update is claimed.

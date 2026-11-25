# Sales Order Web POC - Product Requirements

Date: 2026-11-19  
Status: Implementation authorized; core development POC verified; product acceptance pending  
Product repository: Techno.Paper.WebPortal  
Reference context: company 26, FY 2026-27, site HO, voucher SORD

## 1. Purpose and outcome

Demonstrate a complete, permission-driven sales-order workflow in the existing React web portal:

1. Sign in with an existing ERP username and password.
2. Resolve the user's permitted company, financial year and site.
3. Land on the dashboard.
4. Open Sales through an authorized module/menu.
5. List, view, create, edit and delete eligible sales orders.
6. Save and reopen the complete supported order in the explicitly approved development databases.
7. Demonstrate a supported new database field appearing in defaults, forms, create, update and get without changing or redeploying application code, after metadata approval.

The POC must prove correctness and a reusable architecture, not simulate persistence or reproduce every desktop screen. Approval of the POC is required before developing the full Sales module.

Legacy application/library source and executable configuration remain unchanged. The owner approved POC CRUD and reviewed supporting schema changes only for the designated disposable `EcoTech_Comp` and `EcoTech_main` development databases. Confirm the approved SQL Server instance with the owner before running any writes. This supersedes the earlier clone-only restriction for those exact development targets only; it is not permission for production/shared-database writes.

## 2. Confirmed decisions

| Decision | Requirement |
|---|---|
| Application location | Keep the POC frontend and ASP.NET Core API source under this repository. |
| Frontend | Evolve the existing Next.js/React/TypeScript application; do not scaffold a replacement SPA. |
| Identity | Existing ERP credential verification is implemented server-side; the owner successfully signed in as ERP SA in the browser. |
| New fields | Discover schema changes automatically; an administrator approves field metadata before exposure. Supported activated fields need no React/API code changes. |
| Module boundaries | Retain desktop-style business module isolation, implemented as maintainable web features rather than copied DLL or global-state patterns. |
| UI | Modern, reusable controls; do not require pixel parity with WinForms. |
| Authorization | Menus, routes, actions, field/control groups and API operations must use effective user permissions. |
| POC lifecycle | Add, edit and delete are included, with legacy business restrictions. Approval, closure and printing execution are excluded initially. |
| Data safety | Only the explicitly approved disposable development targets above; never production/shared targets. |
| Metadata administration | ERP SA explicitly designated as administrator; no action-permission bypass. |
| Missing voucher permissions | Preserve desktop behavior: no extra voucher-date restriction when no matching rows exist. |
| Totals | Preserve verified FullValue/percent behavior: Gross is calculated, current Bill/Round Off/Net remain zero. |
| Demonstration DDL | Additive nullable `SOrd.WebPocReference nvarchar(60)` and `SOrdDet.WebPocMeasure decimal(12,3)` approved. |

The owner subsequently requested end-to-end implementation. The design and acceptance criteria remain the target contract; implemented POC limitations and remaining acceptance work are recorded separately rather than implying full ERP parity.

## 3. Current state and evidence

### Portal baseline before implementation

The inspected repository uses Next.js 16, React 19, TypeScript, Tailwind, Radix-based UI controls, TanStack Table and an oRPC lookup adapter.

| Existing surface | Observed state | POC requirement |
|---|---|---|
| [Login](../../app/page.tsx) | Email/password UI, demo accounts and unconditional dashboard redirect | Replace demo authentication with real username/password verification. |
| [Dashboard shell](../../app/dashboard/layout.tsx) | Sidebar/top navigation and page shell | Add server-backed session/context enforcement. |
| [Sidebar](../../components/app-sidebar.tsx) | Static dashboard/order entries | Derive authorized navigation from effective capabilities. |
| [Sales list](../../app/dashboard/sale-order/page.tsx) | Inventory-shaped sample data and local UI actions | Load and mutate real sales orders through the API. |
| [Detail page](../../app/dashboard/sale-order/[id]/page.tsx) | Sample order lookup; query parameter controls view mode | Load the requested authorized aggregate; never trust view/edit URL flags as authorization. |
| [Editor](../../features/sales-order/SalesOrderEditor.tsx) | Section dirty state; Save validates locally and clears state | Save the complete aggregate and show success only after commit. |
| [Lookup adapter](../../router/index.ts) | External lookup fetch; some failures return empty arrays | Authenticate/scope POC lookups and distinguish failures from valid empty results. |

These are historical inspection findings. The POC now uses real session/order APIs and a new [aggregate workspace](../../features/sales-order/OrderWorkspace.tsx); old prototype components are not on its route. See the [runbook](../../README.md) for the current implementation.

### Legacy discovery basis

- Authoritative ERP source: Windows_Paper_Code, revision `b03caaf875bd22acc3be23a9098f95ab6da20c42`.
- Shared libraries: erp-libraries, revision `a83cec95ed3c2fc6584be13127ee941ab14e46e1`.
- EcoTech_Comp contains company/user/menu/permission configuration; EcoTech_main contains transactional/master data. Their routing relationship is preserved and checked against the configured approved context.
- Orders involve SOrd, SOrdDet, allocations, configured expense structures and terms; a two-table save is insufficient.
- Permission sources include UserSite, User_Permission, User_Control_Permission and User_Voucher_Permission.
- The reference database lacks SOrdDet.ClosedType and Purchase_Closed_types used by current source. Definitions were found in explicit VB maintenance routines.
- That maintenance action synchronizes multiple modules, constraints and initializers. It must not be used as a POC repair shortcut.
- Table catalogs do not constitute complete deployable DDL or proof of runtime compatibility.

Prior discovery deliverables remain the detailed evidence record: Techno-ERP-sales-discovery.md, Techno-ERP-sales-modernization-proposal.md, Sales-schema-drift.json, Sales-discovery.configuration.json and both database schema catalogs. This PRD is self-contained for scope and acceptance; those session-held files are not application runtime dependencies.

## 4. Users and primary journeys

### Personas

- **Sales operator:** capabilities permit creating and editing eligible orders.
- **Read-only user:** can view authorized orders but cannot mutate them.
- **Authorized deleting user:** can delete eligible orders within configured restrictions.
- **Metadata administrator:** can review/activate eligible field metadata through a controlled administrative command; no full administration UI is required.

These are test personas, not new production roles. Effective capabilities come from mapped ERP grants. Metadata administration requires a separately approved explicit grant; possession of Add/Edit rights does not confer it.

### Login to dashboard

- Accept username and password, validate on the server, and establish an authenticated session.
- Select permitted company/year/site when more than one exists; resolve automatically when only one is allowed.
- After successful authentication, land on the dashboard. If context selection is needed, complete it within the protected dashboard before loading business data.
- Display current user, company, year and site in the shell.
- Show only authorized module entries. A valid user without Sales access may see the dashboard but no Sales data or menu.
- Context switching must refresh permissions, metadata, lookups and data; handle unsaved changes explicitly.
- Logout and session expiry must invalidate access and clear context-specific client state.

### Sales workflow

- Open Sales -> Orders; list/search authorized records with server pagination.
- View a record including header, lines, expenses, terms and state restrictions.
- Create from a server-generated default payload and context-scoped lookups.
- Edit eligible records; preserve values outside the user's editable fields.
- Display server-calculated prices, conversions, expenses and totals.
- Save atomically; retain entered values and dirty state if validation or persistence fails.
- Reopen the returned record identity and verify persisted values.
- Delete one eligible order with confirmation; refresh the list after commit.

Bulk deletion is excluded. No action may report success based solely on a toast, a redirect or clearing local state.

## 5. Scope and exclusions

### Required POC scope

- Real username/password authentication; protected dashboard and Sales routes.
- Server-authorized context selection and permission-driven navigation.
- Order list, detail, defaults, create, edit and single-record delete.
- Required master lookups, header/line fields, pricing, unit/weight calculations, configured expenses and terms for selected SORD configuration.
- Approved/downstream-use locks, voucher/date restrictions and group/field restrictions.
- Atomic persistence, conflict detection, duplicate-submit protection and audit evidence.
- Controlled metadata discovery/activation and automatic supported-field round-trip.
- Automated checks plus a repeatable demonstration with sanitized fixtures.

### Excluded from this POC

- Full Sales module, other ERP modules, microservices or a general-purpose ERP framework.
- Approval/reversal, close/cancel/hold execution, printing/Crystal execution, email/SMS and e-invoicing.
- New reel-allocation entry/editing and external production/invoice workflows.
- Full user/permission/metadata designer UI, self-registration and password reset.
- Production/shared-database writes, production migration and unattended schema alteration.
- Automatically generating new relationships, workflows, formulas or business semantics from column names.

Existing approval, closure, downstream-use and allocation data still constrain operations. Preserve existing allocations for unchanged lines. If an edit would invalidate or require changing unsupported allocations, reject it with a specific explanation; never discard them silently. If clone configuration requires an excluded workflow to save a valid SORD order, resolve scope/configuration explicitly rather than weakening validation.

## 6. Recommended architecture and module contracts

Use a modular monolith, not separately deployed services per desktop DLL.

```text
Browser: existing Next.js / React portal
  authenticated dashboard shell
  Sales module / sales-order feature
  shared presentational controls and capability-aware wrappers
                 |
        same-origin authenticated API boundary
        Next.js proxy/BFF if required by deployment
                 |
ASP.NET Core API: one deployment
  session + authorized context + capabilities
  Sales Orders application operations
  calculation/validation rules
  constrained metadata/defaults/field-value support
  parameterized SQL repositories and transaction coordinator
                 |
       approved disposable control + transactional SQL databases
```

### Repository and dependency direction

- Keep Next.js routes as thin entry points; retain the existing order route family.
- Group Sales-specific logic under the existing feature area, with an explicit Sales module boundary as more Sales features arrive.
- The API is under `backend/Techno.Paper.Api`, targeting .NET 10 LTS; it does not reference WinForms assemblies.
- HTTP endpoints call application operations; business rules do not depend on React, HTTP or SQL UI helpers.
- Infrastructure implements persistence/identity integrations; do not import WinForms assemblies.
- Do not move unrelated existing frontend files solely to achieve an ideal directory layout.
- The existing oRPC layer may act as a typed frontend adapter, but must not become a second authorization/calculation/persistence implementation.

### Module registration

A module descriptor provides a stable module key, permitted menu entries, route entry points, labels and required capability keys. The shell uses server-resolved grants to render entries. Frontend registration is not permission evidence.

Sales must load through route-level feature boundaries with explicit loading/error states. A future module must be addable without changing Sales business logic. Do not fetch Sales orders/lookups or initialize its editor while the dashboard is displaying an unrelated module.

### Reusable UI

Reuse or extract existing Button, Input, Select, Dialog, Table and notification primitives. Add shared field rendering, lookup selection, error summary, action toolbar, confirmation dialog and decimal/date display only where actually shared.

The Sales editor remains feature-specific: header, line grid, expenses, terms and totals. One order form state owns the aggregate and dirty tracking; section-local save simulations are removed from the POC path. Support keyboard navigation, labels, accessible validation, visible focus, unsaved-change warnings and desktop/tablet layouts.

Unauthorized actions are hidden; actions unavailable because of record state are disabled with a reason when disclosure is permitted. The API independently enforces both.

## 7. Authentication and authorization requirements

### Authentication

- Inspect the legacy credential verification mechanism without exporting/logging passwords or credential values. Use approved development accounts.
- Implement a narrowly scoped server-only compatibility verifier; do not reference the whole desktop application.
- Never create or reset legacy credentials as an undocumented workaround. If safe compatibility cannot be established, stop this slice and obtain approval for an alternative identity method.
- No production credentials, demo-account bypass, client-side verification or password material in responses, browser storage, logs or source control.
- Use HTTPS and secure, HttpOnly, SameSite session cookies; protect state-changing requests against CSRF, including proxy/BFF paths.
- Use server-managed revocable sessions. Define expiry/idle timeout before implementation; logout invalidates the server session.
- Apply bounded login attempts/rate limiting and generic invalid-credential messages. A database outage must be an explicit service failure, not successful login or empty permissions.
- Restrict browser origins and keep database connectivity/credentials entirely server-side.

### Effective capabilities

The server returns context-scoped capabilities such as View, Add, Edit, Delete, Print and relevant control-group/field grants. This is a proposed API vocabulary, not a new ERP role model.

Evaluate at least:

1. Authenticated user and active permitted company/site/year.
2. Active menu/module access.
3. Action letters and voucher/date windows.
4. Control-group and approved field access.
5. Record state, approval locks and downstream quantity/usage constraints.

Check view permissions on list/detail/defaults/lookups and re-evaluate applicable permissions immediately before every write. A user-supplied identity, company route, database name, capability flag, field name or URL mode is never authoritative.

### Permission policy dispositions

| Ambiguity from discovery | Required disposition before write acceptance |
|---|---|
| Company-specific and global rows can both match | Identical effective grants are accepted; conflicting distinct grants fail explicitly. No guessed precedence; broader parity policy remains future work. |
| Missing voucher-permission rows | Owner approved desktop compatibility: no additional date restriction. Configured financial-year and view windows still apply. |
| Desktop record authentication returns current user, not creator | No invented creator-only edit rule. Existing action/state restrictions apply. |
| AllowValueUpto is not loaded by the inspected desktop path | No new value-limit enforcement is claimed. |
| Metadata administration is new | Owner approved ERP SA as explicit metadata administrator; activation is audited. |

Until ambiguous effective access is resolved, fail closed for affected operations with an explicit configuration error. This is a temporary POC safety gate, not a claim of desktop parity. Do not invent broad admin bypasses.

Permissions must not remain indefinitely stale in the browser. Refresh on context change and re-check server-side per request; revoked rights must block the next applicable API request.

## 8. Metadata-driven defaults and field persistence

### Design principle

Use a **typed order aggregate plus an approved dynamic-field map**. Generic mechanics handle field discovery, defaults, rendering and persistence; explicit Sales rules handle business meaning and lifecycle.

Do not expose every physical database column through reflection or implement a generic "write any table" endpoint. Adding a column cannot safely define a new relationship, authorization rule, formula or workflow.

### Eligible fields and activation

For the POC, automatic field extensibility applies to scalar columns on approved SOrd header and SOrdDet line mappings.

1. An authorized database change adds a column to the approved disposable development database; the application does not issue arbitrary DDL.
2. An authorized schema refresh discovers the delta and proposes metadata.
3. The administrator reviews type, default, permissions and placement, then activates a metadata version.
4. Subsequent requests use that active version; no build or deployment is required.

Provide a minimal authenticated administrative command/API for review and activation. A graphical metadata designer is not required.

The implemented POC supports nullable integer, bit, bounded char/varchar/nchar/nvarchar (up to 4,000 storage bytes), date/datetime/datetime2 and decimal/numeric (precision at most 28, scale at most 8). Only safe literal defaults are interpreted; expression defaults and nonnullable extension columns require a separate reviewed change. Discovery returns physical definitions; activation explicitly rejects unsupported fields/defaults.

Exclude identity/computed/generated fields, keys, relationship columns, technical numbering, audit/approval/security columns and sensitive data from automatic write exposure. Existing float/calculated business fields remain behind explicit tested Sales mappings.

Dropping/renaming columns, narrowing types, adding required fields without an approved valid default, and adding relationships require a reviewed migration/design change. Database-only columns already present must not be dropped to match old VB declarations.

### Metadata minimum contract

Each activated field defines:

- Stable field key, aggregate section and server-only table/column mapping.
- Data type, length, precision/scale, nullability and allowed operations.
- Label, control type, section/order and optional approved lookup key.
- Read/create/edit permissions and any group linkage.
- Default source, validation constraints and sensitivity classification.
- Metadata/schema version and activation/audit information.

Column names may suggest labels but never determine permission or business semantics automatically. Sensitive or unreadable fields are absent from returned values and schema metadata.

### Default payload

The server returns a complete editable order template containing:

- Authorized context and selected voucher, metadata version and applicable capabilities.
- Explicit header defaults, empty lines, configured header/line expense definitions and configured terms.
- All authorized active dynamic fields, including keys whose value is null.
- Supported lookup references, constraints and calculated/read-only designations.

Creating a line uses the same server-defined line defaults and active field definitions. The frontend must not maintain a second hard-coded list of eligible dynamic fields.

Default precedence is: protected server context/system values; approved configured business defaults; supported approved database defaults; null where permitted. Reject conflicting/unsupported defaults. Do not execute arbitrary SQL or JavaScript supplied through metadata. Database expressions need an explicitly supported server implementation.

### Create, update and get

- Use the same activated field definitions in defaults, renderer, validation, read projection and insert/update mapping.
- Keep core fields typed and dynamic values in a validated field map; use decimal strings and explicit ISO date/time wire conventions rather than incidental JavaScript number coercion.
- Distinguish omitted fields from explicit null. Omitted update values remain unchanged; explicit null clears only a nullable writable field.
- Missing required create values are errors unless an approved default supplies them.
- Read-only/unauthorized fields cannot be written; reject crafted values instead of silently accepting them.
- Reject unknown/inactive keys, type/length/scale violations and unsafe lookup selections with field-level errors.
- Preserve unreadable/noneditable stored fields during updates. Delete-and-reinsert detail strategies must not erase protected columns or allocations.
- Dynamic values participate in the same transaction and concurrency checks as typed values.
- Schema/metadata version mismatches return a conflict and refresh guidance. Preserve local input; do not silently drop fields or retry a changed contract.
- Resolve table/column identifiers only through validated server mappings; parameterize values. Do not concatenate browser-provided SQL identifiers or formulas.

### Required extensibility demonstration

After the application is running:

1. Add a nullable bounded text field to SOrd and a nullable decimal field to SOrdDet in the approved development database using a reviewed script.
2. Discover both, review their generated metadata, grant access and activate.
3. Without changing source, rebuilding or redeploying, verify each authorized field appears in schema/defaults and the appropriate editor section.
4. Create an order/line with values, reopen with GET, edit values, then explicitly clear them to null and reopen again.
5. Verify permission-denied users cannot read or write either field, including crafted requests.
6. Verify an unapproved/internal column is neither exposed nor writable, and unsupported schema changes are reported explicitly.

Runtime metadata refresh/activation is allowed; code changes, manual field-specific handlers or static frontend schema regeneration do not satisfy this demonstration.

## 9. Sales behavior and database integrity

- Preserve selected context scoping, financial prefix/site/voucher and existing document formats. DocId, V_No and RecId are distinct.
- Validate required header fields, order reference/date, party, delivery data, line items, expenses and terms using the discovered rules.
- Preserve reference normalization, selected price-list behavior, conversion factors, quantity/weight logic, agreed rate and discount behavior.
- Price lookup currently selects highest matching rate, not newest date. Do not accidentally change this during migration.
- Support approved SORD formulas, including configured line/header expenses. A configuration value such as LINE_EXPENSE identifies a calculation section, not authorization to execute arbitrary expressions.
- Calculate configured tax on the discounted line base, roll up rounded line tax into matching header heads, and support independently configured header tax. Verify nonzero results with synthetic definitions until approved SORD tax heads are available; the existing untaxed setup must not change.
- Evaluate supported expressions with an explicit safe vocabulary; reject unsupported formulas. Do not port the legacy SQL-expression fallback into the API.
- Establish sanitized golden fixtures for rounding and decimal precision before choosing numeric representations. Any intentional difference requires documented approval.
- Reject edits/deletes of approved or otherwise locked orders. Enforce downstream work/invoice usage and relevant quantity restrictions.
- Revalidate business state, lookups, permissions, numbering and calculated totals on the server before commit.

### Atomic save and delete

- Save the complete supported aggregate in a single transaction using one transactional connection.
- Preserve required header/detail, allocations, expenses/structures and terms relationships even where the database lacks foreign keys.
- Injected failure at any write stage must roll back the entire operation.
- Delete uses the verified legacy dependency order and supported aggregate cleanup inside one transaction. If a dependency cannot be safely handled, block deletion explicitly.
- Use concurrency-safe numbering inside the transaction; do not reproduce the observed MAX+1-before-transaction race.
- Use idempotency for create/retries, scoped to user/context/operation; same key and payload returns the original result, while different payload returns a conflict.
- Establish a server-verified version token covering the persisted aggregate. No existing rowversion may be assumed. The implementation must detect stale edits/deletes, including changes to dynamic values.
- Distinguish persisted business values from server-generated/calculated values in responses.
- Record actor, context, operation, record identity, result, timestamp and correlation ID in an approved audit destination; never log passwords or unrestricted payloads.

## 10. API capability and response contracts

The table describes capabilities. Implemented endpoint paths and request examples are in the [HTTP file](../../backend/Techno.Paper.Api/Techno.Paper.Api.http); OpenAPI is available during development. Section 22 supersedes the original fixed-context behavior with authorized company/year/site selection and explicit revision headers.

| Capability | Proposed boundary | Required result |
|---|---|---|
| Session | Login, current session, logout | Verified identity and session lifecycle |
| Context | Allowed contexts, select context | Server-authorized company/year/site |
| Capabilities/navigation | Current user/context grants | Menu/action/group grants without credential data |
| Metadata | Order schema and active version | Authorized typed and dynamic field definitions |
| Defaults | New order and new line templates | Complete valid starting payload for selected configuration |
| Lookups | Approved lookup keys and filters | Scoped, paginated values; explicit dependency errors |
| Search/detail | Orders and order by identity | Authorized persisted data plus current state/actions |
| Calculate/validate | Proposed order aggregate | Authoritative values and field/rule errors |
| Create/update/delete | Explicit Sales commands | Atomic result, identity/version and safe retry behavior |
| Metadata administration | Discover, review, activate | Audited, restricted version activation |

Use explicit request/response contracts and bounded paging/filtering; never expose arbitrary database routes, table names or raw SQL.

Distinguish unauthenticated (401), forbidden (403), unavailable/not-visible record (404 according to the agreed nondisclosure policy), version/idempotency conflict (409), invalid input (400), business validation (422) and service failure (5xx). Return stable error codes, field paths where relevant and correlation IDs. Do not replace dependency failures with empty success-shaped data.

## 11. Isolation and readiness gates

Readiness approvals obtained during implementation:

1. Owner explicitly approved the two named disposable development databases, Windows integrated SQL connectivity and POC writes.
2. Context/company routing is validated against server-only configuration; request-supplied databases/contexts cannot redirect writes.
3. ERP credential compatibility was implemented and the owner confirmed browser sign-in. No password values were exported.
4. Namespaced `webpoc.Session`, `webpoc.Field`, `webpoc.Command` and `webpoc.Audit` support tables were reviewed and deployed explicitly, not at startup.
5. The two nullable demonstration columns were separately approved and deployed using the guarded script.
6. Broad legacy maintenance was not executed. Missing closure structures remain outside the POC; approval/closure/downstream/allocation orders are conservatively locked.
7. Missing-voucher behavior, metadata administrator and surprising current totals were explicitly approved.

Runtime requires Development hosting; writes require `DevelopmentWritesApproved`. This local approval does not establish production-grade isolation or least privilege. Production/shared coexistence, hardened deployment and account provisioning require separate design and approval. The legacy application/library repositories and executable configuration remain unchanged.

## 12. Delivery slices after PRD approval

Build each slice with focused tests first, then a demonstrable end-to-end result. Do not create implementation issues until scope is approved.

| Slice | Dependency | Demonstrable result |
|---|---|---|
| 0. Readiness and fixtures | Clone access and business decisions | Verified targets, credential approach, schema plan, permission policies and golden examples |
| 1. Identity and shell | 0 | Real login, context, dashboard, logout and denied-route behavior |
| 2. Read-only Sales | 1 | Permission-filtered menu, list/detail/lookups from approved development data |
| 3. Defaults and metadata | 2 | Shared field definitions/default payload, review/activation and renderer |
| 4. Create | 3 | Server validation/calculation and atomic save/reopen |
| 5. Edit and delete | 4 | State restrictions, protected-value preservation and conflicts |
| 6. Acceptance demonstration | 5 | No-code new-field round-trip, permission matrix and failure/concurrency evidence |

This PRD selects add/edit/delete as core scope; it supersedes the earlier proposal's optional-delete recommendation. Approval/closure/reporting remain deferred.

## 13. Acceptance and verification

All mandatory checks must pass against the approved disposable development targets. A screenshot or client-only demonstration is insufficient. The criteria below remain acceptance targets, not a blanket pass declaration; intentional scope limitations are recorded in the implementation record.

| ID | Scenario | Measurable acceptance |
|---|---|---|
| A01 | Valid/invalid login | Valid test user reaches protected dashboard; invalid credentials establish no session; backend outage is explicit. |
| A02 | Session lifecycle | Unauthenticated deep links/API calls are denied; logout/expiry blocks subsequent requests; no browser-stored password/token. |
| A03 | Authorized context | User cannot select or access another company/year/site by modifying any request; context switch clears prior-context data. |
| A04 | Module isolation | Dashboard shows only granted modules; direct Sales route/API access is denied without permission; unrelated routes do not load Sales data. |
| A05 | Permission matrix | View-only, Add-only, Edit-only, Delete-only and denied test grants produce matching UI and API outcomes; forged capabilities do not work. |
| A06 | Voucher/group rules | Date limits and restricted control fields apply on read/write; policy edge cases have signed-off expected results. |
| A07 | Real list/detail | List is paginated and scoped; detail returns the requested persisted aggregate rather than sample data. |
| A08 | Defaults | Every active authorized field has a defined default/null state; configured expenses and terms are present. |
| A09 | Create/reopen | Complete order commits; a fresh session can reopen matching header, lines, expenses, terms and dynamic values. |
| A10 | Edit integrity | Permitted edits persist; omitted/protected values and unaffected allocations are preserved; null clearing obeys metadata. |
| A11 | Delete | Eligible single record and supported dependents are removed atomically; denied/approved/downstream-used records remain unchanged. |
| A12 | Calculations | Golden fixtures cover unit conversion, weight/reels, price choice, agreed rate/discount and rounding/expense totals; outputs match approved scales exactly. |
| A13 | Atomicity | Failure injected after header, detail, structure/expense and terms stages leaves no partial change; delete failure also rolls back. |
| A14 | Create concurrency | At least 20 concurrent eligible creates produce unique valid DocId/V_No/RecId values and complete aggregates without unhandled failures. |
| A15 | Retries/conflicts | Repeated same-key create yields one order; reused key/different payload conflicts; stale edits/deletes cannot overwrite newer state. |
| A16 | No-code header/line field | Both supported added columns complete discovery, approval, render, create, GET, edit and clear-null round-trip without code/build/deploy. |
| A17 | Field safety | Inactive/sensitive/unknown/read-only/unauthorized values cannot be read or written; unsupported types/schema/defaults are explicit. |
| A18 | Contract change | Stale metadata version conflicts without losing entered data or silently dropping fields. |
| A19 | Honest failures | Failed lookup/save/permission service shows actionable failure, not empty success or "All changes saved"; failed save retains dirty input. |
| A20 | Legacy safety | Only explicitly approved development targets and reviewed schema/POC records are changed; legacy application/library source and executable configuration remain unchanged. Production/shared targets are prohibited. |

Use existing frontend build/lint tooling, focused UI/API integration tests and API/domain/database tests appropriate to the approved implementation. Add missing test tooling only as part of implementation scope. Run browser-based checks for login, dashboard/navigation, form access, persistence and metadata changes.

For each acceptance check, retain expected result, fixture/context, command or steps, actual outcome and evidence. Performance is not yet a signed-off SLA: record environment, dataset and measured login/list/detail/save latencies during the POC, and agree production targets separately.

## 14. Approval and completion

### Required before implementation

- Approve this PRD, including a separate modular ASP.NET Core API within this repository.
- Supply authorized isolated development access and approved authentication/order test fixtures; the owner explicitly approved the named direct development targets.
- Sign off permission-policy ambiguities and metadata-administrator authority.
- Approve relevant development schema changes and metadata/audit storage.
- Finalize session timeout, supported scalar limits, wire precision/date conventions and aggregate version-token mechanism in the first relevant slice.

### POC is complete only when

- All A01-A20 checks pass with persistent evidence.
- A reviewer can repeat login -> dashboard -> Sales -> create -> reopen -> edit -> delete using the permitted test personas.
- The no-code new-field demonstration passes for both header and line fields.
- Known exclusions and deviations are documented; no mandatory behavior is silently simulated.
- The product owner explicitly accepts or rejects the POC and records follow-up changes.

Full Sales development starts only after that explicit acceptance and a separately approved scope. No production readiness, full-ERP parity or migration completion is claimed by this document.

## 15. Implementation and verification record

Verified on 2026-11-19 against the explicitly approved development targets. Reproduction commands, configuration and endpoint examples are in the [runbook](../../README.md).

### Implemented boundaries

- [API](../../backend/Techno.Paper.Api/Program.cs): .NET 10, real ERP verification, revocable SQL sessions, CSRF/origin enforcement, rate limiting and explicit ProblemDetails errors.
- [Sales service](../../backend/Techno.Paper.Api/Services/OrderService.cs): bounded list/lookups, defaults/calculation, complete supported aggregate transactions, optimistic concurrency, create idempotency, numbering locks and retained issued-number tombstones.
- [Metadata service](../../backend/Techno.Paper.Api/Services/MetadataService.cs): constrained discovery/approval, shared defaults/read/write contract, literal defaults, protected-field rejection and explicit unsupported/missing-active-column conflicts.
- [React workspace](../../features/sales-order/OrderWorkspace.tsx) and [shared field renderer](../../components/order-field.tsx): permission/state-driven controls, dynamic fields, expenses/terms, dirty-state handling and real persistence. Route parameters decode padded ERP document IDs exactly once before API encoding.
- Thin Sales routes isolate the feature from the dashboard. A configurable multi-module registry, multi-context selector and graphical metadata administrator are not implemented.

### Executed checks

| Check | Actual result |
|---|---|
| Backend build | Passed, zero warnings/errors. |
| Managed xUnit runner with approved live integration flag | **28 passed; zero failures/skips.** Final run approximately 50 seconds. |
| Frontend payload tests | **3 passed.** Dynamic fields, explicit null/decimal strings, protected-field projection and editable expense/term inputs. |
| TypeScript, focused ESLint, production frontend build | Passed. Nonblocking Next partial-prefetch configuration and Node module/deprecation warnings remain. |
| Transaction/retry/concurrency tests | Create/calculate/GET/edit/delete, four save rollback stages, delete rollback, 20 concurrent creates, stale version/idempotency conflicts and deleted-number non-reuse passed. |
| Extension service tests | Header/line round-trip, explicit null clearing, metadata-administrator-only visibility, forged-field rejection, hidden-value preservation and missing-active-column conflict passed. |
| Browser authentication | Owner confirmed valid SA login/dashboard/Sales access. Synthetic invalid credentials returned 401 and established no session. |
| Browser persistence | Synthetic `POCUI1613` / `WEBPOC-BROWSER` order created, reopened, edited, reloaded, both extension values cleared to null, then deleted through the UI. No sample-data save simulation. |
| No-deployment demonstration | API was running before approved demo DDL; SHA256 remained `8E60B9F687F91F33BDA6C44082276C4C352120AFE5DDA615AE6553260AE2FD27` across DDL application. Discovery returned both columns; authenticated activation returned 204 for each; both defaults and UI sections exposed the fields without field-specific application handlers. |
| Logout/access denial | Owner approved logout test. UI returned to sign-in; active SA server-session rows decreased from two to one; session/orders returned 401 and protected deep-link navigation returned to sign-in. Unrelated sessions were not revoked. |
| Unsafe requests/hosting | Missing-CSRF browser request and no-Origin POST returned 403. Production API startup was explicitly refused. |
| Cleanup | Zero remaining marked automated/browser POC orders; zero temporary missing-column metadata rows; no open deployment/test transaction. Demo columns/approved A metadata, audit records and idempotency tombstones remain intentionally. |
| Repository hygiene | Focused lint and whitespace checks passed; local configuration and compiled output are ignored. No legacy source changes or commits. |

The binary hash records the live DDL demonstration at that point, not the identity of every subsequent build. Later changes added explicit schema-drift rejection and delete rollback verification; the final automated suite includes those changes.

### Acceptance still outstanding

The results above do **not** declare A01-A20 universally passed:

- A01-A02: valid/invalid authentication, logout and denied access were exercised; timed idle/max-expiry and deliberate database-outage scenarios need dedicated acceptance.
- A03-A06: the fixed 26/HO context and current SA grant path work; full View-only/Add-only/Edit-only/Delete-only/denied browser personas, date-window/control-group combinations and broader company permission policies require approved accounts and business review.
- A07-A11: eligible aggregate CRUD passed in service/browser checks. A complete approval/closure/downstream/permission restriction matrix still needs review. Orders with allocations/downstream use or selected price lists are read-only, not full desktop editing parity.
- A12: approved current expense/zero-total semantics and the exercised manual-price calculation passed. Full unit conversion, weight/reels, price-list, discount and rounding golden-fixture coverage is not complete; selected-price-list editing is deferred.
- A13-A16: automated transaction/concurrency/retry checks and the no-code field demonstration passed for the supported aggregate. A fresh independently authenticated second-session reopen is still useful reviewer acceptance.
- A17-A19: protected/hidden fields, default parsing, decimal precision and missing-active-column checks passed. Broader schema-change cases, a dedicated stale-form browser conflict and network/save-failure UX need acceptance coverage.
- A20: the original clone-only wording is superseded only by the explicit named-development-target approval. This is not a shared/production writer pilot.

The initial delivery remained fixed-context; section 22 records the subsequent authorized selection change. The POC remains manual-price SORD with string terms and an allowlisted expense vocabulary. Approval/closure/print execution and allocation editing are excluded. Audit storage records successful actor/operation/record/time; full failed-operation/correlation/retention auditing is a production follow-up. Remaining npm dependency audit findings require review before broader deployment.

Product-owner acceptance of the POC and approval of the final Sales-module scope remain outstanding.

## 16. Requested workspace corrections

The owner requested a compact professional light UI inspired by the supplied workspace screenshot and public login reference, rather than the original dark card-based POC.

Confirmed choices:

- Show **all active database-granted modules**, not the desktop shell's fixed allowed-module list.
- Keep other modules' authorized menus visible and open a clearly labelled **Not yet available in web POC** page. Only Sales Order executes migrated business operations.

Implemented:

- Authenticated `/api/navigation` reads company/global `User_Permission` rows and system restrictions. Module placement follows `ReportFor`, not the execution DLL's `MnuModule`; ordering uses `SNo`, nesting uses `Parent`. Captions preserve desktop Forms/Web Service aliases. Icons are presentation-only, with a generic fallback for new module names.
- Nine-dot searchable module launcher; selection changes the route, current module and related left menu. Menu groups collapse, navigation scrolls independently, and desktop/mobile sidebar controls preserve usable content width.
- Top-right circular local sample avatar, profile details and logout menu.
- Original PaperSoft-branded light login, compact Segoe UI controls, denser header fields and a light list/editor. No third-party branding/assets or social-login/password-reset behavior were copied.
- All authorized line fields render as editable table columns using lookup/numeric/date behavior. Item identity and Tax actions remain pinned while other columns scroll.
- Each draft item owns a stable key, its values and its expense rows. Tax opens a row-specific draft dialog; Apply and Cancel have distinct behavior. DTO conversion produces the existing aligned `lines`/`lineExpenses` contract only at the API boundary.
- Backend tests also verify two independently populated expense rows through save/reopen and removal of the earlier row. Frontend tests cover immutable modal drafts, row/expense alignment, missing-row rejection and hierarchy filtering.

The reference database contains the active Report/Form IV entry `MnuForm4Register` pointing to absent parent `MnuExcise`. Desktop tree traversal cannot display it. The web keeps it hidden, displays an administrator warning and does not repair legacy configuration. Inactive/restricted ancestors also hide descendants; conflicting grants and cyclic hierarchies fail explicitly.

Verification of this correction: **35 backend tests and 7 frontend tests passed**, with zero backend build warnings/errors, focused ESLint, TypeScript checking and production build. Browser checks verified 16 authorized modules, Purchase module selection/informational menus, profile dialog, and a two-line order with independent 10%/20% discount entries. Cancel retained the original row's 10%; save/reload preserved both; removing the first row retained the remaining row's 20%. The synthetic browser order was deleted afterward; both SQL and a fresh UI search returned no matching record.

The final production preview was checked at 1366, 390 and 320 CSS pixels wide. Page width stayed within the viewport, item inputs measured 32 pixels high, and the Item/Tax columns stayed pinned without overlapping at either horizontal-scroll limit. Row expense dialogs fit mobile viewports and Cancel returned focus to the row's Tax button. Desktop navigation collapse/expand, ancestor-preserving search, and the mobile drawer's open/close controls were exercised. An empty responsive-test draft was discarded through the unsaved-changes confirmation; the preview was left on the dashboard.

This section supersedes the earlier single-Sales-tile/card-editor UI description, not the underlying development-only scope or outstanding wider business-persona acceptance.

## 17. Add New numbering and continuous item entry

The owner approved provisional numbering rather than early reservation: show the next order reference (`RecId`) and voucher number immediately on Add New, allocate safely on Save, and notify when concurrent activity changes the automatic reference. Manual reference overrides remain available; clearing the field restores automatic mode.

Desktop evidence: Sales `FrmSaleOrder.FHP_Type` shows the next numeric reference and calls shared `FGetDoId`; `FGetDoId` reads scoped maximum voucher number without reserving it. The Add save path calls `FGetDoId` again. The web uses the same site/voucher/FY-prefix scope, retaining its existing transaction locks, idempotency and deleted-document tombstones. It does not promise a guaranteed number before Save or repair the legacy concurrency behavior.

The defaults contract now includes provisional `numbers`; `automaticReference` on a new command requests allocation at Save instead of treating the displayed reference as a manual value. Existing clients omitting the flag retain manual behavior. Protected `V_No` is returned read-only, not accepted through metadata writes. Calculation returns a current provisional suggestion without reserving it. `DocumentNumbering` accepts only server-owned aggregate table mappings and parameterized site/voucher/prefix values, reusing the same query for preview and locked allocation.

The owner also approved one blank initial item row and automatic creation of one successor when any editable cell changes. `lib/entry-rows.ts` owns generic entry activation, stable keys, successor normalization and line-limit behavior; the Sales adapter provides metadata-derived values and expenses. Untouched placeholders are excluded before DTO projection. Partial entries are not silently dropped. Tax Apply activates a row only when its draft changed; Cancel leaves it untouched. Saved/calculated orders get a fresh entry row only when permissions and record state allow editing. Add line remains a keyboard/focus shortcut, not a separate allocation path.

Validation: **36 backend and 11 frontend tests passed**, with zero backend build warnings/errors, focused lint, TypeScript checking and production build. Browser verification exercised immediate numbering, repeated Rate edits without duplicate successors or focus loss, omission of untouched rows, normal validation of incomplete entries, manual/automatic reference selection, and the Add line focus shortcut. A synthetic concurrent save consumed reference 1112; UI Save then persisted 1113 with one actual item and displayed the renumbering notification. Reopening and calculation retained final voucher number 177.

Expense-dialog Cancel retained the blank entry's original percentage; applying a changed percentage activated it and added one successor. Removing the earlier row preserved the remaining row's 20% expense. The 390-pixel viewport had no document overflow. Both synthetic persisted orders were deleted, the remaining synthetic draft was discarded, and SQL confirmed no automated/browser marker orders remained. Existing command/audit tombstones were intentionally retained. The final API and portal were left responsive for review; wider production/business-persona acceptance remains outstanding.

## 18. Field-level validation feedback

The owner reported that the secondary-unit conversion message did not identify where to correct the order. The API now includes explicit field paths in Problem Details rather than requiring the client to infer locations from text. Metadata validation, dates, lookups, line size/quantity/rate, reference, terms and numeric expense validation identify affected controls. The reported conversion failure identifies the item and secondary-weight cells in the exact row and explains that a configured conversion is required; alternatively zero Secondary weight and enter Weight directly. ERP master data is not changed.

Shared field controls retain the compact light layout, adding red borders and short inline guidance only on invalid controls. Full descriptions are available to assistive technology. The first affected enabled control receives focus and is scrolled into view; expense failures open the corresponding row dialog. `lib/field-validation.ts` binds API line indices to stable client item keys so trailing blank entries and row removal cannot move another item's errors. Normal non-field failures retain the existing summary and diagnostic reference.

Verification: **39 backend and 13 frontend tests passed**, backend build with zero warnings/errors, focused ESLint, TypeScript checking and production build. Real browser checks reproduced a required header failure, highlighted/focused its input, reproduced the reported conversion failure with both row cells red, and obtained successful calculation with no remaining invalid controls after correcting Secondary weight. A malformed row-discount percentage reopened the correct Tax dialog and highlighted the percentage; correction calculated successfully. The mobile document remained within 390 CSS pixels. Verification used a separate synthetic draft, not the owner's open draft; no order was saved.

## 19. Charcoal module navigation and creation shortcuts

The owner approved adapting the reference's hierarchy using PaperSoft blue rather than copying the entire reference interface. Desktop now shows a 64-pixel charcoal module rail, independently scrolling authorized module icons/labels, a compact white submenu, a pale-blue selected transaction with an inset blue indicator, and a blue active module. Green is reserved for Add shortcuts; validation retains red. The nine-dot launcher/mobile drawer remains available without the desktop rail on narrow screens.

The Sales submenu and its heading expose circular green plus links only for implemented routes authorized for Add. A shared `RecordCreateLink` renders the same icon treatment in the list toolbar. Explicit route eligibility is centralized in `menuCreateHref`; unmigrated modules have no misleading Add action. All links still participate in the existing unsaved-change navigation guard and server permissions remain authoritative.

The order list has a compact action bar with functional New, Search, Clear search and Refresh controls, result count for the current page, blue linked references, subdued approval badges and navigation arrows. Refresh retains the applied query/page, while clearing search resets pagination. Search gets its own full-width toolbar row on small screens. No nonfunctional filter/export/bulk-action icons were added.

Verification includes **14 frontend tests**, focused lint, TypeScript and production build. Browser checks confirmed all 16 database-backed modules plus Home in the rail, module selection changing related menus, Sales plus opening Add New with its provisional number, no Purchase creation shortcuts, working search/refresh/clear API requests, sidebar collapse/expand, and mobile drawer operation. Document widths remained within 1366, 390 and 320 CSS pixels. No business records or legacy/database configuration were changed.

## 20. User-specific listing personalization

The owner selected all six options: show/hide, reorder and resize existing columns; compact/comfortable rows; rows per page; default sort/direction. Additional order-header fields are out of scope. A toolbar Settings button opens a reusable modal; arrows provide keyboard-accessible column reordering, widths are 80–600 pixels and at least one data column stays visible. Open is always available. Apply persists only after successful validation/server response; Cancel discards the modal draft; Reset defaults deletes only the current preference.

Approved SQL persistence is an additive `webpoc.ListingPreference` table in `EcoTech_Comp`, with a composite primary key of username/company/listing, bounded versioned JSON and UTC update timestamp. The authenticated server session supplies identity/context, never request-selected user names. View permission is required, but Add/Edit order privileges are not required to personalize one's list. Unsafe requests retain origin/CSRF checks and the development-write approval guard. Unknown lists/columns/sort keys, unsupported schema versions, duplicate/missing columns and all-hidden layouts are rejected. Concurrent first saves use a transactional range-locked upsert; last explicit save wins. Preferences follow the same user/company on the next list opening or reload on another device; open tabs do not live-sync.

Performance boundaries: one preference GET per listing mount and one small indexed settings lookup; no preference fetch for search/refresh and no save for each checkbox/move. Only layout is client-side. SQL continues enforcing site/FY/voucher/date-window permissions and returns a bounded page of 10/25/50/100 rows. Sorting is server-allowlisted and precedes pagination across the whole filtered result, with DocId descending as a stable tiebreaker. Reference ordering uses the existing textual identifier, Gross uses its numeric SQL column, State uses unapproved/approved ordering. Default behavior remains date descending and 25 rows. No business-table index/schema changes were made; future large-dataset sorting needs representative workload/index analysis rather than a blanket performance guarantee.

Verification: **48 backend and 17 frontend tests passed**, with zero-warning backend build, focused lint, TypeScript and production build. Integration tests cover 12 synthetic orders across the 10-row boundary, ascending/descending gross sorting, all allowed sort keys/page sizes, isolation between users/companies, concurrent initial save, scoped reset, denied view and disabled writes. Synthetic business records/preferences are cleaned up. The additive deployment was run twice and verified with zero open transactions. A development preference lookup read a 652-byte payload using two logical reads and zero physical reads; this is a local observation, not a production benchmark.

Browser verification confirmed saving a reordered, resized layout with State hidden, comfortable spacing, page size 10 and Gross ascending; reload restored every option. Invalid width disables Apply and Cancel preserves saved settings. Exactly one preference request occurred at list load, remaining one after Cancel and order Refresh. Mobile settings stay scrollable within the viewport; wide tables scroll inside their own container. Verification changes are reset to defaults afterwards; existing business orders and open drafts are not edited.

## 21. Finished Product item eligibility

The owner requires Sales Order item choices to come only from the item master's Finished Product type. Discovery confirmed `ItemMast.ItemType='FM'`, labelled `Finished Mtrl.` in `ItemType`; the Windows Sales Order lookup uses that same type code. Item search now applies this restriction before its existing bounded search/site filtering. Other lookups and master data are unchanged.

The owner explicitly approved retaining historical non-FM items when unchanged on their original persisted order/line identity. New lines, copied lines without an original identity, and replacement item selections must use FM. Validate and Save both enforce this using the transaction-loaded stored line, not a client-supplied grandfathering flag. A violation identifies the exact `lines[index].ItemCode`, allowing the existing red-field/focus UX to explain the issue. Existing conversion, quantity, permissions, site/date restrictions, record locks and concurrency checks remain in force; historic orders are not auto-migrated.

Verification: backend build passed with zero warnings/errors; all **19 affected SQL workflow tests passed**, including a new lookup/type allowlist test, rejection on both Validate and Save, preservation of a synthetic historical item's unchanged identity, rejection of non-FM replacement/copied lines, and successful replacement with FM. The conversion regression now uses a real FM item with zero conversion, keeping that validation covered independently. Tests operate on marked synthetic orders and clean their business aggregates; the owner's existing orders and drafts are untouched.

## 22. Authorized company, financial-year and site workspace

The owner requested company/year selection immediately after valid credentials, conditional site selection, selected-context Sales data, reusable company information and in-portal switching. The owner explicitly approved earlier years as **read-only**, preserving write approval only for company 26 / FY 2026-27 / HO, and warning on unsaved orders while rejecting stale requests from other tabs.

Windows discovery traced `FrmCompany`, `FrmSiteSelection` and shared `FOpenConnection`: company rows already represent a company/year pair; exact SA/SUPER users see all rows, while ordinary users require `UserSite` company grants and an exact intersection of encoded site codes with `SiteMast`. A sole authorized site is automatic; multiple sites require selection. Site details override only nonblank matching company fields. Web code retains these rules, without copying concatenated SQL, arbitrary database routing, startup maintenance, licence mutations or process-global state.

The available nine company/year records currently route to the same approved `EcoTech_main`; no missing financial years are synthesized. HO was the sole site at initial delivery. At the owner's subsequent request, two clearly labelled demo master rows were added: D1 / Demo Site 1 (POC) and D2 / Demo Site 2 (POC). Existing SA/SUPER rules expose all three, so site selection is now required; ordinary-user grants were not expanded. Demo sites remain read-only and no orders were seeded. Unsupported future database routes are explicitly unavailable. Conflicting grants, invalid company dates/prefixes and unavailable or unauthorized sites fail explicitly.

Verified authentication now creates a pending session and routes to the selector outside the protected dashboard. Modules cannot load before selection. The authorized choice is stored as nullable company/site plus a fresh revision on the namespaced SQL session. Selection uses compare-and-swap `expectedVersion`; module requests require `X-ERP-Context` matching the server session. The reusable identity service resolves grants, dates, prefix, site and whitelisted company details from that context. Client tabs capture their own revision before asynchronous request work. Stale tabs show a conflict warning without silently replacing or discarding their draft; selection/re-entry remounts workspace state and caches.

The compact selector shows company name, year, dates, site choices and read-only/unavailable status. Header company information and the profile menu expose Change company/year/site. The existing unsaved-order navigation guard also covers these links. Current company information includes registered/trading name, year boundaries, selected site, mode and whitelisted address/contact/tax details; it does not expose connection details or arbitrary master columns.

Order listing and direct document loading enforce site, financial-year prefix **and date boundaries**, including permission date windows. Typed SQL date parameters avoid server date-format parsing dependencies. Historical contexts retain applicable View/Print grants but remove Add/Edit/Delete/metadata-admin capabilities; Save/Delete also enforce read-only server-side independently of client capabilities. Personal listing preferences remain editable and scoped to their authenticated company. Existing FM item rules, record locks, numbering, metadata, CSRF, concurrency and session revocation remain in force.

Deployment is explicit additive script `004-session-company-context.sql` on `EcoTech_Comp`; it adds only nullable support-session fields and was rerun successfully with zero open transactions. Legacy sources, existing company/site rows and existing business orders are unchanged; the subsequent demo request added only the two new site rows described above.

Verification at initial delivery: **60 backend tests passed with no skips and 19 frontend tests passed**. The backend build has zero warnings/errors; focused frontend lint, TypeScript, production build and whitespace checks passed. Context integration tests exercise pending sessions, automatic sole-site selection, unauthorized site rejection without a session change, stale selection/request revisions, ordinary-user grant intersection, historical read-only write guards, and selected site/year document exclusion. Pure rules cover sole-site and multi-site selection; after adding demo sites, integration coverage also checks required explicit selection, read-only access for additional sites and exclusion of HO documents. Both restored preview services respond successfully, including the selector, CSRF and OpenAPI endpoints.

The owner chose automated verification rather than shared-session browser switching, to avoid affecting open work. Live login/selection, responsive company-dialog appearance, dirty-draft switching and cross-tab warning presentation therefore remain browser acceptance items; no such manual verification is claimed, and existing browser tabs were not navigated or edited.

Subsequent frozen-selector correction: successful operations previously left `busy` true, allowing retained client-route state to restore a completely disabled selector. Sign-in, selection and both sign-out surfaces now use full-document navigation at identity/workspace boundaries, and selection/sign-out always clear busy state in `finally`. This also prevents cached dashboard providers/drafts from crossing an intentional workspace change.

Correction verification: the original stuck selector was refreshed and D1 selection enabled Open workspace; its selection was then cleared without submitting. In a separate verification tab, mocked mutation responses confirmed selection errors re-enable controls, successful selection creates a fresh dashboard document, and sign-out/sign-in return to an enabled selector in fresh documents. These UI checks did not change the shared server session or submit real credentials/orders. All 19 frontend tests, focused lint, TypeScript and production build passed. Actual shared-session cross-year switching and dirty-draft/cross-tab presentation remain outside these non-mutating checks.

## 23. Shared task management and voucher-configured sequential approvals

The owner approved an enhanced approval engine rather than copying the desktop toggle, with existing ERP permissions retained as a safety gate. Approved decisions: sequential stages, one eligible group member per stage, self-approval allowed for this POC, current-workspace-only visibility, web-only automatic publication, and rejection requiring a reason followed by edit/save resubmission from stage one. Only the final stage sets the legacy `SOrd.ApprovedBy`. No approval reversal is exposed. This section supersedes earlier approval exclusions for this bounded workflow only.

Read-only discovery confirmed that Windows toggles `SOrd.ApprovedBy` in browse mode and uses the `Approved By` control group; there is no existing multi-stage task schema to reuse. The new implementation does not modify Windows source or existing ERP user permission rows. The current write-approved workspace remains company 26 / FY 2026-27 / HO; historical/demo sites cannot publish or decide business tasks.

Deployable `005-tasks-and-approvals.sql` creates normalized support tables in `EcoTech_main`: task type, scoped group, group membership, type/group grant, task instance, audience, per-user receipt, task history, voucher approval flow/stages, and approval request/stage snapshots. `webpoc.erpTaskInstance` is the shared instance table. References use existing ERP username, company/site, voucher prefix and padded document key conventions. Document-key columns explicitly match the legacy `Latin1_General_CI_AI` collation; deployment handles the separate control-database collation when seeding users. No business-table DDL is needed. Deployment is serialized and transactional, was run twice, and returned zero open transactions; failed initial checks rolled back the entire deployment.

SA/SUPER are initially mapped to the company 26 / HO approver group. Manual Memo/Note publishing requires configured type/group rights. Source-linked creation/approval/result tasks are server-owned. A creator may receive a direct notification, but linked tasks still require source-record View, selected site/year/date-window access and a registered module adapter. Unknown routes are not executed. Other users gain no implicit task rights. Read receipts are per user; reading a task never approves a record or marks it read for another recipient.

Publication happens inside the existing order transaction, after validated persistence and before commit. Failures roll back the aggregate, command record, task instances and approval request together. Idempotent order retries do not duplicate tasks. Editing an unapproved order cancels outstanding tasks and snapshots a new approval request; partial approvals on an older revision do not carry over. Approval checks both document hash and request rowversion, ERP Approved By permission, active scoped group membership/action grant, date windows, read-only status, self/repeat policy and existing record locks. Decisions serialize with order writes. Task and stage histories retain actor, time, action and comments. Only final approval writes the legacy approval field, after which normal edit/delete guards continue to apply.

The initial definition has one stage, but the same engine supports up to 20 contiguous stages. `006-sord-two-stage-example.sql` is an optional preview-by-default configuration, not automatically applied: it assigns SA review then SUPER finance approval and disables repeated approvers. Existing request snapshots are unaffected by later definition changes. Administration remains reviewed SQL; parallel/unanimous votes, amount conditions, delegation, reminders, email and escalation are future scope.

The dashboard adds a compact paged Tasks table with category/status/date, unread styling, refresh and permission-aware Memo/Note publication. Dialog tasks remain in a popup. Transaction tasks use the same Sales Order editor; a stage panel shows progress, decisions and comments and disables decisions while a draft is dirty. A notification bell sits beside the profile/company area. One shared provider polls bounded inbox/type endpoints every 45 seconds only while visible, refreshes on visibility/local task events, and displays failures explicitly. This is intentionally bounded polling, not a claim of real-time push.

Extension contracts separate the shared publisher/approval engine from module visibility and navigation adapters. Sales Order is the only implemented transaction target; Sales Invoice/Dispatch will require their own authorized adapters and save integrations. Windows-generated records and historical orders are not bulk-backfilled automatically.

Verification completed:

- All 64 backend tests passed against the approved development databases, including task publication rollback and the configured multi-stage workflow snapshot. Backend build completed with zero warnings/errors.
- All 22 frontend tests passed; focused ESLint, TypeScript checking and the production build passed. The task contracts reject unsupported navigation targets and oversized inbox pages.
- Migration 005 was deployed twice successfully with zero open transactions. Migration 006 was previewed only; the active definition remains one stage.
- Following private owner sign-in, a synthetic order was created through the authenticated browser API. Its creation and approval tasks appeared in the notification bell; opening the approval task used the existing order editor and persisted a read receipt. The UI blocked blank rejection, accepted final approval, and SQL confirmed the synthetic order's `ApprovedBy='SA'`; the returned order was locked.
- A Memo was published through the dashboard UI and displayed in a plain-text popup without navigating away. Literal HTML-like text remained text. CSS motion was disabled only in the verification page to work around paused exit animations in the hidden browser harness; application styles were not changed. Separate Note-popup and narrow-viewport browser checks were not completed before that verification tab became unavailable.
- Scoped transactional cleanup removed only the exact marked synthetic order, its request/tasks and the synthetic memo: four task instances removed, zero remaining synthetic orders/tasks/requests and zero open transactions. Normal audit/idempotency tombstones remain; document numbers were not reset. No pre-existing orders, drafts or permission grants were changed by these checks.

The HTTP request examples include workspace-scoped inbox/type retrieval, Memo publication/read receipts and concurrency-protected approval decisions. Broader multi-persona browser acceptance and production readiness remain outside this POC verification.

## 24. Reusable action bar, clean-draft copy and controlled unapproval

The owner approved a compact KPI-inspired record action bar, copy-to-new, actor details and a dedicated reversal permission. This supersedes earlier exclusions of unapproval only for the guarded workflow below. PDF/email/attachments/journal were explicitly deferred; they are not shown as nonfunctional buttons.

- A module-independent typed `RecordActionBar` renders the primary Save action, grouped Options and a Record details popover. Transaction actions no longer occupy separate header buttons; approval decisions use the same menu. Existing item/expense editing controls and validation remain local. Blue/slate styling, keyboard-accessible existing primitives, upward-opening menus and a wrapping sticky footer preserve entry space.
- Record details exposes only server-owned Created by, Modified by and Approved by usernames. Missing legacy values are labelled, not guessed. Modified by remains the last document editor; approval/reversal history retains its own actor. No speculative joins to employee/full-name data and no audit-field activation through metadata are introduced.
- `GET /api/orders/{id}/copy` requires source View/date access, Add and a writable selected workspace. It builds an unsaved draft from current metadata/defaults and copies authorized business fields, item values, editable expenses and matching current terms. It resets numbers, dates (including SQL smalldatetime), PO reference, identities, audit/workflow state, allocations and calculated totals. Current metadata protects extensions. Source line identities are discarded, so historical non-FM exceptions cannot be inherited by a new line. Normal calculation/validation/save publishes a separate new approval request; merely copying persists nothing.
- `POST /api/orders/{id}/unapprove` requires a dedicated scoped group `CanReverse` permission plus ERP approval rights and current source/workflow versions. A nonblank reason is mandatory. Read-only/date restrictions, closed headers/lines, downstream business use/ledger postings and reel allocations block reversal. The existing serializable transaction/application lock protects it against save/delete/decision races.
- Reversal clears the approval field, cancels old open tasks/request, retains original stage decisions, appends previous approver/actor/reason/time/source version in `erpApprovalReversal`, publishes a result notification and audits atomically. The panel reports Reversed with actor/reason; the next edit/save starts stage one of a new request. An already-approved legacy document can be reversed under the same gates without inventing a historical web request.
- Additive deployable 007 adds the denied-by-default permission and immutable reversal table/index. Initial permission seeding happens only when introducing the column, so reruns cannot restore revoked rights. It was deployed twice with zero open transactions; Windows source and legacy business-table definitions are unchanged.

Verification: six focused backend integration tests passed (copy/save independence, existing CRUD/tasks, reversal grants, mandatory reason, stale versions, read-only/site isolation, closure/allocation/downstream guards, rollback and stage-one resubmission); backend build had zero warnings/errors. All 24 frontend tests passed, including response-only audit data and explicit reversal contracts. Focused lint, TypeScript checking and production build passed. Existing nonblocking Next/Node warnings remain.

Live browser acceptance used only a marked synthetic Sales Order: Options showed permitted groups, approval populated Approved by, Unapprove was exposed after approval, an empty reason disabled confirmation, and reasoned reversal returned an editable order. Copy opened a new dirty unsaved draft with fresh dates/number, empty customer PO, one copied item and no audit identity. Saving without the PO returned 422 and highlighted its field. At viewport widths 390 and 320 px, document width equalled viewport width and the sticky footer remained within the viewport. CSS motion was disabled only in the hidden verification page, not in application source.

Cleanup removed the exact marked browser order, its four task instances, request/stages and reversal row. Remaining synthetic order/task/reversal counts and open transactions were zero; the copied draft was never saved. Audit/idempotency tombstones remain and numbering was not reset. No pre-existing orders, drafts, legacy permissions or selected workspace were changed by verification.

## 25. Per-row tolerance popup and automatic Windows calculation parity

The owner selected **Apply to this row / Cancel only**. The legacy Apply-all implementation's excess-only shared-state behavior is intentionally not copied.

- Excess/shortage columns are replaced by one per-row tolerance button. A reusable, calculator-injected dialog displays order quantity, percentages, quantities and upper/lower net quantities. Percent mode derives quantity to 3 decimal places; entering quantity switches that side back to direct quantity mode. Only the existing quantity columns persist, under the row's stable key. Cancel and read-only viewing do not mutate the document; edited dialog state participates in navigation/approval guards.
- Shared server-side decimal rules reject negative tolerance and shortage above order quantity. Full validation opens the correct row's popup for hidden tolerance-field errors.
- Archived Sales and SalesNew both multiply secondary weight by item conversion. Save and incomplete-draft previews now use one `SalesLineCalculator`, with MM/CM/INCH conversion, zero-counterpart-only reel/weight derivation and configured `Sl_Order_Weight_round_to_places` rounding. Line amount is weight times manual rate to 2 decimal places.
- Midpoint fixtures compare the legacy VB `Format` API with server decimal output: midpoint formatting rounds away from zero, not to even. The same rounding policy applies to the existing expense vocabulary, with configuration retaining percent, FullValue, discount and AgreedRate target behavior. Selected/enabled price-list workflow remains excluded.
- A bounded authorized read-only draft endpoint recalculates after a 400 ms quiet period, including after Tax Apply, without demanding complete header/lookup entry or reserving numbers. Full Save still validates all original permissions, metadata, dates, lookups and mandatory values. Request cancellation, stale-signature checks and stable-row merging avoid overwriting newer inputs, losing focus or moving row expenses. Failures remain explicit and field-bound.
- No Windows changes, item-master repairs, business schema changes or dependency installations.

Verification: backend baseline exercised 78 cases; its single row-label regression was corrected and the exact failing integration test rerun successfully. This includes new incomplete-draft/no-number-reservation and nonunit-secondary-conversion preview/Save/tolerance integration cases with exact synthetic-record cleanup. Thirteen focused rule/expense tests passed after refining hidden tolerance-error paths. Nineteen focused frontend tests passed, including cancelled-transport/stale-response suppression, stable-row merging and existing draft/payload validation. Focused lint, TypeScript checking and production builds passed; the API OpenAPI document exposes both calculation endpoints.

Authenticated browser verification used a separate unsaved draft only. Weight 100 and rate 2.125 updated line and gross amounts to 212.50 without a completed header. Percentage tolerances produced the correct upper/lower limits; shortage 100.001 highlighted its field and disabled Apply. Cancel reopened with original zero quantities; Apply reopened with 2.125 excess / 4.125 shortage. A second row retained zero tolerances, and rapid rate edits settled at the latest value (312.50 / 250.00 for the two rows) with exactly one trailing entry. At 1440, 390 and 320 px, page width equalled viewport width and the dialog stayed within the viewport; its percent/quantity inputs measured 80/96 px. Motion was disabled only in the verification tab to avoid hidden-browser animation pauses. No browser draft was saved, no pre-existing order was edited, and existing user pages were left alone.

## 26. Buyer-only SQL-driven table value help

The owner approved one generic schema/SQL-driven lookup, initially **Buyer (`PartyCode`) only**, for review before expanding to other Sales Order fields. Consignee, Delivery To, Item/BF/GSM/forms and their business rules remain unchanged.

- The shared `ErpLookup` displays descriptions and emits only a string key or null. Search input belongs to the popup, not the foreign-key field. Cancel/Escape preserve selection, Clear affects the draft only, and ordinary required-field/foreign-key validation remains authoritative. Duplicate names are disambiguated by configured business code, city and ERP code columns.
- Desktop uses the existing popover; small screens use the existing dialog. F4/Arrow Down opens, arrows/Home/End navigate rows, Enter selects, and closing restores trigger focus. The table scrolls locally; available-height sizing keeps desktop footer actions inside the viewport, and mobile pagination wraps instead of expanding the page.
- Additive deployment 008 creates `webpoc.LookupDefinition`, `LookupColumn`, `LookupField` and the curated `LookupPartySource` view. Definition/column configuration controls the source, textual key, scope, page size, captions, widths, searchable columns and selected-description parts. `LabelOrder` plus `LabelSeparator` composes descriptions without executable templates, omitting empty parts. The sole binding is `sales-orders / header / PartyCode -> parties`.
- The Party source retains the previous SubGroup/City and SiteList behavior; it does not add Windows Nature/Active/customer/distributor filters. Legacy definitions and save validation are untouched. The script was applied twice; configuration seeds preserve later edits, while source-view SQL is redeployed deliberately. Startup does not migrate.
- Authorized definition/search/exact-key resolution endpoints execute only reviewed registered `webpoc` views, validate source columns and parameterize user inputs. Sales View and current-workspace guards remain mandatory. No browser SQL, arbitrary query endpoint or SQL administration UI is introduced.
- Search is debounced 200 ms and paged on the server, 25 rows by default. Saved-description hydration is independent of search results and runs for disabled/read-only fields. A username/workspace/lookup-scoped cache batches up to 100 keys, expires after 30 seconds and is bounded to 500 entries. Missing keys and failures are explicit; failures are not cached, Retry refreshes, and old in-flight responses cannot overwrite newer cached descriptions.
- Ordinary future lookups need a reviewed source view, definition, columns and binding to an authorized field, not another React control/API. New modules require their access-policy adapter and binding metadata once; new business filters or relationships still require review. Configuration changes take effect on fresh requests/reopening/refresh, not live push. Removing the Buyer binding withdraws the pilot without deleting configuration or business data.

Verification: three focused backend integration tests passed, covering Buyer-only/code-only persistence and existing CRUD, configurable captions/labels/paging, duplicate descriptions, exact resolution beyond page one, requested-key casing, missing keys, site scope, literal SQL-like search, denied View and malformed configuration. Backend build had zero warnings/errors. Twenty-six focused frontend tests passed, covering batching/cache boundaries, failure/retry, bounded batches, stale cache responses, captured workspace headers, code-only payloads and existing draft/calculation behavior. Lint, TypeScript and the final production build passed.

Authenticated browser acceptance used a separate unique verification tab and no Save. Keyboard selection displayed the description while the actual Validate request contained only the original PartyCode. Paging changed result rows; unmatched search/Cancel preserved selection; Escape restored focus; Clear returned the empty Buyer prompt. At 1440, 390 and 320 px, page width equalled viewport width, popup/dialog bounds stayed within the viewport and Cancel remained clickable. A tall-popup overflow discovered during acceptance was fixed with shared-control height constraints and local table scrolling.

A browser-only intercepted defaults response supplied a known party key and read-only lock, and one intercepted resolve response returned 503. The disabled Buyer retained its key visibly during failure; Retry resolved the real description. These fixtures changed no server permissions, configuration or orders, and interception was removed afterwards. SQL verification found exactly the Buyer binding, zero remaining probe definitions/views and zero open transactions. API and portal readiness returned 200. Broader rollout, multi-persona acceptance and production certification remain subject to owner review.

## 27. Windows Buyer help-grid column and width mapping

The owner requested the Windows Sales Order Buyer presentation. Archived `FrmSaleOrder.FHP_Party` configures six visible columns: **Party Name 180, Code 70, Address 250, City 100, Distributor 200, D.City 100 px**, in that order. Deployment [009-buyer-windows-layout.sql](../../backend/Techno.Paper.Api/sql/009-buyer-windows-layout.sql) maps these captions/widths into the existing Party lookup configuration.

- Displayed Code is `SubGroup.ManualCode`; the hidden identity remains `SubCode` and is still the only saved foreign key. Windows' hidden Zone/DZone/CityCode values do not become new controls or selection side effects.
- The reviewed view adds Address using the exact Windows null-safe Add1 + Add2 concatenation, Distributor using the existing distributor SubGroup relationship and D.City using that distributor's City relationship. Missing cities/distributors are blank. Existing site scope, Name-only selected description, eligibility, permissions and save rules remain unchanged.
- The generic control retains the configured pixel widths instead of stretching columns to its container. Desktop popup width derives from their 900 px total plus web padding/borders/native-scrollbar allowance; Windows' 980 px window chrome and 300 px window height are not copied. Mobile uses the existing bounded dialog and local horizontal scrolling without shrinking these columns.
- Deploy 009 after 008, including when rerunning 008 because it restores its earlier view definition. Rerunning 009 deliberately restores this approved Buyer layout; other lookup configurations and field bindings are untouched. No legacy tables or source files are changed.

Verification: 009 applied twice with zero open transactions. Two focused backend integration tests passed, checking exact configured captions/widths, related Address/Distributor/D.City values, hidden identity and code-only persistence alongside existing configuration/scope behavior. Test build had zero warnings/errors; lint, TypeScript and the final production build passed. Authenticated browser measurements at 1440/390/320 px returned exactly `[180,70,250,100,200,100]`; page width equalled viewport width, popup/dialog and Cancel remained contained. Desktop had no horizontal overflow; mobile scrolling stayed inside the lookup. Only a separate unsaved verification tab was used. Final portal/API readiness returned 200.

## 28. Approved rollout to all existing Sales Order lookups

The owner explicitly selected **all 11 existing portal fields, including items**, not the unimplemented Windows city/voucher/price-list/amendment/contractual-order workflows. Existing eligibility, saved codes and selection rules remain the baseline; this is a shared-control/presentation rollout, not a new business-rule migration.

[010-sales-order-value-help.sql](../../backend/Techno.Paper.Api/sql/010-sales-order-value-help.sql) registers the remaining reviewed views, definitions and bindings:

| Fields | Lookup IDs | Windows visible columns / widths |
| --- | --- | --- |
| Buyer / Consignee / Delivery To | parties / consignees / delivery-parties | Party Name 180, Code 70, Address 250, City 100, Distributor 200, D.City 100 |
| Against Form | forms | Name 200, Per 100 |
| Order Type / Excisable | order-types / yes-no | Name 100 |
| Item | items | Name 200, Code 175, SKU 80, Varity 140, GSM 100, Length 100, Width 100, BFName 60, Grain 40 |
| BF / GSM | bf / gsm | Name 100 |
| Size Unit / Grain | size-units / grain | Unit 100 / Grain 100 |

- Item detail labels use existing VarityMast/GSMMast/BFMast joins. Displayed Code is ManualCode, while ItemMast.Code remains the hidden saved key. TaxForms.Tax_per is display-only. Existing CM/MM/INCH and L/S codes are retained while descriptions show Inch/Long/Short.
- Party/Item SiteList behavior and finished-product-only ItemType FM are preserved. Windows Nature/Active/distributor and form transaction/site filters are not silently added. Selecting items does not introduce automatic BF/GSM/dimension filling, pricing or stock workflows.
- Only the support lookup-width constraint expands to 40-400 px, resolving its generated name from the catalog and replacing it transactionally. Legacy tables/source are untouched. New seeds preserve subsequent reviewed edits; source views redeploy explicitly; conflicting field bindings fail. Deployment order is 008, 009, 010.
- Every field uses the same control, including static choices and disabled/read-only fields. Single-column popovers retain exact 100 px table columns but a 280 px minimum outer width for action controls. Label flex sizing prevents long names/placeholders from widening frozen item columns; this fixed a mobile pointer obstruction found during acceptance. Add line and validation focus recognize combobox buttons.
- Separate per-field definition requests were removed: option responses supply popup metadata and shared batched exact-key resolution supplies descriptions. Blank controls do not query until opened. Scope-isolated, bounded cache, cancellation/stale guards and explicit error/Retry behavior remain unchanged.

Verification: 010 applied twice with zero open transactions and exactly 11 Sales bindings. Five focused backend integration tests passed, covering all bindings/original code-label compatibility, exact item columns and related master display values, non-FM exclusion, Buyer persistence, configurable paging/scope/error behavior and existing CRUD. Backend build had zero warnings/errors. Twenty-six focused frontend tests passed; final lint, TypeScript and production build passed.

Authenticated browser checks opened all 11 fields: captions/widths and per-field titles matched configuration, every popup was contained, and old datalist inputs numbered zero. Selecting all 11 displayed descriptions while the actual Validate request carried their original scalar codes; no Save was invoked. Add line focused the next Item combobox. Representative Party/Form/Item/BF/Size Unit/Grain dialogs retained exact widths and accessible Cancel at narrow measured viewports (387 and 320 px), with no page overflow; adjacent item controls remained clickable after the label-sizing fix.

A browser-only read-only defaults fixture rendered 100 repeated item rows / 506 lookup controls. All descriptions hydrated through exactly 11 resolve requests (one key per lookup) and zero separate definition requests. This verifies client batching for repeated keys, not production database latency or multi-user load. The interception and temporary tab storage were removed; no master data or pre-existing orders were changed by verification. Audit/idempotency tombstones from the synthetic integration tests remain. Representative concurrency/query-plan tuning and owner production approval are still required.

## 29. Approved BF/GSM master workspace and contextual creation

The owner approved BF/GSM first, one generic workspace/right drawer, Windows-compatible master persistence and reviewed database/field improvements. Layout confirmation: existing lookup grids remain; only master forms use the right drawer. Scope confirmation: BF/GSM global within the business database; future Party all sites and Item current site, neither implemented here.

- Existing Master menu/resource bindings route to the shared workspace. The same field renderer, action bar, form and API serve both menu maintenance and lookup creation.
- Retain generated site-prefix/numeric six-character internal keys, required 35-character descriptions, SQL-collation duplicate checks, server audit fields and Edit's `Transfered='N'`. No business records, source VB or Sales formulas are rewritten.
- Additive rowversion detects Windows/web edits. Unique-name indexes enforce the Windows duplicate rule across writers; migration aborts on duplicates. Transactional key allocation and portal history prevent web key recycling after deletion. Trim/collapse whitespace deliberately improves web input normalization.
- Master operation grants and visible ancestor/system restrictions are checked server-side independently of Sales permissions; unsupported/read-only POC contexts cannot write. No missing-grant administrator fallback.
- Add-new appears only for implemented, mapped, authorized BF/GSM masters. Preserve order/lookup state; persist first, then verify eligibility via exact-key lookup resolution and select only the code. Failed application is clearly distinguished from failed save and retried without another creation.
- Metadata uses reviewed revisioned definitions/fields and an active pointer. Eligible nullable scalars can be added with explicit SQL plus metadata publication and no bespoke React CRUD code. Unknown/protected/key/audit/scope/FK fields fail explicitly. Old metadata and conflicting row versions fail rather than silently overwriting. Hiding a field preserves stored values.
- View/Add/Edit/Delete/Print/Exit are real operations. Delete is blocked by existing references. Print is a bounded authorized filtered-list preview plus browser Print/Save PDF, not a port of Crystal Reports.
- Deploy [011-simple-masters.sql](../../backend/Techno.Paper.Api/sql/011-simple-masters.sql) after 008-010. [012-master-field-example.sql](../../backend/Techno.Paper.Api/sql/012-master-field-example.sql) is optional and not applied by default. No startup DDL, arbitrary browser SQL or unimplemented Party/Item/fixed-enumeration creation.

Acceptance covers both masters' CRUD/audit, idempotent replay and changed-command rejection, stale edit/delete tokens, concurrent duplicate creation, independent master authorization, read-only denial, protected fields/lengths, existing-reference deletion guards, and a temporary reviewed field revision proving persistence/stale-form rejection/hidden-data preservation. Browser acceptance must check lookup creation/cancellation without saving or editing a pre-existing order, both menu workspaces, print preview, keyboard focus and desktop/mobile containment. Production performance, full multi-persona certification and owner sign-off remain separate.

Verified: 18 focused backend cases (including endpoint registration/startup, master database behavior, navigation and two existing Sales regressions); 30 focused frontend cases; zero-warning backend build, TypeScript, targeted lint and production build. The optional field-publication SQL was executed inside an outer transaction and rolled back: revision 2 worked, the default pointer stayed at 1 and no example column remained.

Browser acceptance used a separate authenticated FY2026-27/HO page. Both existing module shortcuts opened the real BF/GSM workspaces; create/view/edit/delete and audit actor values worked, duplicate creation produced an explicit 409, and filtered BF print preview contained the one synthetic record. New master inputs focused immediately and enforced required/35-character limits. The right drawer measured 480px at 1280px viewport and full width at 390px/320px, with all visible controls contained. Keyboard close and declining a dirty discard preserved values.

From an unsaved new order, BF/GSM Add-new persisted each master, resolved its canonical key and selected the description. Cancelling preserved the search, page 2, focus, prior selection and buyer-order-number draft. A Validate/calculate request contained only BF/GSM codes (one meaningful item, no untouched entry row or descriptions); the incomplete draft correctly returned 422 and was never saved. Buyer/Item/Order-type grids exposed no unsupported Add-new action. A page-local injected failure for the newly saved BF's exact-key resolution retained the old order selection and reported **saved but not applied**; retry resolved successfully with zero further master-create requests. Interception was removed and only the synthetic order draft was discarded. No pre-existing order was edited, saved, reloaded or navigated for acceptance. Synthetic masters were deleted through the authorized API; their specifically identified test command/audit fixtures were cleaned separately.

## 30. Approved simple-master expansion and configurable audit

The owner approved the compatible Windows single-field family only, detailed old/new capture enabled by default per implemented master, reviewed SQL configuration rather than a new admin UI, and audit history restricted to the configured metadata administrator with the master's own View access. Minimum operational actor/time history remains enabled. Capture applies to portal writes, not Windows/direct SQL edits.

Implemented 11 Master-module resources through the existing generic handler/workspace/right drawer:

| Resource | Legacy table / key / description | Input limit | Scope |
| --- | --- | ---: | --- |
| BF | BFMast / Code / Name | 35 | Global |
| GSM | GSMMast / Code / Name | 35 | Global |
| Varity | VarityMast / Code / Name | 25 | Global |
| Godown | GodownMast / GodCode / GodName | 20 | Current site |
| Department | DepartmentMast / Code / Name | 35 | Current site |
| Classification | ClassificationMast / Code / Name | 30 | Global |
| Item Make | ItemMakeMast / Code / Name | 35 | Global |
| Quality Specification | SpecificationsMast / Code / Name | 35 | Global |
| Rejection Reason | RejectionReasonMast / Code / Reason | 50 | Current site |
| Deduction | DeductionMast / Code / Name | 35 | Global |
| Basic Machine | MachineMast / MachineCode / Name | 35 | Global |

Evidence: Windows Master factory `ClsFunction` and shared `FrmSingleFieldMaster` confirm table/column mapping, scope, configured lengths, required names, generated site-prefix codes, SQL-collation duplicate rules, actor/audit columns, `Transfered='N'` on edits and reference-protected delete. Reviewed `MaxLength` may reduce physical column length; it cannot widen it. Godown/Department/Rejection use server-owned `Site_Code`, with scoped reads/prints/writes/history and per-site name uniqueness. Web print intentionally retains the current site's scope rather than printing the entire legacy table. Machine retains its ten-character key; the other keys remain six characters. Its basic Master-module form does not replace the full Machine-module aggregate.

Unit is deferred because its description is the physical primary key. Designation, Bin, Tariff, VAT, Item Category/Group, Policy, Item and Party require separate validation/relationship/dependent-write handlers. No legacy source, primary key, existing business value or missing-site row was rewritten. No new Sales lookup mappings were added beyond the approved BF/GSM mappings.

[013 deployment](../../backend/Techno.Paper.Api/sql/013-master-family-and-audit.sql) preserves BF/GSM revisions, registers nine resources, adds rowversions/unique indexes and audit context/value-policy storage. It aborts instead of repairing incompatible data/schema. It was explicitly applied twice successfully to approved development EcoTech_main; no startup DDL.

[014 policy template](../../backend/Techno.Paper.Api/sql/014-master-audit-policy.sql) configures `CaptureAuditValues` per reviewed master. Policy changes alter the metadata fingerprint; previously opened forms must refresh. Capture-off events retain actor/time/operation/context but not before/after values, earlier history remains intact, and subsequent enabling resumes snapshots without inventing gap values. Normal records and command-response snapshots still contain business values; this is not a privacy-erasure or tamper-proof compliance system. Field snapshots include only reviewed fields. Audit reading is authorized independently of Sales permissions and remains available in read-only contexts.

The generic Options action opens paged audit history in the shared right drawer, with old/new field changes, actor, local display of UTC timestamps, company/site, operation and definition revision. Deleted records remain auditable. Backend pagination uses compatibility-100 `ROW_NUMBER`; audit IDs serialize losslessly as strings. The optional [012 field publication](../../backend/Techno.Paper.Api/sql/012-master-field-example.sql) now preserves scope/limits when copying revisions.

Verified:

- **31 focused backend cases passed**, covering all 11 real-table roundtrips and exact over-limit rejection, three current-site isolation cases, administrator/readonly audit access, capture off/replay/no-key-reuse/re-enable behavior, permissions, concurrency, references, navigation, endpoint registration and existing Sales regressions. The revision-publication case was rerun successfully after preserving scope/length in its fixture.
- **32 focused frontend cases passed**; TypeScript, targeted ESLint and production build succeeded. Backend build had zero warnings/errors. Existing framework/Node configuration warnings were not changed.
- Optional 012 executed twice in separate nested SQL batches under an outer rollback transaction; 014 also validated there. BF remained revision 1; no optional/test column or open transaction remained.
- Live API and portal returned HTTP 200 after restart; all 11 authenticated form endpoints returned expected scope/length and enabled administrator audit capabilities.
- An owned background browser page verified Godown synthetic create/edit/delete history, exactly three events, matching old/new descriptions, UTC/context fields, the expanded edit-value table and a 320px audit drawer. Additional background pages verified Department 35, Rejection Reason 50, Machine 35 and Classification 30 native input limits and initial focus. Godown's native input limit was 20.
- Existing user pages and manual changes were preserved. Only the identified synthetic Godown and its two command/three audit fixtures were removed; background verification pages were closed. The final database contained 11 active revision-1 definitions with detailed capture enabled and no example/test columns.

Maintenance requirements are recorded in [AGENTS.md](../../AGENTS.md) and linked from workspace customization guidance. Full legacy field-level UCP parity, production performance certification, additional auditor roles/retention and specialized-master development remain separate approved work.

## 31. Windows Master Environment Setting

The owner requested the full Windows Environment Setting form with corresponding tabs and fields. Discovery traced `Master.FrmEnviro` designer/load/save/help-grid routines, its menu factory, physical Enviro schema and Sales setting consumers. Confirmed Master scope: **Gate, Sales, Excise, Purchase, CostSheet, Stores**; other modules' identically named environment screens, separate tables and hidden credential fields are excluded.

An exact source-to-SQL comparison passed for **62 persisted column/control mappings and all 48 Windows-required controls**. The disabled nonpersisted Kanta Value display and conversion example are also represented. Reviewed metadata captures native captions, groups, choices, lookup types, multi-selection codes, number digit/scale rules and required fields. All twelve Excise account bindings, four costing groups, three designations, two voucher/category lists and all Sales/Gate/Purchase/Stores settings are present.

The owner explicitly approved browser-safe editable path configuration (no local picker/upload/execution), complete six-tab validation, and order rounding restricted to **0–6**, matching the existing portal calculator. Malformed numbers are rejected rather than silently coerced by VB `Val`. Missing stored values are not shown as invented Yes/No defaults. Existing Warning email criteria is preserved but not newly selectable, following the Windows A/M picker. Optional empty lookup tables do not force fabricated master entries.

The dedicated singleton handler preserves site ownership and numeric 1–99 IDs, uses independent Master Environment menu/ancestor/system permissions, and exposes View/Edit/Save/Cancel/Exit plus authorized audit, without unsupported singleton Delete/Print. Reviewed layout/fields use the same master revision/policy model and shared controls. Multi-selections display descriptions while preserving `|code|,|code|` semantics. Open-rate voucher selection retains the PORD/PORDR filter; seven SQL views provide the exact reference families.

Environment lookup follow-up: the nine multi-select fields use a searchable checkbox help grid. Existing picks are checked, search/paging preserves selections, and the header checkbox affects only the displayed page. Apply changes the form draft, Cancel leaves it untouched, and Save persists the existing pipe-wrapped code format. The form shows comma-separated descriptions and uses Windows-derived field widths where space permits. The change is limited to portal presentation; lookup authorization, SQL definitions and required-field rules are unchanged.

[015 deployment](../../backend/Techno.Paper.Api/sql/015-environment-settings.sql) was applied twice with zero open transactions, adding rowversion, a non-null-site unique index and reviewed configuration without updating existing business values. Unknown/server-owned/credential fields reject; untouched hidden values remain unchanged. Serializable save combines rowversion/metadata checks, idempotency, values and old/new audit atomically. Shared menu-access and audit helpers avoid a parallel permission/audit implementation. Existing caller permission contracts were kept consistent with asynchronous lookup authorization.

Verification: **49 backend cases passed**, including all prior Master/Navigation/Sales regressions and 18 Environment cases: field census, own-menu versus Sales access, no-grant/read-only/nonadministrator/site denial, exact digit/decimal/range and malformed/reference validation, full settings create/edit, actor/history, capture-off/resume, unchanged replay/reused-command conflict, stale rowversion, server-owned-column rejection and pre-commit failure rollback. All successful configuration writes and policy changes were tested in outer rollback transactions; the original record version and approved values were unchanged afterward.

**34 frontend tests passed**; backend build had zero warnings/errors; TypeScript, targeted ESLint and production build passed. API OpenAPI and portal HTTP 200 readiness were verified after restart. Browser session expiration requires private owner sign-in for final authenticated interactive acceptance; no password was requested or application authentication bypass introduced. Maintenance guidance and API/deployment examples were updated.

## 32. Sales Order item-table screen fit

The owner approved compacting **only the item table**. Metadata-driven column sizing gives Item/Description wider shares, reduces numeric/choice widths, wraps headings and retains fixed row/tolerance/Tax/action widths. Tolerance and Tax use icon buttons with accessible row labels and Edit/View tooltips; their existing dialogs, authorization and stable-row callbacks are unchanged. All columns remain present, including approved extensions. The calculated minimum increases with additional fields; narrower containers retain local horizontal scrolling instead of reducing control sizes indefinitely.

Styling is scoped to `erp-order-items`, not the shared grid or field controls. Order details, totals, lookup result grids, navigation, SQL metadata and business calculations are unchanged. Fixed column widths keep sticky Item and Tax/actions aligned when the table stretches.

Authenticated isolated-draft browser verification retained all **18 current columns**, including Web measurement. At a 1440px viewport with expanded navigation, the original table required **1796px** in a **1126px** container; the compact table fits that same **1126px** without horizontal overflow. Desktop widths 1366/1440/1920 fit; 1280/768/390 retain contained scrolling with no page overflow. The current minimum is 1036px. Column-weight proportions, fixed offsets, wrapped headers, control containment and unchanged expense typography/padding were measured. Mobile scrolling preserves Item at 32px, Tax at 36px from the right, actions at the right edge and both scroll shadows.

Keyboard traversal, BF F4/open, both row dialogs, mobile Tax containment/cancel, trailing-entry preservation and row removal passed. An unsaved draft still calculated **2 x 12.50 = 25.00** through the existing API; no orders or settings were saved. **20 focused frontend regressions**, TypeScript, targeted ESLint and production build passed. The restarted portal and unchanged API returned HTTP 200. An unsaved draft still calculated **2 x 12.50 = 25.00** through the existing API; no orders or settings were saved. **20 focused frontend regressions**, TypeScript, targeted ESLint and production build passed. The restarted portal and unchanged API returned HTTP 200.

## 33. Item quantity/weight summary and hidden demo fields

The owner requested quicker incremental changes and explicitly deferred Playwright to the end on request. A compact totals strip beneath the item grid follows the Windows `FCalculate` footer: Quantity sums `Reel`, Weight sums `Qty`, each with two-place running-total rounding. It remains outside the row scroll area, updates from current active draft rows/server calculations and excludes untouched entry rows. Exact decimal arithmetic avoids floating-point loss; invalid/negative edits visibly withhold the affected total.

Only Sales editor rendering hides `WebPocReference` and `WebPocMeasure`. Their SQL metadata, stored values and unchanged writable payload projection remain intact. Other fields, totals/financial rules, permissions and lookup grids are unchanged.

Rapid iteration uses hot reload, focused unit tests and type-check/lint. Full production/acceptance checks are batched at the end; browser/Playwright automation requires the owner's explicit request.

Verification for this slice: **16 focused tests**, TypeScript and targeted ESLint passed, covering Windows running-total rounding, decimal precision/scientific values, invalid edits, entry exclusion and hidden-value payload preservation. The hot-reload server and Sales editor route compilation returned HTTP 200. No Playwright, full production build or database writes were performed.

# Web ERP implementation and maintenance

These requirements apply to this portal. Never change the Windows ERP source or deploy to original/production databases as part of a POC task.

Before changing a feature or shared component, consult the workspace [technical design index](../.github/docs/technical-design/README.md). When behavior, data flow, or a contract changes, create or update the corresponding short design in the same change and add new pages to the index. Keep small supporting components on the owning feature's page.

## Legacy parity and scope

- Trace the Windows menu factory, form validation/save/delete/print paths, shared helpers and actual SQL keys/foreign keys before implementing a feature. A form with few controls is not necessarily a simple master.
- Use the shared metadata-driven workspace for reviewed single-field masters. Keep aggregates, dependent updates, natural-key edits, grading/account relationships and document-specific rules in deliberate handlers.
- Preserve generated internal codes, code-only lookup persistence, field limits, SQL-collation duplicate checks, site defaults/filtering and reference-protected deletion. Document intentional differences; never repair existing business data silently.
- Bind each resource to its own existing menu grants, ancestor visibility and system restrictions. Do not borrow Sales permissions, trust browser capabilities, or reproduce missing-grant `AEDP` fallbacks.
- Menu/action permission support does not prove every legacy field-level `User_Control_Permission` rule is implemented. Assess those rules before exposing specialized controls; document remaining boundaries.

## Generic configuration

- Reuse `MasterService`, the master workspace/drawer, `ErpField`, `RecordActionBar` and `ErpLookup`; do not clone CRUD screens or permit browser SQL/table names.
- Publish new definition/field revisions and atomically switch the active pointer. Preserve scope and `MaxLength` when copying revisions. Never mutate published rows to change field behavior.
- Adding a supported nullable scalar requires reviewed additive DDL plus an approved metadata field. Do not automatically expose catalog columns. Keep keys, scope, audit, permissions, computed/generated and FK fields server-owned.
- Hiding a field must not delete its stored values. Compare metadata fingerprints and record rowversions separately; reject stale writes explicitly.
- Unit's description is its physical primary key. Do not register it as ordinary generated-code CRUD. The Master module's basic Machine form is separate from the Machine module's full equipment aggregate.
- Environment Setting is the `environment` handler over one `Enviro` row per current site, not generated site-prefix/name CRUD. Keep the six Master-form tabs and reviewed field/layout rows together when publishing a revision. Other modules' environment tables and hidden credentials require separate discovery/approval.
- Reuse the resource-authorized SQL lookup service and multi-lookup wrapper. Persist canonical codes and legacy pipe-wrapped multi-selections, not display names or browser SQL. Do not turn empty/unavailable references into invented records or defaults.
- Keep desktop EXE/data/image paths configuration-only; never execute/read/upload arbitrary files from these values. Preserve existing nulls and hidden settings. Validate all required tabs, supported numeric precision/ranges and references before any write.

## Commands, auditing and reliability

- Check authentication, captured workspace context and operation authorization on the server for every request. Preserve read-only and approved-development-write restrictions.
- Use parameterized queries and validated server registry identifiers. Keep data, operational history and idempotent command results in one bounded transaction. No retries with a changed payload or UUID.
- Retain minimum create/edit/delete actor/time history even when detailed value capture is disabled: it supports creator attribution and prevents audited key recycling. Do not purge it independently of numbering/command history.
- `MasterRegistration.CaptureAuditValues` configures subsequent portal before/after value capture per master. Preserve existing logs; do not fabricate historical values or claim this captures Windows/direct SQL edits.
- Capture only approved values, UTC occurrence time, server actor, company/site and definition revision. Restrict history reading to the configured metadata administrator plus that master's View access, including server enforcement.
- Normal business records and idempotency response snapshots still contain values when detailed auditing is off. This setting is not a data-erasure/retention mechanism or a tamper-proof compliance guarantee.
- Surface failures with standard diagnostic references. Distinguish an uncertain save from a saved master that failed to apply to its caller; retries must not create duplicates.
- No startup migrations, cascading master deletes, silent defaults/repairs, broad swallowed exceptions or client-owned audit fields.

## UX and verification

- Keep existing lookup grids/column widths. New master forms open in the shared right drawer; preserve lookup query/page, caller draft, canonical code and focus.
- Support keyboard focus/close, explicit dirty-discard confirmation, busy guards, viewport containment and local scrolling. Print only authorized bounded datasets.
- Validate each registered table's real CRUD/audit/limits, site isolation, independent permission denial, stale tokens, references and idempotency. Verify audit-on/off gaps, history access and enabled capture resumption.
- Run focused tests, type-check/lint and production build. Verify a live API start, not just successful compilation.
- During the owner's rapid Sales Order UI iteration, run only relevant unit tests plus type-check/lint after each slice. Use the local development server for hot reload; defer production rebuilds and full acceptance suites to the final batch. Run Playwright/browser automation only when the owner explicitly requests it. API behavior changes still require their targeted backend validation.
- Verify environment saves/creation/audit/policy/replay within rolled-back transactions. Do not commit test values to active settings just to exercise the form; changing coefficients can affect open Sales drafts.
- Browser acceptance must use an owned isolated page and uniquely identified synthetic records. Do not save or discard another user's order. Clean only test-owned records/history and temporary schema fixtures.
- Update README deployment/configuration guidance, API examples and the directly related PRD. Keep SQL compatibility at the actual database level (currently 100); use existing `ROW_NUMBER` paging.

<!-- BEGIN:nextjs-agent-rules -->

## This is NOT the Next.js you know

This version has breaking changes — APIs, conventions, and file structure may all differ from your training data. Read the relevant guide in `node_modules/next/dist/docs/` (resolved from this file's directory; in monorepos the `next` package may not be visible from the repo root) before writing any code. Heed deprecation notices.

This block is written and re-added by `next dev` — verify at `node_modules/next/dist/server/lib/generate-agent-files.js`. Removing it from a diff only re-creates the uncommitted change; committing it with your work keeps the tree clean.

<!-- END:nextjs-agent-rules -->

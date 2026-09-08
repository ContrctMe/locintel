# Engineering improvements — 2026-09-08

Baseline: LocIntel `main` / `origin/main` at `110429e`. Work happens on
`codex/engineering-improvements` in both repositories. No deployment is part of
this change. Upstream commits are `e74e2c5` (shared imports/lookups/pickers),
`0262df5` (fork maintenance and test concurrency), `00d1562` (picker retry), and
`5b07779` (idempotent full-SHA sync). Upstream publication is pending explicit
authorization; these commits are retained in the local template feature branch.

## Ownership and behavior

| Upstream template | LocIntel product |
| --- | --- |
| Bounded CSV implementation and limits contract | Incident import limits and row validation |
| Upload staging releases its connection during object-store reads | Incident staging uses atomic SaveChanges after reading and parsing |
| Tenant-filtered bulk stored-file lookup | Incident, case and AI-brief attachment consumers |
| Server-search site picker; paginated file picker | Incident/report/filter, marketplace, patrol, alert and evidence controls |
| Scoped site metadata lookup with bounded IDs | Incident labels and analytics coordinates for displayed sites |
| Complete namespace rename, snapshot ancestry guard, test concurrency | Canonical upstream remote and product-specific sync inventory |

CSV sites: 4 MiB, 10,000 rows, 16 columns, 500 characters per field.
CSV incidents: 4 MiB, 5,000 rows, 16 columns, 20,000 characters per field.
Invalid structure and exceeded limits return 400 without creating a batch.
Incident domain validation still marks individual bad rows in the preview;
undefined numeric category/severity values and values exceeding database field
limits are invalid. No alerts are emitted
for committed historical imports.

Bulk file reads keep tenant and origin filtering, omit missing files, and do
not issue download access. Parent-case/incident authorization and custody
behavior remain enforced by their existing flows. Site metadata keeps the
normal site-read scope, so it does not expose an inaccessible site's name or
coordinates merely because another feature references its ID.

## Sync inventory

Canonical upstream: `https://github.com/Contrct-Owner/multi-tenant-site-saas.git`.
Local-checkout remotes are not a portable source of truth. The normalized
snapshot records its complete source commit. Until the upstream improvement
branch is published and merged, retain the local source commits. After publication,
use `template/codex/engineering-improvements` as the sync ref;
`template/main` may not contain the new baseline and must not overwrite it.

Intentional product differences to review during every sync:

- Incidents, Entities, Cases, Marketplace, Alerts, Network and Patrols modules;
  their catalog registration, capability/entitlement keys and navigation.
- Anthropic/Twilio adapters, notification preferences and product notices.
- POST signup form and console referrer policy; explicit evidence issuance,
  custody logs, need-to-know case/entity access and legal holds.
- Existing LocIntel runtime-lifetime fixes in HTTP limiting, connector completion,
  scan/preview/purge handlers and generated-handler CI checks. These are shared
  candidates if upstream changes those paths; do not overwrite them blindly.
- Applied migrations remain immutable, including their existing formatting.

## Verification

The maintained command sequence is in [Engineering maintenance](engineering-maintenance.md).
The current run uses `LOCINTEL_TEST_SHUFFLE=20260908`, at most two integration
collections per process, and runs the browser stack separately. Coverage now
matches `LocIntel.*` rather than the unrenamed template assemblies.

Upstream checks passed: 76 platform unit tests, 56 architecture tests, 13 focused
integration/contract tests, 78 console tests, frontend typecheck/lint, and the
two-sync regression including complete rename and rejection of an older snapshot.

LocIntel checks passed:

- Full integration suite: 495 passed, one intentional scale-baseline skip, zero
  failures (seed 20260908, 10m53s). After the final database-field validation
  change, all five incident-import boundary tests passed again, including the
  newly added field-limit case; the full suite was not repeated for that change.
- 104 unit tests, 72 architecture tests, and 113 frontend tests.
- 144 browser checks: 48 each in Chromium, Firefox, and WebKit. These include
  product site search beyond the first 200 sites and search-error recovery.
- Clean solution build, frontend typecheck/lint and production build; CI gate,
  infrastructure security, reporting-image and DigitalOcean render checks.
- Integration-only coverage: 88.0% lines and 70.2% branches across 27 LocIntel
  assemblies. This is measured coverage, not a production acceptance claim;
  live Anthropic and Twilio adapters remain outside this run.

The sync regression also verifies that requesting the same complete source SHA
is a no-op. Applied migration files were preserved unchanged.

## Remaining operating acceptance

These changes do not establish production sizing or provider qualification.
The gateway/fleet deployment, sustained mixed-storage load, backup restoration,
and hosted acceptance remain tracked in their existing operational documents.

# Reporting: authoring a report in a fork

- **Owner:** Project maintainers.
- **Parent:** [LocIntel README](../README.md).
- **Decision:** [ADR 56](decisions/0056-reporting-execution-and-delivery.md) settles
  what the template owns, how the three gates apply, how PDFs are accounted for,
  and the durability and lifecycle rules. Read it before changing execution.

The template owns job execution, quota, storage, ZIP assembly, retention and
download authorization for every registered report. This guide covers the part a
fork owns: defining a report, authorizing its data, and configuring its limits,
basemaps and entitlements.

## Registering a definition

Start from [SiteReportDefinition](../src/Modules/LocIntel.Modules.Reporting/SiteReportDefinition.cs)
or [AggregateReportDefinition](../src/Modules/LocIntel.Modules.Reporting/AggregateReportDefinition.cs).
Implement [IReportDefinition](../src/Modules/LocIntel.Modules.Reporting/IReportDefinition.cs)
and register the concrete type with scoped DI:

```csharp
services.AddScoped<IReportDefinition, MyApplicationReport>();
```

Register it before Wolverine builds its handlers, and register by concrete type —
lambda factories force service location and Wolverine refuses them. The registry
rejects duplicate IDs at startup. `Aggregate = false` participates in single and
bulk requests; `Aggregate = true` receives all captured site IDs for one aggregate
PDF. The console discovers registered types. Fork types initially receive the JSON
options editor; add a feature-local options form when the application needs one.

Implement the contract as follows:

1. Give the definition a stable ID, display name and integer version. Validate
   options with a bounded, strict parser; return a useful validation error rather
   than silently ignoring unknown fields. The API also caps options at 16 KiB.
2. Authorize using the explicit request organization, user and site IDs. At
   admission and generation the dependency argument is null; check the resources
   the options intend to include. At download it contains the resources actually
   included in the saved PDF, so recheck their current access and state. Return
   false on access loss. Optional-image fallback must never hide an authorization
   failure.
3. Read application data through established contracts or the owning module's
   permitted data access. Read fresh data when rendering; the captured selection
   is not a data snapshot. Bound rows, image sizes and geometry complexity.
4. Write a complete PDF to the supplied stream with PDFsharp/MigraDoc. Do not
   dispose that stream or create a separate job queue. Observe cancellation during
   I/O and expensive loops. The executor limits output bytes and execution time,
   and abandons a render that outruns `Reports:ItemTimeoutSeconds`.
5. Return only the protected resource IDs actually included, plus user-safe
   warnings. A required visual failure must fail the item; an optional visual may
   become a placeholder and a warning. Never put provider URLs or secrets in
   warnings. See `ReferenceReportAccess` and `ReferenceReportRenderer` for the
   implemented photograph and overlay authorization and fallback behaviour.

The executor owns selection capture, reservations, charging, attempts, storage,
ZIPs, manifests and retention for every registered definition. One successful PDF
item costs one quota unit, including an aggregate; a fork does not record another
usage event from `RenderAsync`. Retries and duplicate delivery retain the item's
identity, which prevents successful PDFs from being charged again.

## Site photographs

Upload photographs from a site's Files view. File creation accepts an optional
`siteId`; attachment requires both site-read and file-manage scope on that site.
The existing ticket, scan and completion flow applies, and the report picker
shows that site's clean JPEG/PNG files. Omitting `siteId` retains organization
uploads for existing integrations.

Reference reports recheck every attached photograph's site against current
site-read and file-read scope at admission, generation and download, including
photographs attached to a site other than the report's subject. Losing that
access prevents downloading PDFs that contain the photograph. Custom definitions
must enforce equivalent checks on their own dependencies.

## Versioning a definition

The registry holds one version per ID. Jobs pin that version; replacing it with a
different version makes old queued work fail as unavailable and prevents old
artifact downloads. To overlap deployments while retaining old downloads, register
the new implementation under a new ID and keep the old definition until its
accepted jobs and artifacts have drained. Do not silently render an old job with a
new incompatible definition. This is a documented template limitation, not
side-by-side version support.

## Fonts

The bundled Liberation Sans resolver covers the qualified English and Latin-script
layouts. A custom global resolver must be installed before the first render and
must continue to resolve the reference font family while reference layouts remain
registered. Retain the bundled font and library licence notices in published
images. Qualify additional scripts, fonts and layout changes with rendered output,
not text-only PDF assertions.

## Basemaps

No provider is enabled by default. Supply one whose terms permit server-side PDF
use:

```json
{
  "Reports": {
    "Basemaps": {
      "licensed-provider": {
        "Url": "https://tiles.example.invalid/{z}/{x}/{y}.png",
        "Attribution": "Replace with the provider's required attribution"
      }
    }
  }
}
```

Deployment secrets may supply a provider key inside the configured URL. URLs and
keys are never returned by the catalog or placed in PDF warnings.
`GET /api/reports/basemaps` returns only provider IDs and attribution, to users
allowed to generate reports. Tenant and browser basemap URLs are not trusted
worker inputs. Connections require HTTPS on port 443, use validated public DNS
addresses without a second resolution, disable proxies, cookies and redirects, and
reject local, private and transition-network destinations. Tile bytes pass the
bounded image decoder. Provider transport and image failures produce the optional
map placeholder. A synthetic tile fixture does not verify real DNS, TLS, provider
credentials or redistribution terms.

## Limits

`Reports:MaxBatchItems`, `MaxAggregateSites`, `MaxQueuedJobsPerOrg`,
`ItemTimeoutSeconds`, `ArtifactDays`, `MetadataDays`, `MaxPdfMiB` and `MaxZipMiB`
are operational budgets, independent of the commercial entitlements below. Each
has a ceiling enforced at startup; an out-of-range value fails the host rather
than silently clamping.

## Entitlements

`reports.enabled` and `reports.monthly` use the existing entitlement catalog,
operator assignments, exceptions and downgrade preflight. Set them through the
operator entitlement API or operator console; organization users cannot raise
their own allowance. The reporting page displays remaining, consumed and reserved
PDF counts.

Response codes a fork's client should distinguish: **402** is a plan restriction,
**429** is the separate operational job-queue limit, and **503** is transient
admission contention. Retry an uncertain submission with its original idempotency
key and unchanged body.

On a drained deployment the migrate role runs `PlanEntitlementBackfill` after
schema migrations. It fills missing bundle codes, including `reports.monthly`, for
known Active, Trialing and PastDue subscriptions using the fork's current
`PlanCatalog`, pages 200 subscriptions at a time and inserts with
`ON CONFLICT DO NOTHING`. Existing assignments, their timestamps and entitlement
exceptions are untouched, and an interrupted pass can be rerun safely. This is
missing-code backfill, not reconciliation of changed prices or assigned limits.

Upgrade order: drain and stop API and worker writers, run the new image's migrate
role against each regional database with owner credentials, check the exit status
and inserted-row count, then start API and worker with the application
credentials. Do not run the backfill concurrently with older billing writers.

## Deployment

Deploy an API publisher and at least one worker listener using the shared
PostgreSQL transport and the same private object store. Local disk storage is only
shared when all processes mount the same filesystem; independently deployed
containers need the configured shared object-store adapter. API and worker roles
connect as `app_user`; the migrate role applies the reporting schema.

## Proving a rendering change

`tools/reporting-proof.sh` runs `LocIntel.Tools.ReportingProof`, which renders the
reference definitions against local fixtures and verifies the output. Use it when
changing layout, fonts or image handling; it needs no provider credentials.

## Related documentation

- [ADR 56: reporting execution and delivery](decisions/0056-reporting-execution-and-delivery.md)
- [Production conventions](production.md)
- [Architecture decisions](decisions/README.md)

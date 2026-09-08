---
title: "Reporting: the template owns execution and delivery, forks own content"
status: accepted
pinned: false
date: 2026-09-07
---

# 0056. Reporting: the template owns execution and delivery, forks own content

## Decision

LocIntel owns reliable report execution and delivery. Forks own report content,
layout, branding, options, data selection and report-specific authorization.
A fork adds a report by registering an `IReportDefinition`, without touching job,
ZIP, retention or download handlers.

Three request shapes exist. A **single** report is one PDF for one site. An
**aggregate** report is one PDF spanning an explicitly captured collection of
sites. A **bulk** report applies one registered type and shared options to many
sites, producing separate PDFs plus a ZIP. Aggregate and bulk are different
things, not two names for the same fan-out.

Selection is captured at submission and never silently expanded or truncated.
An oversized request is rejected whole. Organization-wide selection requires
access to every included site; a narrower request is labelled "accessible sites".
Data is read fresh at generation time: the captured selection is not a snapshot,
and no point-in-time guarantee is offered, including within one PDF.

## Tenancy, permissions and the three gates

`reports.enabled` is gate 1 and controls new submissions and explicit retries.
`reports:generate` is gate 2 for submission, cancellation and retry. Reading a
run and downloading its output gate on `files:read`, because outputs are ordinary
site files: the reader additionally needs `sites:read` over every included site.
Scope filters through the intersection of those grants; it never fails the call.

Requester ownership alone never authorizes a download. Each item records the
protected resources actually included, and download rechecks current access to
all of them. Losing access to one resource blocks that artifact, not the run.
Job details after access loss use identifiers and safe summaries, never protected
names. Recheck happens again before generation: an aggregate fails if any selected
site became unauthorized, and a bulk job fails only the affected items.

Disabling the entitlement must not prevent cancellation or authorized downloads of
already accepted work, so `Cancel` deliberately omits the gate-1 check.

## Accounting

The accounting unit is one PDF output. Single and aggregate reserve one unit;
bulk reserves one per captured item. ZIP assembly, downloads and redelivery add
nothing. The period is a UTC calendar month.

Reservation is atomic with the durable job or the whole request is rejected.
Publication converts a reservation to consumed usage in its reserved month;
failure and cancellation release unfinished units. Explicit retry reserves only
the failed items against the retry month. Reservations survive month rollover and
process crashes, and deleting report metadata never refunds usage — which is why
`reporting.quota_entries` deliberately carries no cascading foreign key to the job.

`reports.monthly` is a strict Limit/Block entitlement. Template defaults are
1,000 PDFs per month on the default tier, 5,000 on Growth and 50,000 on Scale.
These are editable template allowances, not measured capacity. The usage probe
counts reserved and consumed units together so downgrade preflight sees
outstanding work.

## Durable execution

Job state lives in PostgreSQL; Wolverine delivery and the recovery sweep can both
resume it. Only short metadata operations hold a transaction or row lease.

Object storage and PostgreSQL are not one atomic transaction. The artifact's
intended object key is persisted before any bytes are written, so a stable
identity exists across every crash window, and orphan cleanup can find it. Every
state transition is guarded by job state, revision and lease owner, so duplicate
delivery, a resumed sweep, worker death and cancellation cannot publish a stale
artifact or charge an item twice.

Artifact readiness is separate from item success: a succeeded item does not imply
a ready ZIP. The bundle is assembled only once every item is terminal, and a
bundle failure is recoverable without regenerating successful PDFs.

Cancellation is cooperative and does not promise an immediate stop inside a
synchronous rendering call. Those calls are bounded by a timeout the executor
enforces, cancellation is checked between stages, and a canceled attempt cannot
publish a later ZIP.

Jobs pin the report definition's version. A deployment that cannot execute that
version fails the job clearly rather than substituting a different definition.

## Artifacts and lifecycle

Successful PDFs become durable site files through the outbox and then follow the
ordinary file lifecycle: trash, legal hold, erasure and organization purge. An
individual PDF attaches to its site; an aggregate PDF is one file linked to every
included site, and reading it requires the full included selection. ZIP bundles
belong to the run, not to a site, and their bytes are never duplicated.

Runs link to their files and sites, and files link back to their originating run.
Run provenance is kept while files still reference it. Unpublished attempts and
ZIPs expire after seven days; metadata without published PDFs after 30 days.
A download URL's lifetime never extends beyond the artifact's remaining retention.

Report artifacts are never registered as broadly downloadable files that bypass
the reporting checks; any Files integration preserves the stricter contract on
every download path.

## Maps, photographs and resource limits

Browser tile URLs are not automatically trusted for worker-side fetches. Report
basemaps are explicitly configured providers, validated at startup and resolved
per address at connect time, with bounded timeouts and response sizes, no
redirects, and no provider key in logs, PDFs or manifests. Requests never supply
arbitrary image URLs.

Only approved clean images are usable, and compressed bytes, decoded pixels,
image count, PDF size, temporary disk use and generation time are all bounded.
Neither the complete set of PDFs nor a whole ZIP is accumulated in process memory;
output streams through a bounded temporary file into the object store.

Definitions mark visuals required or optional. A missing required visual fails
the item; an optional failure produces a labelled placeholder plus a warning in
both the PDF and the manifest. Permission revocation is an authorization failure
and is never softened into an optional-image fallback.

## Consequences

The registry holds one version per report ID. Replacing a definition with a new
version makes queued work fail as unavailable and blocks old artifact downloads.
Overlapping deployments register the new implementation under a new ID and retain
the old definition until its accepted jobs and artifacts drain. This is a
documented template limitation, not side-by-side version support.

Layouts are qualified for English and Latin-script names and addresses with the
bundled Liberation Sans resolver. Complex-script and right-to-left layout are not
qualified; a fork changing fonts or scripts must qualify with rendered output,
not text-only PDF assertions.

Deployment requires an API publisher and at least one worker listener sharing the
PostgreSQL transport and the same private object store. Local disk storage is only
shared when every process mounts the same filesystem.

See [docs/reporting.md](../reporting.md) for the fork authoring guide.

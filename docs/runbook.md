# Incident runbook

Written for whoever is on call for a fork, not for the person who built it.
Each procedure assumes the deploy guide's topology (migrate → api/worker)
and the operator console at `/operator`. When a user reports an error, ask
for the **trace id** from the message — it joins directly to the exported
traces.

## Dead letters are growing

Failed background work lands in the dead-letter store; the operator page
shows it (count, message type, exception, tenant) and polls every 30s.

1. Read the exception. **Transient** (timeout, connection refused,
   provider 5xx): fix or wait out the cause, then **Replay** — the
   durability agent re-injects the envelope and the work completes. Replay
   is safe for the template's messages: handlers are idempotent
   (re-delivery is a tested path) and a replay that fails again just
   returns to the store.
2. **Deterministic** (validation error, null tenant, poison payload): fix
   forward if the cause is a bug; **Discard** only when the message can
   never succeed and its work is superseded. Discard is permanent.
3. A steadily growing store with healthy dependencies usually means a
   handler bug shipped — check the exception's stack against the last
   deploy (`/healthz` has the version).

## A dependency looks down

`/api/operator/health` (Dependencies card) probes the database, object
store, and SMTP with latencies. Cross-reference the deploy guide's
degradation table for blast radius — notably: Postgres is the one true
dependency; email down means the *contact tier* can't start sessions but
everything else runs; provider (WorkOS/Stripe) outages degrade logins and
checkout but never entitlement enforcement.

## Migration failed mid-deploy

The migrate role runs before api/worker and exits non-zero on failure —
api/worker replicas keep running the OLD version (they gate on migrate
completing). Nothing is half-applied beyond the failing migration's own
transaction. Fix the migration **forward** (applied migrations are
immutable — write a new one), redeploy. Never edit an applied migration;
never run api/worker with owner credentials to "get past" a permissions
error (that silently disables RLS — ADR 38).

## Email: bounced address, angry user

The 422 on contact-link issuance tells the tenant to contact support.
Operator page → **Email suppressions** → search the address → verify with
the tenant that it's real → Unsuppress. Repeated bounces damage the
sender reputation every org shares, so verify first.

## "Which org is this customer?"

Operator page → **Find a customer** → paste the ticket's From address →
the hit's org buttons jump to that org's management panel. From there:
entitlements, suspend/reactivate, export, or **Impersonate** (time-boxed,
banner shown, audited into the tenant's own trail — tell the customer if
policy requires it).

## Restore from backup

Practice this before you need it. PITR-restore the database to a new
instance, point a staging api/worker at it (migrate role first — it will
no-op if current), and verify: `/healthz` ok, an org's sites list renders,
and the RLS smoke from the deploy guide returns zero rows without a
tenant. The object store is separate — file rows restored from before an
upload have no bytes; the download endpoint 404s them (no corruption,
just absence).

## Secrets

API keys and webhook secrets rotate from the Developers page with overlap
windows (24h default) — prefer rotation over revocation for live
integrations. The database app password rotates by updating config and
restarting api/worker (migrate re-applies the role grant). WorkOS/Stripe
keys rotate at the vendor, then in config, then restart — boot guards
refuse a missing key rather than limping.

## Watch these

Wire alerts in your collector on: dead-letter count > 0 for 15m; 5xx rate;
`/api/operator/health` probe failures; webhook delivery failure streaks
(per-org deliveries are on the Developers page). The template ships the
signals (OTLP), not the alert rules — thresholds are deployment-specific.

## Browser CI failed

Download the `browser-failure-diagnostics` artifact. Alongside Playwright traces,
`stack-logs/<run>/` contains API, console, public-app, and PostgreSQL logs from
before teardown. Correlate failed request paths/statuses and trace IDs before
rerunning; a successful rerun alone does not explain the original failure.
CI enables HTTP request-duration logging to distinguish slow server requests
from proxy/browser delays. Locally, reproduce that logging with:

```bash
env 'Logging__LogLevel__Microsoft.AspNetCore.Hosting.Diagnostics=Information' \
  tools/e2e-stack.sh --project=chromium
```

Every run prints its unique temporary log directory. Failed runs also copy logs
into the artifact tree; subsequent Playwright runs clear that tree, so preserve
evidence before another run. Treat traces/logs as sensitive: they can contain
test session cookies and identifying data, and should not be published openly.
`bash tests/e2e-stack-artifacts.test.sh` checks the failure path with an invalid
browser project after booting the real stack. It requires Docker and the normal
browser dependencies, and succeeds only if the original invocation failed and
all four diagnostic logs were retained.

Related: [production guide](production.md), [current verification and open risks](software-maturity-review-details.md),
and [project overview](../README.md).

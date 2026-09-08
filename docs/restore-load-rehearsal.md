# Local restore and sustained-load rehearsal

2026-09-08. This is a disposable local Development rehearsal, not hosted staging
acceptance, off-server backup qualification, or production capacity sizing.
No staging destination was supplied and no provider resources were provisioned.

## What was exercised

- Quiesced two API replicas and the worker before taking a PostgreSQL custom dump
  and copying the local object store and data-protection keys.
- Restored into a separate database/container stack and fresh object/key directory.
  The target's migration bootstrap supplied the same local runtime role; a roles
  dump was also retained. This does not establish managed-provider role portability.
- Verified the original evidence bytes by SHA-256, authorized case access through
  the original session, evidence metadata, exact custody event IDs/actions, and hold.
- Verified the actual `app_user` has neither superuser nor BYPASSRLS and that another
  tenant cannot read the restored case.
- Verified held-file deletion returns 409; released and reapplied the hold through
  the target worker/outbox while the sustained read workload was running.
- Used the existing k6 acceptance harness across sites, search, detail, feed,
  public listing, and near-sort routes. Responses must contain the expected data;
  failed responses, dropped arrivals, latency breaches, and replica imbalance fail.

Source application commit: `e9fbbaf` (marketplace batch, including import fixes).
Image: `sha256:77cb1c8626b4cc5b20c47d06cc4d8f6da053ab49b1d43f4eb5be5d55179c0205`.
Dataset: 106 sites, including 100 seeded through application writes. Failed initial
fixture attempts account for extra synthetic records; no real data was used.

The seed runner originally issued eight same-organization site writes in parallel,
which conflicted with the existing capacity reservation guard. It now seeds serially;
timed workload requests still have no retries or excluded failures.

The first load run also exposed case-sensitive instance-header lookup in the k6
harness. HTTP headers are case-insensitive; k6's normalized spelling differed from
`X-LocIntel-Instance`, so every response counted as an unknown replica. The good
control reproduced the failure. Lookup now ignores header case, and all six
positive/negative harness controls pass without relaxing thresholds.

## Measured result

The corrected five-minute run passed every unchanged threshold at 24 scheduled
requests/second: 7,201 successful business responses, zero failures and zero dropped
arrivals. Aggregate p95 was 16 ms and p99 was 19 ms. Replica shares were 50.007% and
49.993% (accepted range: 45–55%).

| Route | p95 (ms) | p99 (ms) |
| --- | ---: | ---: |
| Sites | 15 | 18 |
| Search | 16 | 19.01 |
| Detail | 12 | 14.01 |
| Feed | 18 | 20 |
| Public | 12 | 15 |
| Near | 15 | 19 |

Every route stayed below the existing 100 ms p95 / 250 ms p99 limits. This proves a
modest local stability workload on restored data; it does not establish saturation
capacity or hosted latency. The first run's replica-header failure is retained as a
failed run, not counted as acceptance.

## Repeat the rehearsal

Use an otherwise unused pair of Compose project names and subnets. The examples
below are local disposable resources only. Do not point them at an existing stack.
Keep backup, fixture and key files private and outside Git. Install an official k6
binary, verify its release checksum, and put its directory on PATH (tested: 2.2.0).

```bash
export COMPOSE_PROJECT_NAME=locintel-rehearsal-source
export GATEWAY_RUNTIME=/private/tmp/locintel-rehearsal-source
export GATEWAY_SUBNET=172.30.247.0/24
tools/gateway-stack.sh up 2 1
rehearsal_address=$(tools/gateway-stack.sh compose port --index 1 gateway 8080)
SEED_SITES=100 FLEET_BENCH_FIXTURE="$GATEWAY_RUNTIME/fixture.local.json" \
  node tools/fleet-bench.mjs "http://$rehearsal_address" 15 32 2
FIXTURE="$GATEWAY_RUNTIME/fixture.local.json" PROOF=/private/tmp/locintel-rehearsal-proof.json \
  node tools/restore-proof.mjs seed

mkdir -m 700 /private/tmp/locintel-rehearsal-backup
tools/gateway-stack.sh compose stop gateway api worker
tools/gateway-stack.sh compose exec -T postgres pg_dump -U postgres -d locintel -Fc \
  > /private/tmp/locintel-rehearsal-backup/database.dump
tools/gateway-stack.sh compose exec -T postgres pg_dumpall -U postgres --roles-only \
  > /private/tmp/locintel-rehearsal-backup/roles.sql
cp -R "$GATEWAY_RUNTIME/state" /private/tmp/locintel-rehearsal-backup/state

export COMPOSE_PROJECT_NAME=locintel-rehearsal-target
export GATEWAY_RUNTIME=/private/tmp/locintel-rehearsal-target
export GATEWAY_SUBNET=172.30.246.0/24
tools/gateway-stack.sh up 2 1
tools/gateway-stack.sh compose stop gateway api worker
# Only the newly created disposable target database is replaced.
tools/gateway-stack.sh compose exec -T postgres dropdb -U postgres locintel
tools/gateway-stack.sh compose exec -T postgres createdb -U postgres locintel
tools/gateway-stack.sh compose exec -T postgres pg_restore -U postgres -d locintel --exit-on-error \
  < /private/tmp/locintel-rehearsal-backup/database.dump
mv "$GATEWAY_RUNTIME/state" "$GATEWAY_RUNTIME/initial-state"
cp -R /private/tmp/locintel-rehearsal-backup/state "$GATEWAY_RUNTIME/state"
tools/gateway-stack.sh compose start api worker gateway
tools/gateway-stack.sh wait-api
rehearsal_address=$(tools/gateway-stack.sh compose port --index 1 gateway 8080)
export RESTORE_BASE="http://$rehearsal_address"
export FIXTURE=/private/tmp/locintel-rehearsal-source/fixture.local.json
export PROOF=/private/tmp/locintel-rehearsal-proof.json
node tools/restore-proof.mjs verify
# Verify once immediately after restore: issuing the download appends custody.

FLEET_BENCH_FIXTURE="$GATEWAY_RUNTIME/fixture.local.json" \
  node tools/fleet-bench.mjs "$RESTORE_BASE" 15 32 2
FIXTURE="$GATEWAY_RUNTIME/fixture.local.json" RATE=24 CAPACITY_SECONDS=300 VUS=32 \
  ROUTES=sites,search,detail,feed,public,near SUMMARY=/private/tmp/restore-load-summary.json \
  k6 run tools/capacity.k6.js
# In another shell with the source FIXTURE/PROOF and target RESTORE_BASE:
node tools/restore-proof.mjs work
```

Run `node tools/capacity.test.mjs` with `K6` set to the binary path to verify the
harness accepts good responses and rejects 503s, invalid bodies, latency breaches,
dropped arrivals and imbalance. The workload rate leaves headroom under existing
anonymous and authenticated gateway quotas; it is not a saturation experiment.

Stop/remove each disposable stack with its corresponding environment variables and
`tools/gateway-stack.sh compose down --volumes --remove-orphans`. Retain the private backup only as local diagnostic
evidence; `/private/tmp` is neither durable storage nor an off-server backup.

## Hosted acceptance still required

Repeat with approved staging access, synthetic data, independently provisioned
compute/load generation, managed PostgreSQL role constraints, actual object-store
backup/versioning, encrypted keys, provider adapters, and longer representative
read/write traffic. Define recovery-point/recovery-time objectives and retention
before calling the backup strategy production-ready.

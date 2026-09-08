---
title: "Business capacity is reserved with durable acceptance"
status: accepted
pinned: false
date: 2026-09-07
---

# 0055. Business capacity is reserved with durable acceptance

## Decision

`sites.max` is a strict admission limit across replicas. Occupied capacity is
persisted sites (including closed sites, matching the existing usage definition)
plus outstanding accepted creates. A rejected import reserves nothing and queues
no rows. An accepted import reserves all its creates in the same Ingest transaction
that marks the batch committed and persists its outbox messages.

The maintainer selected whole-batch capacity reservation over partially accepted
imports. This guarantees capacity admission, not simultaneous visibility of every
row: the existing asynchronous per-row processing remains.

## Implementation

Platform owns `platform.capacity_reservations`, an RLS-protected infrastructure
ledger. The platform helper uses the caller's existing database transaction, as
the shared audit sink already does. Ingest and Tenancy retain their own domain
tables and DbContexts; no cross-module EF transaction or distributed coordinator
is introduced. Each staged create's stable row ID identifies its reservation.

Direct creation and import acceptance use the same organization/entitlement lock.
Admission uses fresh site counts, not the removed per-process count cache.
The consumer deletes the reservation in the same transaction as creating the
site. Failure rolls both changes back. Replayed creates do not create a second
site or consume another reservation. If another accepted import already created
the external ID, this intent releases its now-unneeded reservation.

Locks use PostgreSQL's non-blocking transaction advisory operation. Waiting while
holding database connections could starve the second connection needed for an
owning module's usage query. HTTP capacity contention returns 503; contention
on the same batch's commit/discard returns 409. The caller can deliberately retry.
Only `CapacityBusyException` receives short jittered retries followed by
indefinite delayed durable retries; invalid
work and other failures retain the existing error policy. No success is returned
for an unreserved over-limit batch.

## Other business policies

| Invariant | Current contract |
|---|---|
| Site maximum | Strict at admission, existing sites plus pending reservations |
| Hierarchy depth | Structural limit on the proposed hierarchy/node, not a fleet counter |
| Contact-link monthly usage | Approximate meter with the existing Grace policy; usage recording is not an atomic billing ledger with Identity issuance |
| Boolean features / retention tiers | Evaluate current effective settings; no quantity reservation |
| Gateway request fairness | Operational admission, separate from business entitlements (ADR 54) |

An administrative plan reduction does not revoke accepted work or remove existing
sites. Outstanding reservations remain valid; further creates are refused until
capacity is available. Concurrent administrative changes are not serialized with
all admission reads: a create may be accepted under the limit it observed while
the administrator changes that limit.

## Recovery and deployment

Apply the additive Platform migration before new binaries. Drain old import
messages before cutover when possible: old messages have no reservation and are
checked at consumption; over-limit legacy intent requires explicit reconciliation.
Do not run old accepting binaries alongside the strict-admission version and claim
the new guarantee. Downgrade only after draining/reconciling pending reservations.

Reservations do not expire. A stalled or dead-lettered create still owns capacity;
releasing it by elapsed time could let replay exceed the limit. Diagnose pending
work using organization, batch, reservation ID and creation time. Replay valid
work; cancel/release only after making its corresponding durable intent incapable
of running. Reconcile pending work before permanent organization purge. Automated
operator cancellation/reconciliation is not implemented by this change.

The HTTP `Committed` status means durable acceptance, not all rows applied. Tests
and clients requiring completion must observe all requested rows or the relevant
worker evidence; observing the first row is insufficient.

## Verification

Real-PostgreSQL regressions cover competing direct/import admissions, complete
batch rejection, pending capacity, redelivery, transaction rollback and RLS.
Full-suite and deployment verification status is recorded in the
[maturity review](../software-maturity-review-details.md). This is not a new
throughput or live-provider acceptance claim.

Related: [module decisions](README.md), [production deployment](../production.md).

# Cross-tenant sharing: the recipe

Companion to ADR 48. That ADR settles the rule - **every row has exactly one
owning org; another org that needs to see or act on it gets its own row,
materialized through the outbox**. This document is the how: the two ways a
thing reaches other orgs, the moment a recipient gets its own row, and the
decisions a feature must make explicitly rather than by accident.

The running example is a request from one org that other orgs respond to
(an RFQ, a job posting, a data-sharing invitation). Substitute your nouns.

## Two modes, opposite cost curves

How a thing reaches other orgs depends on whether the owner CHOSE them. This
is the push-versus-pull question from feed design, and both answers stay
inside ADR 48.

### Targeted: push

The owner named the recipients. The owner's aggregate holds them as data -
`invitedOrgIds` on the `Request` - and on publish a handler materializes one
owned row per recipient:

```csharp
// in the handler for RequestPublished, running as the OWNER
await bus.FanOutAsync(
    request.InvitedOrgIds,
    new InvitationOffered(request.Id, request.Title, request.ClosesAt),
    correlationId: request.Id);
```

`FanOutAsync` publishes one envelope-tenanted copy per org, so each
`InvitationOffered` is handled under THAT org's RLS session, and the handler
writes that org's own `Invitation` row:

```csharp
// the handler for InvitationOffered, running as the RECIPIENT
public static async Task Handle(InvitationOffered m, RequestsDbContext db, ITenantContext tenant, CancellationToken ct)
{
    // first thing: two copies of this event for one org are handled in
    // parallel by the local queue; serialize them or the second dies on the
    // unique index and lands on a late retry (AggregateLock)
    await db.TakeAsync(tenant.OrgId!.Value, m.RequestId, ct);
    // keyed on (correlation, own org): a redelivered fan-out lands once
    var existing = await db.Invitations.FirstOrDefaultAsync(i => i.RequestId == m.RequestId);
    if (existing is not null) return;
    db.Invitations.Add(new Invitation { OrgId = tenant.OrgId!, RequestId = m.RequestId, ... });
    await db.SaveChangesAsync();
}
```

Withdrawal is the same shape in reverse: `RequestWithdrawn` fans out, and
each recipient's handler deletes or statuses its own row. Fan-out cost is
proportional to the list the owner chose, which is fine because the owner
chose it.

### Open: pull

The owner did not choose. Anyone may respond. **Do not push into every
tenant** - materializing a row into ten thousand orgs per request is
expensive, and it is also an action on every one of those tenants' data
just to announce something.

Instead the owner publishes a **public projection** into a platform-global
read model, and other orgs discover it by reading:

1. A table with NO RLS, declared as `PlatformGlobal` on the module's
   `ModuleCatalog` entry with a reason ("public projection of open requests; discovery only, holds
   nothing the owner keeps private").
2. Fed by the owner's events - `RequestOpened` inserts, `RequestClosed`
   deletes - through a handler exactly like `OrgDirectorySync`.
3. Carrying ONLY what anyone may know. This is stricter than the event
   rule in ADR 48: the table is readable by every tenant, so anything
   sensitive stays on the owner's row and is never projected.

Late joiners see open requests because nothing had to be pushed to them.
Closing a request removes one projection row, not N tenant rows.

`org_directory` is this pattern already in the template, and the same
mechanism is the directory an owner uses to CHOOSE recipients in the
first place - a platform-global projection of orgs that have published a
profile. One pattern serves discovery in both directions.

## The join: engagement materializes

In open mode a recipient has no row until it ACTS. The moment it responds -
submits a quote, claims the job - it creates its own owned `Quote` row, and
that publishes an event the owner's handler turns into a materialized
`QuoteSummary` on the owner's side.

So the recipient-side object exists in both modes. What differs is only how
the recipient found out: an invitation row was pushed to it, or it pulled
the projection and chose to engage. Design the recipient's object once.

## Criteria: choose snapshot or live, out loud

"All vendors in region X with capability Y" is where designs go wrong by
not choosing. Two honest options:

- **Snapshot.** Resolve the criteria to a list at publish time and push.
  Orgs that qualify later do not see it. Right for anything with a
  deadline - an RFP closes; the invitee set should not drift.
- **Live.** Store the criteria on the public projection and let readers
  filter. Newcomers see it. Right for standing listings - "we buy X".

Put the mode on the aggregate as an explicit field rather than inferring
it. A request can carry BOTH - a chosen list to push and open criteria to
project - and a recipient may arrive by either path; the engagement join
above makes that a non-event.

## Authority never enters a policy

Every decision about a shared thing is a command on the object the decider
OWNS, with a concurrency check, publishing an event:

- "Requester picks one of five quotes" - `Award(quoteId)` on the
  requester's `Request`; `RequestAwarded` fans out; each losing recipient's
  handler statuses its own `Invitation`.
- "First to accept wins" - `Accept()` on the recipient's own `Invitation`
  publishes `InvitationAccepted`; the OWNER's handler arbitrates with a
  version check on `Request` and publishes the outcome. Two orgs accepting
  simultaneously is two owned rows and one arbitration - not a race in the
  database.
- "Owner evicts a member" - a command on the owner's aggregate; the
  member's handler deletes its own participation row.

If you find yourself wanting a `WITH CHECK` clause to express who may do
what, the authority is in the wrong place.

## Projection handlers serialize per aggregate

The upsert above is only once-per-key if the two copies cannot interleave.
They can: Wolverine's local queue handles messages in parallel, so two
copies of a fan-out - or two quick events for the same aggregate - each
miss the other's uncommitted row, and the second dies on the unique index
and lands on a retry, late. Take `AggregateLock.TakeAsync` first thing in
every projection handler: a transaction-scoped advisory lock on the
aggregate id (owner side) or on `(own org, aggregate id)` (a recipient's
copy, so fifty recipients of one request do not serialize with each
other). It lives exactly as long as Wolverine's transaction and refuses to
run outside one. `org_directory`'s handler takes it; `AggregateLockTests`
proves two transactions on one key serialize and two keys do not.

## A projection remembers the version it applied

The lock stops two copies interleaving. It does not stop an OLDER event -
delivered late, or redelivered after a newer one landed - from overwriting
the row. So every replicated event carries the owner row's version at
publish time (`OrganizationUpserted.SourceVersion` is the row's `xmin`),
the read-model row remembers the last version it applied, and the handler
applies an event only when `ProjectionVersion.IsNewer(incoming, applied)`
- under the lock, so the compare and the write are one step. Equal is
stale too: that is the redelivery. The compare is modular, the way
PostgreSQL compares transaction ids, so a wrapped `xmin` still orders.
Incoming zero is always invalid, including for a missing row. A stored zero is
the migration sentinel for an unsynchronized projection and accepts any nonzero
first version. After synchronization, modular ordering assumes versions are less
than `2^31` transactions apart. Unit and real-handler tests cover these boundaries,
wraparound, stale/duplicate events, and concurrent delivery.
Three rules, then, for every projection handler: take the lock, upsert on
the key, apply only a newer version. `org_directory` does all three.

## Order is not guaranteed

Two events published seconds apart - a vendor's `Accepted`, then its
`Started` - can reach the owner's handler in either order on a busy outbox,
and a redelivery can bring an old one back after a newer one landed. The
owner has the authority, so the owner must decide what an out-of-order
action means. The default, unless a feature has a reason to differ:

**Read each action as "the other party reached this point", and apply it
monotonically.** Rank the steps. An action applies when the owned row is at
or before the step it implies - and it implies every earlier step, so fill
in the timestamps that were skipped. An action for a step the row has
already passed is stale: record nothing, change nothing. Either way the
owner answers with its resulting state (`RequestStateChanged` to every
participant), which is what corrects the sender's optimistic row when its
action no longer applied.

```csharp
// running as the OWNER; the vendor's events may arrive in any order
static bool Apply(Request row, VendorResponded m) => m.Step switch
{
    Step.Accepted   when Rank(row.Status) < Rank(Step.Accepted)   => Advance(row, Step.Accepted, m.At),
    Step.Started    when Rank(row.Status) < Rank(Step.Started)    => Advance(row, Step.Started, m.At),
    Step.Completed  when Rank(row.Status) < Rank(Step.Completed)  => Advance(row, Step.Completed, m.At),
    _ => false, // stale, or a step this row's mode never takes
};
```

Terminal and branching steps (declined, cancelled) are not on the ladder:
gate them on the exact state they leave from. `FanOutOrderingTests` in the
unit project carries this as a worked example.

That example is not a shipped Request workflow and does not prove a fork's
handler behavior. The template's production-path evidence is
`OrgDirectoryVersionTests` (real transactional ordering, redelivery, and
concurrency), integration `FanOutTests` (tenant-routed outbox delivery), and
`StorageTests.Idempotency_key_replays_and_conflicts` (HTTP idempotency). A new
domain workflow still needs its own real-handler ordering and duplicate-delivery
checks; a stable fan-out deduplication key alone does not supply that guarantee
on the PostgreSQL transport.

## Entitlements

Recipients per request and whether open broadcast is allowed at all are
plan limits - gate 1 of the three gates:

```csharp
// EntitlementCatalog, in the fork's module
"requests.max_recipients"   // long, default 5
"requests.open_broadcast"   // bool, default false
```

Check them in the publish command before the fan-out, and the upsell
writes itself.

## Checklist for a new cross-org feature

- [ ] Name the true owner of each object. One org.
- [ ] For every other org that can see or act on it, name the object THAT
      org holds, and give it its own `IOrgScoped` table.
- [ ] Targeted or open? If targeted, `FanOutAsync` with the owner id as
      correlation, and an upsert keyed on it in the recipient handler. If
      open, a platform-global projection carrying only public fields,
      declared `PlatformGlobal` on the module's catalog entry with a reason.
- [ ] Criteria? Snapshot or live - as an explicit field.
- [ ] Every decision is a command on an owned object with a concurrency
      check. No policy clause encodes authority.
- [ ] Limits are entitlements, checked before the fan-out.
- [ ] Every projection handler takes `AggregateLock` first thing, and applies
      an event only when its source version is newer than the one applied.
- [ ] The owner applies the other party's actions monotonically: an action
      means "they reached this step"; an earlier step arriving late is stale.
- [ ] `EnableTenantRls` on every new table; the migration skill's checklist
      applies unchanged.

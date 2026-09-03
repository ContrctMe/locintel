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
public static async Task Handle(InvitationOffered m, RequestsDbContext db, ITenantContext tenant)
{
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

1. A table with NO RLS, listed in `RlsCoverageTests.PlatformGlobal` with a
   reason ("public projection of open requests; discovery only, holds
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
      open, a platform-global projection carrying only public fields, in
      the `PlatformGlobal` allowlist with a reason.
- [ ] Criteria? Snapshot or live - as an explicit field.
- [ ] Every decision is a command on an owned object with a concurrency
      check. No policy clause encodes authority.
- [ ] Limits are entitlements, checked before the fan-out.
- [ ] `EnableTenantRls` on every new table; the migration skill's checklist
      applies unchanged.

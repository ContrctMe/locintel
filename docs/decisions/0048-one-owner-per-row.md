---
title: "One owner per row; cross-tenant sharing by materialization"
status: accepted
pinned: true
date: 2026-09-02
supersedes-in-part: 0047 (the two-party, catalog and recipient-list shapes added in rounds 2-4 of fork feedback)
---

# 0048. One owner per row; cross-tenant sharing by materialization

## Decision

**Every tenant-scoped row has exactly one owning org.** `IOrgScoped` and
`EnableTenantRls` are the only tenancy primitives. When org B needs to see or
act on something org A owns, org B gets **its own row** - an object B owns,
in B's tenant, materialized from A's object through the outbox - never a
second owner column, a recipients side table, or a `published` flag on A's
row.

This removes `ITwoPartyScoped`, `IRequiredCounterpartyScoped`,
`IPublishedCatalogScoped`, `EnableTwoPartyRls`, `EnablePublishedCatalogRls`,
`EnableRecipientListRls`, and the matching query-filter conventions. They
shipped across three rounds of fork feedback; this ADR records why that was
the wrong response to a real problem, so a fork can see the reasoning and
not just the deletion.

## The problem those shapes were solving

"Org A created a thing, and org B needs to see it - and sometimes act on it."
A broadcast request and the vendors invited to bid. A share and its members.
A vendor profile any org may read once published. Real needs; a fork hit all
three.

Each shape answered by **giving one row more than one owner**: a counterparty
column, an `EXISTS` over a recipients table, a `published` predicate. Each is
a controlled violation of the one invariant the whole tenancy story rests on -
this template's foundational sentence is *"ownership: which org owns a row"*,
singular - and every RLS policy, every offboarding purge, every region
resolution assumes it.

## Why multi-owner rows are the wrong model

The evidence is the growth of the helper that tried to make them work. Every
parameter added to `EnableRecipientListRls` was a workaround for a consequence
of a row having more than one owner:

| Parameter | The multi-owner consequence it patched |
| --- | --- |
| `recipientPredicate` | A revoked member keeps a row in the SHARED access table (for the audit trail), so every policy must remember to exclude it. With an owned row you delete the member's object; the audit trail is the event history. |
| `writableByRecipient`, `parentTable`, `parentOwnerColumn` | RLS was being used to encode WORKFLOW authority - "may a vendor award?", "may the owner evict?" - on a row that is nobody's. With owned objects, authority is which object you own and which commands exist. |
| `parentKeyColumn` | Children of a shared thing inherit the shared-ness, so every child needs the same cross-tenant plumbing. |
| `IRequiredCounterpartyScoped` | The fork's `Quote` was already the vendor's own object. It got a two-party shape bolted on anyway, with a null-forgiving `VendorOrgId` accessor - the model was halfway to the right answer and got dragged back. |

Thirteen parameters, zero callers in the template, a fourth round that still
did not fit. A model that fights harder each round is not under-parameterized;
it is wrong.

Beyond the API, multi-owner rows break things the template relies on:

- **Region (ADR 35).** A row owned by two orgs in two regions is incoherent.
  Owned copies live where their owner lives.
- **Offboarding (ADR 25).** Purging one org's rows must touch nothing else.
  Deleting the requester today corrupts every vendor's view of the request.
- **Legibility where it matters most.** The security-critical property of the
  recipient shape - being on the list grants read, never write - lived in a
  boolean argument. The raw policy made it visible; the helper hid it.
- **Cost.** `EXISTS (SELECT … FROM access …)` runs per row on every read of
  the parent. An owned row is an indexed `org_id` like everything else.
- **Divergence.** The C# query filter and the SQL policy stated the same rule
  in two languages with nothing checking they agreed.

## The right model, which the template already uses

The template solves exactly this problem at the module boundary: Identity
does not read Tenancy's org table - it materializes its own `org_directory`
read model from `OrganizationUpserted` events (ADR 37). Cross-boundary
visibility by **materialization through the outbox**, not by reaching across
at read time. This ADR generalizes that from module boundaries to tenant
boundaries.

The cleanest framing is double-entry bookkeeping. A transfer is not one row
with two owners; it is two entries, each owned by one account, linked by a
transaction id, kept consistent by the transaction. An RFQ is the requester's
`Request` plus each vendor's own `Invitation` and `Quote`, linked by the
request id, kept consistent by events.

### The recipe

The full recipe - targeted push versus open pull, the engagement join,
snapshot-versus-live criteria, and where authority lives - is
`docs/cross-tenant-sharing.md`. `FanOutAsync` in Platform is the push
primitive. In brief:

1. **The owner's aggregate** is an ordinary `IOrgScoped` row in the owner's
   tenant. It knows who it was shared with as DATA (a list of org ids, a
   status per recipient) - never as a policy.
2. **A domain event** publishes the sharing: `RequestBroadcast(requestId,
   recipientOrgIds, summary…)`. The event carries what recipients are allowed
   to know; anything the owner keeps private stays out of it.
3. **A handler per recipient** materializes an `IOrgScoped` row in EACH
   recipient's tenant - envelope-tenanted to that org (`PublishForOrgAsync`),
   so it is written under that org's RLS session. `PerOrgSweepService` and
   `TenantedMessaging` already exist for this.
4. **Recipients act on their OWN row.** A vendor submits a `Quote` it owns.
   That publishes `QuoteSubmitted`, and the requester's handler materializes a
   `QuoteSummary` on its side. Nobody ever writes a row they do not own.
5. **Revocation is deletion** (or a status on the recipient's own row). No
   predicate in a policy. The event history is the audit trail.

### How each removed shape maps

| Removed shape | Model it instead as |
| --- | --- |
| Two-party (owner + counterparty) | Two owned objects linked by id: the owner's aggregate and the counterparty's own projection of it. Each side writes only its own. |
| Required counterparty | Same - the counterparty's object is simply required to exist; that is a command precondition, not a column constraint. |
| Published catalog (`published OR owner`) | The owner's row plus a **platform-global read model** published from it on `Published`/`Unpublished` events - exactly `org_directory`. Platform-global tables carry no RLS by design and are listed in `RlsCoverageTests.PlatformGlobal` with a reason. |
| Recipient list (owner + side table) | The owner's aggregate holding the list as data, plus one owned `Participation`/`Invitation` row per recipient, materialized by event. |
| Children of a shared thing (members, bulletins) | Children of the OWNER'S aggregate, owned by the owner; each recipient gets its own projection of what it may see. |

## Costs, stated plainly

- **Consistency between parties is eventual.** A recipient sees the
  materialized row after the outbox delivers it - milliseconds to seconds.
  The template already accepts this everywhere the outbox is used; a flow
  that needs the other party to see a change in the same HTTP response is
  the exception to design for explicitly, not a reason to share rows.
- **Data is duplicated** and kept coherent by events. This is the standard
  read-model tax the template already pays for `org_directory`.
- **More parts per feature**: aggregate, event, handler, projection - against
  one table with a clever policy. The clever policy is "less code" the way a
  shared mutable global is less code.

## Migrating a fork that adopted the removed shapes

A sync after this ADR surfaces compile errors at exactly the types named
above. Treat each as a modeling task, not a find-and-replace:

1. For each table that used a removed shape, name its true owner. Usually
   the org that created it.
2. For every OTHER org that could see or write it, decide what object that
   org actually holds - an invitation, a participation, a quote, a projection
   of a profile - and give it its own `IOrgScoped` table in the module.
3. Write the event the owner publishes and the handler that materializes the
   recipient's row (`PublishForOrgAsync` to the recipient org; the handler
   runs under that org's RLS session).
4. Move workflow authority out of policies into commands: "award" is a
   command on the requester's `Request` that publishes an event, not a
   `WITH CHECK` clause.
5. Replace the migration's shape helper with `EnableTenantRls` on every new
   table, and delete the old cross-tenant policy in a new migration - never
   by editing the applied one.
6. Data migration: for each existing multi-owner row, insert the recipient
   side's row(s) from the current owner/list columns, then drop those
   columns. Do it in a migration that runs as the migrate role.

The previous shapes' adversarial tests encoded real knowledge - that being
listed must grant read and never write, that revocation must revoke writes
too, that a policy must never reference its own table. Under this model those
are not properties to test; they are consequences of every row having one
owner.

## Why pinned

"One owner per row" is a data-model invariant. Once a fork has tables with
two owner columns and data in them, reversing it is a migration across every
such table. The cost of holding the line is a few more events; the cost of
crossing it compounds.

# 53. The tenant variable is transaction state, and the message store has its own connection

Date: 2026-09-07
Status: accepted
Pinned: false

## Context

Row security reads the tenant from `app.org_id` (ADR 38). Since ADR 38 the
variable was set once per connection, when Npgsql opened it, as session
state. That is correct while a database connection belongs to one client
for its whole life, which is true of a direct connection and of a pooler in
session mode.

It is wrong behind a pooler in transaction mode, which is the mode that
multiplexes: the pooler hands a server connection to a client for one
transaction and to another client for the next, and session state set by
the first client is read by the second. For the tenant variable that is
another tenant's rows. `tools/replica-stack.sh 2 --pgbouncer transaction`
demonstrated it on 2026-09-07: three of the five fleet cases failed with
idempotency keys and quotas attributed to the wrong org.

Transaction pooling is the standard posture for Postgres behind many
application instances, and the reason to want it is measured in
`docs/scaling.md`: every api replica otherwise holds its whole pool against
the server, so twenty replicas at forty connections is eight hundred server
connections for a few dozen in-flight transactions.

## Decision

The tenant variable is set per command, as transaction state. The
`TenantSessionInterceptor` prepends `SET LOCAL app.org_id = '<id>'` to every
command a module context runs, in the same batch, ahead of it. Inside the
explicit transaction Wolverine's frame opened, the setting lives for that
transaction; outside one, the batch itself forms an implicit transaction
block and the setting lives exactly as long as the command. It costs no
round trip. A context with no tenant sets nothing, and the policies'
`NULLIF(current_setting('app.org_id', true), '')` turns unset into
match-nothing. `SET` rather than `set_config` because a utility statement
returns no result set, so scalar commands still answer with their own row.

Wolverine's message store keeps its own direct connection.
`ConnectionStrings:locintel-messaging`, when configured, is what
`PersistMessagesWithPostgresql` uses; the app role rewrite (ADR 38) applies
to it as well. Its node agents and leader election take session-level
advisory locks, which are exactly the state a transaction pool cannot
carry, and it needs only a small pool.

No other session state is set anywhere. An architecture test
(`SessionStateTests`) refuses a session-scoped `set_config` or a `SET` that
is not `LOCAL` outside the migration paths.

## Consequences

- PgBouncer in transaction mode is a supported topology; the fleet suite
  runs through it (`tools/replica-stack.sh N --pgbouncer`) and the Aspire
  host offers the same container (`LOCINTEL_PGBOUNCER=1`). Session mode
  still works and is no longer the recommendation.
- Six session sets per request become one `SET LOCAL` per command, inside
  the batch; the statement count per request changes little, the round
  trips fall.
- Deployments behind a transaction pooler set `locintel-messaging` to a
  direct connection and give the pooler `max_prepared_statements`; the
  production doc says so.
- The previous open-time set is gone. A fork that relied on the variable
  being present on a raw connection outside a module context (there were
  none) now sees no tenant, fail-closed.

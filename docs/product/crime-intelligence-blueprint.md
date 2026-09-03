# Crime intelligence platform: product blueprint

Status: draft, 2026-09-01. This is the working plan for building the crime
intelligence SaaS on top of the upstream multi-tenant location template (renamed at fork init). Consult it alongside the
ADRs in `docs/decisions/`; where this document and an ADR disagree, the ADR
wins until it is reopened. Decisions taken while building are recorded in
the "Decisions log" at the bottom.

## Customers and shape

Site-based tenants: retail chains (loss prevention), property managers,
campuses, business improvement districts, security firms. The hierarchy
(region > district > site) is the scope axis for everything. Crime
intelligence is site-based fact data over a hierarchy, which is exactly
what the template models.

## How the template maps

| Template piece | Role in this product |
| --- | --- |
| Three gates (ADR 06-09) | Entitlement: plan includes intel sharing / marketplace / video evidence / AI. Grant: file, investigate, approve, purchase. Scope: subtree filtering of incidents and cases. |
| Stamped facts (ADR 02/04/26) | Every incident stamps ancestor path + site business date; all analytics read those stamps. |
| Guest principals (ADR 07) | Anonymous tip submission on the public app. |
| Storage tickets/quarantine/lifecycle (ADR 19) | Evidence handling base; legal hold must block lifecycle purge. |
| Locator geo + map (ADR 43) | Site coordinates, Leaflet map, vendor coverage matching, guard geofences. |
| Checklists (ADR 45) | Guard patrol rounds, security audits, delivery confirmation. |
| Org-defined site attributes (ADR 46) | Site risk profile: CCTV coverage, hours, alarm vendor, crime tier. |
| API keys + webhooks (ADR 40) | Vendor dispatch integration; video systems pushing events. |
| RRULE + materialized occurrences (ADR 27/28) | Patrol schedules and standing guard coverage. |
| Notifications (ADR 32) | Email only, no user preferences; alerts need SMS/push + preferences (fork territory). |

## Gaps the template does not fill (design before data exists)

- **Need-to-know gate.** Person-of-interest records are gated per case, not
  per role. A fourth gate after scope. Design it before any entity data.
- **Two-party ownership.** Marketplace rows are owned by a requesting org
  AND a fulfilling vendor org. New RLS policy shape (either-party read,
  role-restricted write) with its own golden isolation tests.
- **Cross-tenant sharing.** Intelligence network publishes COPIES of records
  into a platform-level share with explicit membership. Never grant one org
  read access into another's schema.
- **Read audit.** ADR 12's four audit kinds gain a fifth: every read of a
  person record.
- **Event-stream ingest.** ADR 18 ingest is for site rosters; generalize to
  deduplicated event streams.
- **Field offline.** ADR 45 chose no offline service worker. Field incident
  capture may need an outbox-style queue; reopen deliberately.
- **Payouts.** Stripe Connect is a different integration from the billing
  seam (ADR 39). Invoice-only mode is required regardless.

## Modules, in build order

Each is a vertical slice via the `new-module` skill: own schema, DbContext,
migration with RLS, Wolverine registration, arch-test registration, fixtures.

1. **Incidents.** Type taxonomy (theft, ORC, assault, vandalism, trespass,
   fraud, safety), severity, occurred-at (UTC instant) + stamped business
   date, location within site, loss amount, narrative, involved entities,
   attachments, status workflow. Intake: console, PWA, anonymous tip,
   connectors. Deletion: soft-delete with restore, overridden by legal hold.
2. **Entities and links.** Persons of interest, vehicles, groups; aliases,
   descriptors, plates. Entity-incident links form the cross-site graph.
   Records carry suspected/confirmed status, an evidence requirement, a
   mandatory retention expiry, and need-to-know access.
3. **Cases.** Group incidents + entities; investigators, tasks, notes,
   timeline, disposition, prosecution package. Chain-of-custody log on every
   evidence touch (upload, view, download, export) with content hashes.
   Legal hold.
4. **Evidence.** Video, photos, receipts, statements, police reports over
   Storage. Short-lived signed URLs, viewer watermark, redaction (faces,
   bystanders), retention per jurisdiction and evidence class.
5. **Alerts and BOLOs.** Bulletins by radius or subtree, high-severity
   alerts, repeat-offender proximity. Requires SMS/push transports and
   per-user preferences.
6. **Ingest connectors.** Police open data (Socrata, ArcGIS), CAD/RMS
   exports, alarm panels, POS exception reports, VMS (Genetec, Milestone,
   Verkada, Axis), access control, LPR. Each with its own privacy posture.
7. **Map and analytics.** Layers: incidents, public crime data, patrols,
   vendor coverage. Site risk score, loss by category, time-of-day heat,
   repeat offenders, district comparison. Cross-tenant benchmarks only as
   k-anonymous aggregates.
8. **Patrols and guard ops.** Tours on checklists, RRULE schedules in site
   zone, geo-verified check-in/out, daily activity reports feeding
   incidents. Also the fulfillment side of guard requests.
9. **Intelligence network.** Cross-tenant sharing among consenting orgs
   (the ORC use case). Platform-level share schema; source stays owned,
   shared copy has its own ownership and retention.
10. **AI assistance.** Classification, entity resolution/dedupe, MO
    similarity linking, summarization, PII redaction, per-site risk
    prediction. Human in the loop on anything touching a person record.

## Marketplace

**Actors.** Buyers are tenants. Sellers are vendor-kind organizations: guard
companies, investigators, alarm response, CCTV/access-control installers,
restoration/board-up, legal and prosecution support, drone/mobile patrol,
key holding, equipment suppliers. Platform operator takes a fee. Vendors as
organizations means ADR 05 cross-org users, API keys, and webhooks all work
unchanged.

**Catalog.** Service categories each carry a structured request schema.
Guard request: site, start/end or RRULE, headcount, armed/unarmed, licensing
requirements, post orders. Asset request: item, quantity, deliver-to site,
install required. Vendor profile: coverage (regions or service radius
matched to site coordinates), categories, licenses and insurance with
expiry, ratings.

**Lifecycle.** Draft > (direct assignment to preferred vendor / standing
contract, OR request-for-quote broadcast to matched vendors) > quotes >
acceptance > work order > fulfillment tracking > completion > dispute >
invoice > settlement > rating. Urgency tiers: emergency (escalating radius
broadcast, SMS, response SLA), scheduled (approval flow), standing
(materialized occurrences per ADR 28).

**Fulfillment.** Guards check in/out against site geofences; activity
reports land as incidents. Asset deliveries confirm with photo proof
against a checklist. Requests link to their originating incident/case; the
link lives on the marketplace side so Incidents never consumes the
marketplace contract (ADR 37 ladder).

**Money.** Stripe Connect for payouts, fee, holds. Invoice-only mode for
enterprise tenants who pay vendors off-platform.

**Tenancy.** Two-party rows (see gaps). Staged disclosure: area during
quoting, exact address after acceptance. Raise/approve is a grant; spend
limit is a limit-shape entitlement (ADR 08/09).

**Trust and safety.** Vendor verification, license checks by jurisdiction,
insurance expiry blocks new assignments, guard background-check
attestations, SLA tracking, per-tenant preferred/blocked lists, disputes
with evidence.

**v1 shape.** Direct-to-preferred-vendor, no payments, no bidding. Proves
the two-party tenancy model before money raises the stakes.

## Compliance threads (run through every module)

GDPR/CCPA rights requests vs legal holds; BIPA for any biometrics; stay out
of CJIS scope (never store law-enforcement criminal justice information);
state private-investigator and guard licensing; defamation exposure from
person records; consent for cross-tenant sharing; retention per
jurisdiction.

## Decisions log

- 2026-09-01: blueprint drafted. Open questions: product name for
  `tools/init.py`, v1 customer segment, whether entities ship in v1,
  primary jurisdiction.
- 2026-09-01: product name **LocIntel** (init run immediately). v1 segment is
  **retail loss prevention**. **Entities ship in v1** with the need-to-know
  gate and read audit built alongside. Primary jurisdiction **United
  States** (CCPA, BIPA, state guard/PI licensing, CJIS avoidance).
- 2026-09-01: **Incidents module shipped** (schema `incidents`; capabilities
  `incidents:read|report|manage`). Facts stamp `hierarchy_id` + ltree `path`
  + `business_date`; tier-2 soft delete with `legal_hold` refusing deletion;
  notes (tier 2) and attachment links to Storage files (tier 3). `SiteInfo`
  now carries `HierarchyId`. Lessons: Wolverine emits an untyped 200 stub
  for any endpoint without a declared 200 type, so "no content" mutations
  echo a typed `IncidentMutated` instead of 204; the migration round-trip
  test had never covered Checklists - both modules are in it now.
- 2026-09-01: **Entities module shipped** (schema `entities`; capabilities
  `entities:read|manage`). The fourth gate is `EntityVisibility`: an entity
  is readable via a linked incident whose STAMPED path is in the reader's
  scope (links carry the path), via a time-boxed `access_grants` row, or
  via `entities:manage`. Every detail read publishes `entity.viewed` (domain
  audit, always on). Confirmed status requires a linked incident. Retention
  is mandatory (`expires_at`, default 365d, max 3y); a worker enumerator
  fans out `ExpireEntities` per org and the handler trashes expired unheld
  rows; managers can trigger a sweep. `ActorRef` moved to Platform for
  modules whose writes accept people and API keys. Deferred: entity photos
  (biometric exposure) until Evidence; `IEntityDirectory` until Cases.
- 2026-09-01: **Cases module shipped** (schema `cases`; capabilities
  `cases:read|manage`). Visibility is membership or a linked incident in
  scope (links stamp the path) or manage; members work (tasks, notes,
  evidence), managers run the case. Evidence is Storage files by id with an
  append-only `custody_events` chain (Added / Downloaded / Exported /
  Removed / HoldPlaced / HoldReleased); downloads go THROUGH the case so
  they are attributable. Legal hold cascades to files via the new
  `FileHoldRequested` outbox message (Storage handles it); downloads sign
  via the new `ISignedFileAccess` read contract. Entities on a case resolve
  through `IEntityDirectory.LookupVisibleAsync`, so a reader without
  need-to-know sees a restricted placeholder. Prosecution package v1 is a
  JSON document (PDF later). Offboarding exporters added for Checklists,
  Incidents, Entities, and Cases - the lifecycle test enumerates every
  module's section and none of the fork's modules had one.
- 2026-09-01: **Marketplace v1 shipped** (schema `marketplace`; requester
  capabilities `marketplace:read|manage`, vendor capabilities
  `vendor:manage|fulfill`; entitlement `marketplace.enabled` is the first
  real gate-1 402 in the fork). Vendors are ORGANIZATIONS with a published
  `vendor_profiles` row (the first cross-org read policy: owner writes,
  everyone reads once published) and credentials with expiry (expired ones
  block accepting new work). Requests are two-party rows (`org_id` +
  `vendor_org_id`, RLS `two_party` policy mirrored by the context's own
  "Tenant" filter); site facts are snapshotted onto the request because the
  vendor's tenant context can never read the requester's Tenancy rows. Flow
  is direct-to-vendor, no payments, no bidding: Draft > Submitted >
  Accepted/Declined > InProgress > Completed > Verified/Disputed, Cancelled
  from the early states. Timeline events carry check-in/out positions with
  haversine distance to the site (250 m geofence). Notices to the other org
  go through `SendOrgNotice`. Preferred/blocked lists per requester org.
  Test hygiene: GatesTests restores `sites.max` (xUnit's in-class order is a
  hash of the test name and the fork rename reshuffled it).
- 2026-09-01: **Alerts and bulletins shipped** (schema `alerts`;
  capabilities `alerts:read|manage`). Incidents now publishes the
  `IncidentReported` integration event (tenant on the envelope); Alerts
  turns High/Critical ones into feed alerts and emails managers via
  `SendOrgNotice`. Bulletins (BOLO / Advisory / Safety) target a subtree
  (`scope_path`, null = org-wide) and reach readers whose scope OVERLAPS
  (`ScopeOverlap`: either side ancestor of the other); acknowledgements
  are per user with optional site and note; issuing also drops a feed
  alert. Reads are per user (`alert_reads`). Transports: email only; SMS
  and push remain fork territory as ADR 32 anticipated; no alert purge job
  yet (90-day convention). Dashboard gained incident and alert cards.
- 2026-09-01: **Anonymous tips shipped**: `POST /public/tips` on the guest
  tier (ADR 7) files an incident with Source = Tip, Medium severity,
  `reported_by` = empty, optional `reporter_contact` (new column,
  migration `TipIntake`), a `tip` tag, and a quotable receipt; honeypot
  field, length floor, guest rate limits (ADR 30). Public app: `/tips`
  page linked from the footer, posting server-side with the host
  forwarded. Leftover from init: the `PREMISE_API` env var (uppercase
  escaped the rename) is now `LOCINTEL_API` in AppHost, both web apps,
  and the production guide.
- 2026-09-01: **Analytics and map shipped** (console). Incidents now stamp
  `local_hour` and `local_weekday` at write time (migration
  `LocalTimeStamps`; older rows are null and simply absent from those two
  rollups) so time-of-day heat is a group-by, never a read-time zone
  conversion. Stats gained ByHour/ByWeekday. Console `/analytics`: window
  and site filters, stat tiles, Leaflet map of incident density per site
  (one hue, radius = count), bars by category/severity/hour/weekday, site
  table, daily table. Leaflet added to the console workspace.
- 2026-09-01: **Intelligence network shipped** (schema `network`;
  capabilities `network:read|manage`; entitlement `network.enabled`).
  Shares are consortiums owned by one org. `share_access` is each org's
  OWN single-owner row and every other policy (shares, roster, bulletins)
  is one hop through it - never through its own table, so nothing
  recurses; the invitee's access row is created under THEIR tenant by a
  `ShareInvitationRequested` message, and removal by `ShareAccessRevoked`.
  Bulletins are COPIES (kind/name/aliases/descriptors snapshot from a
  record the publisher may see) readable by active members, withdrawn by
  the publisher, importable by other members via the
  `ImportEntityRequested` outbox message that Entities handles (new
  Suspected record whose summary names the share and publisher). Lesson:
  a migration's Down must drop cross-table policies before the tables.
- 2026-09-02: **Patrols and guard ops shipped** (schema `patrols`;
  capabilities `patrols:read|perform|manage`). Routes are ordered
  checkpoints (jsonb, optional coordinates, 100 m geofence) stamped with
  the site's path; schedules are RRULEs with a site-local start time,
  expanded on read for the day through Platform's `RecurrenceExpander`
  (no materialized horizon: a single day is cheap); runs stamp path and
  business date at start and match an expected occurrence by route and
  scheduled start. Daily activity report joins runs, missed rounds, and
  the day's incidents via the new `IIncidentDirectory.ListForDayAsync`.
  Console: live patrol with checkpoint taps (GPS when available), routes
  and weekly schedules, report by date.
- 2026-09-02: **Browser smoke run** through Aspire with the new
  `LOCINTEL_AUTH=local` switch (AppHost picks the provider; DevBootstrap
  keys seeded users as `local_{email}` under it; `.claude/launch.json`
  has an `aspire` configuration). Every new console page rendered with no
  console errors: dashboard cards, incidents, entity, case, alerts,
  analytics map, patrols, network, marketplace, vendor portal, public
  tips. Two fixes came out of it: entitlement labels for the new plan
  keys, and the vendor profile form seeding its state before the query
  resolved.
- 2026-09-02: **Incident CSV import shipped** (Incidents module, feature
  folder `Imports`; migration `ImportBatches`). Stage parses a Clean
  Storage file with the CSV parser now shared from `Platform.Text`
  (moved out of Ingest), resolves sites by external id or name, validates
  every row and applies gate 3 per row; commit lands valid rows as
  incidents with Source = Import (stamps as usual; no IncidentReported
  events - history is not news); discard deletes the staged rows. Console:
  Import CSV dialog on the incidents page with preview of invalid rows.
- 2026-09-02: **AI assistance shipped** (blueprint module 10, v1).
  `ITextIntelligence` port in Platform with a deterministic local
  heuristic adapter (tests, dev, vendor-free forks) and a Claude adapter in
  `LocIntel.Integrations.Anthropic` (structured JSON output over closed
  category/severity sets, low effort by default, `Intelligence:Provider`
  switch). Endpoints: `POST /api/incidents/{id}/assist` suggests category,
  severity, tags, summary (read-only; the person applies it via the new
  edit dialog) and `POST /api/cases/{id}/assist/brief` drafts a brief
  whose entities pass the need-to-know directory (restricted records are
  named as such), filed as a note only by the person. Entitlement
  `ai.assist`. Deferred: MO similarity across incidents, PII redaction.
- 2026-09-02: **Marketplace v2: broadcast requests and quotes** (migration
  `Rfq`). A request without a chosen vendor is Mode = Broadcast: on
  submit it fans out `request_recipients` rows (published vendors offering
  the category, not blocked, preferred first, ten at most) - the recipient
  row is what lets a vendor READ a request that has no `vendor_org_id`
  yet (requests policy gained an EXISTS on recipients; WITH CHECK stays
  two-party, so recipients never write the request row). Vendors quote
  (one per vendor, resubmit replaces; expired credentials block quoting)
  or decline per recipient; the buyer awards a quote, which sets the
  vendor, budget, and Accepted, rejects the others, and hands off to the
  existing fulfillment flow. Vendors see only their own quote; buyers see
  all. Console: broadcast option, quotes card with Award, vendor quote form.
- 2026-09-02: **Repeat-offender alerts and printable package.** Entities
  publishes `EntityLinked` (with the entity's link count) on every new
  entity-incident link; Alerts raises a `RepeatOffender` feed alert (one
  per entity+incident, Medium at 2 links, High at 3+) on the incident's
  path and notifies managers. The prosecution package is now a print-ready
  console page (`/cases/{id}/package`, browser print-to-PDF); fetching it
  is the export and logs custody on every file, as before.
- 2026-09-02: **SMS and browser notifications shipped.** `ISmsTransport`
  port in Platform (local catcher, `NoSmsTransport` when off, Twilio REST
  adapter in `LocIntel.Integrations.Twilio`; `Notifications:Sms` = off |
  local | twilio, off allowed in production). `SendOrgNotice` gained
  `Kind` (alerts | marketplace | network) and a short `Sms` text; Identity
  sends email to managers as before and texts members who opted in to
  that kind, hold its capability, and have an E.164 phone in the new
  `notification_preferences` table (`/api/me/notifications`). Every
  Alerts, Marketplace, and Network notice now carries a kind. Browser
  notifications: the console polls the alert summary for members who
  opted in and raises a Notification when unread grows (no push service;
  a real push channel remains fork territory). Phone verification is
  deferred.
- 2026-09-02: **Marketplace routing and SLA shipped** (migration `Routing`).
  `VendorMatcher` reaches only vendors that serve the site: service areas
  must include the site's country (`SiteInfo` now carries `CountryCode`),
  a vendor with a base and radius must have the site inside it, vendors
  with neither serve anywhere; preferred first, then nearest. Broadcasts
  start with `InitialRecipients` and every submitted request gets a
  `response_due_at` per urgency. The SLA sweep (worker every five minutes,
  or `POST /api/marketplace/requests/sla/sweep`) widens an unanswered
  broadcast to the next vendors (SMS for emergencies), reminds a direct
  vendor, tells the buyer, extends the window, and gives up after
  `MaxEscalations`. The fixture collapses windows to zero and starts
  broadcasts at one recipient so escalation is testable.
- 2026-09-02: **Template sync, upstream is the base** (merge of
  `template-renamed`, upstream `2088a25`). The fork tracks the template by
  renaming each upstream snapshot with `tools/init.py` onto a parallel
  branch parented on the fork's own init commit, then merging it
  (`tools/sync-upstream.sh`). Rule for every sync: conflicts resolve in
  upstream's favour and fork code is re-seated on what upstream lifted -
  never the reverse. This sync adopted: the module catalog (the seven
  product modules register there, nowhere else), `AuditAsync` in place of
  five per-module audit helpers and seven inline publishes, the
  `PerOrgSweepService` base for the entity-retention and SLA sweeps, the
  Platform SMS seam (the fork keeps only the Twilio adapter, the
  preferences, and the fan-out), whole `SiteInfo`, and the two tenancy
  shapes: `request_events`, `request_recipients` and `quotes` are
  `ITwoPartyScoped` (migration `PlatformTenancyShapes` swaps their
  policies onto `EnableTwoPartyRls`; the property is `CounterpartyOrgId`
  because the shape names it, `VendorOrgId` stays as a non-null read
  accessor) and `vendor_profiles` is `IPublishedCatalogScoped` (its
  policies were already the shape's). `requests` keeps its hand-written
  policy: a broadcast is readable by every recipient, a third party the
  two-party shape cannot express. `vendor_credentials` likewise (reads
  follow the profile's published flag).
- 2026-09-02: **Second template sync** (upstream `700787e`, round-two
  items 9-14 all landed). `requests` moves onto the new recipient-list
  shape (`ITwoPartyScoped` + `AddRecipientListFilter`, migration
  `RecipientListShape` swaps its policy onto `EnableRecipientListRls`;
  same predicate: owner or awarded vendor may write, every broadcast
  recipient may read). `quotes` and `request_recipients` move to
  `IRequiredCounterpartyScoped`, dropping the nullable-plus-accessor
  workaround; `ServiceRequest.VendorOrgId` is now `CounterpartyOrgId`
  (the API records keep `vendorOrgId`). Network stays hand-written on
  purpose: its reads depend on membership STATUS (a removed member loses
  access) and its member and bulletin rows are written by the member or
  publisher, not the owner - neither fits the shape yet. Public-app routes
  now regenerate with `pnpm --filter public run routes`; the sync script
  captures its parent before `init.py` runs, because the upstream init
  now commits and moves `template-renamed` itself.
- 2026-09-02: **Third template sync** (upstream `b190fc5`, round-three
  items 15-17 landed; the merge was clean). `shares` moves onto the
  extended recipient-list shape (`Share` is `IOrgScoped` over
  `owner_org_id`; `AddOwnerAndRecipientsFilter` + `EnableRecipientListRls`
  with the status gate, migration `OwnerAndRecipientsShape`). `share_members`
  and `shared_bulletins` stay hand-written: they join `share_access` on the
  parent's `share_id`, not its `id`, which the helper fixes - filed as
  round-four feedback. The fork's own tests already had no hand-rolled
  waits, so the new hygiene test passed untouched.
- 2026-09-03: **Fourth template sync: ADR 48 adopted in full.** Upstream
  removed every multi-owner tenancy shape (two-party, required counterparty,
  published catalog, recipient list) and replaced them with materialization
  through the outbox. The marketplace and network were remodeled onto
  owned rows:
  - Marketplace: the requester owns `requests`, `request_recipients` (its
    list as data), `request_events` (its timeline copies) and
    `received_quotes` (its projection of each vendor's quote); the vendor
    owns `vendor_assignments` (its projection of the request plus its own
    standing), `quotes`, its profile and credentials. `RequestOffered` /
    `RequestStateChanged` fan out the requester's snapshot; `VendorResponded`
    and `QuoteSubmitted` go back to the requester, whose handler arbitrates
    (monotonically - a Started arriving before its Accepted still applies)
    and answers with the state that sticks. Published profiles project into
    the platform-global `vendor_directory` (allow-listed in the RLS coverage
    test) that matching, the buyer catalog and vendor names read from;
    credential numbers never leave the vendor. Timeline entries share a
    `SourceId` across copies. Migration `OneOwnerPerRow` splits existing rows
    into each side's own rows before the shared columns and policies go.
  - Network: the owner owns `shares` and `share_members` (the roster as
    data); each member's `share_access` row is its projection of the share
    (owner, description, status, roster snapshot) kept current by
    `ShareRosterChanged`; members change their own standing and tell the
    owner (`ShareMembershipChanged`); a publisher owns `shared_bulletins` and
    every active member gets a `shared_bulletin_copies` row, re-offered on
    every roster change so a late joiner receives what it missed; leaving or
    removal deletes the copies.
  - Consequences accepted: cross-org reads are eventual (the tests wait on
    the outbox where they used to read the shared row), and each feature
    has more parts (event, handler, projection). The API records did not
    change shape; the console needed nothing.
  - Also from this round: the one-call gate (`Gate` / `GateResults`, plus a
    fork-side `ActorGate` for endpoints whose writer may be an API key) -
    a signed-in principal missing a grant now answers 403 everywhere;
    `AddModuleDbContext` in every module; the removed shape helpers' SQL
    frozen as `LegacyTenancyShapes` per module so applied migrations keep
    compiling.
- 2026-09-03: **Fifth template sync** (upstream `71c4f31`, round-five items
  19-22 all landed; three add/add conflicts, upstream's versions taken).
  `ActorGate` and `ActorRef` now come from the template (the fork's copies
  went); `vendor_directory` is declared platform-global on the marketplace's
  catalog entry instead of in the coverage test, and that test now reads the
  EF model, so `shares` and `shared_bulletins` (owner columns not named
  `org_id`) are covered for the first time. The fork's per-module
  `LegacyTenancyShapes` shims went in favour of Platform's
  `FrozenMigrationHelpers`; the two `OneOwnerPerRow` Down() bodies restore
  the old policies as literal SQL rather than helper calls. One fork-local
  adjustment to an upstream test: the helper freeze stamp is 2026-09-03 (when
  ADR 48 landed here), because the fork's three shape migrations are stamped
  2026-09-02, the day those helpers were still the recommended shape.
  Also: every projection handler now takes a transaction-scoped advisory
  lock on its aggregate (`AggregateLock.TakeAsync`) - two copies of a
  fan-out handled in parallel had both inserted the projection and the
  second landed on a late retry (feedback, round six, item 24).

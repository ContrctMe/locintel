# Software maturity review and forward roadmap

## Scope and goal

- **Area:** Whole-project software design, architecture, implementation quality,
  efficiency, test strategy, and production maturity
- **Status:** Follow-up remediation active; prior verification is historical evidence
- **Owner:** Project maintainers
- **Last updated:** 2026-09-05
- **Reviewed state:** Commit `2b18a4c` plus the current uncommitted working tree

This review evaluates LocIntel as a forkable foundation for location/site-based,
multi-tenant SaaS. Ratings are relative to a production-ready template, not to a
prototype or example application. Its purpose is to record what is strong today,
what remains risky, and the acceptance criteria for the next stages of work.

## Executive assessment

The subsequent objective review reopened work in this priority order:

1. Cross-tab tenant/session correctness, including stale-tab writes.
2. Malformed API error parsing, request cancellation/deadlines, and safe stalled-mutation recovery.
3. Enforced and repeatable CI, including investigation evidence for intermittent failures.
4. Selected live-provider and deployment-topology acceptance.
5. Longer load/soak verification against explicit latency/resource objectives.
6. Stale comments/documentation and demonstrated maintenance hotspots.

The original nine-phase ledgers below describe the preceding remediation, not
completion of this new work. The user authorized requiring `checks` on `main`;
that GitHub setting is now applied and verified. Commits, pushes, and deployments
remain outside that approval.

### Current acceptance status (2026-09-05)

This summary supersedes pending/completion statements in the historical ledgers
below. Local verification does not establish hosted or production acceptance.

| Priority | Current evidence | Still required |
| --- | --- | --- |
| 1 — Session/tenant correctness | Server context guard and cross-tab browser regressions implemented; latest browser matrix 120/120 passes | Hosted verification of the submitted tree |
| 2 — Transport and recovery | Error parsing, native cancellation, deadlines, and unconfirmed-mutation recovery implemented; native cancellation negative control fails as intended | Hosted verification; historical intermittent slow reads remain unexplained |
| 3 — CI enforcement/repeatability | GitHub `main` requires `checks` from Actions app 15368, including administrators; strict up-to-date mode is off; local aggregate-gate regressions pass | Submit current changes with authorization, then verify all hosted jobs on that revision; retain and investigate intermittent-failure evidence |
| 4 — Provider/topology acceptance | Local adapter and topology tests provide bounded evidence only | Select live providers and deployment target; authorize and execute acceptance there |
| 5 — Load/soak | Prior local mixed-load measurements are historical, not production SLO proof | Agree latency/resource objectives and run longer soak on the selected topology |
| 6 — Maintenance/documentation | Feature-boundary improvements and detailed evidence are recorded | Finish consolidation and reassess demonstrated hotspots after higher-priority acceptance |

Branch protection was verified using GitHub's branch-protection API after the
approved update. No commit, push, or deployment accompanied the policy change.

Fresh full integration verification of the current tree passes **267 tests**
(155 + 112), with one expected opt-in scale skip and normal process exits.
The build completed with zero warnings/errors. Commands: `dotnet build
tests/LocIntel.IntegrationTests --no-restore -m:1 -v:q`, then
`Logging__LogLevel__Default=Warning LOCINTEL_TEST_SHUFFLE=9051
tools/run-integration-shard.sh 1 2` and the same command with seed `9052`, shard
`2 2`. Runner durations were 1m06s and 1m11s; TRX evidence is under
`tests/LocIntel.IntegrationTests/TestResults/shard-1-65167/` and
`tests/LocIntel.IntegrationTests/TestResults/shard-2-65164/`. This includes the
new real-database fleet-paging test in the complete suite. The CI gate regression
also passes workflow wiring and all 40 negative cases. No fresh coverage or
production soak claim follows from this run.

### Follow-up evidence history

Entries retain the state observed at each run, including failures and superseded
next steps. Consult the current acceptance summary above for outstanding work.

- Current combined browser matrix passes 120/120: Chromium 40/40 (50.9s),
  Firefox 40/40 and WebKit 40/40 (about 1.1 minutes each), normal exit and no
  retries. This includes the direct native-cancellation regression. During
  verification a fresh-login callback returned 429 in 5.656ms because the test
  reused the seeded first identity/org's accumulated budget. The test now
  creates its own first identity/org, preserving production limits. Evidence:
  `/tmp/locintel-login-429.vekbL1/`. The sign-in helper now rejects HTTP errors
  directly while accepting valid 304 cached navigations (an initial overly
  strict 2xx assertion was corrected). Typecheck, lint, gate regression and
  diff checks pass. Historical unexplained slow reads remain documented risks,
  not retroactively explained by these test fixes. At that run, GitHub had no
  main protection or applicable rules; the subsequently approved required-check
  setting is recorded above. Submission/hosted verification, selected
  provider/topology acceptance, soak testing, and final documentation
  consolidation remain outstanding.

- The cancellation regression now observes the actual AbortSignal and native
  fetch rejection, not Playwright's requestfailed event for a held interception.
  It first asserts the old request is pending and not aborted, then requires
  aborted=true and AbortError after switching, with the old tenant absent and
  new tenant visible. The observer returns the original native fetch promise.
  Five focused WebKit repetitions pass (16.6s); typecheck passes. A negative
  control temporarily removed signal forwarding ONLY for site-list requests:
  the test failed with aborted=false and no settlement, despite the new tenant
  rendering. Production forwarding was restored immediately, and the same test
  passed again (1/1, 4.9s). Negative evidence:
  `/tmp/locintel-cancel-negative.BOaVQ8/`; observed native-abort traces:
  `/tmp/locintel-native-cancel.7TMOvN/`. Temporary timing/attachment instrumentation
  and early-release changes were removed. This strengthens the cancellation
  check without changing production behavior or timeouts. Full-matrix verification
  remains required; the unrelated historical 5.03-second read is not explained.

- WebKit investigation: 15 unchanged focused tenant-switch repetitions pass
  (56.9s). Request timing logs record 1,058 completions, maximum 444.8792ms,
  at `/var/folders/33/vztx0_zd35q7vbzt_q8ph_b40000gn/T/locintel-e2e.J1KbZ2/`.
  The subsequent full WebKit run passes the original failing case but finishes
  39/40: the cancellation test waits 30s for requestfailed despite the new tenant
  rendering. Its API logs have 1,112 completions, maximum 543.2658ms. Artifacts:
  `/tmp/locintel-webkit-abort.eygDBt/`. These are separate observed failures;
  neither establishes the original 5.03-second read's cause.
- A test-only hypothesis experiment released the intercepted request after
  selectOption and matched requestfailed to that exact request. It failed 5/15
  (10 passed, 3 minutes), so releasing interception alone is not a fix. In one
  failed trace the held read completes HTTP 200 during the switch. The experiment
  was reverted; production behavior and timeouts remain unchanged. Next diagnostic
  step: observe the actual passed AbortSignal and native fetch settlement around
  the switch, rather than infer cancellation solely from Playwright events.
  Full-matrix acceptance and the overall remediation goal remain open.

- E2e typechecking is repaired: corrected base-config path, declared the
  existing Node types dependency, and added the script picked up by recursive
  workspace/CI typecheck. All packages pass, with zero downloaded packages.
  CI gate regression and diff checks pass. FleetPagingTests passes on real
  PostgreSQL: 201 sites returned exactly once across five pages, and none
  visible to the other tenant. An initial admin seed lookup failed because
  its tenantless context applied EF filters; only that setup lookup now uses
  IgnoreQueryFilters. HTTP reads retain app_user and RLS.
- Full browser verification finished 119/120: Chromium 40/40, Firefox 40/40,
  WebKit 39/40. The repeated tenant-switch case failed waiting for B-only site;
  its final site read returned HTTP 200 after 5034.593ms against a 5000ms
  assertion. Timing is established, root cause is not. Trace and stack logs
  are preserved at `/tmp/locintel-webkit-switch.BLNW4S/`. No timeout/retry policy
  was weakened. All test processes exited; full-matrix acceptance remains open.

- Checklist selection now follows the existing sites query's next-page offsets
  instead of stopping at 200 entries. Sites, daily checklists, and templates
  have loading/error/retry states; checklist API access is separated from UI in
  its feature. The controlled-response 201-site browser test passes Chromium
  1/1 (5.7s), including failed next-page recovery and selection retention. This
  is not real-database scale proof. See [checklist site selection](checklist-site-selection.md).
  Public locator reads now run concurrently through the existing API module,
  preserving unavailable-vs-empty semantics. A regression verifies both requests
  start before either completes. Each upstream request retains its 30-second
  deadline; this is not an end-to-end production latency SLO. Frontend units
  pass 29 console + 11 public, application typecheck/lint/diff checks pass.
  Focused cross-browser verification passes 9/9: Chromium 3/3 (8.1s), Firefox
  3/3 (10.9s), WebKit 3/3 (8.8s), normal exit and no retries. Full-suite
  verification remains open, alongside real-data cardinality acceptance and
  the e2e-package typecheck repair, before remote enforcement/provider/soak work.

- The latest review adds public logout failure semantics, checklist picker
  truncation above 200 sites, missing checklist read-error states, and sequential
  public locator reads to the local remediation scope. These remain ahead of
  release enforcement, provider/topology acceptance, soak testing, and final
  documentation consolidation. Public logout now reports unconfirmed outcomes
  and allows retry, with the transport outside the route. Three failure tests
  reproduced false success before the change; frontend units now pass 29 console
  + 10 public, application typecheck/lint/builds pass, and the focused real-contact
  Chromium regression passes 1/1 (3.1s). Full browser verification remains open.
  See [public session recovery](public-session-recovery.md). A separate direct
  e2e-package typecheck exposed a broken base-config path and missing Node type
  resolution; add this to CI/harness remediation. No remote settings changed.

- Added a real two-tab regression in `session-boundary.spec.ts`. The focused
  Chromium command `tools/e2e-stack.sh --project=chromium --grep 'another tab discards'`
  fails: the second tab retains Tenant A data and its draft after the first tab
  switches the shared cookie to Tenant B. This was the pre-fix reproduction.
- The separate stale-role-draft regression also fails (3.9s): after changing
  the shared cookie to Tenant B outside the stale tab's notification path,
  submitting its Tenant A role draft creates that role in Tenant B. The user
  belongs to both tenants, so this is wrong-tenant intent, not an RLS bypass.
  Command: `tools/e2e-stack.sh --project=chromium --grep 'stale-tab role draft'`.
  Fixing notification alone is insufficient; request-time session-context
  validation must cover stale writes as well.
- Implemented `SessionContextMiddleware`: `/me` supplies a non-authenticating
  fingerprint of identity, tenant, session, and impersonation claims. Requests
  carrying a stale fingerprint receive 409 before business middleware/endpoints.
  Existing clients without the optional browser precondition retain their API
  contract. The console transport sends its observed context and discards old
  response generations; its session boundary uses BroadcastChannel to reset
  other tabs after explicit changes and resets on context conflicts.
- The real-cookie `SessionContextTests` regression passes (stale read/write
  rejection, no wrong-tenant role, refresh then valid read). Typecheck/lint pass.
  Both original browser failures now pass, alongside pending-mutation and failed
  switch checks: Chromium 4/4, 9.3s. Broader browser/identity-transition verification
  is still required before closing priority 1. No claim is made yet for external
  login/cookie changes that do not emit the console notification, idle-tab refresh,
  or stalled requests; the latter remains priority 2.
- Architecture and frontend unit checks passed after the first implementation;
  the full matrix then passed Chromium 33/33 (41.4s), Firefox 33/33 (52.3s),
  and WebKit 33/33 (50.4s), with normal exit and no retries. These results
  cover the first implementation, not the subsequent external-change work.
- Added fresh-login and external-cookie/focus regressions; both initially
  failed. New session observations now notify peer tabs, and focus/visibility
  checks compare `/me` against the existing context before accepting new state.
  Fresh-login, explicit switching, and stale-write checks passed in the first
  focused rerun (3/4). The remaining focus test used `bringToFront`, which a
  standalone Chromium probe showed emits no focus/visibility event in headless
  mode (both pages remain visible/focused). The test now dispatches the browser
  focus event explicitly while retaining real cookies, requests, and UI checks;
  its rerun passed all four focused cases (7.9s). Typecheck/lint pass after
  correcting a ref initializer. Full affected backend and expanded browser
  verification is underway before priority 1 is closed.
- Current affected backend verification passed: 116 + 147 integration tests
  with one opt-in scale skip (`shard-1-39760`, `shard-2-39987`), normal exits;
  console 14/14 and public 3/3 unit tests; formatting checked 387 files.
  The expanded matrix passed Chromium 35/35 (40.6s), Firefox 35/35 (54.0s),
  and WebKit 35/35 (53.3s), normal exit, no retries. Priority 1's implemented
  browser/server context protections are locally verified. External changes
  without a console notification are detected on focus/visibility or the next
  guarded request, not while a tab is frozen. Live deployment/provider target
  information has been requested for priority 4, without blocking local work.
- Priority 2 started with the confirmed error-parser defect. Six malformed-value
  cases failed before the fix. The shared parser now accepts only string or
  string-array validation values, keeping malformed bodies inside a safe generic
  ApiError instead of throwing a TypeError. Valid strings/arrays remain covered.
  Console tests passed 21/21, public 3/3, with typecheck/lint and diff checks.
  Request deadlines, cancellation propagation, and stalled-mutation recovery
  remain outstanding; priority 2 is not complete.
- Subsequent transport work adds a native 30-second deadline through response
  body consumption, caller cancellation, and explicit unknown-outcome errors for
  interrupted writes (no automatic retries). All console query functions now
  forward their cancellation signal. Direct upload chains have a 120-second
  overall deadline, including storage transfer and polling; completion is not
  sent after a failed transfer. The session boundary preserves unknown-outcome
  warnings when replacing the old tree. Three transport regressions and the
  upload regression failed before implementation; the committed-but-unanswered
  write browser case reproduced the lost warning before the boundary fix.
  The focused Chromium rerun passed all three session cases (6.8s).
- A browser `/me` response without its context header now fails bootstrap;
  an established session is discarded if a later probe loses that protocol.
  The shell shows a session-verification error with retry instead of treating
  an unavailable session endpoint as a signed-out visitor. Its malformed-header
  unit regression failed before the fix. Current frontend checks pass: console
  28/28, public 3/3, typecheck/lint/diff checks. The expanded three-engine matrix
  is not yet verified; priority 2 remains open pending its result and remaining cases.
- The first expanded Chromium run passed all 37 preceding cases, including the
  three new deadline/cancellation/header cases, then failed the existing upload
  retry assertion at five seconds. The second ticket POST had no response before
  test teardown; its trace does not establish why it stalled. Evidence is saved
  at `/tmp/locintel-upload-deadline-failure.DRV0rS/`. Five unchanged focused
  repetitions passed (12.3s total); no assertion timeout or retry policy was
  weakened. This is an unresolved intermittent latency observation, not a proven
  fix. The second unchanged full Chromium run also passed 37/38, but failed a
  different five-second assertion: the stale-tab test's setup stayed at Loading
  sites, with its final GET `/api/sites` unanswered before teardown. The upload
  case passed. Its trace and API log are preserved in
  `/tmp/locintel-session-latency.4o6bHe/`. The API log contains request-abort
  OperationCanceledExceptions classified as unhandled server errors; their
  relationship to the stall is not established. Neither failed full run reached
  Firefox/WebKit. No process remains running from these attempts. Full matrix
  verification and cancellation/latency diagnosis remain open; repeated green
  focused tests are not being substituted for that acceptance requirement.
- Latest review verification before these transport edits passed architecture
  44/44, backend unit 46/46, and integration 116 + 147 with one scale skip
  (`shard-1-41743`, `shard-2-41742`), normal exits. Coverage and image artifacts
  below remain historical. Newly identified follow-up: upload status polling
  must target the file rather than searching the newest 50 files; public SSR
  fetches also still need bounded failure handling. Hosted enforcement, selected
  live providers/topology, longer soak testing, and documentation consolidation
  remain outstanding in the original priority order.
- Cancellation classification is now regression-tested in a real TestServer
  pipeline: an aborted request previously returned 500; it now returns an empty
  499 when output has not started, without entering the server-error branch.
  Independent operation cancellation still returns 500 with a trace ID. Both
  cases pass. A diagnostic Chromium run passed 38/38 (45.0s); request-timing logs
  are preserved at `/tmp/locintel-cancel-timing.aJbCTa/`. This does not establish
  the cause of the earlier intermittent stalls.
- Added typed GET `/api/files/{id}` and switched scan polling to that stable
  identity. The real-database regression first failed with 405, then passed after
  the endpoint was added: a clean file remains readable after 50 newer uploads;
  another tenant, an unknown ID, and a deleted file return 404, while a member
  without permission receives 403. The client regression also failed before
  switching away from paginated polling. Storage, cancellation, and contract
  checks pass 11/11; the OpenAPI client has been regenerated.
- Public SSR reads now share the existing maybe/fallback transport and have a
  30-second deadline through body consumption. Contact redemption and public
  sign-out upstream requests are bounded too. Two deadline regressions failed
  before the fix; fallback and host/cookie forwarding checks now pass. Latest
  frontend tests: console 29/29, public 6/6; typecheck/lint pass. Architecture
  passes 44/44. The combined three-engine matrix is in progress (Chromium 38/38,
  44.9s); full backend shards and remaining browser engines are not yet verified
  for this snapshot.
- That combined run then passed Firefox's first 37 cases but failed upload
  retry. Unlike the earlier unanswered requests, this trace identifies HTTP 429
  on both file-list refresh attempts after successful upload/completion and the
  direct status read. The test shared the seeded owner's org and rate-limit
  budgets with prior cases. Evidence is preserved at
  `/tmp/locintel-firefox-upload.M3cYAD/`. The upload test now creates its own owner
  and organization; no production rate limit, assertion timeout, or retry policy
  was changed. This addresses that test-isolation defect, not the unrelated
  unanswered-request observations. Its full-matrix verification is pending.
- A fresh read-only GitHub inspection on 2026-09-05 again returned classic
  protection 404 (Branch not protected) and no applicable rules for `main`.
  The connected account has admin rights, but remote settings were not changed.
  Hosted enforcement/submission still requires explicit authorization.
- Current backend shards pass 121 + 145 tests with one opt-in scale skip and
  normal exits (`shard-1-46471`, `shard-2-46470`), covering the new file-read
  endpoint and cancellation handling. The Firefox response artifact confirms
  the uploaded file was already Clean before the subsequent list refreshes were
  rate-limited. The full browser matrix is now rerunning with isolated upload
  test identity; the earlier unanswered-request latency causes remain open.
- The full combined browser rerun passes Chromium 38/38 (45.5s), Firefox 38/38
  (about one minute), and WebKit 38/38 (about one minute), with normal exit,
  unchanged assertions, and no retries: 114/114. Together with the 266 integration
  passes, 44 architecture passes, and 29 console + 6 public unit passes, the
  priority-2 transport/cancellation/recovery and upload-status changes are locally
  verified. Historical unexplained latency remains a priority-3 reliability
  investigation; a green rerun is not proof of its cause. No remote changes,
  new image verification, or fresh coverage report are claimed.
- Next priority: CI enforcement and repeatable failure evidence. GitHub's latest
  successful `checks` runs are on published `main` at `7582d09f` and earlier;
  they do not verify local `2b18a4c` plus this dirty tree. Enabling the required
  gate and submitting the local changes for hosted verification require explicit
  authorization. Provider/topology acceptance, longer soak testing, and final
  documentation/hotspot consolidation remain in scope after that.
- Priority 3 now preserves per-run browser-stack diagnostics automatically.
  An intentionally invalid project boots the real stack, reaches Playwright's
  expected error, and asserts nonzero exit plus nonempty API, console, public,
  and PostgreSQL logs. It failed before the change (missing api.log) and passes
  afterward; evidence is at
  `/var/folders/33/vztx0_zd35q7vbzt_q8ph_b40000gn/T/locintel-e2e-artifact-check.Tcy9aE/`.
  Failure logs are copied before teardown into the same CI artifact tree as
  traces, and the original exit status is preserved. CI also enables HTTP timing
  logs. The workflow gate regression still passes all 40 negative cases; shell
  syntax and diff checks pass. A normal Chromium run passes 38/38 (42.1s), exit 0,
  with logs at
  `/var/folders/33/vztx0_zd35q7vbzt_q8ph_b40000gn/T/locintel-e2e.cIS9Ba/`.
  See the [runbook](runbook.md#browser-ci-failed) for retrieval and sensitivity
  guidance. Remote branch protection and submission remain unauthorized; the
  absence of an approval reply is not permission to change them.

The follow-up review invalidated the previous release-candidate conclusion.
Coverage had excluded async application code and frontend caches lacked tenant
identity. Coverage is now corrected; explicit session transitions are verified.
Zero projection versions are now rejected on insert and update. Readiness and
durable processing are verified separately. Global audit partition maintenance
has moved out of tenant retention and passed lifecycle/isolation and full-suite
verification, with an intermittent test-host shutdown failure recorded below.
The previous numeric ratings have been replaced with qualified assessments.
Current judgment: a substantially hardened foundation with current local release
checks, not a deployment-certified release candidate. Live-vendor configuration,
merge enforcement, and deployment-scale capacity remain outside the local proof.

## Follow-up remediation ledger (2026-09-04)

1. **CI gate — locally verified.** The aggregate runs with `always()` and rejects
   every dependency result other than success. `python3 tests/ci-gate.test.py`
   passes success, empty-input, and 40 negative cases, and checks that the gate
   depends on every workflow job. Architecture suite: 44 passed. GitHub read-only
   inspection returned `Branch not protected` for classic protection and no
   applicable rules for `main`. Merge enforcement is therefore absent; local
   verification does not claim that the edited workflow has run on GitHub.
2. **Coverage — verified.** Async application state machines are included. The
   report checks for site Create/List/Update, projection Handle, and ingest
   StageAsync bodies; it rejects the former incomplete artifacts. Full runs:
   47 unit passed; integration 123 + 117 passed, one opt-in scale test skipped.
   Unit: 20.9% line / 12.5% branch; integration: 86.7% / 69.2%; combined:
   86.9% / 69.6%. The denominator is now 10,777 lines and 2,574 branches, up
   from the incomplete 3,988 lines and 624 branches. Reports are under `coverage/`.
3. **Tenant transitions — locally verified for explicit console transitions.**
   A shared session boundary cancels reads, drains existing mutations before
   changing the cookie, and replaces both QueryClient and the component tree.
   It covers both organization selectors, post-creation switching, impersonation
   start/stop, sign-out, and account deletion. Five Chromium tests pass: distinct
   tenants, delayed reads, late old responses, failed switch/read responses,
   repeated switching, pending writes, and impersonation/logout. The full built
   browser/accessibility suite passed 26/26; frontend typecheck/lint, console
   14/14 and public 3/3 unit tests also passed. Cross-tab cookie coordination is
   not provided; see State management for the scope and pending-write ceiling.
4. **Projection validation — locally verified.**
   The shared comparison rejects zero before modular arithmetic; the handler now
   uses the comparison for missing rows too. Regressions cover zero against high
   versions, missing/migrated rows, real-handler wraparound, and concurrent delivery.
   The 12 real-handler tests and all 51 platform unit tests pass in Release.
   Full Release shards passed 130 + 117 tests, with one opt-in scale skip;
   CSharpier checked 377 files and the diff whitespace check passed.
5. **Worker readiness/smoke — locally verified.** Readiness now checks
   runtime startup/cancellation, listener state, and individual read/write
   privileges on role and durable-envelope stores. Initial positive, revoked-write,
   stopped-listener, local-queue pause, outage/liveness tests pass. The
   readiness changes also passed all 247 integration tests (one scale skip)
   and 44 architecture tests, before the subsequent cleanup correction.
   The smoke script now requires cleanup completion with only the
   worker running, preserves a fresh TTL record, asserts non-root runtime users,
   and fails on worker/API exits or error logs. The first expanded image smoke
   correctly failed: a persisted cleanup envelope was marked Handled but its
   expired row remained. The EF tenant filter suppressed global cleanup. The
   handler now refuses tenant context and delegates its tenantless DELETE to the
   existing expired-only RLS policy (a rolled-back database probe deleted exactly
   one expired row, not the fresh row). All five sweep/cleanup tests now pass,
   including expired/fresh records in two tenants and rejection of tenant-scoped
   invocation. The rebuilt `locintel:phase5-fixed` image smoke passed: the worker
   alone removed the expired record, retained the fresh one, and acknowledged
   the persisted cleanup envelope; both serving roles passed non-root, probe,
   log, and exit assertions. The affected-suite rerun passed 131 + 117 tests,
   with one scale skip; architecture passed 44 and formatting checked 377 files.
6. **Global audit maintenance — locally verified; shutdown flake recorded.**
   `MaintainAuditPartitions` runs through the existing daily global sweep; tenant
   retention no longer performs DDL. The additive `SafeAuditPartitionMaintenance`
   migration serializes upkeep, recovers default-partition rows into a missing
   month, and drops only empty old partitions. This corrects the prior 400-day
   global deletion versus Scale's 730-day entitlement without capping overrides.
   Nonempty data is removed only by tenant retention. Initial 16 audit/sweep
   tests pass, including concurrent upkeep, rollback/retry, and new-partition
   RLS. Generated SQL reviewed at `/tmp/locintel-audit-maintenance.sql`; expanded
   isolation, completed-message, and migration round-trip tests passed 26/26.
   Architecture passed 44 tests and CSharpier checked 382 files. Full integration
   shard 2 passed 135 tests with one opt-in scale skip. Shard 1 completed all
   117 tests successfully but hung during shutdown and was aborted by the
   five-minute inactivity detector: the suite result is **failed**, not passed.
   The TRX and mini dump are under `tests/LocIntel.IntegrationTests/TestResults/shard-1-16416/`.
   Database inspection found no active application query; the mini dump shows
   the runner waiting but lacks the heap needed to identify its async wait.
   An unchanged repeat passed all 117 tests and exited normally in 49 seconds
   (`shard-1-17484/integration-shard-1.trx`). Together the successful shards
   cover 252 tests with one scale skip. The intermittent shutdown failure remains
   an explicit final-verification risk; its preceding advisory-lock warning is
   a clue, not an established root cause.
7. **Maintainability — locally verified.** Authentication/keyring/cookie configuration,
   storage/scanner/secrets selection, and request policies now have cohesive
   hosting modules. `Program.cs` retains role selection, database identity,
   module/messaging registration, startup, and endpoint mapping in their original
   order, and is 433 lines. Release build passed with zero warnings; startup,
   proxy, rate-limit, and role-readiness regressions passed 42/42; architecture
   passed 44/44 and formatting checked all five changed hosting files.
   Full integration reruns passed 117 + 135 tests with one scale skip, with
   normal shutdown in both shards. Frontend work now gives selected-organization
   controls ownership of their actions/drafts and derives the selected object
   from refreshed query data; organization selection resets entitlement drafts.
   The role editor now owns draft initialization and saving independently of
   the page. Frontend typecheck/lint and 14 console + 3 public unit tests pass.
   New real-browser checks cover draft reset, refreshed lifecycle status, role
   editing, and failed-action retry; both passed in Chromium against rebuilt
   console/public artifacts (2/2). Site hours/closures and their projected
   preview now own their queries, mutations, and drafts in `SiteHours`; the
   site-detail page is 235 lines. Failed closure writes preserve the date for
   retry. Typecheck/lint, 51 platform unit tests, and formatting across 386 C#
   files pass. The new site browser check initially exposed missing test
   hierarchy setup (404); with independent setup, all three feature checks
   passed against rebuilt artifacts, including materialized windows and closure
   retry. The first full browser run passed 28 tests and exposed an unlabeled
   organization selector when the operator gained multiple memberships. Both
   desktop/mobile selectors now have accessible names, and the tenant-switch
   regression checks both layouts. The next run passed accessibility but found
   hierarchy setup racing a loading screen that incorrectly offered provisioning,
   plus a role locator matching existing rows. Hierarchy now distinguishes
   pending, failed, and genuine 404 states; a delayed/503 browser regression
   covers that distinction. The role test now targets its dialog checkbox.
   The rebuilt full browser/accessibility suite passed 30/30 in Chromium,
   without retries or weaker assertions.
   Review reconciliation subsequently found the old unused-actor-helper item
   still open. `ActorGate`, `ActorGateOutcome`, and `ActorRef` had no production
   callers; they and their five helper-only tests were removed, along with the
   unused result overload and stale agent guidance. API-key authorization and
   the documented human-only write endpoints are unchanged. The subsequent
   Release architecture suite passed 44 tests and unit suite passed 46 tests;
   the lower unit count reflects deleted unused code, not suppressed failures.
8. **Providers/browsers — locally verified; live-vendor limits remain.** Real ClamAV container tests now cover
   HTTP upload completion through the scan handler, clean multiframe content,
   infected content, and a nonresponsive scanner followed by an explicit retry.
   All 10 ClamAV checks pass in Release (three real-daemon pipeline tests and
   seven protocol checks), with normal teardown. The timeout leaves status
   Uploaded, scanned_at/preview_key null, and download denied; a successful
   retry unlocks the clean file and generates its preview. The first harness
   resolved a scoped bus incorrectly, then Docker stop/start changed the mapped
   port (56198 to 56223), invalidating recovery. Correct scoped resolution and
   native container pause/resume now test timeout/recovery at a stable endpoint;
   no production code or assertions were weakened. Evidence:
   `tests/LocIntel.IntegrationTests/TestResults/clamav-phase8-verified.trx`.
   At that point Firefox/WebKit verification was incomplete; expanded provider checks
   are recorded below.
   Existing adapter tests use MinIO, Azurite, LocalStack KMS,
   stripe-mock, the WorkOS emulator, and Mailpit; these are not live-vendor proof.
   Both cloud adapters reproduced overwrite through retained upload tickets
   (`cloud-overwrite-red.trx`). S3 now signs `If-None-Match: *`; Azure SAS grants
   Create without Write. MinIO/Azurite reject replacement, and MinIO also rejects
   removal of the signed condition (`cloud-overwrite-fixed.trx`, 2/2).
   Completion also accepted zero/oversized stored objects (`stored-size-red.trx`).
   The storage port now reports actual length instead of mere existence;
   completion rejects zero/missing or over-declared-size objects before scanning.
   The affected storage/provider/ClamAV/ingest run passed 25 checks
   (`storage-provider-phase8.trx`). The expanded run, including server-side
   writes and empty-versus-missing metadata, passed 27/27
   (`storage-provider-expanded-phase8.trx`).
   Cloud upload admission remains a post-upload check, not a provider-side byte
   quota; see production guidance. The shared browser uploader also ignored
   ticket headers and PUT failures; a browser regression reproduced missing
   headers, then passed after the shared uploader forwarded headers, stopped on
   failed PUTs, and used same-origin credentials. Both Files and Ingest share
   that path. Typecheck/lint, 14 console + 3 public tests, 44 architecture tests,
   51 platform unit tests, all 388 formatting checks, and CI negative cases pass.
   A first
   browser-harness attempt failed before tests when dotnet build exited 139;
   the identical standalone build then passed with zero warnings/errors.
   The initial three-engine verification was incomplete: the combined stack passed all
   31 Chromium tests, but Firefox encountered HTTP 429s inherited from the same
   seeded account's earlier traffic. The run was interrupted after 47 passed,
   seven failed, and 39 not run; traces are preserved under
   `/tmp/locintel-cross-browser-failure.SDTqCv/test-results/`. The runner now starts
   each engine on a fresh stack rather than increasing rate limits; that runner
   was subsequently exercised: Chromium passed 31/31 and Firefox passed 31/31
   on separate fresh stacks. WebKit passed 29/31, failing keyboard navigation
   and the hours projection preview. A standalone WebKit probe confirmed that
   macOS Tab skips links while Option-Tab reaches them, matching Apple's Safari
   keyboard documentation. The keyboard test now uses that native gesture on
   macOS WebKit only and passed against the rebuilt application (1/1, 2.7s).
   Focus and Enter navigation assertions remain unchanged. The hours trace
   contains successful schedule creation and a subsequent windows GET returning
   an empty array, with no further refresh before failure. A controlled browser
   regression returns one empty pre-rebuild response and then delegates to the
   real API; it reproduced the stuck preview (9.2s). The hook previously made
   only one delayed invalidation after 1.5 seconds. It now invalidates immediately
   and uses React Query's two-second foreground polling while the preview is
   mounted, allowing recovery from late projections, including valid empty ones.
   This costs up to 30 reads/minute per foreground preview; notification-based
   refresh is an upgrade trigger if measured traffic warrants it. Frontend
   typecheck/lint and 14 console plus three public tests pass; the rebuilt
   browser regression passed in WebKit (1/1, 8.0s), retaining schedule removal
   and failed-closure retry assertions as well as projected hours visibility.
   The post-fix full run passed Chromium 31/31 (37.6s) and Firefox 31/31
   (50.0s), including the controlled stale-projection regression. WebKit then
   passed 31/31 (50.7s). The complete fresh-stack matrix passed 93/93 with normal
   runner exit, no retries, and unchanged rate limits and assertion timeouts.
   Concurrent full backend shards also failed a
   short-lived impersonation check and ClamAV startup while many containers and
   browser checks competed for resources. Those are failed runs, not passing
   evidence. Both exited normally: shard 1 passed 147 and failed one
   (`shard-1-24049/integration-shard-1.trx`); shard 2 passed 108, failed three,
   and skipped the opt-in scale case (`shard-2-24082/integration-shard-2.trx`).
   Shard 1 then passed all 148 tests and exited normally when run alone
   (`shard-1-24757/integration-shard-1.trx`, 1m14s), including the expiry check.
   Shard 2 then passed 111 tests with one opt-in scale skip and normal exit
   (`shard-2-25035/integration-shard-2.trx`, 1m19s). Together the isolated shards
   verified all 259 integration cases without changing timeouts or assertions.
9. **Operating envelope — locally measured and final-suite verified.** The scale harness now distinguishes
   10,000-row commit acceptance from actual persistence of all 10,000 imported
   sites, and seeds organizations with the configured default region identifier
   rather than the unrelated literal `default`. The completion probe passed:
   commit acceptance 4.14s versus 12.43s to persist all sites (805 sites/s),
   recorded in `scale-commit-probe.trx`. The harness now runs 16 closed-loop
   read clients for 60 seconds alongside staging/commit, fan-out, and audit
   upkeep, reporting per-route percentiles/statuses and test-host CPU/RSS.
   It also asserts durable queues drain with no dead letters. The first expanded
   run failed: 47,369 of 72,682 reads were throttled because the benchmark changed
   the org quota after its five-minute cache was populated. The run did not
   establish mixed import capacity (`scale-mixed-first.trx`). Setup now changes
   and verifies the effective quota before the first tenant request. The second
   run also seeds expired files in every org and asserts both Erased tombstones
   and removal of actual stored bytes. Error reporting is aggregated, and a
   traffic failure no longer masks an import failure. macOS reported zero for
   lifetime peak RSS, so the harness now reports end-of-traffic RSS instead;
   zero was not a valid memory measurement. The second run's assertions passed
   (`scale-mixed-effects.trx`): 60.2s at 97.9 requests/s, all 5,896 reads HTTP 200,
   import completion 55.82s from commit start, 1,003 purge tombstones plus actual
   byte removals in 1.52s, audit upkeep 80.6ms, and subsequent drain 8.9ms.
   Test-host CPU was 203s and end-of-traffic RSS 916.8 MiB. This is **not clean
   operational proof**: shutdown logged PostgreSQL 53300 connection exhaustion
   while durable receivers released ownership. Also, traffic ended before purge
   began because staging plus commit exceeded its fixed window. Both gaps
   required the subsequent changes recorded below; no production envelope is declared. A standalone built
   frontend loading probe passed, separate from backend contention: five fresh
   Chromium contexts per target measured median visible-heading times of 231ms
   for console sites and 529ms for the public SSR locator. Scope and transfer
   observations are in `perf-baseline.md`; these are small seeded-dataset
   observations, not full interaction readiness or high-cardinality UI proof.
   The extended backend run kept reads active through background completion
   while connection exhaustion was investigated. Read-only
   PostgreSQL snapshots during this run showed 29, 42, and 43 application
   connections against `max_connections=100`, with oldest active transactions
   about 0.4–1.2 seconds. Import persistence advanced from 3,532 to 7,173 rows.
   These observations do not show a long-running transaction leak; they do not
   yet establish the cause of the earlier shutdown connection burst. The
   extended run again completed business assertions but logged 53300 errors
   from `ReleaseIncomingAsync` during receiver shutdown, plus an advisory-lock
   warning. A new startup regression reproduced the unbounded default budget
   (100 per pool, versus the proposed 20), while an explicit maximum of seven
   was preserved. Startup now bounds omitted pool maximums to 20, preserving
   explicit native maximums and larger minimums. The first implementation's
   Npgsql `ContainsKey` check did not distinguish an omitted supported keyword;
   parsing the normalized serialized string with the standard connection-string
   builder corrected this. Startup regressions now pass 3/3: omitted maximum
   becomes 20, explicit seven remains seven, and explicit minimum 25 is honored
   (`connection-pool-explicit.trx`). The subsequent extended workload used
   the new default; its completed result is recorded below.
   A subsequent harness change adds another drain assertion after all read
   traffic stops, covering messages emitted by the final requests. The measured
   full-feed hotspot (extended-run p95 2.15s) also motivated replacing repeated
   per-site schedule scans with one standard lookup. The connector regression
   now includes a site with no schedules to guard against accidental sharing.
   The bounded-pool run measured the original feed implementation and subsequently
   passed with clean teardown logs (`scale-mixed-bounded-pools.trx`): 92.3s,
   74.5 requests/s, all 6,873 reads HTTP 200, import completion 70.93s,
   purge completion 10.93s, test-host CPU 296.2s and end RSS 820.7 MiB.
   This one run supports the pool-bound fix locally, not a fleet-wide guarantee.
   Focused feed/attribute tests passed 2/2. The optimized mixed run then passed
   with clean teardown (`scale-mixed-lookup.trx`): 95.8s, 7,605 reads, zero HTTP
   errors, all imports and purge effects, and final post-traffic drain 10.9ms.
   Feed p95 was 1.36s versus 1.65s before the lookup; this is a single local
   comparison, not statistical proof. Method, current results, accepted ceilings,
   and upgrade triggers are recorded in `perf-baseline.md`. The final Release,
   formatting, architecture, unit and integration coverage pipeline passed;
   detailed current results are in the final-tree verification ledger below.
   These are in-process TestServer observations, not network
   or deployment capacity. Benchmark-only rate limits are raised explicitly.
   Sustained mixed traffic, purge effects, queue drain, resource measurements,
   and built frontend loading observations are now recorded with their limits.
   They do not establish production capacity.

## Current qualitative ratings

| Area | Assessment | Evidence or remaining limitation |
| --- | --- | --- |
| System design | Strong | Explicit ownership, scope, lifecycle, and temporal models |
| Backend architecture | Strong | Modular monolith with enforced module and persistence boundaries |
| Tenant isolation | Backend strong; explicit console transitions verified | RLS tests plus fresh-cache/tree transitions; cross-tab coordination remains a limitation |
| Cleanliness | Mixed | Shared mechanisms help; composition root and feature components remain large |
| Efficiency | Locally measured | Mixed traffic, import/purge completion, and queue drain verified; short single-host runs do not establish deployment capacity |
| Frontend architecture | Improved | Generated transport and lazy routes; feature coordination remains concentrated |
| Test strategy | Strong approach; intermittent-run risk retained | Current backend suites and all 93 browser cases have passing runs; historical shutdown and isolated Firefox latency failures remain documented |
| Coverage confidence | Corrected baseline verified | Async bodies included; denominator checks prevent the prior exclusion regression |
| Production operability | Locally verified; deployment validation required | Current generated-handler image passed durable business-effect smoke; live providers and deployment topology still need environment-specific validation |

## Review scope and evidence

The re-review covered the committed state through `2b18a4c` and the complete
current working-tree remediation. It inspected production wiring, migrations,
scheduling, projection ordering, generated client types, frontend structure, CI
workflows, browser tests, release scripts, and the measured scale baseline.

The assessment uses a deep-module test: a good seam should hide meaningful
policy or operational complexity behind a small interface. The new provider,
projection-version, sweep, and extracted frontend feature mechanisms pass that
test. Some legacy frontend pages still expose more coordination than desirable.

## Progress against the prior review

| Prior finding | Current state | Assessment |
| --- | --- | --- |
| Production used local object storage and EICAR scanning | S3/Azure Blob and ClamAV are selectable; local adapters are refused in Production | Startup guards, real ClamAV, cloud upload controls, and final full integration shards passed; live cloud accounts remain unverified |
| Unknown roles and worker probes | `ROLE` is validated; `/livez` is process-local and `/healthz` verifies role-critical stores | Resolved and covered by positive, permission-loss, and database-outage tests |
| Projection locks did not establish ordering | Events carry the source `xmin`; projections persist and compare it, with `0` reserved for unsynchronized migrated rows | Resolved, including wraparound, redelivery, and the zero-version migration edge case |
| Every worker replica published every sweep | PostgreSQL period leases use assembly-qualified contract identities and grant one publisher per sweep period | Resolved; the recoverable at-most-once scheduling guarantee is explicit |
| No executable release artifact | CI publishes one non-root .NET OCI image and boots all three roles | Resolved and locally verified |
| Frontend client accepted arbitrary requests | Method, route, required path/query initialization, required bodies, and every success response derive from OpenAPI | Resolved; a zero-grandfather ratchet and compile-only misuse cases guard the contract |
| No browser or accessibility suite | Playwright covers critical console workflows, built public SSR/hydration, keyboard navigation, and Axe serious/critical checks | Current-tree 31-case runs passed in Chromium, Firefox, and WebKit; initial Firefox latency failure retained in final ledger |
| Architecture tests scanned incomplete sets | Uncommitted guards derive modules/adapters from authoritative project structure | Resolved in candidate changes; tests pass |
| Frozen migration helpers lacked full compatibility snapshots | Uncommitted signature and SQL golden tests were added | Resolved in candidate changes; tests pass |
| No line/branch reporting | Coverlet and ReportGenerator publish required unit, integration, and combined artifacts | Resolved for trustworthy reporting; coverage floors await a stable history |

## Design and backend architecture

### Strengths

- Modules own their schemas, DbContexts, migrations, and vertical behavior.
- Cross-module behavior travels through contracts, read models, and messages
  instead of direct module references.
- `ModuleCatalog`, `ModuleDbContext`, and module persistence registration provide
  high leverage and good change locality.
- Tenant isolation is enforced twice: named EF query filters and forced
  PostgreSQL RLS under an unprivileged application role.
- Authentication, billing, notifications, object storage, malware scanning,
  secrets, region selection, and authorization are real adapter seams.
- Time, hierarchy, lifecycle, audit, idempotency, projection ordering, and
  scheduled-work ownership are explicit concepts with behavioral tests.
- Nullable analysis, warnings-as-errors, central package versions, OpenAPI drift
  checks, migration round trips, architecture rules, and Testcontainers make the
  repository difficult to change accidentally.

### Composition and cleanliness

The composition root now delegates substantial configuration concerns to
`AuthenticationHosting`, `StorageHosting`, and `HttpPolicyHosting`. Provider
validation remains with provider selection; cookie/keyring policy is together;
rate-limit registration and ordered HTTP middleware share one home. The 448-line
`Program.cs` retains the top-to-bottom role, database identity, module, messaging,
and endpoint startup narrative. This reduces policy coordination at the entry
point without introducing new adapter interfaces or changing registration order.
Focused hosting checks and the phase-7 full integration and architecture suites
pass, as do the current final backend and frontend suites recorded below.

Several endpoint files are also 300–500 lines. Refactor them when related
features change, prioritizing cohesive behavior and test seams over cosmetic file
size reduction.

## Production correctness and operability

### Provider selection and startup validation are real

Production now refuses local authentication, storage, EICAR scanning, key
wrapping, billing, and email. S3, Azure Blob, ClamAV, KMS, Stripe, SMTP, and WorkOS
are wired as production implementations. This closes the previous production
adapter blocker.

Each selected provider uses native options validation with `ValidateOnStart()`.
WorkOS, S3, Azure Blob, ClamAV, KMS, Stripe, and SMTP reject missing and malformed
required values; port bounds, paired credentials, HTTP endpoint shape, Stripe's
complete plan-price map, Azure connection-string parsing, and SMTP sender syntax
are covered. Validation messages name configuration keys without echoing secret
values. Thirty-five boot-guard cases and the Production image smoke cover the
negative and positive paths.

### Readiness is role-specific and separate from progress

Every role exposes probes, and unknown roles are refused. `/livez` bypasses
database-backed request middleware and remains a process-local answer during a
database outage. `/healthz` requires completed bootstrap, a started/non-cancelling
Wolverine runtime, accepting/non-faulted listeners, and usable durable local
queues. Its database check is bounded to three seconds and checks schema usage
and each SELECT/INSERT/UPDATE/DELETE privilege on incoming/outgoing/dead-letter
tables plus API sessions or worker sweep leases. A stopped listener or paused
local queue fails readiness while liveness remains healthy. No private runtime
fields or new health-check dependencies are used.

External providers intentionally do not gate readiness. Self-hosted provider
diagnostics remain on the operator-only health surface, avoiding pod churn during
vendor outages. Integration tests prove positive readiness, role-specific
permission failures (including readable but unwritable envelope stores),
database-outage failure, and uninterrupted liveness. These checks do not prove
that an individual handler made progress; monitor completion, backlog age,
retries, and dead letters separately. The image smoke demonstrated why: an
acknowledged cleanup message originally deleted no expired rows because of an
EF tenant filter. That handler now uses the existing expired-only RLS DELETE
policy with a tenantless-context guard, and fresh rows in both tenants survive.

### Production artifact

The SDK container publisher produces one image for `migrate`, `api`, and `worker`.
The image runs as the configured non-root `app` user, contains pre-generated
Wolverine handlers, and locally completed migrations before booting both serving
roles. Production used Wolverine `Auto` mode and confirmed that pre-generated
types were loaded.

The expanded image smoke now fails on worker/API error or exception logs and
unexpected exits, asserts runtime non-root users, and checks persisted cleanup
acknowledgement plus expired/fresh row outcomes before starting the API. The separate browser
stack now closes the public-app artifact gap by booting its built SSR output and
static client assets through the declared production start command.

## Projection ordering

The new source-version model is sound in normal operation. `OrganizationUpserted`
carries the authoritative organization's PostgreSQL `xmin`; the directory row
stores the last applied value; the handler locks the aggregate and rejects equal
or older events in the same transaction. Unit and handler-level integration tests
cover ordinary ordering, duplication, wraparound, and concurrent delivery.

The migration initializes existing directory rows to `0`. `ProjectionVersion`
now treats that value explicitly as “no source version applied,” accepts any
non-zero first source version, and continues to use signed modular 32-bit ordering
after synchronization. Unit and real-handler integration tests cover sentinel
values immediately below and above `2^31`, invalid incoming zero against missing,
migrated, and high-version rows, wraparound, redelivery, stale delivery, and
concurrent high-version/zero delivery. The same comparison now guards insertion
and updates; incoming zero cannot create a new directory row. PostgreSQL modular
ordering still assumes compared synchronized versions are less than `2^31`
transactions apart; the zero sentinel alone is exempt from that ordering window.

## Recurring work and replica safety

`PerOrgSweepService<TMessage>`, `GlobalSweepService<TMessage>`, `SweepPeriod`, and
`SweepLease` are good deep modules. They consolidate repeated timer behavior,
wait for host startup, isolate per-tick scopes, handle cancellation, and use a
PostgreSQL uniqueness constraint so several worker replicas produce one logical
sweep per period.

Lease identities now combine the message assembly and fully qualified contract
name. A unit test proves that identical simple names cannot collide, and the
two-worker integration test verifies the actual canonical keys.

The lease commits before durable publication, so scheduling is at most once per
period; once published, Wolverine owns durable delivery. A crash in the gap or
partway through per-org fan-out delays missing work until the next period. Every
shipped sweep is condition-based and self-repairing, so the current design is the
smallest correct guarantee. A future sweep whose individual period must never be
missed requires transactional claim and outbox publication before release.

Audit partition upkeep now uses its own global message and daily lease. Per-tenant
retention remains scoped to that tenant's rows. Concurrent global calls serialize
inside the database; failed upkeep rolls back partition creation and row movement
before retry. Pruning only empty partitions preserves the 730-day Scale retention
window and longer overrides, unlike the previous unconditional 400-day drop.
Missing-month repair preserves default-partition rows and reinstates FORCE RLS.
Parent-table locks during repair/pruning remain a measurable operational ceiling,
not a claim of online/nonblocking maintenance.

## Frontend review

### Architecture

React Query, TanStack Router, centralized session state, generated capability
keys, and shared UI primitives are appropriate foundations. Every console route
now uses lazy loading, reducing the entry bundle from 508.08 kB minified / 150.36
kB gzip to 257.67 kB / 82.07 kB in the phase-8 browser build. Sites, operator,
and roles ship as separate 16.28 kB, 11.21 kB, and 10.18 kB chunks. The public routes use TanStack's current
`validator()` API, so production builds are free of the former deprecation and
console chunk-size warnings.

The public build now has a declared production Node host and start command. The
browser stack serves both the built console and built SSR client/server outputs,
so a passing build is no longer mistaken for a runnable frontend artifact.

Track bundle budgets in CI only after several releases establish a stable
baseline; the current measured values are the starting point.

### Feature boundaries

Sites, roles, and operator now live under cohesive feature folders with private
API, query-hook, schema, and component layers and small public entry points.
Network calls no longer live in their UI components, and runtime parsing is
located at the external-response boundary. Router modules compose feature entry
points rather than reaching into feature internals.

The remaining settings, ingest, shell, and other legacy pages stay page-oriented.
Extract them only as related work demonstrates a real seam; repeating the folder
structure without reducing coordination would add ceremony rather than depth.

### Data/API layer

The client contract is now authoritative in both directions. Conditional tuple
arguments make required path/query initialization and required JSON bodies
compile-time mandatory. Named response DTOs or explicit bodyless success statuses
cover every published operation; the typed-response ratchet has no grandfathered
operations, and the console contains no `as Promise<LocalType>` assertions.

The transport normalizes empty, JSON, non-JSON, permission, conflict, and network
failures into `ApiError`. Compile-only contract cases pin missing path, query, and
body failures. The remaining generated `number | string` unions reflect the
serializer's number-reading policy; the UI normalizes them only where arithmetic
is required.

### State management

React Query owns server state and the session provider owns identity/organization
context. Local component state is used for local forms and UI behavior. This is a
sound split; a larger global state library is not justified.

The console now gives each explicit session transition a fresh QueryClient and
component tree, rather than reusing unqualified query keys across tenants. The
pending screen removes old forms immediately. Existing mutations finish under
the old cookie before the transition request; old reads are cancelled and their
cache is discarded. Failed transitions also re-resolve `/me`, because an error
response can still change a cookie. This implements the goal's permitted safe
cache-reset boundary instead of adding organization prefixes independently in
every feature. Upload/ingest chains are covered by their existing mutation cache;
late invalidations target only the detached old client.

The call-site audit covers dashboard, sites, hierarchy, checklists, members,
roles, files, ingest, audit, settings, developers, account, and operator queries.
All are beneath the same provider. Profile writes that reissue cookies are drained
before switching. Login/signup use full navigation; the membership-leave API has
no console caller. This evidence concerns transitions in the mounted console;
cross-tab cookie changes are not coordinated by this boundary. A hung mutation
delays switching rather than risking a write under the next tenant's cookie.

The extracted feature hooks own their query keys and reads; mutations remain on
the existing shared mutation primitive while feature API functions own transport.

### Validation and error handling

Runtime validation is deliberately narrow: session bootstrap and the dynamic site
attribute bag, role grants, and operator entitlement values are validated at their
trust boundaries. The shared transport owns response parsing and error
normalization, including empty and non-JSON bodies. Form validation stays local;
a repository-wide schema layer is not justified.

### Testing

The Playwright suite covers local sign-in, owner and operator visibility,
keyboard navigation, organization creation and switching, site creation,
optimistic-conflict feedback, role assignment, the complete upload/scan/ingest
journey, explicit network failure, and serious/critical Axe checks across the
main console pages. It also verifies that the built public SSR response contains
tenant content and then hydrates that response in Chromium.

The phase-8 fresh-stack run passed 31 cases in each engine (93 total), after
correcting shared-account interference and the two WebKit-discovered issues
recorded above. Current-tree reruns also passed 31 per engine, with the initial
Firefox latency failure and unchanged successful repetitions documented below.
The runner builds both frontends,
serves the console through Vite preview and the public app through its declared
production server, and boots PostgreSQL, migration, and API roles. Workspace
`pnpm test` still excludes Playwright so the fast test command does not silently
depend on Docker. Moderate accessibility findings remain outside the blocking
threshold; expanding that threshold should follow manual triage rather than
turning existing noise into an unactionable gate.

## Test strategy and coverage

The project's backend test philosophy is excellent. Fast architectural rules and
pure unit tests are complemented by broad PostgreSQL/Testcontainers integration
tests that exercise RLS, migrations, concurrency, outbox behavior, providers,
tenancy, and production configuration. This is stronger evidence than a high
unit-test count built mostly from mocks.

The uncommitted guardrail changes close the previous scan gaps:

- Data conventions now use every catalogued module assembly.
- Integration dependency rules discover every adapter referenced by the host and
  compare them with the solution.
- Frozen migration helpers have complete public-signature and emitted-SQL golden
  tests.

The coverage job now requires fresh Cobertura artifacts and generates separate
HTML and GitHub Markdown reports for unit, integration, and combined tiers. The
current baseline is 18.1% line / 9.3% branch for the pure platform unit tier,
87.3% / 70.2% for integration, and 87.4% / 70.3% combined across 16 production
assemblies. Async application bodies are included; generated migrations and
Wolverine code are excluded. No coverage
floor exists, intentionally, while a stable run history is established.

Required outcome:

- Preserve required artifacts and separate tier summaries as test projects are
  added.
- Observe several stable runs before selecting modest, ratcheting floors.

## Efficiency and operating envelope

The current mixed scenario ran 16 read clients for 95.8 seconds while importing
10,000 sites into a 1,000-site fleet, purging actual objects in 1,003 orgs,
maintaining audit partitions, and draining durable queues. All 7,605 reads
succeeded; import completion took 71.26 seconds and feed p95 was 1.36 seconds.
This is short, in-process local evidence, not deployment capacity planning.

The tested local envelope and repeatable commands are published in
[`perf-baseline.md`](perf-baseline.md). No measured result justifies extra
machinery yet. Known upgrade triggers remain:

- Public site listing retrieves the full fleet and sorts distance in memory.
- Listings feed still materializes the fleet; a per-site schedule lookup now
  replaces repeated scans, with a single before/after comparison recorded.
- CSV ingest reads and materializes the complete file.
- Site listing performs two counts plus a page query and uses offset pagination.
- Beyond the tested 11,000-site fleet, remeasure full-feed/public reads before
  deciding whether to bound or stream them.
- Above 10,000 CSV rows, add a supported row/byte limit or stream parsing and
  staging based on measured memory and latency.
- Above 1,000 active orgs, batch or parallelize durable worker fan-out only if
  the sweep completion window is threatened.
- Replace offset pagination with cursors when deep-page churn or measured query
  cost makes the semantic tradeoff worthwhile.

## Prioritized roadmap

### Phase 0 — restore trustworthy verification

- [x] Separate Vitest and Playwright workspace scripts/jobs
- [x] Fix the strict Playwright site-created locator
- [x] Add `coverlet.collector` to the platform unit tests
- [x] Fail when expected coverage artifacts are absent
- [x] Report unit, integration, and combined coverage separately

**Exit criterion:** The required `checks` workflow is green from a clean checkout,
and every green status represents the test tier named by the job.

### Phase 1 — complete production semantics

- [x] Production-selectable storage, scanner, and secrets adapters
- [x] Production guards against local-only adapters
- [x] Distributed ownership of recurring worker schedules
- [x] Source versions on event-fed projections
- [x] Validated process roles and probes on every role
- [x] CI-built non-root OCI image and three-role smoke test
- [x] Role-specific dependency readiness
- [x] Production adapter option validation on start
- [x] Projection zero-sentinel compatibility
- [x] Collision-proof sweep identity and documented delivery guarantee
- [x] Worker log/exit assertions, non-root runtime checks, and durable completion in the image smoke

**Exit criterion:** Two API replicas and two worker replicas can run from one
artifact, bad production configuration fails before serving traffic, readiness
reflects critical dependencies, and projection/sweep failure semantics are
explicit and tested.

### Phase 2 — frontend contract and critical-flow confidence

- [x] OpenAPI-constrained methods, route templates, and parameter shapes
- [x] Initial browser and accessibility suite
- [x] Compile-time-required path parameters and request bodies
- [x] Complete response schemas and remove caller-selected response casts
- [x] Sites, roles, and operator feature modules
- [x] Focused boundary validation and consistent transport errors
- [x] Public SSR and additional critical-workflow browser tests
- [x] TanStack deprecation cleanup and console route splitting

Historical pre-follow-up phase-6 verification on 2026-09-04: the OpenAPI snapshot and zero-grandfather
typed-response ratchet passed (2/2); generated types were idempotent; frontend
typecheck and lint passed; console tests passed 12/12, public tests 3/3, and both
production frontend builds completed. Both integration shards passed (123 + 117,
240 total). The console baseline at that point was 508.08 kB
minified / 150.36 kB gzip for its single main JavaScript chunk. Remaining risk is
structural rather than contractual: large pages still own workflow coordination,
and the serializer's permissive numeric input policy produces localized
`number | string` normalization at arithmetic call sites.

Historical pre-follow-up phase-7 verification on 2026-09-04: frontend typecheck and lint passed; console
tests passed 14/14, public tests 3/3, both production builds completed, and the
browser/accessibility stack passed 15/15. The console entry fell from 508.08 kB
minified / 150.36 kB gzip to 260.15 kB / 82.51 kB, with sites, roles, and
operator delivered as independent lazy chunks. No TanStack deprecation or chunk
size warning remains.

Historical pre-follow-up phase-8 verification on 2026-09-04: the fresh browser stack built and served both
production frontends and passed 21/21 tests. New checks cover organization
creation/switching, role assignment, upload/scan/ingest/commit, optimistic
conflict messaging, an unreachable API, and SSR content plus browser hydration.
The public app's production start command and static assets were exercised, not
only its compiler output. The conflict case creates its own prerequisites and
also passes as an isolated focused test.

**Exit criterion:** Changing an endpoint contract or breaking a critical UI flow
fails CI with a specific behavioral error, and feature changes stay local to a
cohesive frontend module.

### Phase 3 — guardrail completeness and measurable quality

- [x] Complete module/integration assembly discovery in candidate changes
- [x] Migration helper signature and SQL compatibility snapshots in candidate changes
- [x] Stable coverage reporting by module and test tier
- [x] Real ClamAV protocol/integration tests (focused checks pass; broad rerun tracked in the follow-up ledger)
- [x] Verify shipped ordering/idempotency paths and distinguish recipe-only examples from production evidence
- [x] Remove unused `ActorGate`, `ActorGateOutcome`, and `ActorRef` and their helper-only tests

**Exit criterion:** Every architectural guarantee claimed in primary documentation
has an automated check over the complete relevant code set.

The ordering/idempotency finding is resolved for shipped behavior through the
real `OrgDirectoryVersionTests`, integration `FanOutTests`, and HTTP idempotency
test in `StorageTests`, not by counting a private example state machine as
application coverage. The cross-tenant recipe now explicitly requires each
fork's domain handler to supply its own ordering and redelivery tests; no
unimplemented Request workflow is claimed to be production-tested.

### Phase 4 — scale and maintainability

- [x] Establish a tested local mixed-workload envelope with completion/resource measurements; deployment capacity remains unclaimed
- [x] Measure full-fleet and ingest paths and record upgrade triggers
- [x] Benchmark multi-tenant workers and high-cardinality fleets
- [x] Separate global and per-tenant maintenance work (implemented and lifecycle/isolation verified)
- [x] Reduce backend and frontend maintenance hotspots through cohesive hosting and feature modules

Historical pre-follow-up phase-9 verification on 2026-09-04: `tools/scale-baseline.sh` passed against an
isolated PostgreSQL container and rebuilt both frontends. It measured 1,000
sites/schedules, 10,000 audit rows, 10,000 CSV rows, 1,003-org enumeration and
durable fan-out, deep offset pagination, full/public listings, near-sort, and
bundle output. Historical results and the current tested local envelope are published in
`docs/perf-baseline.md`; no speculative optimization was added.

**Exit criterion:** Performance and operability claims are backed by repeatable
benchmarks at published data and replica counts.

## Final-tree verification ledger

Earlier phase results remain evidence for those snapshots, not completion of
the current tree. Final checks after the pool-budget and feed changes:

| Check | Current final-tree status |
| --- | --- |
| Release solution build and formatting | Passed in the final sequential backend pipeline |
| Architecture and pure-logic unit suites | Passed: 44 architecture and 46 unit tests; `final-architecture.trx` and `final-unit.trx` |
| Every integration shard, with corrected coverage | Passed: 119 + 143 tests, one explicit opt-in scale skip; normal runner exits; `shard-1-31894` and `shard-2-31807` |
| Contract snapshots and generated-code drift | Passed: snapshot test 1/1; regenerated API keys/types and public route tree match pre-run current-worktree hashes. Route generator emits a non-fatal circular-dependency warning |
| Frontend typecheck, lint, tests, and production builds | Passed: sequential workspace typecheck, lint, tests, and both production builds |
| Three-engine browser/accessibility matrix | Current-tree passing runs: Chromium 31/31 (37.1s), Firefox 31/31 (48.2s), WebKit 31/31 (47.3s). Initial Firefox run failed one 5.02s read assertion; five focused repetitions also passed unchanged. Original delay remains unexplained |
| Unit/integration/combined coverage reports and async denominator | Passed: fresh final-run artifacts; async denominator check passed. Unit 18.1% line / 9.3% branch; integration 87.3% / 70.2%; combined 87.4% / 70.3%, across 10,867 lines and 2,596 branches |
| Fresh generated-handler OCI artifact and durable business smoke | Passed: `locintel:final-remediation-20260904`; migrations, worker-only expired cleanup/fresh retention and acknowledgement, API/worker probes, non-root users, no serving-role error logs or unexpected exits |
| CI aggregate negative cases | Passed: workflow wiring, success, empty results, and 40 negative cases |
| Fork-sync CI regression | Passed: two successive upstream syncs, correct merge base, fork changes retained |
| Optimized mixed workload and post-traffic queue drain | Passed; 7,605 reads, zero HTTP errors, all business effects, clean teardown |
| Built frontend loading observations | Verified; small seeded dataset, scope in performance document |

The final Firefox failure trace is preserved at
`/tmp/locintel-final-firefox.Y5bY5j/`. Its final HTTP 200 sites response contains
both A-only site and Pending A write under Tenant A, but took 5.02s while the
UI assertion allows 5s. This is not evidence of a cross-tenant write. Five
focused repetitions passed unchanged (22.0s total); no timeout or retry was
increased. A full Firefox rerun with request-duration diagnostics passed all
31 cases in 48.2s, including the pending-write case in 1.6s; its API log is
preserved beside the trace as `full-rerun-api.log`.
The isolated delay's cause is not established by the successful repetitions.

## Final disposition and deployment follow-up

All nine remediation phases have implementation and local verification evidence
in the ledgers above. This closes the reviewed implementation/evidence gaps;
it does not certify an arbitrary fork's deployment. No remote settings were
changed, no live vendor account was exercised, and changes remain uncommitted.

Before a deployment, address these environment-specific requirements:

1. **Merge enforcement:** the accessible `main` protection/rules inspection
   found none. A repository administrator must require the aggregate `checks`
   status, and a hosted run must verify the submitted changes. Local CI-script
   tests do not establish hosted enforcement.
2. **Provider acceptance:** use the selected vendor sandbox/account credentials
   and deployment configuration to validate storage tickets, identity/directory
   callbacks, KMS permissions, billing events, and actual email delivery. Real
   clamd was exercised locally; signature updating and the deployed scanner's
   networking/operations still require validation. Emulator coverage is labeled
   separately in [production guidance](production.md).
3. **Deployment capacity:** load-test the actual API/worker replica count,
   database pool budget, object sizes, network, and SLOs. The measured local
   scenario is not a throughput or peak-memory guarantee. Use the specific
   ceilings and upgrade triggers in [the performance baseline](perf-baseline.md).
4. **Residual reliability limits:** retain investigation evidence for the
   historical test-host shutdown hang and isolated Firefox slow read. Current
   runs pass, but their causes are not both proven resolved. Cross-tab cookie
   coordination is not implemented; an indefinitely pending mutation delays an
   explicit session switch. Sweeps can defer work to the next period after a
   publisher crash; partition repair can block writers; visible hours previews
   poll every two seconds. These are explicit limits, not guarantees hidden by
   the passing checks.

No speculative rewrite, new state framework, arbitrary coverage floor, or
unmeasured distributed optimization is warranted by this review. Keep current
behavioral gates and collect stable operational history before tightening
budgets or broadening claims. Overall maturity: **locally verified foundation,
with deployment acceptance still required**, not an unconditional release candidate.

## Historical verification snapshot (before the follow-up remediation)

These earlier results are not verification of the current working tree. The
follow-up and final-tree ledgers above record the current remediation checks.

- `dotnet build LocIntel.slnx -c Release --no-restore` — passed, zero warnings
- CSharpier — 377 files passed
- Architecture tests — 44 passed
- Platform unit tests — 47 passed; coverage was 20.9% line / 12.5% branch
- Role-specific readiness tests — 2 passed, including permission loss and database outage
- Production boot guards and provider option validation — 35 passed
- Full integration suite — 240 passed across two shards (123 + 117); one opt-in scale test skipped
- Projection version handler tests — 5 passed, including sentinel values below and above `2^31`
- Integration Release shard 1 with coverage — 123 passed, one skipped
- Integration Release shard 2 with coverage — 117 passed
- Integration coverage — 86.7% line / 69.2% branch
- Combined coverage — 86.9% line / 69.6% branch across 16 assemblies
- Frontend typecheck — passed
- Frontend lint — passed
- Console Vitest — 14 passed
- Public Vitest — 3 passed
- Console and public production builds — passed
- Browser/accessibility stack — 26 Chromium tests passed against built console and public artifacts
- Cardinality/worker/ingest/bundle baseline — passed at the published initial envelope
- OCI publish and three-role Production smoke — passed; image runs as `app`
- Fork-sync regression — two consecutive upstream syncs passed
- Earlier workflow-equivalent local gate — reported passing before the follow-up changes; not a current-tree result

The frontend builds emit no deprecation or chunk-size warnings. Their current
bundle sizes are recorded above as the baseline for later budget decisions.

Unit and integration coverage write to fresh, tier-specific result directories.
CI fails when an expected report is absent, and its artifact paths include the
nested shard result directories used by the test runner.

## Parent and related links

- Parent summary: [`../README.md`](../README.md)
- Architecture review: [`architecture-review-2026-09-02.md`](architecture-review-2026-09-02.md)
- Architecture decisions: [`decisions/README.md`](decisions/README.md)
- Production topology: [`production.md`](production.md)
- Operational runbook: [`runbook.md`](runbook.md)
- Performance baseline: [`perf-baseline.md`](perf-baseline.md)
- Cross-tenant sharing: [`cross-tenant-sharing.md`](cross-tenant-sharing.md)

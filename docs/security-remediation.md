# Security remediation

This ledger tracks every finding in [the security assessment](security-assessment.md). Remediation began in the isolated `codex/security-remediation` worktree, based on `feat/performance-optimizations` at `b9f2fe8`. Commit `6e7b0aa` preserves the existing uncommitted Reporting/Storage work and assessment as the remediation baseline. The remediation is now consolidated into `feat/performance-optimizations`; the final integration results are recorded below.

Source changes do not establish the security state of an existing deployment. The verification section records tests actually run; the deployment section identifies prerequisites that require the deployed environment.

## Findings and resolutions

| ID | Resolution in this branch | Verification / operational boundary |
|---|---|---|
| SEC-01 | Settings require organization-wide `org:manage`; writes validate size and reject the typed `map.*` namespace. Hierarchy reads require `sites:read` and return the authorized subtrees plus their ancestors. | Guest, role-free member, owner and tenant-isolation regressions; scoped hierarchy checks. |
| SEC-02 | Leaflet tooltips receive a DOM node populated through `textContent`. | Malicious names remain literal text in unit and built-browser checks. |
| SEC-03 | Protected OAuth state includes a random browser correlation value in an HttpOnly, SameSite cookie; callback validates age and correlation and deletes the cookie before exchanging the code. | Fresh-browser callback rejection and replay rejection; normal login regression. |
| SEC-04 | Idempotency storage keys include organization, user and session; fingerprints include query strings. Expired records cannot replay. Responses are retained only for explicit policies which recheck current authority and resource visibility; one-time secrets are never retained. Other completed retries return 409 without repeating the mutation. | Cross-user secret-response regression, same-session secret redisclosure rejection, settings replay/conflict and report submission replay. See contract clarification below. |
| SEC-05 | Exception evaluation requires current membership; member removal/leave also delete supplementary exceptions. | Legacy orphaned exception fails closed, even when membership is removed directly in the database. |
| SEC-06 | The shared scope resolver refuses subtree grants for organization-wide administration: roles, org, audit, entitlements, ingest and platform operations, including service keys. | Scoped administrator cannot grant wildcard authority or exercise org-wide management. Owners retain their normal workflows. |
| SEC-07 | Ingest requires validated HTTPS destinations, public addresses checked at connection time, pinned socket addresses, no proxy/cookies/redirects, and replacement credentials when origins change. | Public/private/mapped-IP and redirect tests. Explicit Development/Testing loopback HTTP remains available for local fixtures. |
| SEC-08 | Audit webhook delivery uses the same connection-time destination policy, with redirects disabled; delivery does not buffer remote response bodies. | Transport boundary regressions; webhook lifecycle tests. |
| SEC-09 | Export files carry producer origins and require the original privileged capability; legacy export names are recognized conservatively. | Files-only readers cannot list/download modern or legacy audit/org exports. Legacy-name matching may conceal similarly named ordinary files; rename those via a controlled migration if necessary. |
| SEC-10 | All ingest operations require entire-organization authority; shared resolver enforces the same restriction for service principals. | Scoped ingest denial and normal ingest tests. |
| SEC-11 | Public SSR relays only the session cookie and framework chunks, preserves security/expiry attributes, removes Domain, and floors Secure in production. | Cookie relay tests, including chunked cookies. |
| SEC-12 | Public site identifiers must be UUIDs before URL interpolation or upstream access. | Traversal/invalid identifiers rejected without an upstream request. |
| SEC-13 | Public API responses use `private, no-store` because content depends on the resolved principal and tenant. | Cache-control regression. Shared CDN caching requires a separately designed public-only authority/cache key. |
| SEC-14 | Anonymous signup only redirects to hosted signup; it no longer provisions a provider user. Administrative provisioning sets `EmailVerified=false`. | Signup and WorkOS lifecycle regression. |
| SEC-15 | Non-sliding cookies plus stored session creation enforce a 12-hour absolute lifetime, including cookies reissued by org switching. Verified WorkOS session-revoked, password-reset-succeeded and user-deleted events revoke local sessions for the corresponding provider user through the event timestamp. Support impersonation rechecks current platform authority on each request. | Absolute-age test, signed provider-event tests and impersonation tests. WorkOS webhook delivery/subscriptions must be configured; see deployment checklist. |
| SEC-16 | Preserves the existing trash hold fix: select only unheld files, lock/reload/recheck before deleting; hold updates use the same lock. | Existing legal-hold trash regression retained and included in integration suite. |
| SEC-17 | S3 tickets sign Content-Length. Providers without enforceable limits (Azure SAS) use an authenticated, owner-bound, expiring upload relay that checks actual bytes before storage. Outstanding pending/uploaded/quarantined uploads are capped per organization; unfinished S3 deletion retains quarantine and its budget until ticket expiry; oversized completion remains inaccessible; hourly cleanup erases old unfinished/quarantined uploads after ticket expiry and honors holds. | S3 length rejection, Azurite relay size/replay tests and cleanup/budget checks. Lifecycle reconciliation remains required for cloud writes interrupted between provider and database operations. |
| SEC-18 | Bounds connector bytes, CSV/staged rows, connector/export queue admission, export query/archive sizes and spatial JSON vertices/rings/features before expensive geometry processing. Webhook response bodies are not read. Sitemap work has an overall deadline and row/page cap and does not return partial success. | Data-plane boundary tests, import/export tests and frontend deadline tests. Capacity remains bounded per process/org; deployment resource limits and provider quotas remain necessary. |
| SEC-19 | Forwarded headers require configured trusted proxy IPs/networks and constrained hostnames; guest tenancy reads only the processed Host. Non-development serving environments refuse wildcard AllowedHosts. | Known/unknown peer tests and production boot negatives. Operators supply real ingress peer ranges and allowed domains. |
| SEC-20 | Non-development serving roles require both shared Data Protection key storage and a private-key certificate to wrap keys at rest. | Boot-refusal tests; smoke mounts a temporary protected ring. Provision and rotate the real wrapping key independently of its ring. |
| SEC-21 | Credential-bearing provider overrides require HTTPS outside Development/Testing; Azure rejects plaintext/dev-storage endpoints; SMTP requires TLS. Development adapters require an explicit Development or Testing environment. | Production and Staging boot-refusal tests. |
| SEC-22 | Local Docker PostgreSQL port publications bind to 127.0.0.1. | Shell/static infrastructure regression checks. |
| SEC-23 | Runtime connection recipes and AppHost construct app-user credentials without handing owner connection strings to API/worker; migration credentials remain isolated. | Renderer secret separation checks and role smoke. Existing deployed credentials must still be inspected/rotated by operators. |
| SEC-24 | Rendered namespace network policy defaults deny and admits only required ingress, DNS/scanner access and explicit operator-supplied egress ranges. | Renderer positive/negative checks. Actual CNI enforcement and NFS export restrictions require deployment verification. |
| SEC-25 | Workflow actions use immutable SHAs, read-only default permissions and nonpersistent checkout credentials. | CI security source checks. |
| SEC-26 | Local tickets bind upload/download operation; upload publication is atomic and write-once, including after deletion; actual streamed bytes enforce the limit. Path containment uses a directory boundary. | Local ticket confusion, replay, oversize and containment regressions. |
| SEC-27 | Basemap UI explains that browser map keys are visible in tile requests and need provider domain restrictions and quotas. | Frontend test/typecheck/build. |
| SEC-28 | Renderer supplies runtime-default seccomp, read-only container roots and bounded writable scratch space, with non-root/no-escalation/drop-capability settings. | Renderer checks and hardened image role smoke. NFS deployments need CSI/PVC migration before enforcing Restricted Pod Security. |
| SEC-29 | CI captures container runtime versions, emits image SBOM/security evidence and gates HIGH/CRITICAL image vulnerabilities with a pinned scanner. | Source checks and image smoke; exact rebuilt artifact scanning is separately recorded below. Developer machine runtime servicing is an external environment action. |

## Contract clarifications

Idempotency still suppresses duplicate mutations across replicas through PostgreSQL. Security-sensitive response replay is no longer implicit: it requires an explicit current-authorization policy. Settings writes and report submissions retain replay, with permission/resource checks; other completed retries return 409 and require reconciliation through the resource's authorized read API. Integrators must retain one-time secrets from their original successful response. A lost one-time-secret response requires revoking/rotating the resource, never redisclosing it from a generic cache. This narrows ADR 29's broad replay promise to resolve SEC-04 under the requested security remediation; adding another replay policy requires tests proving current resource authority.

Azure upload tickets retain the existing URL/method/headers/expiry response shape, but their URL is a same-origin authenticated API path. The browser still performs PUT followed by completion. Azure SAS cannot enforce a byte ceiling, so exposing its create SAS would bypass that ceiling. The relay stages bounded bytes in `/tmp`, admits one active relay per process, has a two-minute deadline and serializes each file's upload with the existing transaction lock. Size scratch space and gateway request/concurrency limits accordingly. S3 and local uploads retain direct enforceable tickets.

Organization-wide administrative grants cannot be delegated to subtrees. Site/hierarchy operations that have a concrete scope keep subtree support. Stored subtree administrative assignments become ineffective and should be removed or deliberately replaced with organization-wide grants after review.

Export admission is shared across audit, self-service organization and operator-triggered organization exports. One queued/running export is allowed per tenant, with one worker export execution per process. Query materialization is capped at 100,000 rows and 4 MiB serialized data, and organization archives at 32 MiB uncompressed. Oversized organization exports fail without publishing; audit export truncation is recorded in its manifest. Delayed export messages recheck the existing organization-purge fence before writing.

## Additional assessment observations

- The console no longer persists private hierarchy data in sessionStorage; stale stored copies are removed. In-memory query state remains session-scoped.
- Public SSR has nonce-based CSP and route-aware framing rules; `/embed` remains embeddable. The console provides CSP in built HTML and preview-server headers; the production static host must supply the remaining HTTP headers.
- Development auth/billing/mail/SMS/storage/scanner/secrets adapters are restricted to explicit Development/Testing, including in Staging.
- Contact links remain short-lived public-read credentials. Their current privilege is intentionally unchanged; higher-privilege contact actions would require single-use/re-authenticated tokens.
- Storage download tickets now force attachment disposition for local, S3 and Azure objects. Deploy an isolated object origin for active untrusted content. Cloud bucket/CORS/IAM policies are deployment properties and cannot be certified by source inspection.

## Verification

Verification completed on September 7, 2026 (America/Chicago). Results and logs are retained under the ignored `coverage/security/` directory in this worktree; CI retains image evidence as an artifact.

| Check | Result |
|---|---|
| `dotnet build LocIntel.slnx --no-restore --disable-build-servers -m:1` | Passed, zero warnings/errors. |
| Architecture tests | 54 passed. |
| Platform unit tests | 68 passed. |
| Integration tests, serial collections | Full run: 433 passed, two failed, one optional scale benchmark skipped. Both failures were corrected; all 22 affected security/session/lifecycle tests passed on rerun, including one added user-deletion case. Combined: 436 distinct passing cases, one existing optional skip, no unresolved failures. |
| Frontend tests | 99 passed (65 console, 34 public). |
| Frontend typecheck, build and lint | Passed. |
| Built public app in Chromium | Passed: hydration, literal malicious tooltip text, no injected image/script, nonce CSP without violations, embeddable map and private sitemap. Synthetic local upstream; no production service contacted. |
| CI gate tests | Passed, including 45 negative cases. |
| Infrastructure source checks / renderer / shell syntax | Passed: 33 immutable action pins, five loopback publications, secret separation, network/pod policy validation and scanner failure propagation. |
| `dotnet csharpier check .` | Passed. Two unformatted migrations inherited from the baseline are explicitly ignored to preserve their bytes; other migrations remain checked. |
| Pre-merge image: three-role smoke / vulnerability scan | Passed: migrate, worker and API from the same .NET 10.0.11 image, non-root with read-only root, dropped capabilities and bounded scratch. Trivy HIGH/CRITICAL gate passed; six lower-priority OS advisories remain, detailed below. |

The two full-run failures exposed a scoped hierarchy predicate that EF could not translate and an operator handler parameter prohibited by Wolverine's service-location policy. The fixes use the existing ltree-array query and target-tenant operator patterns; test expectations were preserved. The final affected rerun covered `DataPlaneAuthorizationTests`, `SecurityRemediationTests`, `DirectorySyncTests` and `LifecycleTests`; the entire integration suite was not repeated after these two localized corrections.

Tests use real PostgreSQL, MinIO and Azurite for persistence/storage behavior. The serial test settings disable parallel collections and cap test threads at one to reduce local Docker resource pressure following the crash. The developer SDK/shared runtime is still 10.0.102/10.0.2; servicing the machine installation remains an operator task.

## Pre-merge image evidence and upstream advisories

The pre-merge security source was built and pre-generated in Release, published locally as `locintel:security-remediation`, then verified with `tools/smoke-image.sh` and `tools/scan-image.sh`. No image was published to an external registry. The local image identity is `sha256:b9a3f29128bfe29ae6b3bcde4f8d5ecac206a55083da0801b4738690fd0b3fef` (Ubuntu 24.04, .NET and ASP.NET 10.0.11). The worker completed a durable cleanup and retained the unexpired control record; API and worker readiness/liveness passed.

Trivy 0.69.3 scanned this image on September 7 at 19:27 CDT using database version 2, updated September 7 at 19:06 UTC. The CycloneDX SBOM contains 203 components, including 102 NuGet packages. The scan covered OS packages, the application dependency manifest and both shared framework manifests. There were zero HIGH/CRITICAL results under the scanner's Ubuntu severity selection and no NuGet advisories reported. Trivy warned about multiple project roots in the application manifest; its SBOM nevertheless includes the application NuGet dependency inventory. This is scanner evidence, not proof of complete vulnerability coverage.

The all-severity scan records six unique OS advisories: three MEDIUM and three LOW by Ubuntu priority, across nine package findings. Other vendors/CVSS may rate them differently: ICU's CVSS is High while Ubuntu prioritizes its command-line-tool exposure as Low. No `FixedVersion` is listed for these installed packages. The following items remain open upstream; none was suppressed with an ignore rule.

| Advisory / Ubuntu priority | Installed package(s) | Description, applicability and resolution |
|---|---|---|
| [CVE-2026-18374](https://ubuntu.com/security/CVE-2026-18374), Medium | `libc6`, `libc-bin` 2.39-0ubuntu8.8 | Heap overflow requires attacker-controlled `fopen` mode/`,ccs=` input. Ubuntu marks Noble as needing evaluation. No direct application `fopen` call was found; native dependency reachability is not established. Keep mode inputs fixed, follow the vendor evaluation and rebuild/rescan when a supported fix lands. |
| [CVE-2026-18477](https://ubuntu.com/security/CVE-2026-18477), Medium | `tar` 1.35+dfsg-3ubuntu0.4 | Incremental restore rename handling can write outside the destination with local attacker participation. Ubuntu deferred a fix pending upstream patches. Application exports use managed ZIP APIs and no application tar invocation was found. Avoid untrusted tar restore inside the runtime image; rebuild on vendor fix or separately validate a supported minimal base without tar. |
| [CVE-2026-18508](https://ubuntu.com/security/CVE-2026-18508), Medium | `tar` 1.35+dfsg-3ubuntu0.4 | Crafted hardlinks can escape `--one-top-level` extraction boundaries. Same application exposure and vendor-fix status as above; do not extract untrusted tar archives in the runtime, and replace/rescan the base once fixed. |
| [CVE-2025-5222](https://ubuntu.com/security/CVE-2025-5222), Low | `libicu74` 74.2-1ubuntu3.1 | Buffer overflow in ICU's `genrb` command-line tool. Ubuntu lists Noble as vulnerable; `genrb` was not found on the image command path and no application invocation exists. Retain ICU for runtime globalization, avoid adding the affected tooling, and rebuild when the supported package fix is available. |
| [CVE-2024-56433](https://ubuntu.com/security/CVE-2024-56433), Low | `login`, `passwd` 1:4.13+dfsg1-4ubuntu3.2 | Subordinate UID ranges may collide with network user IDs. Fix is deferred. Runtime uses a fixed non-root account, no privilege escalation and no application account provisioning; `newuidmap` was not found on its command path. Verify host/NFS UID allocation separately and update the base when fixed. |
| [CVE-2026-40228](https://ubuntu.com/security/CVE-2026-40228), Low | `libsystemd0`, `libudev1` 255.4-1ubuntu8.17 | journald wall forwarding can emit malicious terminal escape sequences. Fix is deferred. Serving containers run the .NET process, not a systemd/journald service; the application does not invoke it. Keep that runtime model, review host journald separately and rebuild when fixed. |

Evidence files are in `coverage/security/image/`: `image-id.txt`, `runtime-versions.txt`, `scanner-version.txt`, `sbom.cdx.json`, `vulnerabilities.json` (gate), and `all-vulnerabilities.json`. CI retains the SBOM and gate output for each newly built artifact. Promote by the verified image identity and repeat the scan at promotion; advisory databases and mutable base tags change over time. The runtime OS advisories and machine SDK servicing remain follow-up work, rather than being marked fixed by a passing severity gate.

## Deployment acceptance

Before deploying, supply constrained `AllowedHosts`, explicit trusted proxy peers, the shared key path and wrapping certificate, HTTPS provider endpoints, TLS SMTP, and the renderer's actual database/provider/telemetry egress CIDRs. Subscribe the signed WorkOS endpoint to the documented [WorkOS events](https://workos.com/docs/events): `session.revoked`, `password_reset.succeeded`, `user.deleted` and existing directory-sync events. Alert on webhook delivery failures: successful delivery invalidates affected local sessions on their next request; the hard session age is the 12-hour fallback if delivery is unavailable. Revocation conservatively ends all of that user's local sessions created before the event, rather than storing provider bearer tokens or trying to match one provider session.

Validate the rendered controls on the actual ingress/CNI and storage platform: no externally reachable local test databases; no runtime owner credentials; blocked cross-namespace/internal egress; protected Data Protection ring and wrapping key; private object storage and bounded quotas; lifecycle/reconciliation for abandoned provider uploads; active-content attachment/isolation; backup restore; NFS export restrictions; malware definition freshness; telemetry authentication and security alert delivery. The branch does not deploy changes or establish these live controls.

The prior npm advisory query was blocked by automatic approval review because it would submit dependency metadata externally; it has not been retried or bypassed. CI image scanning and existing NuGet audit controls are distinct checks. No claim is made that an unexecuted advisory query or uninspected live cloud configuration passed.

## Integration with performance branch — 2026-09-07

Merged local `feat/performance-optimizations` at `b3b0730` into the security branch. Its earlier Reporting snapshot matches the preserved `6e7b0aa` baseline; conflict resolution retained the security corrections and applied the subsequent entitlement backfill, report recovery, S3 stream-ownership and production-proof changes.

The incoming Reporting image harness now obeys the security branch's production contract: serving roles receive only app-user database credentials, the keyring is certificate-wrapped, WorkOS webhook secret and allowed hosts are explicit, MinIO uses HTTPS with a temporary trusted CA, and runtime capabilities/privilege escalation are restricted. Its full single/aggregate/100-site proof remains enabled in CI; the new artifact-upload action is SHA-pinned.

Validation of the merged source passed: Debug and Release builds with zero warnings/errors, 54 architecture tests, 49 focused integration tests (backfill, Reporting workflow/recovery, S3, security authorization and file trash), formatting, CI gate checks, infrastructure checks and script syntax. The TLS fixture's certificate checks accepted the intended DNS/IP identities and rejected a wrong hostname. No frontend source or API schema changed in this merge.

**Image qualification of the merged revision is blocked locally.** Initial image publication failed with `No space left on device`. Removing only this worktree's disposable Debug outputs left disk space low. A retry stalled, and Docker did not answer a bounded status request. Only the task-owned publish process was stopped; Docker and unrelated workloads were left untouched. The new Reporting production-image proof, merged-image role smoke and merged-image vulnerability scan therefore have not run. Earlier image results above apply to the pre-merge security source, not this merged revision. After restoring disk capacity and Docker responsiveness, run the existing CI image job (or publish the merged image, then `tools/smoke-image.sh`, `tools/reporting-image.sh` and `tools/scan-image.sh`). Logs from this integration are retained in `coverage/security/merge/`.


## Branch consolidation — 2026-09-07

Merged security revision `f87308f` into `feat/performance-optimizations` at `e12b7e3`, retaining the newer Reporting UI, execution bounds, admission checks and ADR 56 documentation. Report-submission idempotency replay now uses Reporting's current `ReportReader` authorization API and rechecks current membership, site visibility and report dependencies. The assessment moved to `docs/security-assessment.md` to preserve the current documentation layout.

Validation of the consolidated source passed:

- Solution build: zero warnings/errors.
- Architecture: 56 tests; Platform unit: 68 tests; Reporting unit: 23 tests.
- Targeted integration: 48 tests covering Reporting workflows/bounds, security remediation and data-plane authorization; OpenAPI snapshot: one test.
- Frontend: 105 tests (71 console, 34 public), typecheck, API and capability-key generation with no generated drift.
- Formatting, whitespace, infrastructure security checks (34 immutable action pins, nine loopback publications, runtime credential separation) and CI gate checks including 45 negative cases.

The full integration suite and production-image qualification were not repeated for this consolidation. Docker responsiveness and disk capacity have recovered; the earlier image publication failure is historical. A newly built consolidated image still needs the existing CI image qualification before promotion.

Verification logs are retained in the original checkout under `coverage/security-consolidation/`; earlier evidence was moved from the retired security worktree to `coverage/security/`. Other local branch tips are preserved as tags under `archive/local-branches/2026-09-07/`, with the original names and commits recorded in `coverage/security-consolidation/archived-branches.json`. Only `feat/performance-optimizations` remains as a local branch after consolidation. Remote branches are unchanged.

## Review corrections — 2026-09-07

The review against `origin/main` identified four regressions. All four are corrected:

| Finding | Implemented resolution |
|---|---|
| BOM-prefixed CSV uploads lose the first column | Retain bounded byte reads, then use BOM-aware `StreamReader` decoding. UTF-8 inputs with and without a BOM retain their external IDs. |
| Report photo picker cannot find supported uploads | File creation accepts an optional authorized `siteId`; the site Files view supports uploads and refreshes report photo choices. Existing organization uploads keep their behavior. |
| A pending connector blocks later scheduled work | Attempt every due connector; existing atomic admission continues to enforce the queue cap and reject duplicate/manual pending syncs. |
| Undefined report enum values expand work unexpectedly | Reject undefined modes and selections before selecting sites, creating jobs or reserving quota, including numeric and quoted numeric inputs. |

Site attachment checks require both site-read and file-manage scope, including for reserved archive filenames. The upload relay rechecks current file authority. Reference reports recheck the attached photographs' own sites against site-read and file-read scope at admission, generation and download. Losing access to a photograph's site also removes access to PDFs containing it. The [reporting guide](reporting.md#site-photographs) documents this behavior.

Validation passed: final solution build with zero warnings/errors; 62 selected integration tests, followed by nine final authorization tests after tightening the archive-name guard; 56 architecture tests; 23 Reporting unit tests; 110 frontend tests; API snapshot/client generation, frontend typecheck/lint, formatting and whitespace checks. The integration runs overlap and are not a count of 71 distinct tests. Logs and TRX results are retained under `coverage/review-fixes/`. The full integration suite and production-image qualification were not repeated for these localized corrections.

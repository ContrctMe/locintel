> Remediation work is tracked separately in [docs/security-remediation.md](security-remediation.md). The findings below preserve the original assessment evidence.

# LocIntel security assessment

**Review date:** 2026-09-07. **Repository:** `/Users/jarod/coding/location-saas`.
**Git base:** `b9f2fe8d612da82df9a76dc7f53694430b8a0fc8`, plus the current modified/untracked working tree, including Reporting.
**Assessment type:** Source, configuration, deployment and supply-chain review, with targeted harmless local reproductions. **Change scope:** Report only; no security fixes applied.

## Executive assessment

The review identified **28 active findings: 10 High, 14 Medium and 4 Low**, plus one source-remediated finding (SEC-16) retained for the audit record. No Critical finding is assigned: no unauthenticated remote-code execution or unrestricted database takeover was established. Several High findings are independently actionable and should block an unqualified production release until corrected or explicitly contained.

The most urgent issues are missing authorization on settings/hierarchy endpoints, stored XSS in the public map, missing browser binding in login, sensitive idempotent response replay, ineffective membership revocation when exceptions remain, and subtree-to-org privilege escalation. Other High findings affect outbound requests, bulk imports and privileged exports. RLS, encryption, rate limits and malware scanning are useful controls, but they do not compensate for these application authorization and trust-boundary mistakes.

Eight targeted local assertions confirmed vulnerable behavior through the actual API and a disposable PostgreSQL database. A separate executable check confirmed the Leaflet HTML sink and upstream URL normalization. Other findings are source-confirmed or explicitly conditional. **There is no evidence in this review that these vulnerabilities have been exploited in a deployed environment.**

Infrastructure has several good foundations: the new DigitalOcean manifests separate migration/runtime credentials, require image digests, use non-root containers and certificate-encrypted shared keys, and keep the compatibility bootstrap private. Its documentation explicitly leaves production deployment acceptance open. This report therefore distinguishes deficiencies in supplied templates from facts about an operating cloud environment.

## Scope, assumptions and method

Reviewed the .NET 10 / EF Core / PostgreSQL API and workers; identity, authorization, tenancy, billing, storage, ingest, audit, checklists, spatial and in-progress reporting modules; WorkOS, Stripe, SMTP, S3/Azure and ClamAV adapters; React console and TanStack public SSR; gateway/Redis fairness; DigitalOcean Terraform/Kubernetes rendering; CI, local scripts, configuration and operational documentation.

The review assumes public SaaS use, untrusted tenants and site content, tenant administrators who are not trusted with the shared service's internal network, and meaningful separation between organizations and between subtree roles. This follows README, CLAUDE.md and the accepted tenancy/permission ADRs. Conditional findings name the additional deployment assumptions they need. No separate production threat-model sign-off or cloud penetration test was requested or performed.

The current tree already had extensive changes and continued changing during the review. Evidence was checked against the source near report completion; recheck the affected surfaces after ongoing Reporting/Storage work lands. Source locations are navigation aids, not a frozen Git diff. The security-best-practices skill provided React/general browser guidance; its reference library has no C# backend guide, so backend analysis used direct code tracing, local tests, existing architecture contracts and primary vendor documentation.

No production/cloud resource was modified or attacked, no real credential was displayed, and no dependencies were installed/upgraded for this review. Temporary test artifacts contain synthetic users/data and run against a disposable database. The database was removed after each run. Existing application/test source was not edited by this review; building refreshed normal build outputs.

### Trust boundaries that determine the findings

| Boundary | Intended authority | Findings affecting it |
|---|---|---|
| Browser/guest → API/SSR | Host selects public tenant; authenticated identity and capability/scope authorize private operations | SEC-01–06, SEC-11–15, SEC-19 |
| Endpoint → EF/PostgreSQL | Tenant RLS plus explicit capability and subtree checks | SEC-01, SEC-04–06, SEC-10 |
| API → durable worker job | Preserve initiating tenant, permission, scope and bounded work | SEC-07–10, SEC-16–18 |
| Worker → tenant-supplied remote URL | Constrained HTTPS destinations, no internal reachability or cross-origin credential forwarding | SEC-07–08, SEC-18, SEC-21 |
| Storage ticket/export → bytes/readers | Write-once validated content and permissions inherited from the source data | SEC-09, SEC-16–17, SEC-26 |
| Runtime → secrets/cloud/cluster | Least privilege, protected keys, isolated networking and recoverable state | SEC-20–24, SEC-28 |
| Repository/dependencies → built artifact | Reviewed immutable tooling, least-privilege CI and vulnerability evidence | SEC-25, SEC-29 |

### Severity and confidence

**High:** Material unauthorized data access, privilege expansion, executable untrusted content or shared-network reachability under realistic stated prerequisites. **Medium:** Bounded/conditional exposure, important revocation/retention/availability failures, or production hardening gaps requiring another condition. **Low:** Limited development exposure or defense-in-depth/tooling gaps. Confidence describes evidence quality, not severity. Missing live configuration evidence is not treated as proof that the live control is absent.

## Finding register

| ID | Severity | Finding |
|---|---|---|
| SEC-01 | High | [Guest requests can read and change tenant settings and read the internal hierarchy](#sec-01) |
| SEC-02 | High | [Stored site names execute HTML in public map tooltips](#sec-02) |
| SEC-03 | High | [OAuth state is not bound to the browser that initiated login](#sec-03) |
| SEC-04 | High | [Idempotent replay discloses sensitive responses across users and bypasses current authorization](#sec-04) |
| SEC-05 | High | [Removing a member leaves temporary grants effective in existing sessions](#sec-05) |
| SEC-06 | High | [Subtree role managers can grant themselves organization-wide authority](#sec-06) |
| SEC-07 | High | [Ingest connector URLs permit SSRF and credential forwarding](#sec-07) |
| SEC-08 | High | [Webhook SSRF validation is bypassed at delivery time](#sec-08) |
| SEC-09 | High | [Privileged organization and audit exports become readable as ordinary files](#sec-09) |
| SEC-10 | High | [Ingest drops subtree scope before preview and commit](#sec-10) |
| SEC-11 | Medium | [Public contact redemption strips the Secure cookie attribute](#sec-11) |
| SEC-12 | Medium | [Public site server function can traverse into other upstream API GET routes](#sec-12) |
| SEC-13 | Medium | [Public cache headers do not match the tenant and session-dependent response](#sec-13) |
| SEC-14 | Medium | [Anonymous signup pre-creates users with an unearned verified-email assertion](#sec-14) |
| SEC-15 | Medium | [Local sessions can renew indefinitely and do not follow provider-only revocation](#sec-15) |
| SEC-16 | Source-remediated | [Legal-hold trash deletion was corrected in concurrent working-tree changes](#sec-16) |
| SEC-17 | Medium | [Cloud uploads exceed declared size and abandoned objects persist](#sec-17) |
| SEC-18 | Medium | [Outbound bodies, bulk inputs and asynchronous work lack complete resource budgets](#sec-18) |
| SEC-19 | Medium | [Host and forwarded-header trust can be enabled without constraining the peer](#sec-19) |
| SEC-20 | Medium | [Production requires a persistent keyring but does not require key encryption](#sec-20) |
| SEC-21 | Medium | [Production configuration accepts plaintext credential-bearing provider transports](#sec-21) |
| SEC-22 | Medium | [Local test databases are published on all interfaces with predictable credentials](#sec-22) |
| SEC-23 | Medium | [Generic production instructions give runtime processes owner credentials](#sec-23) |
| SEC-24 | Medium | [Kubernetes compatibility manifests lack network isolation](#sec-24) |
| SEC-25 | Medium | [CI actions are mutable and job token permissions are implicit](#sec-25) |
| SEC-26 | Low | [Non-production storage tickets allow operation confusion and overwrites after scanning](#sec-26) |
| SEC-27 | Low | [Basemap key guidance misleadingly implies the key stays secret](#sec-27) |
| SEC-28 | Low | [Kubernetes workloads omit explicit seccomp and a read-only root filesystem](#sec-28) |
| SEC-29 | Low | [Local runtime servicing and artifact vulnerability evidence are incomplete](#sec-29) |


## Detailed findings


## High severity


<a id="sec-01"></a>

### SEC-01 — Guest requests can read and change tenant settings and read the internal hierarchy

**Confidence: High; reproduced against the local API with a real PostgreSQL database and RLS enabled. CWE-862.**

**Evidence:** [src/Modules/LocIntel.Modules.Tenancy/Organizations/Endpoints.cs:22](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Tenancy/Organizations/Endpoints.cs:22) exposes the settings list, `:32` exposes individual settings, and `:43-65` writes arbitrary setting keys/values. None of these handlers checks a principal, capability, or scope. [src/Modules/LocIntel.Modules.Tenancy/Hierarchy/Endpoints.cs:128-144](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Tenancy/Hierarchy/Endpoints.cs:128) likewise returns the entire hierarchy without authorization. [src/LocIntel.Api/GuestOrgMiddleware.cs:21-33](/Users/jarod/coding/location-saas/src/LocIntel.Api/GuestOrgMiddleware.cs:21) resolves a tenant from the supplied host; [src/LocIntel.Api/RequestPrincipal.cs:104-113](/Users/jarod/coding/location-saas/src/LocIntel.Api/RequestPrincipal.cs:104) makes that guest organization available to the database context.

**Impact and prerequisites:** An unauthenticated requester who can reach these API routes and select a known organization slug can enumerate internal settings and hierarchy names/paths, change branding/configuration, and create arbitrary setting rows. The local proof used a synthetic host and setting and received HTTP 200 for guest writes and reads. RLS limits each request to its selected organization; it does not authorize that guest to administer it. A requester can repeat this for other discoverable slugs. A restricted gateway route might reduce direct exposure, but authenticated users without privileges also reach these handlers, and finding SEC-12 exposes additional GET paths through public SSR.

The `map.basemaps` setting is especially important: its dedicated endpoint requires `org:manage`, HTTPS templates, entry limits and encrypted keys ([Organizations/MapBasemapsEndpoints.cs:137-210](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Tenancy/Organizations/MapBasemapsEndpoints.cs:137)), yet the generic settings write bypasses that validation. Existing encrypted values are not plaintext secrets, but reading and replacing this raw configuration bypasses the intended ownership boundary.

**Recommended resolution:** Remove these original reference settings endpoints if they are no longer needed, or enforce explicit user/capability checks and a validated key allowlist. Reserve structured settings for their dedicated validated commands. Gate hierarchy reads and return only the permitted subtree plus the minimum ancestor information needed for navigation. Add a route-authorization coverage check so a conventionally discovered handler cannot silently become public.

**Verification:** Guest and role-free requests must receive an appropriate denial; authorized settings updates must still work. Test direct Host and forwarded-host paths, same-org and cross-org requests, raw `map.basemaps` changes, subtree hierarchy reads, and public SSR proxy requests. Retain the existing RLS tests alongside these authorization tests.


<a id="sec-02"></a>

### SEC-02 — Stored site names execute HTML in public map tooltips

**Severity: High. Confidence: High; executable sink reproduced locally. CWE-79.**

**Evidence:** [web/apps/public/src/SiteMap.tsx:42](/Users/jarod/coding/location-saas/web/apps/public/src/SiteMap.tsx:42) calls `marker.bindTooltip(site.name)`. [src/Modules/LocIntel.Modules.Tenancy/Sites/SiteEndpoints.cs:86](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Tenancy/Sites/SiteEndpoints.cs:86) writes the submitted name on creation, and lines 162–163 write it on update. Neither request record restricts HTML syntax. [src/Modules/LocIntel.Modules.Tenancy/Sites/PublicEndpoints.cs:172-183](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Tenancy/Sites/PublicEndpoints.cs:172) retrieves public sites and returns their names to the locator. The installed Leaflet 1.9.4 implementation at `web/node_modules/.pnpm/leaflet@1.9.4/node_modules/leaflet/src/layer/DivOverlay.js:279-280` assigns string content to `node.innerHTML`.

**Abuse and impact:** An account or integration with `sites:manage` over a public site can store an HTML name with an event handler and non-null coordinates. When a visitor opens its tooltip, Leaflet interprets the name as executable markup under the public tenant origin. This permits page manipulation, phishing, reading same-origin accessible data, and requests carrying that visitor's session. HttpOnly protects direct cookie reading, but does not prevent authenticated requests from an XSS payload. Public and console host isolation limits direct access to the console cookie; do not claim cross-origin console takeover without an additional weakness. A suitably strict frontend CSP could block the illustrated inline handler, but deployed frontend headers were not available for verification.

**Resolution:** Pass a DOM element whose `textContent` is the site name to `bindTooltip`. No HTML sanitizer dependency is needed because site names are plain text. Review every third-party map/tooltip HTML sink when adding new displays. Deploy an appropriate frontend CSP as defense in depth, taking the intentional `/embed` route into account.

**Verification:** `node /private/tmp/locintel-frontend-proof.cjs` passes using the installed Leaflet and JSDOM packages. It verifies that the current string form creates an `<img>` whose synthetic error event executes a harmless marker, and that a `textContent` element does not create an image. This is a library-sink check with synthetic data, not a full application browser test. Add one browser regression that saves a literal HTML-looking site name, opens the public tooltip, and asserts text display with no handler execution.

**Primary reference:** Leaflet explicitly describes string tooltip content as HTML and recommends a safe `HTMLElement` for untrusted text: [Leaflet tooltip API](https://leafletjs.com/reference.html#tooltip).


<a id="sec-03"></a>

### SEC-03 — OAuth state is not bound to the browser that initiated login

**Confidence: High; cross-browser callback acceptance reproduced locally. CWE-352.**

**Evidence:** [src/Modules/LocIntel.Modules.Identity/Auth/Endpoints.cs:63-66](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Identity/Auth/Endpoints.cs:63) encrypts a timestamp and return path into state. The callback at `:124-137` checks decryption and age, then exchanges the code; `:196-209` creates a session and signs in the current browser. No browser correlation cookie, one-time state record, or initiating-session comparison exists. [src/Integrations/LocIntel.Integrations.WorkOS/WorkOSAuthProvider.cs:62-87](/Users/jarod/coding/location-saas/src/Integrations/LocIntel.Integrations.WorkOS/WorkOSAuthProvider.cs:62) sends state and exchanges a code without a per-browser PKCE verifier.

**Impact and prerequisites:** An attacker starts a real authorization flow for their own account and supplies its unredeemed callback URL to a victim within the validity window. The victim is signed into the attacker's account. Subsequent uploads, entered information, or business actions can then belong to an account the attacker controls. The attacker needs a valid authorization result; encryption prevents state forgery but does not make genuine attacker-generated state belong to the victim. This is login CSRF/account swapping, not proof of directly stealing the victim's existing account.

The disposable test initiated login in one client and redeemed the returned callback in a fresh client; `/me` identified the initiator's account. The local provider replaces the external identity service in this test, while the vulnerable state/callback/session handlers are the actual application code. A complete WorkOS browser attack was not performed.

**Recommended resolution:** Use the framework's OIDC/OAuth correlation flow, or bind a cryptographically random, expiring, one-time state nonce to an HttpOnly initiating-browser cookie and validate it before code exchange. Add PKCE where supported and keep the verifier specific to that browser transaction. Consume/clear correlation state on success and failure.

**Verification:** Same-browser login succeeds; a callback copied to another client fails before sign-in; missing, expired and replayed state fail; concurrent legitimate login tabs behave deliberately. [OAuth Security BCP, RFC 9700](https://www.rfc-editor.org/rfc/rfc9700.html#section-2.1) requires CSRF protection bound to the client transaction/user agent.


<a id="sec-04"></a>

### SEC-04 — Idempotent replay discloses sensitive responses across users and bypasses current authorization

**Confidence: High; another user's API-key creation response was replayed by a role-free member in the local test. CWE-863.**

**Evidence:** [src/LocIntel.Api/IdempotencyMiddleware.cs:34-46](/Users/jarod/coding/location-saas/src/LocIntel.Api/IdempotencyMiddleware.cs:34) keys requests by active organization and caller-supplied key, with no actor or session ownership. `:67-72` returns the stored response without invoking the endpoint. This middleware runs before endpoint gates ([src/LocIntel.Api/HttpPolicyHosting.cs:146-152](/Users/jarod/coding/location-saas/src/LocIntel.Api/HttpPolicyHosting.cs:146)). [IdempotencyMiddleware.cs:101-105](/Users/jarod/coding/location-saas/src/LocIntel.Api/IdempotencyMiddleware.cs:101) stores response bodies, including one-time API-key or webhook-secret responses. [src/LocIntel.Platform/Infra/PlatformDbContext.cs:50-60](/Users/jarod/coding/location-saas/src/LocIntel.Platform/Infra/PlatformDbContext.cs:50) confirms the organization/key primary key.

**Impact and prerequisites:** A same-org authenticated user who knows the original idempotency key and identical method/path/body can receive another user's cached privileged response without the source permission. A user whose role was removed can also replay their own earlier request without satisfying the current endpoint policy. The proof minted a synthetic key as an owner, then replayed the request as a member with no role and received the identical secret-containing response. No secret value was included in the report.

This does not cross RLS organization boundaries, work anonymously, or execute the original mutation again. Random, undisclosed keys are not feasibly guessed. They nevertheless should not be treated as additional bearer credentials. The optional session-context header and CSRF check precede replay but do not establish ownership of the cached response. Stored responses also remain replayable until cleanup runs, because expiry is not checked on lookup.

**Recommended resolution:** Bind records to the authenticated actor and appropriate session/credential identity, preserve organization isolation, and check current authorization before releasing cached data. Treat sensitive one-time secret responses specially: avoid storing them in the general replay body store, or protect and restrict them under an explicit delivery policy. Enforce expiry at read time. Include relevant query and request semantics in the fingerprint; the current hash only includes method, path and body (`:108-118`).

**Verification:** Two same-org users cannot share replay records; permission/membership removal blocks privileged replay; another org cannot replay; expired records fail; ordinary authorized retries remain idempotent. Cover API-key creation, webhook-secret creation and privileged exports. Separately verify API-key clients: the current middleware skips Service principals, so the documented universal idempotency contract does not cover them.


<a id="sec-05"></a>

### SEC-05 — Removing a member leaves temporary grants effective in existing sessions

**Confidence: High; reproduced after deleting the member through the actual API. CWE-863.**

**Evidence:** [src/Modules/LocIntel.Modules.Identity/Access/GrantScopeResolver.cs:65-89](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Identity/Access/GrantScopeResolver.cs:65) joins role assignments to membership, but evaluates `GrantExceptions` independently of membership. `:91-95` merges any surviving exception into the effective scope. [src/Modules/LocIntel.Modules.Identity/Users/MemberEndpoints.cs:326-329](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Identity/Users/MemberEndpoints.cs:326) removes membership/role assignments without deleting exceptions or revoking that user's sessions. Voluntary leave has the same deletion pattern at `:78-80`. Session validation only checks the session row's revocation state ([src/LocIntel.Api/SessionValidationMiddleware.cs:24-31](/Users/jarod/coding/location-saas/src/LocIntel.Api/SessionValidationMiddleware.cs:24)).

**Impact and prerequisites:** A user with an unexpired exception can keep accessing its capability after organizational removal through a previously issued cookie. A broad `roles:manage` or wildcard exception preserves especially serious powers. The proof granted a synthetic exception, removed the member, and still obtained a successful privileged roles response. Voluntary leave reissues the current browser's cookie, but other sessions can retain the old active organization.

SCIM removal deletes sessions and therefore immediately blocks old cookies ([Users/DirectoryUserSyncedHandler.cs:43-58](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Identity/Users/DirectoryUserSyncedHandler.cs:43)); account deletion also purges exceptions. Those protections do not fix ordinary member removal or the resolver's lack of a membership prerequisite.

**Recommended resolution:** Require a current membership before applying any ordinary user role or exception grant. Delete exceptions in every membership removal/leave path in the same transaction, and define how applicable sessions and impersonation are revoked. Handle legitimate platform support through its explicit, independently validated path. Clean up orphaned existing exceptions.

**Verification:** Test removal and leave with role grants, scoped exceptions and wildcard exceptions, including a second browser holding an old cookie. Repeat for directory removal and rejoining; old exceptions must not silently reactivate. Verify other organizations' valid memberships remain usable.


<a id="sec-06"></a>

### SEC-06 — Subtree role managers can grant themselves organization-wide authority

**Confidence: High; self-grant from a subtree exception to organization-wide wildcard reproduced locally. CWE-269 / CWE-863.**

**Evidence:** [src/Modules/LocIntel.Modules.Identity/Access/Endpoints.cs:110-129](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Identity/Access/Endpoints.cs:110) accepts any non-empty `roles:manage` scope, discards the allowed scope, and writes caller-supplied `ScopePath` on an assignment. The exception endpoint at `:146-166` likewise accepts any such manager and permits self-targeted `Domain="*"`, `Action="*"`, `ScopePath=null`. [GrantScopeResolver.cs:91-95](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Identity/Access/GrantScopeResolver.cs:91) makes a null scope organization-wide. Shared role edits at [Access/RoleManagementEndpoints.cs:42-75](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Identity/Access/RoleManagementEndpoints.cs:42) also lack a delegation boundary.

**Impact and prerequisites:** An owner delegates role management over one subtree; that delegate can issue an org-wide wildcard exception to themselves or assign Owner without a scope. The local proof began with scoped `roles:manage`, self-granted a wildcard exception, then successfully read owner-level billing information. This expands from delegated subtree authority to the whole tenant. It does not make a normal tenant a platform operator: [Access/OperatorContext.cs:19-21](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Identity/Access/OperatorContext.cs:19) additionally requires the platform organization.

Organization-wide role managers may intentionally be allowed to grant arbitrary roles. The defect is accepting subtree-limited management and silently treating it as organization-wide authority.

**Recommended resolution:** The smallest safe policy is to require `NodeScope.EntireOrg` for role, assignment, invitation and exception administration, and reject unsupported scoped administrative grants. If delegated administrators are required, enforce capability and scope ceilings consistently across assignment, exception issuance and shared-role mutation. Apply the same whole-org requirement to other org-level custody operations, such as minting unrestricted API keys, when their capability is scoped.

**Verification:** Subtree managers cannot self-grant null/wider scopes, wildcard exceptions, unrestricted API keys, or edit a role shared outside their delegation. Existing organization-wide administrator workflows remain functional.


<a id="sec-07"></a>

### SEC-07 — Ingest connector URLs permit SSRF and credential forwarding

**Confidence:** High; direct source-to-sink path. **Precondition:** authenticated user with `ingest:manage` (Owner/Admin by default), or compromise of that account. Impact depends on the worker's reachable network.

**Evidence:**

- [src/Modules/LocIntel.Modules.Ingest/Endpoints.cs:353-367](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Ingest/Endpoints.cs:353) saves `request.Url` without scheme/host/IP checks. Update only checks nonblank values and replaces the URL at `:430-447`.
- [src/Modules/LocIntel.Modules.Ingest/ConnectorSync.cs:45-62](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Ingest/ConnectorSync.cs:45) decrypts the credential, constructs a GET to the persisted URL, adds `X-Api-Key`, and executes it.
- [src/Modules/LocIntel.Modules.Ingest/IngestModule.cs:23](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Ingest/IngestModule.cs:23) registers `AddHttpClient("ingest-connector")` without a constrained handler.

**Description / abuse:** A tenant administrator can make the shared worker perform HTTP GETs against loopback, private services, link-local services and arbitrary ports. JSON deserialization happens *after* the outbound request, so an invalid response does not prevent the SSRF. An internal endpoint whose output resembles the expected site array can also have its values exposed through batch previews. Arbitrary response exfiltration is not proven: the parser only accepts its known data shape. HTTP is permitted, so a configured API key can travel without TLS. Redirects can forward the custom API key to another host; changing the connector URL while omitting `ApiKey` also sends the existing stored key to the new destination.

**Resolution:** Require an explicit supported HTTPS URL shape, no user-info or fragment, approved port, and public address classification. Validate the actual address at connection time and connect to that address rather than resolving it again. Reject redirects or revalidate every hop and never forward credentials across origins. For intentionally private enterprise integrations, use an explicitly provisioned connection route/allowlist rather than giving every tenant the shared worker's internal reachability. Apply network egress restrictions as additional containment. Reuse the concrete connection-time validation approach already implemented in `Reporting/Rendering/ReportBasemaps.cs:57-85`; do not merely copy the webhook registration-time check.

**Regression checks:** Production-configured create/update rejects HTTP, loopback, private IPv4/IPv6, mapped IPv4 and non-approved ports. A DNS answer that changes between admission and connection cannot reach a private test listener. A safe external test endpoint returning a cross-origin redirect must not deliver `X-Api-Key` to the target. Use controlled listeners, never a real metadata endpoint.

**Primary reference:** .NET documents automatic redirects as enabled by default and states that custom headers remain on redirects: [HttpClientHandler.AllowAutoRedirect](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclienthandler.allowautoredirect?view=net-10.0).


<a id="sec-08"></a>

### SEC-08 — Webhook SSRF validation is bypassed at delivery time

**Confidence:** High. **Precondition:** `org:manage`, and a reachable internal HTTPS endpoint with a certificate accepted by the worker, or a DNS-controlled hostname with suitable trusted TLS configuration.

**Evidence:**

- [src/Modules/LocIntel.Modules.Audit/WebhookEndpoints2.cs:120-145](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Audit/WebhookEndpoints2.cs:120) checks DNS only at registration. The comment explicitly acknowledges the rebinding gap.
- [src/Modules/LocIntel.Modules.Audit/WebhookDeliveryPipeline.cs:90-99](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Audit/WebhookDeliveryPipeline.cs:90) creates the default `webhook-delivery` client and sends to the stored URL. No registration for a hardened client was found.
- [src/Modules/LocIntel.Modules.Audit/WebhookEndpoints2.cs:307-329](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Audit/WebhookEndpoints2.cs:307) does not normalize IPv4-mapped IPv6 and omits several non-public IPv4 ranges (for example shared-address space).

**Description / abuse:** Register an allowed public HTTPS endpoint; later change its DNS to an internally reachable address, or return an HTTPS redirect to an internal hostname. Delivery uses fresh default DNS/redirect behavior without applying the earlier policy. A 307/308 redirect can carry the signed tenant event and POST body to that target. Status codes are visible through the delivery-history endpoint. This is not a claim that .NET will redirect HTTPS to plaintext cloud metadata: modern .NET rejects HTTPS-to-HTTP redirects and validates TLS certificates, which restricts the available targets.

**Resolution:** Use connection-time public-address validation with a complete normalized IP policy and disable redirects. Revalidate persisted URLs, not just new registrations. Prohibit unexpected ports/user-info and bound outbound connection lifetimes. Reuse the existing report basemap transport's approach and place the common policy somewhere both modules can consume without introducing a module dependency cycle. Network egress controls should also deny internal destinations.

**Regression checks:** Validate rejected mapped/private/shared-address-space addresses; public registration followed by private DNS at delivery fails; redirects are not followed; a public endpoint still receives a correctly signed message.

**Primary reference:** [HttpClientHandler.AllowAutoRedirect](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclienthandler.allowautoredirect?view=net-10.0) (redirect behavior and TLS downgrade limitation).


<a id="sec-09"></a>

### SEC-09 — Privileged organization and audit exports become readable as ordinary files

**Confidence:** High; complete producer-to-download authorization trace. **Precondition:** someone legitimately creates an export in an active organization, and an attacker has that organization's `files:read` grant (the default Viewer role qualifies).

**Evidence:**

- Organization export is restricted to `org:manage` at [src/Modules/LocIntel.Modules.Tenancy/Organizations/Offboarding.cs:160-175](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Tenancy/Organizations/Offboarding.cs:160); audit export requires `audit:read` at [src/Modules/LocIntel.Modules.Audit/ExportEndpoint.cs:27-37](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Audit/ExportEndpoint.cs:27).
- [src/Modules/LocIntel.Modules.Storage/Offboarding.cs:81-95](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Storage/Offboarding.cs:81) and [src/Modules/LocIntel.Modules.Storage/ExportAuditTrailHandler.cs:72-85](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Storage/ExportAuditTrailHandler.cs:72) create Clean `FileObject` records without an origin policy or site scope.
- [src/Modules/LocIntel.Modules.Storage/Data/FileObject.cs:28-30](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Storage/Data/FileObject.cs:28) defaults to `SiteIds=[]` and `Origin=null`.
- [src/Modules/LocIntel.Modules.Storage/FileAccess.cs:21-45](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Storage/FileAccess.cs:21) permits these files whenever the caller has the generic file capability. `Endpoints.cs:173-178` lists them and `:199-208` signs their download URL after that same check.
- Default Viewer grants include FilesRead at [src/Modules/LocIntel.Modules.Identity/Access/RolePresets.cs:71-77](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Identity/Access/RolePresets.cs:71).

**Description / abuse:** An ordinary file viewer can discover and download an organization export containing all sites, configuration, membership email addresses/roles and other module data, or an audit export with row changes, events, decisions and access paths. The export's original authorization is discarded when it enters Storage. This is an intra-tenant authorization bypass; RLS still prevents access from a different organization. Previously authorized exports can also remain readable after the user's source privilege is removed.

**Resolution:** Give exports explicit origin/type metadata and enforce their original source permission and scope at list/read/download/manage time. The existing `IFileOriginAccess` and `ReportFileAccess` pattern provides a local model. Decide whether exports are requester-only or readable by everyone currently authorized for the exported data, and enforce it consistently. Backfill old export records; changing only future exports leaves already-generated archives exposed. Pending asynchronous export work should revalidate that its requester is still permitted before generating privileged output.

**Regression checks:** Owner generates both export types; Viewer cannot see metadata, download, delete or restore them. Appropriate source-authorized principal can still retrieve them. Remove source permission after export and confirm access is denied; check cross-org, subtree-limited and old/backfilled exports.


<a id="sec-10"></a>

### SEC-10 — Ingest drops subtree scope before preview and commit

**Confidence:** High. **Precondition:** a role containing `ingest:manage` assigned at subtree scope (custom roles and subtree role assignments are supported), or a temporary scoped grant. Default org-wide Admin use does not expose this particular discrepancy.

**Evidence:**

- [src/Modules/LocIntel.Modules.Ingest/Endpoints.cs:95-100](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Ingest/Endpoints.cs:95) and `:264-281` take the gate's principal/org but discard `GateOutcome.Allowed.Scope`.
- [src/Modules/LocIntel.Modules.Ingest/StagingService.cs:31-45](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Ingest/StagingService.cs:31) resolves all matching sites and every hierarchy node through a contract with no scope argument; `:110-118` puts old names/timezones into the preview.
- [src/Modules/LocIntel.Modules.Tenancy/Sites/IngestSupport.cs:28-37](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Tenancy/Sites/IngestSupport.cs:28) looks up by external ID under organization isolation only; `:46-48` returns all hierarchy nodes.
- [src/Modules/LocIntel.Modules.Ingest/Endpoints.cs:313-324](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Ingest/Endpoints.cs:313) publishes all actionable rows without principal/scope information. [src/Modules/LocIntel.Modules.Tenancy/Sites/IngestSupport.cs:88](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Tenancy/Sites/IngestSupport.cs:88) selects any matching site, and `:145-160` updates/closes it without checking the initiating user's scope.

**Description / abuse:** A user with ingest privileges for region A can submit an external ID for region B, observe old values in the preview, and commit an update or closure for B. Creates can target any resolvable node. The commit endpoint can also operate on another same-org user's staged batch because it checks only organization and grant. GrantScopeResolver correctly produces the restricted scope; the import flow drops it.

**Resolution:** Either explicitly require an EntireOrg grant for this administrative bulk operation, or thread scope through staging, previews, batch ownership/access and commit. For creates, check the destination anchor; for updates/closes, check the actual current site's scope. Do not trust the CSV's claimed node to authorize an existing site. Preserve an authorized command intent or revalidate the initiating user when delayed work executes. Ensure site-change commands cannot become a bypass around direct site-update policy.

**Regression checks:** Give a user a subtree-scoped ingest role; mixed A/B input must not reveal or modify B. Include a CSV claiming an allowed node but naming an out-of-scope existing external ID, another user's batch, and permission revocation between stage and commit. Confirm same-org allowed imports and cross-org isolation remain correct.


## Medium severity


<a id="sec-11"></a>

### SEC-11 — Public contact redemption strips the Secure cookie attribute

**Severity: Medium. Confidence: High. CWE-614.**

**Evidence:** [web/apps/public/src/routes/contact.redeem.tsx:29-37](/Users/jarod/coding/location-saas/web/apps/public/src/routes/contact.redeem.tsx:29) discards all upstream cookie attributes with `raw.split(';')[0]`, then recreates each cookie using only `path: '/'`, `httpOnly: true`, and `sameSite: 'lax'`. This drops `Secure`. The API deliberately enforces `CookieSecurePolicy.Always` in production at [src/LocIntel.Api/AuthenticationHosting.cs:96-107](/Users/jarod/coding/location-saas/src/LocIntel.Api/AuthenticationHosting.cs:96). TanStack's `setCookie` simply forwards these options to its cookie implementation; it does not restore the dropped security attribute.

**Abuse and impact:** A public visitor can receive a non-Secure authentication cookie even when redemption happened through HTTPS. If the browser subsequently makes an HTTP request to that host, an attacker on the network can observe or replace the cookie before an HTTP-to-HTTPS redirect completes. Existing enforced browser HTTPS policies may reduce exploitability; they do not repair the relay bug. The upstream cookie remains HttpOnly, and no claim is made that stripping attributes defeats the server-side session lifetime.

**Resolution:** Preserve the allowed upstream session cookie's security attributes when relaying it onto the public host, or set an explicit production `secure: true` floor that matches the API. Preserve applicable lifetime/chunk metadata as well; intentionally use host-only cookies. Maintain an explicit local HTTP development exception.

**Verification:** Add a production-mode redemption test with an upstream `Set-Cookie` containing `Secure; HttpOnly; SameSite=Lax; Path=/`; assert the public response retains these attributes. In a browser cookie-jar test, assert it is not sent over HTTP and that local HTTP development still works when explicitly configured.

**Primary reference:** [MDN Set-Cookie Secure attribute](https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Set-Cookie#secure).


<a id="sec-12"></a>

### SEC-12 — Public site server function can traverse into other upstream API GET routes

**Severity: Medium on its own; amplifies the high-severity missing backend authorization findings. Confidence: High for routing escape, source-confirmed backend reachability; no live API call performed.**

**Evidence:** [web/apps/public/src/routes/sites.$siteId.tsx:6-8](/Users/jarod/coding/location-saas/web/apps/public/src/routes/sites.$siteId.tsx:6) accepts client-controlled server-function data with a validator that returns it unchanged, then interpolates it directly into `/public/sites/${data}`. [web/apps/public/src/api.ts:67-77](/Users/jarod/coding/location-saas/web/apps/public/src/api.ts:67) forwards Host and Cookie and returns any successful JSON response without response-shape validation. [web/apps/public/src/upstream.ts:12-16](/Users/jarod/coding/location-saas/web/apps/public/src/upstream.ts:12) constructs a WHATWG URL, which normalizes dot segments before issuing the request. `../../api/hierarchy` therefore becomes `/api/hierarchy`; `%2e%2e/%2e%2e/api/settings` becomes `/api/settings`.

**Abuse and impact:** An unauthenticated visitor able to call the public server function can use it as a same-upstream GET proxy instead of a site-detail reader, including reaching internal API paths that an edge might expose only to the console. API authorization still applies: this does not change the upstream origin, execute arbitrary HTTP methods, or inherently bypass a correctly implemented authorization gate. However, the hierarchy GET at [src/Modules/LocIntel.Modules.Tenancy/Hierarchy/Endpoints.cs:128-142](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Tenancy/Hierarchy/Endpoints.cs:128) has no grant/scope check and returns internal node names/paths for the host-derived tenant; SEC-01 also confirms unguarded settings endpoints. The traversal makes those endpoint defects reachable through the public SSR origin, even if direct API routes were later hidden at an edge.

**Resolution:** Validate the server-function input as the expected UUID at the server boundary and encode each URL path segment. Keep backend authorization repairs as separate required work; blocking this proxy route alone does not fix direct API access. Prefer returning an explicit public DTO rather than treating arbitrary JSON as `PublicSiteDetail`.

**Verification:** The local proof asserts actual Node URL normalization for plain and encoded dot-segment payloads. Add one server-function test that rejects malformed/non-UUID IDs before any upstream request and verifies that valid IDs only request `/public/sites/<id>`. Include `/api/hierarchy`, `/api/settings`, encoded slashes, query/hash delimiters, and dot segments. Confirm backend guest denial independently.


<a id="sec-13"></a>

### SEC-13 — Public cache headers do not match the tenant and session-dependent response

**Confidence: High for response behavior, conditional for shared-cache exploitation. CWE-524.**

**Evidence:** [src/LocIntel.Api/PublicCacheMiddleware.cs:17-29](/Users/jarod/coding/location-saas/src/LocIntel.Api/PublicCacheMiddleware.cs:17) marks every successful `/public` GET `public, max-age=60, stale-while-revalidate=300`. [src/Modules/LocIntel.Modules.Tenancy/Sites/PublicEndpoints.cs:84-104](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Tenancy/Sites/PublicEndpoints.cs:84) chooses organization identity from guest host, contact cookie or active user organization. [src/LocIntel.Api/GuestOrgMiddleware.cs:21-33](/Users/jarod/coding/location-saas/src/LocIntel.Api/GuestOrgMiddleware.cs:21) can use `X-Forwarded-Host`, and [web/apps/public/src/api.ts:67-77](/Users/jarod/coding/location-saas/web/apps/public/src/api.ts:67) forwards host and cookies. No corresponding Vary or authorization-aware cache policy is supplied.

**Impact and prerequisites:** A shared proxy/CDN that honors these headers and keys by the visible URL can reuse one organization's response for another session/forwarded host. A synthetic test confirmed that two users requested the same `/public/org` URL, got different tenant bodies, and both responses declared public caching without Vary. No live CDN was available, so an actual cached cross-tenant response was not demonstrated. Most public DTOs are intentionally public-safe; the demonstrated concern is tenant response confusion and cache poisoning, with confidentiality impact dependent on future/contact-specific data and edge policy.

**Recommended resolution:** Cache only a canonical, unauthenticated, host-bound public projection. Bypass shared caching for authenticated/contact-dependent responses, or use an explicitly correct tenant-aware cache key and safe cookie handling at the edge. Normalize/validate the tenant host before cache lookup. Avoid indiscriminately adding `Vary: Cookie` without considering cache explosion and all forwarded-host inputs.

**Verification:** Put the actual caching proxy in the test path. Alternate organizations, cookies, forwarded hosts and no-cookie requests for the same URL and prove no response substitution. Confirm cached responses never distribute another visitor's Set-Cookie header. Verify the chosen policy for the intended `/embed` and sitemap routes.


<a id="sec-14"></a>

### SEC-14 — Anonymous signup pre-creates users with an unearned verified-email assertion

**Confidence: High for the source behavior; downstream account takeover was not demonstrated. CWE-287.**

**Evidence:** [src/Modules/LocIntel.Modules.Identity/Auth/Endpoints.cs:80-94](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Identity/Auth/Endpoints.cs:80) accepts an email on unauthenticated GET and invokes provider provisioning. [src/Integrations/LocIntel.Integrations.WorkOS/WorkOSAuthProvider.cs:105-112](/Users/jarod/coding/location-saas/src/Integrations/LocIntel.Integrations.WorkOS/WorkOSAuthProvider.cs:105) creates the user with `EmailVerified = true`, without mailbox proof. The same helper is also used for trusted directory provisioning, which has a different trust boundary.

**Impact and prerequisites:** Anyone can cause a new arbitrary address to be represented as verified at WorkOS. A GET/prefetch also creates identity-provider state. This undermines the meaning of verified email and can interact with verification-dependent enrollment or federation policies. The endpoint does not itself set a password, mint a local session, or bypass all AuthKit authentication; existing-user conflict handling does not change an existing user's verification state. Immediate account takeover should not be inferred from this finding alone.

**Recommended resolution:** Let hosted AuthKit perform public signup and email verification. Keep administrative/directory provisioning behind a trusted, explicit path; never set verified-email state from unauthenticated form/query input. If public precreation is necessary, use an unverified account and complete the provider's verification process. Keep mutations off GET and bound signup/provider-resource abuse.

**Verification:** Public signup cannot mark an unowned mailbox verified. Test the provider sandbox's verification-required login flow, existing users, trusted directory provisioning and duplicate requests. [WorkOS User API](https://workos.com/docs/reference/authkit/user) provides the user-verification operations and fields.


<a id="sec-15"></a>

### SEC-15 — Local sessions can renew indefinitely and do not follow provider-only revocation

**Confidence: High for lifecycle gaps; no stolen real session was used. CWE-613.**

**Evidence:** [src/LocIntel.Api/AuthenticationHosting.cs:106-107](/Users/jarod/coding/location-saas/src/LocIntel.Api/AuthenticationHosting.cs:106) enables sliding 12-hour tickets. [src/LocIntel.Api/SessionValidationMiddleware.cs:24-31](/Users/jarod/coding/location-saas/src/LocIntel.Api/SessionValidationMiddleware.cs:24) checks only that the local session exists and is not revoked. [src/Modules/LocIntel.Modules.Identity/Users/UserSession.cs:13-20](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Identity/Users/UserSession.cs:13) has creation/revocation timestamps but no absolute expiry or provider session identity. WorkOS code exchange returns user/org identity but discards provider session identity ([WorkOSAuthProvider.cs:87-103](/Users/jarod/coding/location-saas/src/Integrations/LocIntel.Integrations.WorkOS/WorkOSAuthProvider.cs:87)); inbound event handling accepts only directory events (`:177-178`).

**Impact and prerequisites:** An attacker who already acquires a valid cookie can keep it active through renewal until an explicit local revocation occurs. Resetting a password or revoking only the WorkOS session cannot reliably terminate the separate local ticket because there is no mapping or event-driven invalidation for that action. A 12-hour sliding interval is not a 12-hour maximum session age.

Existing local logout, revoke-others and SCIM user removal do invalidate local sessions. The finding is the missing absolute/re-authentication boundary and inconsistent provider-side containment, not the absence of session revocation. Operator impersonation also relies on its local session and expiry claim; permission-only removal should be tested while impersonation is active.

**Recommended resolution:** Enforce a server-side absolute session age using the stored creation time, with shorter/re-authenticated requirements for high-impact account/operator actions. Carry provider session/user security-state identifiers as needed and process verified revocation/password-security events, or explicitly revalidate at a bounded interval. Define and document the relationship between local and provider logout/reset actions.

**Verification:** Advancing time beyond the absolute lifetime denies even frequently renewed cookies. Provider-only revocation/reset invalidates mapped sessions within a documented bound; local logout and SCIM still work. Verify an operator stripped of authority cannot retain support access beyond the chosen revocation guarantee. [ASP.NET Core cookie authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-10.0) documents ticket renewal and absolute-expiration controls.


<a id="sec-17"></a>

### SEC-17 — Cloud uploads exceed declared size and abandoned objects persist

**Confidence:** High for missing controls; actual storage cost depends on provider quotas/lifecycle policy. **Precondition:** `files:manage` and a cloud storage deployment without compensating bucket lifecycle/quota controls.

**Evidence:**

- [src/Modules/LocIntel.Modules.Storage/Endpoints.cs:58-80](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Storage/Endpoints.cs:58) validates the *declared* size against 100 MiB.
- [src/Integrations/LocIntel.Integrations.AmazonS3/S3ObjectStore.cs:49-78](/Users/jarod/coding/location-saas/src/Integrations/LocIntel.Integrations.AmazonS3/S3ObjectStore.cs:49) accepts but never uses `maxBytes` in the signed request.
- [src/Integrations/LocIntel.Integrations.AzureBlob/AzureBlobObjectStore.cs:34-57](/Users/jarod/coding/location-saas/src/Integrations/LocIntel.Integrations.AzureBlob/AzureBlobObjectStore.cs:34) also accepts but never uses it in the SAS.
- [src/Modules/LocIntel.Modules.Storage/Endpoints.cs:116-123](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Storage/Endpoints.cs:116) returns 413 after an oversized object has already arrived, without deleting it or expiring the record.
- [src/Modules/LocIntel.Modules.Storage/FileTrash.cs:38-42](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Storage/FileTrash.cs:38) only sweeps `Deleted` objects, not old `PendingUpload` records. No pending-upload garbage collector was found.

**Description / abuse:** Obtain a ticket with a small declared size, upload a much larger object directly to the provider, then omit `/complete` (or let it return 413). Gateway request-size controls do not inspect these direct provider uploads. Repeating this consumes durable storage and cost indefinitely, even though the bytes cannot pass the Clean/download gate. Existing write-once cloud protections are useful but do not impose a byte limit or tenant storage budget.

**Resolution:** Enforce a size condition at the provider boundary where supported; otherwise use a narrowly bounded upload gateway or immediate object-created validation and deletion. Preserve write-once/immutable upload semantics. Add expiry and garbage collection for abandoned/rejected uploads, a durable per-org storage/pending-upload budget, and provider lifecycle cleanup as a backstop. Remove invalid bytes on complete failure and account for tickets that are never completed.

**Regression checks:** The provider adapter must reject an object exceeding its authorized byte budget, or prove bounded cleanup if the provider cannot enforce it synchronously. An abandoned ticket expires and its object disappears. Test upload arrivals near ticket expiration and duplicate completion without deleting legitimate Clean objects.

**Primary references:** [AWS presigned URL security model](https://docs.aws.amazon.com/AmazonS3/latest/userguide/using-presigned-url.html); [Azure service SAS fields and permissions](https://learn.microsoft.com/en-us/rest/api/storageservices/create-service-sas); [Azure Put Blob size limits](https://learn.microsoft.com/en-us/rest/api/storageservices/put-blob). Provider maximum object size is much larger than this application's declared 100 MiB cap.


<a id="sec-18"></a>

### SEC-18 — Outbound bodies, bulk inputs and asynchronous work lack complete resource budgets

**Confidence:** High for the code paths and missing application budgets; quantitative exhaustion not load-tested. **Precondition:** `ingest:manage` or `org:manage`, or compromise/misbehavior of an existing upstream connector/webhook destination.

**Evidence:**

- [src/Modules/LocIntel.Modules.Audit/WebhookDeliveryPipeline.cs:90-99](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Audit/WebhookDeliveryPipeline.cs:90) buffers the complete response although it only needs the status code.
- [src/Modules/LocIntel.Modules.Ingest/ConnectorSync.cs:54-73](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Ingest/ConnectorSync.cs:54) buffers the complete HTTP response, deserializes an unrestricted list, and materializes another list.
- [src/Modules/LocIntel.Modules.Ingest/Endpoints.cs:106-114](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Ingest/Endpoints.cs:106) reads up to an allowed 100 MiB CSV entirely into text, then the parser builds records/dictionaries and source rows; [StagingService.cs:64-126](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Ingest/StagingService.cs:64) tracks and persists every row, including invalid rows, with no row/column/field budget.
- [src/Modules/LocIntel.Modules.Ingest/Endpoints.cs:483-505](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Ingest/Endpoints.cs:483) accepts repeated sync jobs without an outstanding-job deduplication/admission cap; connector count and intervals have no hard bounds. [ConnectorSchedule.cs:46-55](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Ingest/ConnectorSchedule.cs:46) fans out every due connector.
- [src/Modules/LocIntel.Modules.Audit/Handlers.cs:47-60](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Audit/Handlers.cs:47) fans each event out to every matching webhook endpoint; create has no endpoint count cap.

**Description / abuse:** A destination can return a very large or endless body and monopolize worker memory even for a webhook that should only acknowledge receipt. A compact CSV/JSON containing many small records amplifies into many managed objects and staged DB rows; each sync or export job continues after its API request has returned. The gateway's request fairness and the API's local concurrency cap therefore do not bound this background cost. A HttpClient timeout is a time bound, not a safe memory or row budget. An exhausted worker can delay unrelated organizations' durable jobs.

**Resolution:** Use `ResponseHeadersRead`; discard webhook bodies; impose actual stream-byte ceilings on connector responses even when Content-Length is missing or false. Add endpoint/connector count, field length, import row/column, staged-byte and per-org active-job limits. Parse/process bounded batches, deduplicate scheduled/manual syncs, and keep per-process worker concurrency and job deadlines. Validate SyncIntervalHours to a sane positive range so invalid values cannot repeatedly poison a sweep. Similar limits are already explicit in Reporting's `ReportLimits` and `ReportImages`.

**Regression checks:** Oversized chunked responses fail at the configured byte budget, webhook response bodies are not buffered, excessive import rows/field sizes fail before mass staging, repeated sync admission is bounded, and a failing connector cannot enqueue concurrent unlimited copies. Verify bounded memory using a controlled fixture rather than sending a real exhaustion payload to a shared environment.

**Primary reference:** [HttpClient.SendAsync](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclient.sendasync?view=net-10.0) confirms the used overload completes after reading the entire response and documents timeout as OperationCanceledException. The webhook catches only HttpRequestException, so timeout attempts also bypass its normal per-attempt delivery row/retry bookkeeping; handle timeout separately from shutdown cancellation.

**Additional instances of the same resource-boundary problem:**

- **Spatial geometry:** [src/Modules/LocIntel.Modules.Spatial/Overlays/GeoJsonFeatures.cs:37-56](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Spatial/Overlays/GeoJsonFeatures.cs:37) materializes GeoJSON before checking 5,000 features and runs geometric validity checks without a vertex/ring/property budget. [Overlays/OverlayTilesEndpoint.cs:107-119](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Spatial/Overlays/OverlayTilesEndpoint.cs:107) later transforms/intersects stored geometry repeatedly. An HTTP byte cap and feature count do not cap geometric complexity. Enforce per-request and per-feature point/ring/property limits before costly parsing/validation. Reuse the bounded-selection idea in [Overlays/ReportOverlaySource.cs:36-49](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Spatial/Overlays/ReportOverlaySource.cs:36). Test a polygon with many vertices and an oversized FeatureCollection with small bounded fixtures; an actual outage was not induced.
- **Public sitemap:** [web/apps/public/src/api.ts:109-117](/Users/jarod/coding/location-saas/web/apps/public/src/api.ts:109) fetches up to 50,000 sites, potentially 250 sequential API pages for one anonymous request. [web/apps/public/src/upstream.ts:12-27](/Users/jarod/coding/location-saas/web/apps/public/src/upstream.ts:12) gives individual calls a timeout; the sitemap has no overall deadline/client cancellation. Cache or pre-generate paginated sitemap artifacts and enforce an end-to-end budget at the public SSR server. Verify a slow upstream and client disconnect cancel the whole operation; API-only fairness is not sufficient evidence of SSR admission control.

Treat the throughput impact as unmeasured. The report confirms missing budgets and plausible workload amplification, not a measured cross-tenant outage.


<a id="sec-19"></a>

### SEC-19 — Host and forwarded-header trust can be enabled without constraining the peer

**Confidence: High for configuration behavior; exploitability depends on routing. CWE-346 / CWE-441.**

**Evidence:** [src/LocIntel.Api/HttpPolicyHosting.cs:52-63](/Users/jarod/coding/location-saas/src/LocIntel.Api/HttpPolicyHosting.cs:52) enables forwarded-host/proto/IP handling and clears both trusted-peer lists. [src/LocIntel.Api/appsettings.json:8](/Users/jarod/coding/location-saas/src/LocIntel.Api/appsettings.json:8) permits all hosts. Independently, [src/LocIntel.Api/GuestOrgMiddleware.cs:21-25](/Users/jarod/coding/location-saas/src/LocIntel.Api/GuestOrgMiddleware.cs:21) reads raw `X-Forwarded-Host` even when the proxy-trust option is disabled. Authentication callbacks and billing/SSO return URLs are constructed from the request host (`Identity/Auth/Endpoints.cs:415`, `Entitlements/BillingEndpoints.cs:111`, `Identity/Users/SsoEndpoints.cs:92`).

**Impact and prerequisites:** A direct client or untrusted intermediary reaching such a listener can supply spoofed host/scheme/IP information, select a guest tenant, affect generated URLs, or contaminate a host-insensitive cache. Guest tenant selection alone grants only public authority; protected-data compromise requires an additional bug such as SEC-01. Identity-provider callback allowlists may reject hostile callback URLs. A host header by itself is not proof of account takeover.

The supplied Envoy config strips client-supplied forwarding/trusted headers ([deploy/gateway/envoy.yaml:59-74](/Users/jarod/coding/location-saas/deploy/gateway/envoy.yaml:59)), and `Gateway:Required` checks a strong private key. These substantially mitigate the supplied gateway topology. The unsafe mode remains available to other forks/deployments and raw-header use bypasses the application's declared trust setting.

**Recommended resolution:** Configure explicit trusted proxy networks/addresses and allowed public hosts, isolate the API listener, and consume the middleware-normalized `Request.Host`. If public SSR needs an additional tenant header, authenticate that transport and validate the header there. Keep the gateway's stripping and required-key behavior.

**Verification:** Requests from an untrusted peer cannot change normalized host/proto/IP; trusted proxy traffic and every legitimate tenant domain still work. Test multi-hop forwarding and SSR host propagation. [Microsoft proxy guidance](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0) describes trusted peers and host restrictions.


<a id="sec-20"></a>

### SEC-20 — Production requires a persistent keyring but does not require key encryption

**Confidence: High; conditional on the selected deployment configuration. CWE-312.**

**Evidence:** [src/LocIntel.Api/AuthenticationHosting.cs:63-69](/Users/jarod/coding/location-saas/src/LocIntel.Api/AuthenticationHosting.cs:63) requires `DataProtection:KeyPath` in serving Production roles. `:72-89` encrypts the keyring only if `CertificatePath` is supplied; startup accepts an ordinary filesystem keyring without that protection. Explicit filesystem persistence removes the framework's automatic encryption-at-rest selection, as documented by [Microsoft key-storage guidance](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-storage-providers?view=aspnetcore-10.0).

**Impact and prerequisites:** A reader of an inadequately protected Linux key volume or its backups obtains application key material used for auth cookies, OAuth state and contact tokens. That materially expands the effect of a storage/backup disclosure. Forging a user cookie still interacts with the local session-record validation; possession of keys should nevertheless be treated as an authentication-system compromise. No key volume was accessed or key value extracted in this review.

The new DigitalOcean renderer supplies the certificate and is protected against this omission. The finding concerns the generic configuration contract. Volume encryption and strict ACLs are useful but do not automatically provide a separate key-wrapping boundary from backup/volume readers.

**Recommended resolution:** Require an explicit supported production key-protection mechanism in addition to persistence: the existing certificate option or an appropriate managed KMS key wrapper. Validate it at startup and restrict key-store readers/writers and backup access. Maintain a tested recovery/rotation procedure with access to old decrypting keys only where required.

**Verification:** A Production host with a plain directory and no approved protector fails readiness/startup; protected replicas can exchange cookies/tokens; stored XML contains encrypted key material; recovery and rotation preserve intended sessions without exposing keys.


<a id="sec-21"></a>

### SEC-21 — Production configuration accepts plaintext credential-bearing provider transports

**Confidence: High for accepted options; no plaintext production traffic was observed. CWE-319.**

**Evidence:** [src/LocIntel.Api/ProviderOptionsValidation.cs:5-8](/Users/jarod/coding/location-saas/src/LocIntel.Api/ProviderOptionsValidation.cs:5) accepts both HTTP and HTTPS URLs. Production WorkOS validation uses it ([AuthenticationHosting.cs:32-36](/Users/jarod/coding/location-saas/src/LocIntel.Api/AuthenticationHosting.cs:32)), as does Stripe (`Program.cs:191-194`). [src/Integrations/LocIntel.Integrations.Smtp/SmtpNotificationTransport.cs:31-39](/Users/jarod/coding/location-saas/src/Integrations/LocIntel.Integrations.Smtp/SmtpNotificationTransport.cs:31) explicitly disables TLS when `UseStartTls=false` and then authenticates/sends mail. `Program.cs:252-275` validates SMTP configuration without requiring secure transport. [SmtpOptions.cs:13-14](/Users/jarod/coding/location-saas/src/Integrations/LocIntel.Integrations.Smtp/SmtpOptions.cs:13) says the false option is for local sinks, but that restriction is not enforced.

**Impact and prerequisites:** A production configuration mistake or unsafe copied emulator configuration can transmit API keys, SMTP credentials, password-reset links or contact links without transport encryption. Defaults for real WorkOS/Stripe endpoints and SMTP STARTTLS are secure; this is an insecure override accepted in Production, not a claim that the normal deployment sends plaintext. A protected local relay may be an intentional exception and should be represented explicitly.

**Recommended resolution:** Require HTTPS for credential-bearing external provider overrides outside explicit development/test configurations. Require TLS for SMTP authentication and sensitive email delivery; support an explicit, constrained trusted-relay exception only where necessary. Apply equivalent validation to storage/KMS custom endpoints and verified database TLS in the deployment configuration. Keep emulator exceptions narrow rather than weakening all production transports.

**Verification:** Production options reject plaintext external endpoints and authenticated non-TLS SMTP; real providers succeed with certificate verification enabled; local emulators remain usable in their intended environment. Inspect actual deployed endpoint schemes and SMTP negotiation without logging credentials.


<a id="sec-22"></a>

### SEC-22 — Local test databases are published on all interfaces with predictable credentials

- **Severity:** Medium; **confidence:** High; **scope:** local developer/test execution, not the production manifests.
- **Evidence:** [tools/e2e-stack.sh:49](/Users/jarod/coding/location-saas/tools/e2e-stack.sh:49) publishes `-p "$pg_port:5432"` and sets a predictable PostgreSQL owner password. [tools/replica-stack.sh:74-75](/Users/jarod/coding/location-saas/tools/replica-stack.sh:74) repeats that exposure; `:86-91` also publishes PgBouncer with the same predictable owner credentials and privileged authentication user. [tools/smoke-image.sh:83,109](/Users/jarod/coding/location-saas/tools/smoke-image.sh:83) publishes the API/worker ports without a host bind address as well.
- **Description and impact:** Docker publishes unspecified host addresses on all interfaces. While a stack runs, another machine that can reach the developer/runner's published ports can authenticate directly as the database owner, read or change fixture data, and interfere with test results. PostgreSQL owner/superuser access also considerably exceeds application database privileges. The default ports and credentials are fixed in public repository scripts. Smoke API/worker listeners unnecessarily broaden exposure too, although their configured providers are placeholders and they do not have passwordless development auth.
- **Preconditions / existing mitigations:** A test stack must be running and the host/network firewall must permit access. The scripts generally tear resources down, fixture data is synthetic, API processes in the e2e/fleet stacks bind loopback, and the gateway Compose reference already binds `127.0.0.1` ([deploy/gateway/compose.yaml:98](/Users/jarod/coding/location-saas/deploy/gateway/compose.yaml:98)). These limit duration/scope but do not make the DB port local-only. No actual remote access or data compromise was tested.
- **Resolution:** Change every local `-p` to an explicit loopback mapping such as `-p "127.0.0.1:$pg_port:5432"`; do the same for PgBouncer and image-smoke HTTP ports. Use a unique random test database password if shared CI/developer hosts are supported. The smallest necessary fix is the bind address.
- **Verification:** Start a disposable fixture and inspect `docker port` / `NetworkSettings.Ports` for only loopback host bindings. Confirm test clients still connect, and a second host cannot connect. Add a small existing shell/artifact check against accidental unrestricted publish flags.
- **Primary source:** [Docker port publishing](https://docs.docker.com/engine/network/port-publishing/) explicitly documents all-interface defaults and loopback binding.


<a id="sec-23"></a>

### SEC-23 — Generic production instructions give runtime processes owner credentials

- **Severity:** Medium; **confidence:** High; **scope:** documented production recipe and local orchestration, not the new DigitalOcean secret split.
- **Evidence:** [docs/production.md:127-131](/Users/jarod/coding/location-saas/docs/production.md:127) says the owner is handed only to migrate but immediately instructs api/worker to receive the same connection string plus replacement application credentials; [docs/production.md:178](/Users/jarod/coding/location-saas/docs/production.md:178) describes `ConnectionStrings:locintel` as owner credentials rewritten for api/worker. [src/LocIntel.Api/Program.cs:66-81](/Users/jarod/coding/location-saas/src/LocIntel.Api/Program.cs:66) performs that rewrite in `IConfiguration`, rather than changing the process environment or source secret. AppHost follows the pattern via `WithReference(postgres)` and application credential overrides ([src/LocIntel.AppHost/AppHost.cs:93-99,135-143](/Users/jarod/coding/location-saas/src/LocIntel.AppHost/AppHost.cs:93)).
- **Description and impact:** Replacing a configuration value ensures ordinary connections use the application role but does not remove the original owner password from the process environment, mounted configuration, or orchestrator definition. A runtime environment disclosure or compromised API/worker process can recover a migration/owner credential, defeating the intended role separation and enabling database administrative actions, including destructive schema/audit changes.
- **Preconditions / existing mitigations:** This requires a deployment that follows the older recipe plus a runtime secret disclosure/compromise; normal application connections are rewritten to the app role. It is not an unauthenticated database-owner bypass. The DigitalOcean README and renderer are already correct: [deploy/digitalocean/README.md:193-208](/Users/jarod/coding/location-saas/deploy/digitalocean/README.md:193) and [render.py:36-37](/Users/jarod/coding/location-saas/deploy/digitalocean/render.py:36) give runtime roles only the app database secret. The gateway Compose runtime also receives app-user credentials. Do not report the DigitalOcean topology as leaking the owner credential.
- **Resolution:** Make the correct DigitalOcean pattern authoritative everywhere: construct runtime connection strings with only the application identity, reserve admin credentials for the migration job, and revise the contradictory generic configuration table/prose. Where possible assert that runtime database secrets never reference the owner credential. Retain the rewrite only for explicitly local backward compatibility if needed.
- **Verification:** Inspect rendered manifest secret references and configuration *names* without logging values; ensure api/worker cannot reference the admin Secret. In a disposable deployment, prove the serving role cannot perform owner-only DDL. Review container environment and mounted configuration sources for actual credential separation.


<a id="sec-24"></a>

### SEC-24 — Kubernetes compatibility manifests lack network isolation

- **Severity:** Medium hardening gap; **confidence:** High for absent manifests, Medium for deployment reachability; **scope:** future application of the supplied Kubernetes bootstrap.
- **Evidence:** [deploy/digitalocean/render.py:21-76](/Users/jarod/coding/location-saas/deploy/digitalocean/render.py:21) produces only Namespace, Job, two Deployments and a headless Service. The namespace has no policy labels and no NetworkPolicy is rendered. [render.py:40,48-53,74-75](/Users/jarod/coding/location-saas/deploy/digitalocean/render.py:40) exposes the API listener within the cluster and mounts the key share; [deploy/digitalocean/main.tf:80-85](/Users/jarod/coding/location-saas/deploy/digitalocean/main.tf:80) allows the whole DOKS cluster through the database firewall; [main.tf:87-93](/Users/jarod/coding/location-saas/deploy/digitalocean/main.tf:87) attaches the NFS share to the VPC.
- **Description and impact:** A namespace and ClusterIP/headless Service are not ingress/egress security boundaries. Without a policy supplied outside this repository, any reachable compromised workload can attempt API/private identity traffic and database connections, and serving pods can make arbitrary outbound connections. The database firewall is cluster-wide, not application-pod-specific. This broadens lateral movement, secret exfiltration and any application SSRF consequences. Shared Data Protection key storage additionally requires both read and write isolation; certificate encryption protects key confidentiality at rest but does not turn arbitrary write access into a safe operation.
- **Preconditions / existing mitigations:** This is a newly created, dedicated compatibility cluster/VPC, not a proven publicly exposed service. The app requires a strong gateway identity key, still authenticates and authorizes business requests, runtime database credentials are constrained, NFS export ownership is an explicit operator prerequisite, the keyring uses certificate encryption, and service-account tokens are not mounted. A cluster administrator may impose policies outside the repository; no live cluster exists to verify that. Do not claim that networking alone bypasses tenant authorization.
- **Resolution:** Add native default-deny ingress/egress plus minimal allow rules for intended gateway-to-API, probes where needed, DNS, Postgres, scanner, OTLP and selected provider traffic. Use CNI/provider mechanisms appropriate for dynamic external endpoints rather than a brittle hardcoded address list. Restrict NFS at the node/export/VPC layer too: kubelet-mounted NFS traffic is not equivalent to pod-originated traffic, so pod NetworkPolicy alone does not secure that mount. Keep this slice dedicated until ingress, egress and key-share access are proven.
- **Verification:** Render tests should require the intended policies. On the actual CNI, test an unrelated namespace/pod cannot reach private API/identity/DB or access the key export, while API/worker dependencies and probes continue working. Verify node/export restrictions separately. Retain these checks before exposing public routing.
- **Primary sources:** [Kubernetes NetworkPolicy defaults](https://kubernetes.io/docs/concepts/services-networking/network-policies/), [DigitalOcean NFS features](https://docs.digitalocean.com/products/nfs/details/features/), [ASP.NET Core key-storage access requirements](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0).


<a id="sec-25"></a>

### SEC-25 — CI actions are mutable and job token permissions are implicit

- **Severity:** Medium hardening gap; **confidence:** High for source configuration, Medium for token impact.
- **Evidence:** [.github/workflows/checks.yml:5-18](/Users/jarod/coding/location-saas/.github/workflows/checks.yml:5) has push and pull_request execution and no top-level `permissions`; `:18-19,106-109,167-168,198-201,288` use major-version action tags, including `pnpm/action-setup@v4`; checkout steps do not set `persist-credentials: false`. There is no workflow-level or job-level permissions block anywhere in this file.
- **Description and impact:** A compromised action maintainer/tag can change executable workflow code without a repository diff. A malicious action/dependency can alter test/build outputs and read job-accessible credentials. Token write authority depends on repository/organization defaults, which this forkable template leaves implicit. Persisting checkout credentials makes the job token available to later shell/package scripts.
- **Preconditions / existing mitigations:** Requires a compromised upstream action/dependency or already-malicious workflow execution. These are predominantly official actions; no deployment credentials, publish job, `pull_request_target`, attacker-controlled shell interpolation or self-hosted runner was found in the workflow. Fork PR tokens are normally restricted by GitHub, so a confirmed repository-write exploit cannot be inferred.
- **Resolution:** Pin actions to reviewed full commit SHAs; use `permissions: { contents: read }` at workflow scope; grant any extra permission only to the one job that needs it. Use `persist-credentials: false` where subsequent authenticated git operations are unnecessary. Keep references updated through a reviewed dependency-update process.
- **Verification:** A static workflow check should reject non-SHA remote action references and missing permission declarations. Confirm all checks still work with read-only token permissions and no persisted checkout credentials; verify actual repository settings separately.
- **Primary source:** [GitHub secure use reference](https://docs.github.com/en/actions/reference/security/secure-use) recommends immutable action commits and minimum token permissions.


## Low severity


<a id="sec-26"></a>

### SEC-26 — Non-production storage tickets allow operation confusion and overwrites after scanning

**Confidence:** High. **Precondition:** local storage selected in a non-Production environment; production startup explicitly disallows it. Treat as development/staging exposure and a test-contract gap, not a deployed cloud-storage exploit.

**Evidence:**

- [src/Modules/LocIntel.Modules.Storage/LocalStoreEndpoints.cs:24-44](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Storage/LocalStoreEndpoints.cs:24) sends both upload and download tokens through the same `Redeem` function and does not bind a token to a method/purpose.
- [src/Modules/LocIntel.Modules.Storage/LocalObjectStore.cs:35-55](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Storage/LocalObjectStore.cs:35) changes only `maxBytes` between tokens; `WriteAsync` uses `File.Create` and overwrites an existing file (`:125-138`).
- [LocalStoreEndpoints.cs:26-33](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Storage/LocalStoreEndpoints.cs:26) checks Content-Length, but accepts a missing value and never bounds actual bytes read.
- [src/LocIntel.Api/StorageHosting.cs:91-100](/Users/jarod/coding/location-saas/src/LocIntel.Api/StorageHosting.cs:91) prevents local provider selection in Production.

**Description / abuse:** Reuse a still-valid upload ticket to replace bytes after they were scanned Clean; the database remains Clean. Put the upload token on the download route to obtain bytes before quarantine is cleared. A read-ticket holder can use the same token on the upload route with a missing Content-Length to overwrite the object despite its `maxBytes=0`. The header-only size check also permits chunked requests larger than the advertised budget. The adapter comment says single-use, but no such behavior exists.

**Resolution:** Sign and check operation/purpose, enforce create-new atomically for uploaded objects, and count actual streamed bytes. Keep this provider restricted to disposable development data. Add the same immutability/ticket contract tests for local, S3 and Azure adapters so local integration tests cannot validate a weaker flow than production.

**Regression checks:** upload-token-as-download and download-token-as-upload fail; a second PUT cannot replace a successfully uploaded object; chunked data beyond maxBytes fails and removes partial output.


<a id="sec-27"></a>

### SEC-27 — Basemap key guidance misleadingly implies the key stays secret

**Severity: Low; unsafe credential guidance rather than a proven compromise. Confidence: High.**

**Evidence:** [web/apps/console/src/features/settings/map-basemaps-card.tsx:53-56](/Users/jarod/coding/location-saas/web/apps/console/src/features/settings/map-basemaps-card.tsx:53) says the provider key is encrypted and never shown again. In contrast, [src/Modules/LocIntel.Modules.Tenancy/Organizations/MapBasemapsEndpoints.cs:97-109](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Tenancy/Organizations/MapBasemapsEndpoints.cs:97) decrypts the stored key and substitutes it into a URL returned to every authorized `sites:read` caller. The map endpoint's own comment at lines 52–60 correctly states that this is not a secrecy boundary.

**Impact:** An administrator following the UI may provide a server-only or broadly privileged vendor secret. That key would become available in browser network traffic to map viewers and could be reused for billable requests or other provider permissions. Browser-publishable, origin-restricted tile keys are expected and are not themselves a vulnerability.

**Resolution:** Change the UI guidance to explicitly require a browser/public tile key, limited to tile reads and authorized origins, with quota limits where the provider supports them. If a provider requires a server secret, implement its documented server-side retrieval path rather than placing the secret in a browser tile URL. Review/rotate any existing key that was mistakenly assumed private.

**Verification:** Assert the UI identifies browser visibility; inspect configuration ownership/process to ensure only appropriately scoped public keys are entered. Do not claim a discovered live secret: none was inspected or disclosed.


<a id="sec-28"></a>

### SEC-28 — Kubernetes workloads omit explicit seccomp and a read-only root filesystem

- **Severity:** Low defense-in-depth; **confidence:** High; **scope:** compatibility manifests.
- **Evidence:** [deploy/digitalocean/render.py:31-35](/Users/jarod/coding/location-saas/deploy/digitalocean/render.py:31) specifies non-root UID/GID 1654, disables service-account token mounting, denies privilege escalation and drops all capabilities, but has no `seccompProfile` or `readOnlyRootFilesystem`. The created namespace at `:21` does not enforce a Pod Security Standard.
- **Description and impact:** When the cluster does not enable seccomp-by-default, the workload can run with an unrestricted syscall set. Writable root filesystems let a compromised process alter writable image paths and persist additional payloads for that container's lifetime. These omissions increase post-compromise options; neither is an initial application vulnerability.
- **Existing mitigations:** Non-root, no privilege escalation, all capabilities dropped, CPU/memory limits, no service-account token, and read-only certificate/CA mounts are already good controls. Actual DOKS seccomp defaults/admission controls were not inspected. A writable root filesystem does not imply non-root can write every image file.
- **Resolution:** Add pod `seccompProfile: {type: RuntimeDefault}` and container `readOnlyRootFilesystem: true`, with bounded writable temporary volumes only for measured needs. Enforce an appropriate namespace Pod Security Standard after compatibility validation. Keep shared keys writable only where required.
- **Verification:** Run the existing image smoke with the same security settings, verify provider/native PDF/image operations work, check container `Seccomp: 2` where accessible, and assert denied image-root writes without disrupting intended temporary/output storage.
- **Primary sources:** [Kubernetes seccomp defaults](https://kubernetes.io/docs/tutorials/security/seccomp/), [Kubernetes application security checklist](https://kubernetes.io/docs/concepts/security/application-security-checklist/).


<a id="sec-29"></a>

### SEC-29 — Local runtime servicing and artifact vulnerability evidence are incomplete

- **Severity:** Low tooling/supply-chain gap; **confidence:** High for observed versions and workflow, deployment-specific for exposure.
- **Evidence:** [global.json:3-4](/Users/jarod/coding/location-saas/global.json:3) permits the installed SDK 10.0.102; this audit's `dotnet --info` reported host/shared framework 10.0.2. Local e2e/fleet scripts run the API directly with that host ([tools/e2e-stack.sh:58-67](/Users/jarod/coding/location-saas/tools/e2e-stack.sh:58), [tools/replica-stack.sh:125,134](/Users/jarod/coding/location-saas/tools/replica-stack.sh:125)). [.github/workflows/checks.yml:178-184](/Users/jarod/coding/location-saas/.github/workflows/checks.yml:178) publishes and functionally smokes an OCI image but has no image/OS vulnerability scan; `src/LocIntel.Api/LocIntel.Api.csproj:9-11` leaves the .NET base image inferred by the SDK.
- **Description and impact:** Package audit alone does not report all shared-runtime or base-OS vulnerabilities. The observed developer runtime predates multiple Microsoft servicing updates; local processes use it even though source NuGet package versions are current. Without retained image/runtime vulnerability evidence, known OS/framework exposure can be missed when artifacts are reused.
- **Existing mitigations and limits:** NuGet audit is enabled for direct and transitive packages and warnings are errors; this is not a claim that .NET dependencies have no audit gate. Read-only metadata for `locintel:compat-dev`, `locintel:reporting-verify`, `locintel:gateway-dev` and `locintel:ci` reported `DOTNET_VERSION=10.0.11`, `ASPNET_VERSION=10.0.11`, and non-root `User=app`. Those images are on the latest patch listed in the official .NET 10 release/security index as inspected. No framework exploit was demonstrated, no actual deployment was inspected, and the base OS was not scanned. Do not classify every historical .NET CVE as applicable: in particular the Data Protection CVE-2026-40372 advisory distinguishes affected NuGet binary loading from unaffected shared-framework configurations.
- **Resolution:** Update the local SDK/runtime to the current supported servicing release; record/verify the actual runtime version in image smoke output; add one native/standard image vulnerability scan for the produced artifact and retain its SBOM/result keyed by digest. Define an owner and turnaround for security updates to .NET, Envoy, Redis, PgBouncer, ClamAV and base OS images. Promote the scanned digest, as the DigitalOcean renderer already requires.
- **Verification:** `dotnet --info` and container runtime metadata agree with the approved patch; repeat the dependency audit; produce a dated image scan for the exact deployment digest. No new deployment image is needed solely to change documentation.
- **Primary sources:** [Microsoft .NET 10 security update index](https://github.com/dotnet/core/blob/main/release-notes/10.0/cve.md), [NuGet transitive auditing in .NET 10](https://learn.microsoft.com/en-us/dotnet/core/compatibility/sdk/10.0/nugetaudit-transitive-packages), [Data Protection advisory applicability](https://github.com/dotnet/announcements/issues/395).


## Source-remediated during review


<a id="sec-16"></a>

### SEC-16 — Legal-hold trash deletion was corrected in concurrent working-tree changes

**Status: Source-remediated during the review; not counted among active findings. Original severity: Medium.**

The initial review found that ordinary trash cleanup selected expired Deleted files without consulting LegalHold. Changes made elsewhere in the working tree during this audit now filter held files, acquire the file's transaction-scoped lock, reload it and recheck hold/status/deadline before deleting bytes ([src/Modules/LocIntel.Modules.Storage/FileTrash.cs:38-49](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Storage/FileTrash.cs:38)). SetHold uses the same lock before updating the hold ([src/Modules/LocIntel.Modules.Storage/Endpoints.cs:224-234](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Storage/Endpoints.cs:224)). The transaction requirement is enforced by [src/LocIntel.Platform/Data/AggregateLock.cs:57-68](/Users/jarod/coding/location-saas/src/LocIntel.Platform/Data/AggregateLock.cs:57).

Those changes address the reported source-level defect and race. This review did not apply that fix and did not execute a dedicated concurrent legal-hold regression after the change. Retain this entry so the original observation and disposition are visible, without claiming the vulnerable sweep still exists.

**Remaining verification:** Trash a file, place a hold, advance beyond retention, and prove cleanup retains its bytes. Release the hold and prove cleanup can erase it. Coordinate a hold with a paused sweep and verify the defined lock ordering. Existing foreground legal-hold coverage ([tests/LocIntel.IntegrationTests/StorageTests.cs:199](/Users/jarod/coding/location-saas/tests/LocIntel.IntegrationTests/StorageTests.cs:199)) does not by itself prove the background/concurrency case. Check generated reports as well as uploaded files.


## Existing controls to preserve

- Tenant-owned EF data has query filtering and PostgreSQL RLS, with transaction-local tenant state. The local proof ran through that RLS-protected path, demonstrating why RLS and endpoint authorization must both exist ([src/LocIntel.Platform/Data/ModuleDbContext.cs:109](/Users/jarod/coding/location-saas/src/LocIntel.Platform/Data/ModuleDbContext.cs:109), [TenantSessionInterceptor.cs:293](/Users/jarod/coding/location-saas/src/LocIntel.Platform/Data/TenantSessionInterceptor.cs:293)).
- Session cookies are encrypted and HttpOnly, normally Secure in Production, and backed by revocable local session records. Logout/revoke-others and SCIM removal provide real revocation. API keys contain cryptographic randomness and are stored as hashes; ordinary API authentication checks revocation and expiry.
- The API supplies defensive JSON response headers, generic client errors and trace identifiers. The normal unsafe browser-request pipeline checks Origin when a session cookie is present. Installed TanStack Start also supplies default server-function CSRF middleware; absence of an app-local copy was not counted as a vulnerability.
- Storage download signing checks Clean status and authorization. S3/Azure upload tickets use create-only semantics. ClamAV failures preserve unscanned state; production refuses the development scanner/local storage/local secrets providers. Preserve these controls while correcting budgets and export permissions.
- Webhook/connector secrets are envelope-encrypted; HMAC signatures and rotation overlap are implemented. Spatial raw SQL binds values with parameters. No exploitable SQL-injection path was established in the reviewed builders.
- Reporting provides useful patterns: job admission/artifact/image budgets, server-owned basemap settings, connection-time public DNS validation, disabled HTTP redirects/cookies/proxy, and current source-permission checks on report artifacts. Reuse these focused mechanisms where older flows lack equivalent protection.
- Envoy strips caller-provided trust headers and uses a private authenticated identity path; the supplied gateway host-published ports bind loopback. DigitalOcean runtime credentials are separated from migration credentials; containers are non-root with capabilities dropped, resources/probes specified and service-account token automount disabled.
- CI uses frozen frontend installs and multiple architecture/isolation/runtime checks. NuGet audit is enabled for direct and transitive dependencies and warnings are treated as errors. **A missing npm scan is not evidence that .NET has no dependency audit.**

## Additional observations and deployment verification requirements

These are explicitly bounded observations or unknowns, not extra confirmed production compromises.

| Area | Observation and required follow-up |
|---|---|
| Frontend security headers | API CSP does not protect the separately served console/public HTML. Verify actual document CSP, nosniff, referrer policy and framing restrictions at the serving proxy. Preserve the intentional public embed route; do not apply a blanket framing policy that breaks it. A strict CSP reduces SEC-02 impact but does not fix its unsafe sink. |
| Browser residual data | [web/apps/console/src/features/hierarchy/hooks.ts:19-29](/Users/jarod/coding/location-saas/web/apps/console/src/features/hierarchy/hooks.ts:19) stores hierarchy by org in sessionStorage and treats it as fresh for five minutes; session-boundary clearing removes React Query state, not this persisted data. Clear private persisted state on identity changes, or include principal/scope generation. Test two accounts using the same tab, especially after fixing SEC-01 hierarchy filtering. No remote cross-tenant exploit from this cache was demonstrated. |
| Active storage content | Malware scanning is not HTML/SVG sanitization. Cloud downloads accept caller content types and do not universally force attachment. Keep uploaded active content on a separate, non-authentication origin and choose safe Content-Disposition behavior. Default S3/Azure origins differ from the app; no same-origin application XSS was proven here. |
| Local storage path handling | [LocalObjectStore.cs:151-159](/Users/jarod/coding/location-saas/src/Modules/LocIntel.Modules.Storage/LocalObjectStore.cs:151) uses a string prefix for containment, which can admit sibling paths sharing the prefix. Current keys are server-generated/signed, so no remotely controllable traversal into this path was established. Use a path-component-safe containment check before adding caller-controlled keys. |
| Non-production adapters | Local auth is allowed whenever the environment is not literally Production, including Staging ([AuthenticationHosting.cs:39](/Users/jarod/coding/location-saas/src/LocIntel.Api/AuthenticationHosting.cs:39)). Keep all shared/staging environments with real users/data on production-strength providers; consider an explicit Development/Testing allowlist. The private emulator fixture itself is intentional. |
| Contact links | Tokens are reusable until expiry and map to a revocable contact record, not a consumed one-time invitation. Current contact grants are public-read only. If contact functionality gains private authority, add one-time redemption/session lifetime and URL-log redaction tests before relying on magic-link secrecy. Verify gateway/OTLP logs do not retain auth callback codes, contact tokens or signed download query strings. No real log contents were inspected. |
| Database permissions | Verify the actual api/worker role has no superuser, BYPASSRLS, owner or unintended DDL authority over tenant tables. The source rewrite and migrations alone do not prove every deployment uses the correct credentials. RLS does not contain an already-compromised process with shared app credentials that can select its own tenant context; restrict runtime code and credentials accordingly. |
| Bucket/KMS policy | Inspect real public-access blocks, least-privilege object prefixes, CORS, versioning/lifecycle, ticket constraints, KMS identity/policy and rotation. No cloud bucket/IAM settings were available for validation. Apply SEC-17 independently of assumed lifecycle defaults. |
| Cluster/key storage | Verify Kubernetes Secret encryption/RBAC, control-plane access, actual CNI enforcement and NFS export/node restrictions. File UID/root-squash alone does not prove another workload cannot access a share. Certificate encryption does not replace key-store write protection. |
| Backups and deletion | [docs/runbook.md:77-101](/Users/jarod/coding/location-saas/docs/runbook.md:77) describes recovery and alerts, but no restore drill was executed here. Before production, demonstrate consistent restore of PostgreSQL/outbox, objects and required decryption keys, then verify tenant isolation and replay behavior after restore. Validate retention/deletion semantics against SEC-16. |
| Detection | Verify failed-auth/privilege changes, repeated SSRF denials, AV failures, unusual export generation/downloads, queue growth and storage abuse create actionable alerts. Inspect real audit/OTLP retention/access policies and ClamAV signature freshness. Code instrumentation alone does not establish effective alerting. |
| Dependency coverage | No full Git-history/entropy secret scan or container base-OS scan was performed. Native rendering packages, Envoy, Redis, PgBouncer, ClamAV and deployment tooling need artifact-specific inventory/scanning. Exact deployment digests matter. |

## Recommended resolution sequence

| Priority | Work | Acceptance evidence |
|---|---|---|
| Immediate application fixes | SEC-01–06: close public administration, render tooltip names as text, bind login to its initiating browser, isolate replay records, require active membership and enforce administrative scope | The local vulnerable-behavior proofs become negative regression checks; existing legitimate login, role, tenant and retry flows still pass. |
| Before exposing shared workers/data | SEC-07–10: constrained outbound transport, source-authorized exports and scoped ingest | Controlled internal/redirect/DNS test targets are blocked; low-privilege users cannot obtain export archives or change out-of-scope sites. Backfill existing exports. |
| Same release for data safety | SEC-11–15 and SEC-17–18: preserve Secure cookies, constrain SSR paths/cache, repair identity/session lifecycle, storage and job budgets; verify SEC-16 stays remediated | End-to-end browser cookie/path/cache tests, revocation/time tests, retained held objects and bounded abandoned/chunked/bulk work. |
| Deployment acceptance | SEC-19–25: explicit proxy/key/TLS boundaries, loopback local fixtures, runtime-only credentials, network policy, least-privilege CI | Rendered checks plus verification on the actual ingress/CNI/key store; action SHAs and explicit CI permissions; no owner secret available to serving roles. |
| Follow-up hardening | SEC-26–29 and deployment-observation table | Local/cloud ticket contract parity, truthful key guidance, hardened-container smoke and dated scans tied to promoted image digests. |

Address shared authorization and transport roots rather than adding only route-specific patches. Do not publish or deploy fixes solely because a static check passes: retain the smallest behavior-level regression for the abuse path and run the affected existing suites. No implementation changes, ticket creation or deployment were performed as part of this report.

## Validation ledger

| Check | Result | Scope / limits |
|---|---|---|
| Current API/integration-test build | **Passed**, zero warnings/errors: `dotnet build tests/LocIntel.IntegrationTests/LocIntel.IntegrationTests.csproj --no-restore --disable-build-servers --nologo -v minimal` | Builds current source with cached dependencies; not a run of the entire integration suite. |
| API security reproduction harness | **8 vulnerable behaviors confirmed** | Actual API handlers, cookie flow and disposable PostgreSQL/RLS; synthetic data only. External identity uses the local test provider. No real WorkOS, customer DB or cloud exploitation. |
| Leaflet and URL checks | **Passed** using installed Leaflet/JSDOM and Node URL handling | Reproduced HTML element/event behavior; safe textContent alternative; dot-segment normalization. No full browser exploitation of a deployed site. |
| .NET advisory query | **Completed; no advisories reported across 24 solution projects** | `dotnet package list --project LocIntel.slnx --vulnerable --include-transitive --format json --no-restore`; restored dependency graph only. This is not a guarantee of no unknown vulnerabilities. |
| npm advisory query | **Incomplete** | Initial feed request failed. Automatic approval review rejected the external dependency-metadata disclosure on retry; no bypass attempted. No clean-result claim. |
| DigitalOcean renderer | **Passed** `python3 deploy/digitalocean/render.py --check` | No cloud resources created. Rendered resource inventory verified. |
| Image/runtime metadata | Existing local LocIntel images reported non-root user and .NET/ASP.NET **10.0.11**; local host reported **10.0.2** | Metadata inspection only, no base-OS scan or live deployment attestation. |
| Offline secret signatures | **891 tracked text files checked; no selected high-confidence key/token signatures matched** | Private-key/AWS/GitHub/Stripe-live/Slack signatures, with no values emitted. Not entropy analysis, ignored-file analysis or full Git-history scanning. Development credentials are known and covered separately. |
| Full application/security test suite | **Not run** | Targeted checks were chosen for this assessment; report does not claim all tests pass. |
| Live cloud posture, external penetration test, restore drill | **Not performed** | Requires deployment/account evidence; unknowns remain in the table above. |

The API reproduction harness confirmed:

1. Guest settings write.
2. Guest settings read.
3. Guest internal hierarchy read.
4. A login callback accepted by a browser that never initiated login.
5. A role-free member receiving another actor's API-key response through replay.
6. A subtree role manager granting itself an organization-wide wildcard.
7. A removed member retaining an exception grant.
8. The same public URL varying by session while marked public without Vary.

Temporary local evidence: `/private/tmp/locintel-security-proof/Program.cs`, `/private/tmp/locintel-security-proof-final.log`, `/private/tmp/locintel-security-build.log`, `/private/tmp/locintel-frontend-proof.cjs`, and `/private/tmp/locintel-dotnet-vulnerabilities.json`. These are local working artifacts and may be cleaned by the OS; the findings and validation results are captured in this report. Do not upload raw runtime logs without reviewing them for sensitive information.

**Tool limitation:** Automatic approval review rejected the npm audit because it would send repository dependency names and versions to the public npm registry. That check needs explicit approval for the metadata transfer or an approved local advisory source. This restriction does not question ownership of the codebase and did not prevent completion of the source review.

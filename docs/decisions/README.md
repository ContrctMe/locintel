# Architecture Decision Records

The 48 settled decisions for this template, from the design brainstorm of
2026-08-29. The human-readable register (same content, with the three-gates
overview and build sequence) is published from `design-decisions.html`.

**pinned** = expensive to reverse once data exists. Do not contradict a pinned
decision without the maintainer explicitly reopening it. To change any decision,
edit its file (or supersede it with a new numbered ADR) - a hook asks for
confirmation on edits under this directory.

## Pass one - architecture

- [0001. PostgreSQL](0001-database-postgresql.md) **(pinned)**
- [0002. Hierarchy in time](0002-hierarchy-current-only-stamped-facts.md) **(pinned)**
- [0003. Node vs site](0003-separate-node-and-site-tables.md) **(pinned)**
- [0004. Multiple hierarchies](0004-hierarchy-id-from-day-one.md) **(pinned)**
- [0005. Cross-org users](0005-cross-org-users-with-switcher.md) **(pinned)**
- [0006. Permission model](0006-roles-additive-exceptions-no-deny.md) **(pinned)**
- [0007. Principal tiers](0007-principal-tiers-guest-contact-user.md)
- [0008. Entitlement shapes](0008-four-entitlement-shapes.md)
- [0009. Limit behavior](0009-per-entitlement-limit-policy.md)
- [0010. Entitlement source of truth](0010-internal-entitlement-store.md)
- [0011. Entitlement downgrade](0011-downgrade-preflight-block.md)
- [0012. Audit capture](0012-audit-four-kinds-per-org-policy.md)
- [0013. Audit sink](0013-audit-sink-split-by-kind.md)
- [0014. Auth seam](0014-oidc-generic-plus-capabilities.md)
- [0015. Frontend topology](0015-two-frontend-apps.md)
- [0016. API contract](0016-openapi-first-codegen.md)
- [0017. Persistence boundaries](0017-dbcontext-schema-per-module.md) **(pinned)**
- [0018. Site ingest](0018-ingest-upload-and-connectors.md)
- [0019. Object storage](0019-storage-tickets-quarantine-lifecycle.md)
- [0020. Styling](0020-tokens-and-ui-barrel.md)
- [0021. Session model](0021-httponly-cookie-shared-domain.md) **(pinned)**
- [0022. v1 scope](0022-v1-platform-plus-console.md)

## Pass two - operations and lifecycle

- [0023. Job runtime and messaging](0023-wolverine.md) **(pinned)**
- [0024. Tenant context in background work](0024-tenant-context-via-envelope.md)
- [0025. Deletion and restore](0025-deletion-three-tiers.md) **(pinned)**
- [0026. Temporal model](0026-temporal-three-kinds-business-date.md) **(pinned)**
- [0027. Recurrence](0027-rrule-for-schedules.md) **(pinned)**
- [0028. Recurrence storage](0028-materialized-occurrence-horizon.md)
- [0029. Idempotency](0029-idempotency-key-all-unsafe.md) **(pinned)**
- [0030. Rate limiting](0030-rate-limiting-by-tier.md)
- [0031. Connector credentials](0031-secrets-envelope-encryption.md)
- [0032. Notifications](0032-notifications-pluggable-outbox.md)
- [0033. Telemetry](0033-otlp-only-aspire-dashboard.md)
- [0034. Topology](0034-aspire-dev-one-image-role-flag.md)
- [0035. Data residency](0035-residency-silos-then-routing.md) **(pinned)**
- [0036. Fork model](0036-template-init-module-generator.md)

## Amendments

- [0037. Contract direction and read models](0037-contract-direction-and-read-models.md)
- [0038. Engineering standards: role split, migration round-trips, test tiers, CI gates](0038-engineering-standards-convergence.md)
- [0039. Billing seam: plans are entitlement bundles, the webhook is the writer](0039-billing-seam.md)
- [0040. Integration surface: API keys as service principals, webhooks off the event record](0040-integration-surface.md)
- [0041. Enterprise SSO self-service and directory sync](0041-enterprise-sso-directory-sync.md)
- [0042. Support impersonation](0042-support-impersonation.md)
- [0043. Locator: geo search, map, embed](0043-locator-geo-map-embed.md)
- [0044. Listings foundation: canonical feed + change webhooks](0044-listings-foundation.md)
- [0045. Checklists: the reference vertical, and the ops mobile answer](0045-checklists-reference-vertical.md)
- [0046. Org-defined site attributes](0046-org-defined-site-attributes.md)
- [0047. Localization and accessibility posture](0047-i18n-a11y-posture.md)
- [0048. One owner per row; cross-tenant sharing by materialization](0048-one-owner-per-row.md) — pinned

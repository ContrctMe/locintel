---
title: "Gateway enforcement of operational fairness"
status: accepted
pinned: false
date: 2026-09-07
---

# 0054. Gateway enforcement of operational fairness

## Context

The maintainer clarified that request rates are operational fairness settings,
not purchased guarantees. ADR 52 solved a stronger requirement than the product
has, using the transactional database to coordinate admission of each request.
The measured API-key workload performs two counter updates per admitted request.
See the [capacity evidence](../capacity-validation.md) for the controlled
comparisons and their single-host limitations.

## Decision

This supersedes the request-rate decisions in [ADR 30](0030-rate-limiting-by-tier.md)
and [ADR 52](0052-fleet-wide-rate-limits.md). It does not change idempotency,
worker leases, session revocation, business quotas, or tenant isolation.

- Enforce traffic rates and bursts before application business processing at a
  gateway. PostgreSQL is not the request counter store.
- Treat authenticated tenant rates as operational policy, with explicit tenant
  overrides independent of billing plans. All credentials of the same tenant
  share its fairness partition. Guests receive IP-based abuse protection; an
  arbitrary guest cookie is not a trustworthy identity or a fresh allowance.
- Protect individual application replicas with local, bounded concurrency.
  The rate of arrivals and the number of in-flight operations are distinct.
- Keep resource quotas and business-operation accounting in their owning
  application modules. Remove `api.requests_per_minute` from subscription
  entitlements through a documented migration of existing configuration.
- Ship one runnable reference deployment and a replacement contract/test suite,
  not a gateway framework. Gateway replacement must not require changing domain
  handlers, authentication providers, or business quotas.
- Approximation is acceptable, but adding application or gateway replicas must
  not silently multiply the configured tenant allowance. Describe reset,
  burst, propagation, and outage semantics and verify them under contention.

## Identity seam

The encrypted session cookie and opaque API key remain application credentials.
The gateway must not accept a caller-supplied tenant header as authority. A
private application identity lookup may expose a fairness partition after
using the existing authentication/principal pipeline. Short, bounded caching
of this partition is allowed **only for traffic classification**. The actual
application request still performs normal authentication, revocation checks,
session-context checks, and authorization. A cached partition grants no access.

Tenant switching changes the encrypted cookie, so a cache key must bind to the
credential, not merely user ID. API-key rotation and expiration, contacts,
impersonation, and rejected/expired sessions need explicit coverage. Gateway
metadata is internal and must be stripped from client requests and responses.

## Implementation and qualification

Implementation is in progress; this ADR records the accepted target, not a
claim that the current deployment already enforces it. Product selection,
defaults, identity-cache duration, and failure behavior are to be recorded with
the runnable proof in [gateway fairness](../gateway-fairness.md).

Replacement is complete only after identity, bypass, noisy-neighbor, aggregate
multi-replica, policy-change, outage, capacity, endurance, and replica-failure
checks have evidence. An isolated load generator is required for final capacity
qualification; local tests cannot establish cross-host production capacity.

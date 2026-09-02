---
title: "Billing seam: plans are entitlement bundles, the webhook is the writer"
status: accepted
pinned: false
date: 2026-08-30
---

# 0039. Billing seam: plans are entitlement bundles, the webhook is the writer

## Decision

Billing follows the auth seam's shape (ADR 14): a provider port
(`IBillingProvider`) with hosted UI for everything that touches money -
checkout and the billing portal are provider-hosted URLs, so card data never
crosses the template. The built-in adapter is Stripe
(`LocIntel.Integrations.Stripe`, smoke-tested against stripe-mock with
hand-signed webhooks); the local provider is dev/test only and refused in
Production.

- **Plans are entitlement bundles** (`PlanCatalog`): the free tier IS the
  entitlement catalog's defaults - no subscription row, no plan rows. Paid
  plans only raise values (a unit test holds that, plus code validity and
  shape parseability).
- **The provider's webhook is the only writer of subscription truth.** It
  parses through the provider's own signature scheme, then publishes an
  envelope-tenanted event; the handler mirrors the subscription
  (`org_subscriptions`, RLS'd) and applies the plan onto `org_entitlements`
  with `Source = "plan:{id}"`.
- **Operator custody outranks commerce, in both directions.** A value with
  `Source = "operator"` is never touched by plan application or cancellation.
- **PastDue keeps entitlements working** (grace while the provider retries
  payment). **Canceled strips plan rows** so the org falls back to defaults.
  Suspension remains a human decision - billing never locks anyone out on
  its own.

## Why

The entitlement system was complete but disconnected from revenue; this
closes the loop without importing PCI scope, provider lock-in, or a second
source of entitlement truth. Metadata stamped on the Stripe session and
subscription (`locintel_org`, `locintel_plan`) makes every webhook
self-describing - no lookup tables.

## Consequences

Forks set `Billing:Provider=stripe` with price ids per plan, or implement
`IBillingProvider` for another processor. Downgrades-by-cancellation can
leave an org OVER its new (default) limits; gates block new creation but
never destroy data - consistent with ADR 9's grace philosophy.

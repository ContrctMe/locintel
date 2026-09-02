---
title: "Integration surface: API keys as service principals, webhooks off the event record"
status: accepted
pinned: false
date: 2026-08-30
---

# 0040. Integration surface: API keys as service principals, webhooks off the event record

## Decision

Two halves of one idea - tenants integrate in, and events push out - both
built on machinery that already existed.

**API keys are service principals.** A key (`Authorization: Bearer
locintel_...`) resolves by SHA-256 hash to a `Principal.Service` holding
exactly one role, optionally subtree-scoped - the SAME grant model as
people, so the three gates need nothing new. The secret is shown once;
only its hash is stored. `api_keys` is platform-global (a credential must
resolve before tenant context exists - the sessions argument). A presented-
but-invalid key is a hard 401, never a guest fall-through; revocation and
org suspension both bite per-request. v1 limitation, on purpose: endpoints
that pattern-match `Principal.User` for a human actor (most writes) stay
human-only; widening them needs an actor abstraction, not a bypass.

**Outbound webhooks ride the domain-event stream.** The audit module owns
the org's event record, so subscriptions live there too: the domain-audit
handler fans out one delivery message per matching endpoint (exact names or
`prefix.*`, empty = all). Deliveries are signed with the SAME
`t=...,v1=HMAC-SHA256(secret, "{t}.{body}")` scheme the template verifies
on its inbound billing webhooks - one convention, both directions. Signing
secrets are envelope-encrypted (ADR 31) and shown once. Failures retry
with exponential backoff up to five attempts, and every attempt is a
tenant-visible delivery row (purged by audit retention). Production
requires https and public DNS names (SSRF floor). Webhook CONFIG purges
with the org; the audit trail itself still stays.

## Why

Cookie-only auth made the API browser-only, and connectors made
integration pull-only. Both gaps close by composition: keys reuse roles
and gates; webhooks reuse the event stream, the outbox, envelope crypto,
and the signature scheme.

## Consequences

Forks get scoped machine access and push integration without new concepts.
The `webhook.ping` event exists for integrators to verify plumbing.

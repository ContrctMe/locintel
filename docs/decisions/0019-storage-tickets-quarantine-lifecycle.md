---
title: "Object storage"
status: accepted
pinned: false
date: 2026-08-29
---

# 0019. Object storage

## Decision

IObjectStorage issues tickets (getUploadTicket/getDownloadUrl) rather than streaming - S3 presign and Azure SAS behind one port. Quarantine+scan before visibility; derivative pipeline; retention, legal hold, auditable erasure.

## Why

Bytes never proxy through the API; authz happens at signing time.

## Consequences

Short-TTL URLs; pending state every consumer handles; deletion is a workflow.

Production selection (2026-09-03): `Storage:Provider` and `Scanner:Provider` select the adapters like every other seam; the local store and the EICAR scanner refuse to boot in Production. clamd (INSTREAM) is the built-in scanner; a scanner that cannot answer is an error, never a clean verdict.

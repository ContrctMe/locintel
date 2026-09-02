# Building a listings connector

The template deliberately ships the listings archetype's *foundation* and not
its connectors (ADR 44): every directory provider (Google Business Profile,
Apple Business Connect, Bing Places) requires its own OAuth relationship,
verification flow, and field mapping — work that belongs to the fork that
sells it. What the template guarantees is that a connector never has to
reverse-engineer site truth. Two primitives:

## 1. The canonical feed (pull)

`GET /api/listings/feed` with an API key (`Authorization: Bearer locintel_…`,
ADR 40) whose role grants `sites:read`. Returns every site in the key's
scope as a full listing record:

```json
{
  "generatedAt": "2026-08-30T17:04:11Z",
  "organization": "Acme Dev",
  "listings": [{
    "id": "…", "name": "Boston Flagship", "status": "Open",
    "timeZone": "America/New_York",
    "addressLine1": "1 Washington Mall", "city": "Boston",
    "postalCode": "02201", "countryCode": "US",
    "latitude": 42.3554, "longitude": -71.064,
    "publicUrl": "https://acme.example.com/sites/…",
    "hours": [{
      "name": "Weekdays", "rRule": "FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR",
      "anchorDate": "2026-01-05", "opens": "09:00", "closes": "17:00",
      "closedDates": ["2026-12-25"]
    }]
  }]
}
```

Hours come as the RULES (RRULE + exception dates), not materialized windows —
that's the shape providers want; they expand recurrence themselves. A
subtree-scoped key exports its subtree (gate 3 filters, as everywhere).

## 2. Change notification (push)

Subscribe an outbound webhook (Developers page, or `POST /api/webhooks`) to
`site.*` and `hierarchy.*` events. Deliveries are HMAC-signed
(`X-LocIntel-Signature`, `t/v1`, dual-secret during rotation) and retried
with backoff. The webhook tells you *when*; re-pull the feed for *what* —
diffing feed snapshots beats trusting event payloads for full-record sync.

## Connector loop, in practice

1. On boot and on a schedule (daily): pull the feed, upsert every listing at
   the provider, store the provider's id per site (your table, keyed by
   listing `id`).
2. On webhook delivery: debounce briefly, re-pull the feed, diff against
   your last snapshot, push changed records.
3. Map statuses conservatively: `Closed` here means *closed to the public
   today*, not permanently closed — most providers distinguish; check before
   marking a location permanently closed.
4. Holiday closures arrive as `closedDates` on each hours rule; providers
   with "special hours" APIs (Google) want exactly those dates.

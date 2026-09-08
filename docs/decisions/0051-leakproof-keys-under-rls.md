# 51. Leakproof keys: how indexes work under row security

Date: 2026-09-06
Status: accepted
Pinned: false

## Context

A stress test with one tenant of 1,000,000 sites (and a 500,000-site
neighbour in the same table) found every spatial, hierarchy-scope and search
predicate running as a sequential scan for the API, while the same queries
used the GiST indexes when run as the superuser. The cause is a Postgres rule,
not a missing index: when a table has a row-security policy, a user predicate
may only become an **index condition** if its operator function is marked
`LEAKPROOF`, because an index probe evaluates the operator before the policy
has hidden the rows it should never see (`restriction_is_securely_promotable`
in the planner). PostGIS `&&` and `ST_Intersects`, ltree `<@` and `ILIKE` are
not leakproof. Btree comparisons on built-in types (uuid, text, bigint, double
precision) are.

`ALTER FUNCTION … LEAKPROOF` restores the GiST indexes but needs a real
superuser, which managed Postgres offerings do not grant, and ADR 38 forbids
running the API with owner credentials. The template has to work everywhere
the migrate role can run.

## Decision

Every hot predicate on a large tenant table gets a **key column a leakproof
comparison can answer**, derived on save exactly the way `sites.location` is
derived from the coordinates (ADR 50), and a btree that leads with `org_id`:

| Predicate | Key | Shape |
| --- | --- | --- |
| Map tile, viewport (ADR 49) | `cell bigint`: the zoom-20 Web Mercator tile, Morton-interleaved (`SpatialCells`) | a tile is one `[lo, hi)` range; a viewport is at most 16 merged ranges, made exact by `latitude`/`longitude` |
| Subtree scope, `under` | `path_text text COLLATE "C"` (`PathKeys`) | `path_text = p OR (path_text >= p‖'.' AND path_text < p‖'/')` |
| Search | `site_search_terms (org_id, site_id, term, name)`: every word of name and city, lower-cased, with the site's name | the first word's range on `(org_id, term, name, site_id)` is the page, walked in the index's own order (matched term, then name); further words are `EXISTS` probes per row; search means "a word starts with" |
| Paging | `(org_id, name, id)` | keyset: `after` is the cursor the previous page returned as `next`; offset paging is gone from the sites list and the listings feed |
| Counts and status filter | `(org_id, status)` | one index-only pass groups the whole result by status |

The tile renderer applies the tile as a cell range instead of intersecting a
geography envelope, which also fixes zoom 0 and 1: a 360° or 180° polygon is
empty or an antipodal error as geography, and simply the widest range as a
key. Cluster tiles (below a layer's point zoom) aggregate every point in
scope, so the composition root caches them for 60 s per org, scope, node and
tile - the org is in the key, never ambient (ADR 24).

`InScope()` uses the text range for any entity that implements
`IPathIndexed`, and the ltree predicate for the rest. Small tables keep ltree
and geography alone; the geography column stays everywhere for exactness and
distance (ADR 50 is unchanged).

## Consequences

- An integration test (`ScaleIndexTests`) explains each hot predicate **as
  `app_user`** with sequential scans disabled and refuses a plan that does not
  name the expected index. Check new predicates on tenant tables the same way;
  a plan taken as the migrate role proves nothing about production.
- Search is word-prefix, not substring: "depot" finds "Alpha Depot", "epot"
  does not. Substring search belongs in a search index, not in Postgres under
  RLS. Search results come back by matched term, then name, rather than by
  name alone: ordering by name would mean walking the org's names probing the
  term table for each, which is a scan whenever matches are sparse in name
  order. A search counts at most 10,000 hits and reports the total as a lower
  bound past that (`totalIsLowerBound`), because counting every hit of a
  common word is the one cost that still grows with the org.
- Contract change: `SiteListResponse.nextOffset` became `next` (an opaque
  cursor); `/api/sites` and `/api/listings/feed` take `after`; the feed also
  takes `limit` (default 500, max 2,000) and returns `next`.
- The migration backfills the keys with a temporary SQL twin of
  `SpatialCells.Key`; from then on the application writes them. A row whose
  keys disagree with its sources can only come from a write that bypassed
  `TenancyDbContext`, which the ingest and seed paths do not.
- Where a superuser is available, marking `geography_overlaps`,
  `st_intersects(geography, geography)`, `ltree_isparent` and `ltree_risparent`
  leakproof additionally lets the GiST indexes serve overlay polygons and any
  ad-hoc geography query; it is an operator's choice, not something the
  template depends on.

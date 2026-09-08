---
title: "Map viewport queries: the client sends a bounding box, the server resolves scope"
status: accepted
pinned: false
date: 2026-09-05
---

# 0049. Map viewport queries: the client sends a bounding box, the server resolves scope

## Decision

Site data is inherently map-based, and any entity managing sites wants a map
view of them, so the console's site library ships as two views of one query:
a data table and a map. The map is template surface, not fork territory
(ADR 43 settled that for the public locator; this settles it for the
console).

When the map is the view, **the client sends the viewport as a bounding box
and the server resolves what is in it.** The client never resolves the set
of sites itself and never sends site ids to describe what it is looking at.

The contract is one predicate added to the existing fleet list, applied in
the existing order - scope first, then the box, then search and filters,
then paging:

```
GET /api/sites?bbox=<west>,<south>,<east>,<north>&zoom=<z>&under=&q=&limit=
```

- `bbox` is WGS84 degrees, west/south/east/north. A box with `west > east`
  crosses the antimeridian and is evaluated as two longitude ranges. Any
  span wider than 90° on either axis is cut again: a geography polygon edge
  of 180° has no defined great circle and PostGIS refuses it, and a
  planet-sized box at zoom 0 is a legitimate request (up to eight envelopes,
  each an indexed `ST_Intersects`, OR-ed).
- The box is a filter over `NodeScope`, never a substitute for it: `InScope`
  runs first, the box runs on the rows scope allows. A box that covers the
  whole planet returns exactly what the unboxed list returns.
- Sites without coordinates are never inside a box. The map's adjoining list
  shows only sites in view and names the count of scoped sites that have no
  coordinates, so nothing vanishes silently; the table view still lists
  them.
- The response is the existing `SiteListResponse`: items capped at the
  existing `limit` ceiling (200), plus `total` for the box, plus
  `withoutCoordinates` - the count of sites the scope and search hold that
  can never be inside a box, present only on a bbox query. When `total`
  exceeds the cap the client shows the count and asks the user to zoom in;
  it does not page a map. `zoom` selects the clustering level for point
  layers (ADR 50); the contract carries it from the first version.
- **Selection is client state, keyed by site id.** Panning a selected site
  out of the box drops it from the list, not from the selection. A command
  over selected sites (compare hours, export) sends the ids explicitly, and
  the server checks those ids against scope on the way in, as it does for
  any write.

Client obligations, so the endpoint stays cheap:

- Snap the box outward to the tile grid at the current zoom before sending.
  Two users looking at roughly the same place then issue the same request,
  which is cacheable and stops a drag from producing a distinct query per
  frame.
- Query on the map's move-end event, debounced; never on move.
- On a phone the same rule applies to the bottom-sheet list under the map.
  Scope is chosen once (the scope chip) and the box is the visible map above
  the sheet, so the counted pins are the visible pins.

## Why

- **Gate 3 lives on the server.** Every site query takes a required
  `NodeScope` (CLAUDE.md, ADR 06). If the client resolved the in-view set,
  the server would still have to re-check every id against scope before
  answering, so the work is done twice and the client's copy is the untrusted
  one. Sending the box keeps one authority.
- **The client cannot know what is in the box without asking.** At 14 sites
  it could hold every coordinate; at 5,000 it cannot, and the map view is
  what keeps a fleet that size usable. A box is constant-size; an id list
  grows with the fleet and with the URL.
- **A box composes with what the list already does.** Scope, then search,
  then page is the existing chain; the box is one more predicate in it.
  Counts, caps, and later clustering all fall out of the server owning the
  set. An id list can never cluster.
- **It is what the mature implementations do.** Airbnb, Zillow, Google
  Places, and the Mapbox and Leaflet reference apps all send the viewport
  and let the server answer; the tile-snapping and move-end debounce are the
  two refinements they share.

## Consequences

- The predicate runs on the site's `geography(Point, 4326)` column with a
  GiST index: `ST_Intersects(location, ST_MakeEnvelope(w, s, e, n,
  4326)::geography)`. The column, the extension, the image changes and
  the layer model that grows from this endpoint are ADR 50's decision;
  this ADR only fixes the contract and the division of labour.
- The public locator's `near=lat,lng` (ADR 43) is unchanged. It answers
  "which is closest to me" for a guest over an unpaged public list; this ADR
  answers "what is on my screen" for a scoped user over a paged fleet. They
  are different questions and stay different parameters.
- The contract change lands in `openapi.json` and the generated client
  (ADR 16); review the snapshot diff like code. The table view and the map
  view consume the same generated call with and without `bbox`.
- ReUI's data grid defaults to row-index selection; the site library must
  configure id-keyed selection from the first commit, because selection
  surviving a pan depends on it.
- The console map is MapLibre GL JS (ADR 50), rendering the tiles that
  endpoint family serves; the public locator keeps Leaflet (ADR 43). No
  vendor account either way. Commercial tiles at volume remain fork
  territory, as before.

---
title: "Spatial foundation: PostGIS now, geography on sites, layers as vector tiles from the API"
status: accepted
pinned: true
date: 2026-09-05
---

# 0050. Spatial foundation: PostGIS now, geography on sites, layers as vector tiles from the API

## Decision

ADR 01 pinned PostgreSQL on three pillars and named PostGIS as one of them.
This ADR cashes that in. Site data is inherently spatial, and a template
whose only spatial support is two nullable doubles is under-serving the
thing it exists to manage.

### 1. PostGIS is a template dependency, adopted now

- The `postgis` extension is declared by the Tenancy context
  (`HasPostgresExtension("postgis")`, beside `ltree`) and created by the
  migrate role like every other schema change. The migrate role is the
  database owner (ADR 38). **`postgis.control` is not marked `trusted`**
  (verified on 3.5.4; `ltree` is), so creating it needs a superuser or a
  provider role that is allowed to install it. Local and test owners are
  superusers; AWS RDS, Cloud SQL, Neon and Supabase all permit it for the
  owner role. On a provider that does not, installing the extension is a
  one-time provisioning prerequisite in the runbook and the migrate step
  fails loudly, never silently.
- **One image, referenced from one place.** The official `postgis/postgis`
  images publish no arm64 manifests, which breaks `aspire run` and the
  integration suite on Apple Silicon. The template pins the multi-arch
  community build `imresamu/postgis:17-3.5-alpine` by digest, and the
  AppHost, both Testcontainers fixtures, `e2e-stack.sh` and
  `smoke-image.sh` read the same constant. A fork that prefers a
  first-party image changes one line.
- The EF provider gains `Npgsql.EntityFrameworkCore.PostgreSQL.NetTopologySuite`
  (10.0.x, matching the provider), wired once on the data source in
  Platform. Spatial types in application code are NetTopologySuite types;
  no module hand-writes WKT or raw geometry SQL outside the tile query.

### 2. Sites carry a geography, the API keeps latitude and longitude

- `sites` gets `location geography(Point, 4326)` with a GiST index. It is
  written by the entity in the same save as `Latitude`/`Longitude`, and
  backfilled from them in the migration; the two doubles stay the request and
  response shape, so the generated client (ADR 16) does not change for
  existing calls.
- **Geography, not geometry, for stored data.** Distances come back in
  meters and boxes are planet-correct without a projection decision per
  query. Geometry appears only inside tile generation, where
  `ST_AsMVTGeom` wants Web Mercator.
- The bounding-box list from ADR 49 is `ST_Intersects(location,
  ST_MakeEnvelope(w, s, e, n, 4326)::geography)`, one predicate on the
  indexed column, after `InScope`. The antimeridian split from ADR 49 stays a
  two-envelope union.
- The public locator's in-memory haversine (ADR 43) stays correct and stays
  in place. Moving it to `ST_Distance` is a small change that becomes
  worthwhile the day that list is paged; it is not part of this ADR.

### 3. Three kinds of layer, three different homes

**Basemaps are a client concern with an org setting.** The map component
gets a layer switcher. The template ships two open basemaps
(OpenStreetMap standard and a light/dark styled open set with its
attribution) and an org setting `map.basemaps`: an ordered list of
`{id, name, urlTemplate, attribution, maxZoom, keyRef}`. `keyRef` names a
secret held under the envelope-encryption seam (ADR 31); the browser still
receives the key in the tile URL, which is how every tile provider keys by
referrer, so the setting is a convenience, not a secrecy boundary. Paid
providers stay fork territory, as in ADR 43, but the shape is here so a fork
adds one entry rather than a feature.

**Org-drawn overlays are a new module, `Spatial`.** Territories, delivery
zones, trade areas, regions that mirror the hierarchy: shapes the org owns
and edits. `Spatial` sits above Tenancy on the ladder (ADR 37) and consumes
its node contracts; Tenancy consumes nothing from it. Its schema holds:

- `overlay_layers` - one owning org (ADR 48), name, kind, style JSON,
  optional hierarchy node the layer is anchored to.
- `overlay_features` - one owning org, `layer_id`, `geom geography(Geometry,
  4326)` with a GiST index, `properties jsonb`, and the ancestor `path`
  stamped at write time (ADR 02/04) so scope filters features exactly as it
  filters sites.

Both tables are tenant-scoped and get RLS in the same migration (the
`new-migration` skill). Deletion tier (ADR 25): overlays are user content,
soft-delete with restore. Editing is a console feature over these two
tables; the first version accepts GeoJSON upload and draw-in-place for
polygons, and nothing else.

**Data layers are queries, not tables.** Open-now, checklist completion,
ingest health, coverage: each is a registered query over Tenancy and module
facts joined to `sites.location`, declared in code as
`{name, capability, sql-or-linq}`. They are computed at tile time and never
materialized, so they cannot go stale and they carry no ownership question.

### 4. Layers are served by the API as vector tiles, behind the gates

```
GET /api/tiles/{layer}/{z}/{x}/{y}.mvt
```

- One endpoint, one gate call: the layer's declared capability through
  `Gate.RequireUserAsync`, then `NodeScope` applied to the layer's query,
  then `ST_AsMVTGeom` and `ST_AsMVT` in SQL. The tile is the scope-filtered
  answer, so what a user can see on the map is decided in the same place as
  everything else they can see.
- Point layers use `zoom` (reserved in ADR 49) to pick a clustering level:
  below the layer's declared `minPointZoom` the query aggregates with
  `ST_SnapToGrid` and returns cluster points with counts; at or above it,
  individual sites. The client renders both from the same tile.
- **Tiles are private.** `Cache-Control: private, max-age=60`, an `ETag`
  derived from the layer version and the scope hash. No shared cache, no CDN,
  no public tile URL, because scope differs per principal. A server-side
  cache keyed by `(org, region, scope hash, layer, z, x, y)` is a fork
  optimization, keyed exactly that way so org is never ambient.
- Overlay editing writes go through ordinary endpoints on the `Spatial`
  module; the tile endpoint is read-only.
- The public app does not get tiles in this ADR. The locator keeps its
  GeoJSON list; a public overlay layer (a delivery zone on the customer page)
  is the natural first extension and would be a `Guest`-capable layer on the
  same endpoint.

### 5. The console map is MapLibre GL JS; the public locator stays Leaflet

- The console renders its map with **MapLibre GL JS**: vector tiles are its
  native input, so the tile endpoint above needs no plugin, clustering
  output renders from the same tile, and style JSON is where basemaps and
  layer styling live. It is open source, vendor-neutral and works with
  OpenStreetMap-derived raster basemaps as well as vector ones, so the
  no-vendor-account stance from ADR 43 holds.
- The public locator keeps Leaflet (ADR 43). Its job is a modest, unpaged
  GeoJSON list on an SSR page where first paint matters and WebGL is a cost
  with no payoff. The two apps already differ in framework (ADR 15); they may
  differ in map library for the same reason. The day the public page needs a
  tile layer, it moves to MapLibre by the same argument, not before.
- One map component per app, each behind its app's own seam: the console's
  lives in `@locintel/ui` so pages import it from the barrel (ADR 20), takes a
  basemap id, a list of layer ids and a `NodeScope`-aware tile URL builder
  from the generated client, and owns the move-end debounce and tile-grid
  snapping that ADR 49 requires of clients. Nothing outside it knows the
  library.
- WebGL in the browser suite: headless Chromium renders MapLibre through
  SwiftShader, so the Playwright and axe pass (ADR 47) covers the map page;
  the map canvas itself is `aria-hidden` with the in-view list as the
  accessible equivalent, which is also what the phone sheet already is.

### 6. Seams left for forks, deliberately

- Raster imagery (floor plans, drone, campus maps) needs a tile pipeline and
  object storage semantics the Storage module (ADR 19) does not have.
  Fork territory; the overlay model is where a raster reference would hang.
- Paid basemaps and geocoding (ADR 43) stay keyed and out.
- An external tile server (Martin, pg_tileserv) is the right move only if
  tile volume outgrows the API. The SQL is the same; what moves is who runs
  it, and it would sit outside the gates, so that fork must bring its own
  authorization story.
- Routing and drive-time isochrones need a provider; great-circle and
  boxes are the template's honest ceiling.

## Why

- **The pinned decision already said so.** ADR 01 chose PostgreSQL partly
  for PostGIS. Leaving spatial support at two doubles meant the template
  carried the cost of the pin without its benefit.
- **One authority for what is visible.** The three gates decide what a
  principal sees. Serving tiles from the API with `NodeScope` applied keeps
  the map inside that rule; every alternative (client-side filtering, a
  separate tile server, public tiles) creates a second place where
  visibility is decided.
- **Geography keeps the maths honest.** Meters and planet-correct boxes by
  default; the projection question is confined to one function inside tile
  generation.
- **MVT is the interchange the ecosystem expects.** Leaflet (already in the
  public app, ADR 43), MapLibre and Mapbox all consume vector tiles; a
  Postgres query emitting MVT is the standard shape, so the layer registry
  is the only custom part.
- **Three homes because the three kinds differ in ownership.** Basemaps are
  someone else's data, overlays are the org's data, data layers are derived.
  Treating them alike would either give derived data an owner it does not
  need or make org shapes ephemeral.

## Consequences

- **Pinned**, because the geography column, the GiST indexes and the
  `Spatial` schema are data that a fork accumulates; reversing means a data
  migration, not a config change.
- Migration work in Tenancy: extension, column, index, backfill; in the new
  `Spatial` module: schema, two tables, RLS, arch-test registration, fixtures
  (the `new-module` skill). `Down()` must stay real (ADR 38): drop the column
  and index, never the extension while another schema could use it.
- Images: four places move from `postgres:17-alpine` to the pinned PostGIS
  image. The community image is a provenance trade-off the runbook names;
  pinning by digest is the mitigation. CI pulls a larger image once.
- The tile endpoint returns binary; it declares
  `[ProducesResponseType(typeof(byte[]), 200, "application/vnd.mapbox-vector-tile")]`
  so the typed-client ratchet passes and the generated client exposes it as a
  URL builder, which is what a map library wants anyway.
- Unit tests stay pure: NetTopologySuite geometry is plain logic and is
  allowed; anything touching `ST_*` is integration-proven against the
  PostGIS image.
- No tenant, site or layer name on a metric label (ADR 33); tile timings are
  labelled by `layer` only, and `layer` names are template constants, not org
  data.
- ADR 43 is unchanged in behavior and gains a note that its haversine has a
  SQL successor available. ADR 49's `zoom` parameter is now used.

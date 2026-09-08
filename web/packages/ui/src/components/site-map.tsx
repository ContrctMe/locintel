import type { FeatureCollection } from 'geojson';
import type * as MapLibre from 'maplibre-gl';
import { useEffect, useRef } from 'react';
import { cn } from '../lib/utils';

/**
 * The console map (ADR 50 §5): MapLibre GL JS behind the barrel. Pages hand
 * it a tile layer or points, a basemap, and a viewport callback; nothing
 * outside this file knows the library. The library and its stylesheet load
 * on demand the first time a map mounts, so pages that never show one pay
 * nothing and the module stays safe to import where there is no window.
 *
 * With `tiles`, sites come from the API's vector tiles (ADR 50 §4): the
 * server applies scope and clusters below its point zoom, and selection is
 * feature state keyed by the tile feature's id. Without `tiles`, `points`
 * are drawn as a GeoJSON layer. Either way `points` provide the bounds for
 * Fit to scope. `overlays` are the org's own shapes (ADR 50 §3), each its
 * own tile source drawn under the sites; they come and go without a reload.
 *
 * Accessibility: the canvas is presentational; the list beside the map is
 * the accessible equivalent (the page owns that). Basemaps are open and
 * keyless: OpenStreetMap's raster tiles, and OpenFreeMap's light and dark
 * vector styles - attribution is rendered by the map control.
 */
export type SiteMapPoint = {
  id: string;
  name: string;
  latitude: number;
  longitude: number;
  subtitle?: string;
  selected?: boolean;
};

export type SiteMapViewport = {
  west: number;
  south: number;
  east: number;
  north: number;
  zoom: number;
};

export type SiteMapBasemap = 'osm' | 'light' | 'dark';

/** An org-configured raster basemap (ADR 50 §3, the `map.basemaps` setting), keyed by its id. */
export type SiteMapRasterBasemap = {
  id: string;
  name: string;
  /** Absolute URL template with {z}/{x}/{y}; any provider key is already in place. */
  urlTemplate: string;
  attribution: string;
  maxZoom: number;
};

export type SiteMapTiles = {
  /** Absolute URL template with {z}/{x}/{y}; the API's tile endpoint. */
  url: string;
  /** The layer name inside the tile. */
  sourceLayer: string;
};

/** A colour per value of the tile feature's `status` property (a data layer's legend). */
export type SiteMapStatus = { key: string; color: string };

export type SiteMapOverlay = {
  id: string;
  /** Absolute URL template with {z}/{x}/{y}; the API's overlay tile endpoint. */
  url: string;
  sourceLayer: string;
  /** Style JSON from the layer: fill and stroke as CSS hex, opacity 0-1. */
  style?: { fill?: string; stroke?: string; opacity?: number } | null;
};

// cluster counts are text, and text needs a glyph source: the raster styles
// borrow OpenFreeMap's, the vector styles bring their own
const GLYPHS = 'https://tiles.openfreemap.org/fonts/{fontstack}/{range}.pbf';

const BASEMAPS: Record<SiteMapBasemap, string | MapLibre.StyleSpecification> = {
  osm: {
    version: 8,
    sources: {
      osm: {
        type: 'raster',
        tiles: ['https://tile.openstreetmap.org/{z}/{x}/{y}.png'],
        tileSize: 256,
        maxzoom: 19,
        attribution: '© <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
      },
    },
    layers: [{ id: 'osm', type: 'raster', source: 'osm' }],
    glyphs: GLYPHS,
  },
  light: 'https://tiles.openfreemap.org/styles/positron',
  dark: 'https://tiles.openfreemap.org/styles/dark',
};

/** The style for a built-in basemap, or an org raster entry by id; unknown ids fall back to light. */
function styleFor(
  basemap: string,
  rasters: readonly SiteMapRasterBasemap[],
): string | MapLibre.StyleSpecification {
  if (basemap in BASEMAPS) return BASEMAPS[basemap as SiteMapBasemap];
  const raster = rasters.find((r) => r.id === basemap);
  if (!raster) return BASEMAPS.light;
  return {
    version: 8,
    sources: {
      base: {
        type: 'raster',
        tiles: [raster.urlTemplate],
        tileSize: 256,
        maxzoom: raster.maxZoom,
        attribution: raster.attribution,
      },
    },
    layers: [{ id: 'base', type: 'raster', source: 'base' }],
    glyphs: GLYPHS,
  };
}

const SOURCE = 'locintel-sites';
const LAYER_HALO = 'locintel-sites-halo';
const LAYER_DOT = 'locintel-sites-dot';
const LAYER_CLUSTER = 'locintel-sites-cluster';
const LAYER_CLUSTER_COUNT = 'locintel-sites-cluster-count';
const OVERLAY_PREFIX = 'locintel-overlay-';
// MapLibre paints literal colors, not CSS tokens: the accent and neutral pin
// from tokens.css, in hex, for both schemes
const ACCENT = '#7c6cf0';
const NEUTRAL_DOT = '#5b5b66';
const STROKE = '#ffffff';

type Mode = 'points' | 'tiles';

type Props = {
  /** Sites in scope with coordinates; drawn when there are no tiles, and the bounds for fitting. */
  points: SiteMapPoint[];
  /** The API's vector tiles for the sites layer; when set, tiles are drawn instead of points. */
  tiles?: SiteMapTiles;
  /** Selected site ids (tile mode); in point mode each point carries `selected`. */
  selectedIds?: readonly string[];
  /** Colours for the tile feature's `status` (tile mode); dots without a match paint neutral. */
  statuses?: readonly SiteMapStatus[];
  /** Overlay layers to draw under the sites; an empty list draws none. */
  overlays?: readonly SiteMapOverlay[];
  /** A built-in basemap, or the id of one of `basemaps`. */
  basemap?: SiteMapBasemap | (string & {});
  /** Org-configured raster basemaps the id may name. */
  basemaps?: readonly SiteMapRasterBasemap[];
  /** Bump this to fit the view to the current points (Fit to scope). */
  fitKey?: number;
  /** After the user pans or zooms (debounced), and once after the first frame. */
  onViewportChange?: (viewport: SiteMapViewport) => void;
  onPointClick?: (id: string) => void;
  className?: string;
};

function toGeoJson(points: SiteMapPoint[]): FeatureCollection {
  return {
    type: 'FeatureCollection',
    features: points.map((p) => ({
      type: 'Feature',
      id: p.id,
      geometry: { type: 'Point', coordinates: [p.longitude, p.latitude] },
      properties: {
        id: p.id,
        name: p.name,
        subtitle: p.subtitle ?? '',
        selected: p.selected ? 1 : 0,
      },
    })),
  };
}

/** Selection reads from the feature property (points) or feature state (tiles). */
const selectedExpr = (mode: Mode): MapLibre.ExpressionSpecification =>
  mode === 'points'
    ? ['==', ['get', 'selected'], 1]
    : ['boolean', ['feature-state', 'selected'], false];

/** The dot's fill: the status colour when the layer has one, else the neutral pin. */
function dotColor(statuses: readonly SiteMapStatus[]): MapLibre.ExpressionSpecification | string {
  if (statuses.length === 0) return NEUTRAL_DOT;
  return [
    'match',
    ['coalesce', ['get', 'status'], ''],
    ...statuses.flatMap((s) => [s.key, s.color]),
    NEUTRAL_DOT,
  ] as unknown as MapLibre.ExpressionSpecification;
}

function addSitesLayers(
  map: MapLibre.Map,
  mode: Mode,
  data: FeatureCollection,
  tiles?: SiteMapTiles,
  statuses: readonly SiteMapStatus[] = [],
) {
  if (map.getSource(SOURCE)) return;
  if (mode === 'tiles' && tiles) {
    map.addSource(SOURCE, {
      type: 'vector',
      tiles: [tiles.url],
      minzoom: 0,
      maxzoom: 22,
      // the tile feature's `id` property becomes the feature id feature-state keys on
      promoteId: { [tiles.sourceLayer]: 'id' },
    });
  } else {
    map.addSource(SOURCE, { type: 'geojson', data });
  }
  const layerSource =
    mode === 'tiles' && tiles ? { source: SOURCE, 'source-layer': tiles.sourceLayer } : { source: SOURCE };
  const isCluster: MapLibre.ExpressionSpecification = ['>', ['coalesce', ['get', 'count'], 1], 1];
  const selected = selectedExpr(mode);

  if (mode === 'tiles') {
    map.addLayer({
      id: LAYER_CLUSTER,
      type: 'circle',
      ...layerSource,
      filter: isCluster,
      paint: {
        // radius on a log scale, so ten, ten thousand and a million read as
        // different sizes rather than all capping out
        'circle-radius': [
          'interpolate',
          ['linear'],
          ['log10', ['get', 'count']],
          0.3, 12,
          2, 18,
          4, 26,
          6, 34,
        ],
        'circle-color': NEUTRAL_DOT,
        'circle-opacity': 0.85,
        'circle-stroke-color': STROKE,
        'circle-stroke-width': 2,
      },
    });
    map.addLayer({
      id: LAYER_CLUSTER_COUNT,
      type: 'symbol',
      ...layerSource,
      filter: isCluster,
      layout: {
        'text-field': clusterLabel,
        'text-font': ['Noto Sans Regular'],
        'text-size': 12,
        'text-allow-overlap': true,
      },
      paint: { 'text-color': STROKE },
    });
  }
  map.addLayer({
    id: LAYER_HALO,
    type: 'circle',
    ...layerSource,
    // feature-state is not allowed in a filter, so the halo is always there
    // and selection turns its opacity on
    filter: ['!', isCluster],
    paint: {
      'circle-radius': 16,
      'circle-color': ACCENT,
      'circle-opacity': ['case', selected, 0.18, 0],
    },
  });
  map.addLayer({
    id: LAYER_DOT,
    type: 'circle',
    ...layerSource,
    filter: ['!', isCluster],
    paint: {
      'circle-radius': ['case', selected, 8, 6],
      // a status layer colours by status and marks selection on the ring;
      // without one the accent is the selection colour, as in point mode
      'circle-color':
        mode === 'tiles' && statuses.length > 0
          ? dotColor(statuses)
          : ['case', selected, ACCENT, NEUTRAL_DOT],
      'circle-stroke-color': mode === 'tiles' && statuses.length > 0 ? ['case', selected, ACCENT, STROKE] : STROKE,
      'circle-stroke-width': mode === 'tiles' && statuses.length > 0 ? ['case', selected, 3, 2] : 2,
    },
  });
}

// 1,234 -> "1.2k", 1,234,567 -> "1.2M"; below a thousand the number itself
const clusterLabel: MapLibre.ExpressionSpecification = [
  'case',
  ['>=', ['get', 'count'], 1000000],
  ['concat', ['to-string', ['/', ['round', ['/', ['get', 'count'], 100000]], 10]], 'M'],
  ['>=', ['get', 'count'], 1000],
  ['concat', ['to-string', ['/', ['round', ['/', ['get', 'count'], 100]], 10]], 'k'],
  ['to-string', ['get', 'count']],
];

const overlayKey = (o: SiteMapOverlay) => JSON.stringify([o.url, o.sourceLayer, o.style ?? null]);

/**
 * Bring the map's overlay sources in line with `overlays`: sources that are
 * gone or restyled are removed, new ones added under the sites layers.
 * `known` remembers what is on the map (source id -> key) because a style
 * swap drops everything and the caller resets it then.
 */
function syncOverlays(map: MapLibre.Map, overlays: readonly SiteMapOverlay[], known: Map<string, string>) {
  const wanted = new Map(overlays.map((o) => [OVERLAY_PREFIX + o.id, o] as const));
  for (const [id, key] of [...known]) {
    const o = wanted.get(id);
    if (o && overlayKey(o) === key) continue;
    for (const layer of [`${id}-fill`, `${id}-line`]) if (map.getLayer(layer)) map.removeLayer(layer);
    if (map.getSource(id)) map.removeSource(id);
    known.delete(id);
  }
  // sites stay on top: overlays slot in under the first sites layer present
  const before = [LAYER_CLUSTER, LAYER_HALO, LAYER_DOT].find((l) => map.getLayer(l));
  for (const [id, o] of wanted) {
    if (known.has(id)) continue;
    known.set(id, overlayKey(o));
    // `load` and `style.load` both fire on the first style: a source the
    // earlier pass added is registered, not added twice
    if (map.getSource(id)) continue;
    map.addSource(id, { type: 'vector', tiles: [o.url], minzoom: 0, maxzoom: 22 });
    const fill = o.style?.fill ?? ACCENT;
    const stroke = o.style?.stroke ?? fill;
    const opacity = o.style?.opacity ?? 0.15;
    map.addLayer(
      {
        id: `${id}-fill`,
        type: 'fill',
        source: id,
        'source-layer': o.sourceLayer,
        paint: { 'fill-color': fill, 'fill-opacity': opacity },
      },
      before,
    );
    map.addLayer(
      {
        id: `${id}-line`,
        type: 'line',
        source: id,
        'source-layer': o.sourceLayer,
        paint: { 'line-color': stroke, 'line-width': 1.5, 'line-opacity': 0.8 },
      },
      before,
    );
  }
}

function bounds(points: SiteMapPoint[]): [[number, number], [number, number]] | null {
  if (points.length === 0) return null;
  let west = Infinity, south = Infinity, east = -Infinity, north = -Infinity;
  for (const p of points) {
    west = Math.min(west, p.longitude);
    east = Math.max(east, p.longitude);
    south = Math.min(south, p.latitude);
    north = Math.max(north, p.latitude);
  }
  return [[west, south], [east, north]];
}

export function SiteMap({
  points,
  tiles,
  selectedIds = [],
  statuses = [],
  overlays = [],
  basemap = 'light',
  basemaps = [],
  fitKey = 0,
  onViewportChange,
  onPointClick,
  className,
}: Props) {
  const container = useRef<HTMLDivElement>(null);
  const mapRef = useRef<MapLibre.Map | null>(null);
  const pointsRef = useRef(points);
  const tilesRef = useRef(tiles);
  const selectionRef = useRef(selectedIds);
  const overlaysRef = useRef(overlays);
  const statusesRef = useRef(statuses);
  const basemapsRef = useRef(basemaps);
  // the style in force, so a basemap list refetch does not reset the map
  const styleKey = useRef('');
  // overlay sources on the map right now (source id -> key)
  const knownOverlays = useRef<Map<string, string>>(new Map());
  const viewportCb = useRef(onViewportChange);
  const clickCb = useRef(onPointClick);
  const lastFit = useRef(fitKey);
  // true once the view means something: fitted to points, or moved by the user
  const positioned = useRef(false);
  // ids currently flagged in feature state, to clear on the next change
  const flagged = useRef<Set<string>>(new Set());
  pointsRef.current = points;
  tilesRef.current = tiles;
  selectionRef.current = selectedIds;
  overlaysRef.current = overlays;
  statusesRef.current = statuses;
  basemapsRef.current = basemaps;
  viewportCb.current = onViewportChange;
  clickCb.current = onPointClick;
  const mode: Mode = tiles ? 'tiles' : 'points';

  const applySelection = (map: MapLibre.Map, ids: readonly string[]) => {
    const t = tilesRef.current;
    if (!t || !map.getSource(SOURCE)) return;
    const next = new Set(ids);
    for (const id of flagged.current)
      if (!next.has(id))
        map.setFeatureState({ source: SOURCE, sourceLayer: t.sourceLayer, id }, { selected: false });
    for (const id of next)
      map.setFeatureState({ source: SOURCE, sourceLayer: t.sourceLayer, id }, { selected: true });
    flagged.current = next;
  };

  // mount once: load the library, create the map, wire events
  useEffect(() => {
    let disposed = false;
    let timer: ReturnType<typeof setTimeout> | undefined;
    const el = container.current;
    if (!el) return;
    void (async () => {
      const [lib, , worker] = await Promise.all([
        import('maplibre-gl'),
        import('maplibre-gl/dist/maplibre-gl.css'),
        // Bundled as a chunk, the library cannot find its own script to spawn
        // its worker from (workerUrl resolves to ""), and a worker at the page
        // URL boots nothing: every tile and GeoJSON request then waits forever.
        // `?worker&url`: the bundler builds the worker entry as its own
        // self-contained script and hands back its URL - a plain `?url` copy
        // still imports shared chunks that touch `window` and dies on boot
        import('maplibre-gl/dist/maplibre-gl-worker.mjs?worker&url'),
      ]);
      if (disposed) return;
      lib.setWorkerUrl(worker.default);
      const initial = bounds(pointsRef.current);
      const map = new lib.Map({
        container: el,
        style: styleFor(basemap, basemapsRef.current),
        ...(initial
          ? { bounds: initial, fitBoundsOptions: { padding: 48, maxZoom: 13 } }
          : { center: [-98, 39], zoom: 3 }),
        attributionControl: { compact: false },
      });
      positioned.current = initial !== null;
      map.addControl(new lib.NavigationControl({ showCompass: false }), 'top-right');
      const emitViewport = () => {
        if (timer) clearTimeout(timer);
        timer = setTimeout(() => {
          const b = map.getBounds();
          viewportCb.current?.({
            west: b.getWest(),
            south: b.getSouth(),
            east: b.getEast(),
            north: b.getNorth(),
            zoom: map.getZoom(),
          });
        }, 250);
      };
      const addLayers = () => {
        addSitesLayers(map, mode, toGeoJson(pointsRef.current), tilesRef.current, statusesRef.current);
        flagged.current = new Set();
        applySelection(map, selectionRef.current);
        // a fresh style has no overlay sources, whatever was known before
        knownOverlays.current = new Map();
        syncOverlays(map, overlaysRef.current, knownOverlays.current);
      };
      map.on('load', () => {
        addLayers();
        // the list follows the map from the first frame, not the first drag
        if (positioned.current) emitViewport();
      });
      map.on('style.load', addLayers);
      map.on('movestart', (e) => {
        if ((e as { originalEvent?: unknown }).originalEvent) positioned.current = true;
      });
      map.on('moveend', emitViewport);
      map.on('click', LAYER_DOT, (e) => {
        const id = e.features?.[0]?.properties?.id as string | undefined;
        if (id) clickCb.current?.(id);
      });
      map.on('click', LAYER_CLUSTER, (e) => {
        // a cluster opens up on click: two zoom levels closer, centred on it
        const geometry = e.features?.[0]?.geometry;
        if (geometry && geometry.type === 'Point') {
          positioned.current = true;
          map.easeTo({ center: geometry.coordinates as [number, number], zoom: map.getZoom() + 2 });
        }
      });
      for (const layer of [LAYER_DOT, LAYER_CLUSTER, LAYER_CLUSTER_COUNT]) {
        map.on('mouseenter', layer, () => {
          map.getCanvas().style.cursor = 'pointer';
        });
        map.on('mouseleave', layer, () => {
          map.getCanvas().style.cursor = '';
        });
      }
      mapRef.current = map;
      // test hook: browser suites and a console can reach the instance
      // through the element; nothing in the app reads it
      (el as HTMLDivElement & { __map?: MapLibre.Map }).__map = map;
    })();
    return () => {
      disposed = true;
      if (timer) clearTimeout(timer);
      mapRef.current?.remove();
      mapRef.current = null;
    };
    // the basemap is applied through setStyle below; the map is created once
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // points changed: update the GeoJSON source in place (point mode); if the
  // map was created before its points existed, the first points position it
  useEffect(() => {
    const map = mapRef.current;
    if (map && mode === 'points') {
      const source = map.getSource(SOURCE) as MapLibre.GeoJSONSource | undefined;
      source?.setData(toGeoJson(points));
    }
    const b = bounds(points);
    if (map && b && !positioned.current) {
      positioned.current = true;
      map.fitBounds(b, { padding: 48, maxZoom: 13, duration: 0 });
    }
  }, [points, mode]);

  // selection changed (tile mode): flag feature state
  useEffect(() => {
    const map = mapRef.current;
    if (map) applySelection(map, selectedIds);
    // applySelection reads refs; selectedIds is the input
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedIds]);

  // the tile layer or its legend changed: rebuild the sites source and layers
  useEffect(() => {
    const map = mapRef.current;
    if (!map || mode !== 'tiles' || !tiles || !map.getSource(SOURCE)) return;
    for (const layer of [LAYER_CLUSTER_COUNT, LAYER_CLUSTER, LAYER_HALO, LAYER_DOT]) if (map.getLayer(layer)) map.removeLayer(layer);
    map.removeSource(SOURCE);
    addSitesLayers(map, mode, toGeoJson(pointsRef.current), tiles, statuses);
    flagged.current = new Set();
    applySelection(map, selectionRef.current);
    // the overlays keep their place: they were added before the sites layers,
    // and a re-added sites source draws on top of them regardless
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tiles, statuses, mode]);

  // overlays changed: add, drop or restyle their sources in place
  useEffect(() => {
    const map = mapRef.current;
    if (map && map.getSource(SOURCE)) syncOverlays(map, overlays, knownOverlays.current);
  }, [overlays]);

  // basemap changed: swap the style, layers are re-added on style.load
  useEffect(() => {
    const map = mapRef.current;
    const style = styleFor(basemap, basemaps);
    const key = JSON.stringify(style);
    if (!styleKey.current) styleKey.current = key; // the style the map was created with
    if (map && map.isStyleLoaded() && key !== styleKey.current) {
      styleKey.current = key;
      map.setStyle(style);
    }
  }, [basemap, basemaps]);

  // fit requested
  useEffect(() => {
    if (fitKey === lastFit.current) return;
    lastFit.current = fitKey;
    const map = mapRef.current;
    const b = bounds(points);
    if (map && b) {
      positioned.current = true;
      map.fitBounds(b, { padding: 48, maxZoom: 13, duration: 400 });
    }
  }, [fitKey, points]);

  return (
    <div
      ref={container}
      role="presentation"
      aria-hidden
      className={cn('h-[560px] w-full bg-muted [&_.maplibregl-ctrl-attrib]:text-[11px]', className)}
    />
  );
}

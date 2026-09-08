/**
 * The client obligations ADR 49 puts on a map viewport before it becomes a
 * query: snap the box OUTWARD to the Web Mercator tile grid at the current
 * zoom, so two users looking at roughly the same place issue the same
 * request and a drag does not produce a distinct query per frame; and format
 * it as the endpoint's `west,south,east,north`. Pure functions, unit-tested.
 */
export type Viewport = {
  west: number;
  south: number;
  east: number;
  north: number;
  zoom: number;
};

export type Box = Pick<Viewport, 'west' | 'south' | 'east' | 'north'>;

/** Web Mercator's usable latitude; beyond it the projection is undefined. */
const MAX_LAT = 85.05112878;

const clampLat = (lat: number) => Math.max(-MAX_LAT, Math.min(MAX_LAT, lat));

function lonToTileX(lon: number, z: number) {
  return ((lon + 180) / 360) * 2 ** z;
}

function latToTileY(lat: number, z: number) {
  const rad = (clampLat(lat) * Math.PI) / 180;
  return ((1 - Math.log(Math.tan(rad) + 1 / Math.cos(rad)) / Math.PI) / 2) * 2 ** z;
}

function tileXToLon(x: number, z: number) {
  return (x / 2 ** z) * 360 - 180;
}

function tileYToLat(y: number, z: number) {
  const n = Math.PI - (2 * Math.PI * y) / 2 ** z;
  return (180 / Math.PI) * Math.atan(0.5 * (Math.exp(n) - Math.exp(-n)));
}

/**
 * Expand a viewport to whole tiles at the integer zoom it was seen at. A
 * world-wide view (more than 360° across) collapses to the whole planet
 * rather than a box that crosses itself.
 */
export function snapToTileGrid(v: Viewport): Box {
  const z = Math.max(0, Math.floor(v.zoom));
  if (v.east - v.west >= 360) {
    return { west: -180, south: -MAX_LAT, east: 180, north: MAX_LAT };
  }
  const tiles = 2 ** z;
  const x1 = Math.floor(lonToTileX(v.west, z));
  const x2 = Math.ceil(lonToTileX(v.east, z));
  const y1 = Math.floor(latToTileY(v.north, z));
  const y2 = Math.ceil(latToTileY(v.south, z));
  return {
    west: Math.max(-180, tileXToLon(x1, z)),
    east: Math.min(180, tileXToLon(Math.min(x2, tiles), z)),
    north: tileYToLat(Math.max(0, y1), z),
    south: tileYToLat(Math.min(y2, tiles), z),
  };
}

/** The endpoint's form: west,south,east,north, five decimals (about a metre). */
export function bboxParam(box: Box): string {
  const f = (n: number) => n.toFixed(5).replace(/\.?0+$/, '');
  return `${f(box.west)},${f(box.south)},${f(box.east)},${f(box.north)}`;
}

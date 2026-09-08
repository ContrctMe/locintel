import { describe, expect, it } from 'vitest';
import { bboxParam, snapToTileGrid } from './map';

describe('snapToTileGrid', () => {
  it('expands a viewport outward to whole tiles at the integer zoom', () => {
    // Portland at zoom 12.4: tiles are ~0.088° wide at z12
    const box = snapToTileGrid({
      west: -122.7,
      south: 45.45,
      east: -122.55,
      north: 45.55,
      zoom: 12.4,
    });
    expect(box.west).toBeLessThanOrEqual(-122.7);
    expect(box.east).toBeGreaterThanOrEqual(-122.55);
    expect(box.south).toBeLessThanOrEqual(45.45);
    expect(box.north).toBeGreaterThanOrEqual(45.55);
    // and by less than one tile on each side
    expect(-122.7 - box.west).toBeLessThan(360 / 2 ** 12);
    expect(box.east - -122.55).toBeLessThan(360 / 2 ** 12);
  });

  it('is stable: any viewport inside a snapped box snaps to that same box', () => {
    const box = snapToTileGrid({ west: -122.7, south: 45.45, east: -122.55, north: 45.55, zoom: 12 });
    // shrink the snapped box a little on every side: still inside the same tiles
    const w = box.east - box.west;
    const h = box.north - box.south;
    const inner = snapToTileGrid({
      west: box.west + w * 0.1,
      east: box.east - w * 0.1,
      south: box.south + h * 0.1,
      north: box.north - h * 0.1,
      zoom: 12,
    });
    expect(inner).toEqual(box);
    // and snapping is idempotent
    expect(snapToTileGrid({ ...box, zoom: 12 })).toEqual(box);
  });

  it('a world-wide view is the whole planet, never a self-crossing box', () => {
    const box = snapToTileGrid({ west: -400, south: -80, east: 400, north: 80, zoom: 0.5 });
    expect(box).toEqual({ west: -180, south: -85.05112878, east: 180, north: 85.05112878 });
  });

  it('clamps to the projection edges instead of running past them', () => {
    const box = snapToTileGrid({ west: -179.9, south: -89, east: 179.9, north: 89, zoom: 1 });
    expect(box.west).toBe(-180);
    expect(box.east).toBe(180);
    expect(box.north).toBeLessThanOrEqual(85.06);
    expect(box.south).toBeGreaterThanOrEqual(-85.06);
  });
});

describe('bboxParam', () => {
  it('formats west,south,east,north with trailing zeros trimmed', () => {
    expect(bboxParam({ west: -122.8, south: 45.4, east: -122.5, north: 45.6 })).toBe(
      '-122.8,45.4,-122.5,45.6',
    );
    expect(bboxParam({ west: -122.123456, south: 0, east: 10, north: 45.5 })).toBe(
      '-122.12346,0,10,45.5',
    );
  });
});

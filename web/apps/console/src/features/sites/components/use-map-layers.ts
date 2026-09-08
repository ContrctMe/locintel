import { type SiteMapOverlay } from '@locintel/ui';
import { useCallback, useMemo } from 'react';
import { usePreference } from '../../../lib/preference';
import { can } from '../../../session';
import { overlaysApi } from '../../overlays/api';
import { useOverlays } from '../../overlays/hooks';
import { sitesApi } from '../api';
import { useBasemaps, useDataLayers } from '../hooks';
import type { Me } from '../../../session';

/**
 * Everything the map draws besides the sites (ADR 50 §3): the data layer,
 * the org's overlays and which are hidden, and the basemap - each a
 * preference this browser keeps. Only asked for while the map is showing.
 */
export function useMapLayers({
  active,
  under,
  theme,
  me,
}: {
  active: boolean;
  under: string | null;
  theme: string;
  me: Me | undefined;
}) {
  // the map draws one data layer's tiles (ADR 50 §3-4): a registered query
  // over the sites, scope and clustering the server's; the console's chosen
  // node rides along as `under`. Sites is the default; the rest are offered
  // by the registry for principals who may read them
  const layersQuery = useDataLayers(active);
  const dataLayers = layersQuery.data?.layers ?? [];
  const [layerChoice, chooseLayer] = usePreference('map.layer', 'sites');
  const dataLayer =
    dataLayers.find((l) => l.name === layerChoice) ?? dataLayers.find((l) => l.name === 'sites');
  const layerName = dataLayer?.name ?? 'sites';
  const tiles = useMemo(
    () => ({ url: sitesApi.tiles(layerName, under), sourceLayer: layerName }),
    [layerName, under],
  );
  const statuses = useMemo(
    () => (dataLayer?.statuses ?? []).map((s) => ({ key: s.key, color: s.color })),
    [dataLayer],
  );
  // the org's overlays (ADR 50 §3) ride under the sites; each is its own
  // tile source, and the viewer decides which are showing
  const canSeeOverlays = can(me, 'overlays:read');
  const overlaysQuery = useOverlays(canSeeOverlays && active);
  const [hiddenList, setHiddenList] = usePreference<string[]>('sites.overlays.hidden', []);
  const hiddenOverlays = useMemo(() => new Set(hiddenList), [hiddenList]);
  const toggleOverlay = useCallback(
    (id: string) =>
      setHiddenList(hiddenList.includes(id) ? hiddenList.filter((h) => h !== id) : [...hiddenList, id]),
    [hiddenList, setHiddenList],
  );
  // the basemap: the theme's own by default, OpenStreetMap, or one the org configured
  const basemapsQuery = useBasemaps(active);
  const rasters = useMemo(
    () => (basemapsQuery.data?.basemaps ?? []).map((r) => ({ ...r })),
    [basemapsQuery.data],
  );
  const [basemapChoice, chooseBasemap] = usePreference('map.basemap', 'auto');
  const knownChoice =
    basemapChoice === 'auto' ||
    basemapChoice === 'osm' ||
    !basemapsQuery.data ||
    rasters.some((r) => r.id === basemapChoice);
  const basemap =
    basemapChoice === 'auto' || !knownChoice ? (theme === 'dark' ? 'dark' : 'light') : basemapChoice;
  const basemapChoices = [
    { id: 'auto', name: 'Match theme' },
    { id: 'osm', name: 'OpenStreetMap' },
    ...rasters.map((r) => ({ id: r.id, name: r.name })),
  ];
  const overlayLayers = useMemo(
    () => (overlaysQuery.data?.layers ?? []).filter((l) => l.featureCount > 0),
    [overlaysQuery.data],
  );
  const overlays = useMemo<SiteMapOverlay[]>(
    () =>
      overlayLayers
        .filter((l) => !hiddenOverlays.has(l.id))
        .map((l) => ({
          id: l.id,
          url: overlaysApi.tiles(l.id),
          sourceLayer: 'overlay',
          style: (l.style ?? null) as SiteMapOverlay['style'],
        })),
    [overlayLayers, hiddenOverlays],
  );
  return { dataLayers, layerName, chooseLayer, dataLayer, tiles, statuses, canSeeOverlays, overlayLayers, hiddenOverlays, toggleOverlay, overlays, rasters, basemapChoice, chooseBasemap, knownChoice, basemap, basemapChoices };
}

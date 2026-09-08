import { useInfiniteQuery, useQuery, useQueryClient } from '@tanstack/react-query';
import { sitesApi } from './api';

/** Site edits and hours/closure edits can all rebuild the async windows projection. */
export function useRefreshSite(siteId: string) {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: ['site', siteId] });
    void queryClient.invalidateQueries({ queryKey: ['schedules', siteId] });
    void queryClient.invalidateQueries({ queryKey: ['windows', siteId] });
  };
}

export function useSites(
  filter: string,
  under: string | null = null,
  bbox?: string,
  zoom?: number,
  status?: string,
) {
  return useInfiniteQuery({
    queryKey: ['sites', 'list', filter, under, bbox ?? null, zoom ?? null, status ?? null],
    queryFn: ({ pageParam, signal }) =>
      sitesApi.list(50, pageParam, filter || undefined, under ?? undefined, bbox, zoom, signal, status || undefined),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => (last.next == null ? undefined : String(last.next)),
  });
}

// the one hierarchy query (features/hierarchy): re-exported for the pickers
export { useHierarchy } from '../hierarchy/hooks';

// an org setting, edited on the settings page: fresh enough for a session
export const useBasemaps = (enabled = true) =>
  useQuery({
    queryKey: ['basemaps'],
    queryFn: ({ signal }) => sitesApi.basemaps(signal),
    enabled,
    staleTime: 5 * 60_000,
  });

// the registry is code: what changes is who may see which layer
export const useDataLayers = (enabled = true) =>
  useQuery({
    queryKey: ['map-layers'],
    queryFn: ({ signal }) => sitesApi.layers(signal),
    enabled,
    staleTime: 5 * 60_000,
  });

export const useSite = (id: string) =>
  useQuery({ queryKey: ['site', id], queryFn: ({ signal }) => sitesApi.get(id, signal) });

export const useSiteSchedules = (id: string) =>
  useQuery({ queryKey: ['schedules', id], queryFn: ({ signal }) => sitesApi.schedules(id, signal) });

export const useSiteWindows = (id: string) =>
  useQuery({
    queryKey: ['windows', id],
    queryFn: ({ signal }) => sitesApi.windows(id, signal),
    // Async rebuilds can finish after invalidation; an empty result is valid too.
    // ponytail: 30 reads/min per visible preview; use projection notifications
    // if measured traffic warrants replacing foreground polling.
    refetchInterval: 2000,
  });

export const useSiteAttributes = () =>
  useQuery({ queryKey: ['site-attributes'], queryFn: ({ signal }) => sitesApi.attributes(signal) });

export const useSiteClosures = (id: string) =>
  useQuery({ queryKey: ['closures', id], queryFn: ({ signal }) => sitesApi.closures(id, signal) });

/** Metadata only for the records a product view actually displays. */
export function useSiteMetadata(ids: string[]) {
  const unique = [...new Set(ids)].sort();
  return useQuery({
    queryKey: ['sites', 'metadata', unique],
    queryFn: ({ signal }) => sitesApi.byIds(unique, signal),
    enabled: unique.length > 0,
  });
}

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

export function useSites(filter: string) {
  return useInfiniteQuery({
    queryKey: ['sites', 'list', filter],
    queryFn: ({ pageParam, signal }) => sitesApi.list(50, pageParam, filter || undefined, signal),
    initialPageParam: 0,
    getNextPageParam: (last) =>
      last.nextOffset == null ? undefined : Number(last.nextOffset),
  });
}

export const useHierarchy = () =>
  useQuery({ queryKey: ['hierarchy'], queryFn: ({ signal }) => sitesApi.hierarchy(signal) });

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

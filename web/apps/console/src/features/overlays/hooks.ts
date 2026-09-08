import { useQuery } from '@tanstack/react-query';
import { overlaysApi } from './api';

/** The org's overlay layers; off for principals without overlays:read (no 403 to swallow). */
export const useOverlays = (enabled = true, deleted = false) =>
  useQuery({
    queryKey: ['overlays', deleted],
    queryFn: ({ signal }) => overlaysApi.list(deleted || undefined, signal),
    enabled,
    staleTime: 60_000,
  });

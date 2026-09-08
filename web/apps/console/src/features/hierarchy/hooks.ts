import { api } from '@locintel/api';
import { useQuery } from '@tanstack/react-query';
import { can, useMe } from '../../session';

export type Hierarchy = Awaited<ReturnType<typeof fetchHierarchy>>;
const fetchHierarchy = (signal?: AbortSignal) => api.get('/api/hierarchy', { signal });

/**
 * The org's hierarchy: one query for the shell's Scope tree, the pickers,
 * and the Hierarchy page (code review, 2026-09 - four definitions of the
 * same key disagreed about freshness). 404 means the org has none yet, so
 * there is no retry; a hierarchy edit invalidates the key. Private hierarchy
 * data stays in the session-owned in-memory query cache, never browser storage.
 */
export function useHierarchy(options: { enabled?: boolean } = {}) {
  const { data: me } = useMe();
  return useQuery({
    queryKey: ['hierarchy'],
    queryFn: ({ signal }) => fetchHierarchy(signal),
    enabled: (options.enabled ?? true) && can(me, 'sites:read'),
    retry: false,
    staleTime: 5 * 60_000,
  });
}

import { api } from '@locintel/api';
import { Button, Card, CardContent, FormDialog, Input, Label, Select,
  Table, TableBody, TableCell, TableHead, TableHeader, TableRow, TimeZoneSelect } from '@locintel/ui';
import { useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useState } from 'react';
import { useApiMutation } from '../lib/mutation';
import type { Page } from '../lib/paging';
import { can, useMe } from '../session';
import { StatusBadge } from '../shell';

type Site = { id: string; nodeId: string; name: string; timeZone: string; status: string; path: string };
type Hierarchy = { id: string; nodes: { id: string; name: string; depth: number }[] };

export function SitesPage() {
  const { data: me } = useMe();
  const [filter, setFilter] = useState('');
  const sitesQuery = useInfiniteQuery({
    queryKey: ['sites', 'list', filter],
    queryFn: ({ pageParam }) =>
      api.get<Page<Site>>(
        `/api/sites?limit=50&offset=${pageParam}${filter ? `&q=${encodeURIComponent(filter)}` : ''}`,
      ),
    initialPageParam: 0,
    getNextPageParam: (last) => last.nextOffset ?? undefined,
  });
  const sites = sitesQuery.data?.pages.flatMap((p) => p.items);
  const total = sitesQuery.data?.pages[0]?.total;
  const { data: hierarchy } = useQuery({
    queryKey: ['hierarchy'],
    queryFn: () => api.get<Hierarchy>('/api/hierarchy'),
  });
  const [name, setName] = useState('');
  const [timeZone, setTimeZone] = useState('America/New_York');
  const [nodeId, setNodeId] = useState('');
  const [creating, setCreating] = useState(false);

  const create = useApiMutation({
    mutationFn: () => api.post('/api/sites', { nodeId, name, timeZone }),
    invalidate: [['sites']],
    success: 'Site created',
    onSuccess: () => {
      setName('');
      setCreating(false);
    },
  });

  const nodeName = (id: string) =>
    hierarchy?.nodes.find((n) => n.id === id)?.name ?? '—';

  return (
    <div className="max-w-4xl space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Sites</h1>
        {can(me, 'sites:manage') && (
          <FormDialog
            open={creating}
            onOpenChange={setCreating}
            trigger={<Button>New site</Button>}
            title="New site"
            description="A site is a physical location, placed on a hierarchy node."
          >
            <div className="space-y-3">
              <div className="space-y-1">
                <Label htmlFor="site-name">Name</Label>
                <Input id="site-name" value={name} onChange={(e) => setName(e.target.value)} />
              </div>
              <div className="space-y-1">
                <Label htmlFor="site-tz">Time zone</Label>
                <TimeZoneSelect id="site-tz" value={timeZone}
                  onChange={(e) => setTimeZone(e.target.value)} />
              </div>
              <div className="space-y-1">
                <Label htmlFor="site-node">Hierarchy node</Label>
                <Select id="site-node" value={nodeId}
                  onChange={(e) => setNodeId(e.target.value)}>
                  <option value="">Choose…</option>
                  {hierarchy?.nodes.map((n) => (
                    <option key={n.id} value={n.id}>
                      {' '.repeat(n.depth * 2)}{n.name}
                    </option>
                  ))}
                </Select>
              </div>
              <Button className="w-full" disabled={!name || !nodeId || create.isPending}
                onClick={() => create.mutate()}>
                Create site
              </Button>
            </div>
          </FormDialog>
        )}
      </div>
      <Card>
        <CardContent className="pt-4">
          {((total ?? 0) > 5 || filter) && (
            <Input
              className="mb-3 max-w-xs"
              placeholder="Search name or city…"
              value={filter}
              onChange={(e) => setFilter(e.target.value)}
            />
          )}
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Name</TableHead>
                <TableHead>Node</TableHead>
                <TableHead>Time zone</TableHead>
                <TableHead>Status</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {sites?.map((s) => (
                <TableRow key={s.id}>
                  <TableCell className="font-medium">
                    <Link
                      to="/sites/$siteId"
                      params={{ siteId: s.id }}
                      className="underline-offset-4 hover:underline"
                    >
                      {s.name}
                    </Link>
                  </TableCell>
                  <TableCell className="text-muted-foreground">{nodeName(s.nodeId)}</TableCell>
                  <TableCell className="text-muted-foreground">{s.timeZone}</TableCell>
                  <TableCell><StatusBadge status={s.status} /></TableCell>
                </TableRow>
              ))}
              {sites === undefined && (
                <TableRow>
                  <TableCell colSpan={4} className="text-center text-muted-foreground">
                    Loading…
                  </TableCell>
                </TableRow>
              )}
              {sites && sites.length === 0 && (
                <TableRow>
                  <TableCell colSpan={4} className="text-center text-muted-foreground">
                    {filter
                      ? 'No sites match the search.'
                      : `No sites in scope. ${can(me, 'sites:manage') ? 'Create one with "New site".' : ''}`}
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
          {sitesQuery.hasNextPage && (
            <div className="pt-3 text-center">
              <Button variant="outline" size="sm"
                disabled={sitesQuery.isFetchingNextPage}
                onClick={() => void sitesQuery.fetchNextPage()}>
                Load more ({sites?.length} of {total})
              </Button>
            </div>
          )}
        </CardContent>
      </Card>

    </div>
  );
}

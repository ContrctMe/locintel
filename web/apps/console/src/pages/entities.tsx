import { api } from '@locintel/api';
import { Button, Card, CardContent, FormDialog, Input, Label, Select, Table, TableBody,
  TableCell, TableHead, TableHeader, TableRow, Textarea } from '@locintel/ui';
import { useInfiniteQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useState } from 'react';
import { fmtDate } from '../lib/format';
import { useApiMutation } from '../lib/mutation';
import type { Page } from '../lib/paging';
import { can, useMe } from '../session';

export type EntitySummary = {
  id: string; kind: string; status: string; displayName: string; aliases: string[];
  linkCount: number; expiresAt: string; legalHold: boolean; deletedAt: string | null;
};

export const ENTITY_KINDS = ['Person', 'Vehicle', 'Group'] as const;
export const ENTITY_STATUSES = ['Suspected', 'Confirmed', 'Cleared'] as const;
export const LINK_ROLES = ['Suspect', 'Victim', 'Witness', 'Associate', 'VehicleUsed', 'Other'] as const;

export function EntityStatusBadge({ status }: { status: string }) {
  const tone =
    status === 'Confirmed' ? 'bg-destructive/15 text-destructive'
      : status === 'Cleared' ? 'bg-muted text-muted-foreground'
        : 'bg-amber-500/20 text-amber-700 dark:text-amber-300';
  return <span className={`rounded px-1.5 py-0.5 text-xs font-medium ${tone}`}>{status}</span>;
}

/** Parse "key: value" lines into a descriptors object. */
export function parseDescriptors(text: string): Record<string, string> {
  const out: Record<string, string> = {};
  for (const line of text.split('\n')) {
    const i = line.indexOf(':');
    if (i <= 0) continue;
    const key = line.slice(0, i).trim();
    const value = line.slice(i + 1).trim();
    if (key && value) out[key] = value;
  }
  return out;
}

/**
 * Persons of interest, vehicles, groups (blueprint module 2). What you see
 * here already passed the need-to-know gate: the server lists only entities
 * linked to incidents in your scope, granted to you, or everything if you
 * hold entities:manage.
 */
export function EntitiesPage() {
  const { data: me } = useMe();
  const manage = can(me, 'entities:manage');
  const [kind, setKind] = useState('');
  const [status, setStatus] = useState('');
  const [q, setQ] = useState('');
  const [trash, setTrash] = useState(false);

  const params = new URLSearchParams({ limit: '50' });
  if (kind) params.set('kind', kind);
  if (status) params.set('status', status);
  if (q.trim()) params.set('q', q.trim());
  if (trash) params.set('trash', 'true');
  const query = params.toString();
  const entities = useInfiniteQuery({
    queryKey: ['entities', 'list', query],
    queryFn: ({ pageParam }) =>
      api.get<Page<EntitySummary>>(`/api/entities?${query}&offset=${pageParam}`),
    initialPageParam: 0,
    getNextPageParam: (last) => last.nextOffset ?? undefined,
  });
  const items = entities.data?.pages.flatMap((p) => p.items);
  const total = entities.data?.pages[0]?.total;
  const sweep = useApiMutation({
    mutationFn: () => api.post('/api/entities/retention/sweep'),
    invalidate: [['entities']],
    success: 'Retention sweep queued',
  });

  return (
    <div className="max-w-5xl space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold">People &amp; vehicles</h1>
        <div className="flex gap-2">
          {manage && (
            <Button variant="outline" disabled={sweep.isPending} onClick={() => sweep.mutate()}>
              Run retention sweep
            </Button>
          )}
          {manage && <CreateEntityDialog />}
        </div>
      </div>
      <p className="text-sm text-muted-foreground">
        Records here are allegations about real people. Every detail view is logged; records expire
        on their retention date unless under legal hold.
      </p>
      <div className="flex flex-wrap gap-2">
        <Input className="w-56" placeholder="Search name or alias" value={q}
          onChange={(e) => setQ(e.target.value)} />
        <Select className="w-36" value={kind} onChange={(e) => setKind(e.target.value)}>
          <option value="">Any kind</option>
          {ENTITY_KINDS.map((k) => <option key={k} value={k}>{k}</option>)}
        </Select>
        <Select className="w-40" value={status} onChange={(e) => setStatus(e.target.value)}>
          <option value="">Any status</option>
          {ENTITY_STATUSES.map((s) => <option key={s} value={s}>{s}</option>)}
        </Select>
        {manage && (
          <label className="flex items-center gap-2 text-sm">
            <input type="checkbox" className="size-4 accent-primary" checked={trash}
              onChange={(e) => setTrash(e.target.checked)} />
            Trash
          </label>
        )}
      </div>
      <Card>
        <CardContent className="p-0">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Name</TableHead>
                <TableHead>Kind</TableHead>
                <TableHead>Status</TableHead>
                <TableHead className="text-right">Incidents</TableHead>
                <TableHead>Expires</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {items?.length === 0 && (
                <TableRow>
                  <TableCell colSpan={5} className="text-center text-sm text-muted-foreground">
                    {trash ? 'The trash is empty.' : 'Nothing you can see matches.'}
                  </TableCell>
                </TableRow>
              )}
              {items?.map((e) => (
                <TableRow key={e.id}>
                  <TableCell>
                    <Link to="/entities/$entityId" params={{ entityId: e.id }}
                      className="font-medium hover:underline">
                      {e.displayName}
                    </Link>
                    {e.aliases.length > 0 && (
                      <span className="ml-2 text-xs text-muted-foreground">aka {e.aliases.join(', ')}</span>
                    )}
                    {e.legalHold && <span className="ml-2 text-xs text-muted-foreground">· legal hold</span>}
                  </TableCell>
                  <TableCell className="text-sm">{e.kind}</TableCell>
                  <TableCell><EntityStatusBadge status={e.status} /></TableCell>
                  <TableCell className="text-right text-sm tabular-nums">{e.linkCount}</TableCell>
                  <TableCell className="text-sm">{fmtDate(e.expiresAt)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </CardContent>
      </Card>
      <div className="flex items-center justify-between text-sm text-muted-foreground">
        <span>{total === undefined ? '' : `${items?.length ?? 0} of ${total}`}</span>
        {entities.hasNextPage && (
          <Button variant="outline" size="sm" disabled={entities.isFetchingNextPage}
            onClick={() => entities.fetchNextPage()}>
            Load more
          </Button>
        )}
      </div>
    </div>
  );
}

function CreateEntityDialog() {
  const [open, setOpen] = useState(false);
  const [kind, setKind] = useState<string>('Person');
  const [displayName, setDisplayName] = useState('');
  const [aliases, setAliases] = useState('');
  const [descriptors, setDescriptors] = useState('');
  const [summary, setSummary] = useState('');
  const create = useApiMutation({
    mutationFn: () =>
      api.post('/api/entities', {
        kind,
        displayName: displayName.trim(),
        aliases: aliases.split(',').map((a) => a.trim()).filter(Boolean),
        descriptors: parseDescriptors(descriptors),
        summary: summary.trim() || null,
      }),
    invalidate: [['entities']],
    success: 'Record created',
    onSuccess: () => {
      setOpen(false);
      setDisplayName('');
      setAliases('');
      setDescriptors('');
      setSummary('');
    },
  });
  return (
    <FormDialog open={open} onOpenChange={setOpen} trigger={<Button>New record</Button>}
      title="New record"
      description="Starts as Suspected. Retention defaults to one year; confirming needs a linked incident.">
      <div className="space-y-3">
        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-1">
            <Label htmlFor="ent-kind">Kind</Label>
            <Select id="ent-kind" value={kind} onChange={(e) => setKind(e.target.value)}>
              {ENTITY_KINDS.map((k) => <option key={k} value={k}>{k}</option>)}
            </Select>
          </div>
          <div className="space-y-1">
            <Label htmlFor="ent-name">Display name</Label>
            <Input id="ent-name" value={displayName} maxLength={200}
              placeholder={kind === 'Vehicle' ? 'Silver sedan ABC-123' : 'Name or primary alias'}
              onChange={(e) => setDisplayName(e.target.value)} />
          </div>
        </div>
        <div className="space-y-1">
          <Label htmlFor="ent-aliases">Aliases (comma separated)</Label>
          <Input id="ent-aliases" value={aliases} onChange={(e) => setAliases(e.target.value)} />
        </div>
        <div className="space-y-1">
          <Label htmlFor="ent-desc">Descriptors, one per line as key: value</Label>
          <Textarea id="ent-desc" rows={3} value={descriptors}
            placeholder={'height: 6\'2"\nhair: shaved'}
            onChange={(e) => setDescriptors(e.target.value)} />
        </div>
        <div className="space-y-1">
          <Label htmlFor="ent-summary">Why this record exists</Label>
          <Textarea id="ent-summary" rows={3} value={summary}
            onChange={(e) => setSummary(e.target.value)} />
        </div>
        <Button className="w-full" disabled={!displayName.trim() || create.isPending}
          onClick={() => create.mutate()}>
          Create
        </Button>
      </div>
    </FormDialog>
  );
}

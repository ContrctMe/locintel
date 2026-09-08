import { enumValue } from '../lib/enum-value';
import { api } from '@locintel/api';
import { Button, Card, CardContent, FormDialog, Input, Label, Select, Table, TableBody,
  TableCell, TableHead, TableHeader, TableRow, Textarea } from '@locintel/ui';
import { useInfiniteQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useState } from 'react';
import { fmtDateTime } from '../lib/format';
import { useApiMutation } from '../lib/mutation';

import { can, useMe } from '../session';
import { StatusBadge } from '../shell';

export type CaseSummary = {
  id: string; title: string; status: string; priority: string; leadId: string | null;
  lead: string | null; incidentCount: number; openTasks: number; legalHold: boolean;
  updatedAt: string; deletedAt: string | null;
};
export const PRIORITIES = ['Low', 'Medium', 'High', 'Critical'] as const;
export const DISPOSITIONS = ['Unfounded', 'Resolved', 'ReferredToPolice', 'Prosecuted', 'Other'] as const;
export const MEMBER_ROLES = ['Lead', 'Investigator', 'Reviewer'] as const;

export function PriorityBadge({ priority }: { priority: string }) {
  const tone =
    priority === 'Critical' ? 'bg-destructive text-destructive-foreground'
      : priority === 'High' ? 'bg-amber-500/20 text-amber-700 dark:text-amber-300'
        : priority === 'Medium' ? 'bg-primary/10 text-primary'
          : 'bg-muted text-muted-foreground';
  return <span className={`rounded px-1.5 py-0.5 text-xs font-medium ${tone}`}>{priority}</span>;
}

/** Investigations (blueprint module 3): what you see already passed membership-or-scope. */
export function CasesPage() {
  const { data: me } = useMe();
  const manage = can(me, 'cases:manage');
  const [status, setStatus] = useState('Open');
  const [priority, setPriority] = useState('');
  const [mine, setMine] = useState(false);
  const [q, setQ] = useState('');
  const [trash, setTrash] = useState(false);

  const params = new URLSearchParams({ limit: '50' });
  if (status) params.set('status', status);
  if (priority) params.set('priority', priority);
  if (mine) params.set('mine', 'true');
  if (q.trim()) params.set('q', q.trim());
  if (trash) params.set('trash', 'true');
  const query = params.toString();
  const cases = useInfiniteQuery({
    queryKey: ['cases', 'list', query],
    queryFn: ({ pageParam , signal }) => api.get('/api/cases', { signal, query: { limit: 50, status: status ? enumValue(['Open', 'Closed'], status) : undefined, priority: priority ? enumValue(PRIORITIES, priority) : undefined, mine, q: q.trim() || undefined, trash, offset: pageParam } }),
    initialPageParam: 0,
    getNextPageParam: (last) => last.nextOffset == null ? undefined : Number(last.nextOffset),
  });
  const items = cases.data?.pages.flatMap((p) => p.items);
  const total = cases.data?.pages[0]?.total;

  return (
    <div className="max-w-5xl space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold">Cases</h1>
        {manage && <OpenCaseDialog />}
      </div>
      <div className="flex flex-wrap gap-2">
        <Input className="w-56" placeholder="Search title or summary" value={q}
          onChange={(e) => setQ(e.target.value)} />
        <Select className="w-32" value={status} onChange={(e) => setStatus(e.target.value)}>
          <option value="">Any status</option>
          <option value="Open">Open</option>
          <option value="Closed">Closed</option>
        </Select>
        <Select className="w-36" value={priority} onChange={(e) => setPriority(e.target.value)}>
          <option value="">Any priority</option>
          {PRIORITIES.map((p) => <option key={p} value={p}>{p}</option>)}
        </Select>
        <label className="flex items-center gap-2 text-sm">
          <input type="checkbox" className="size-4 accent-primary" checked={mine}
            onChange={(e) => setMine(e.target.checked)} />
          Mine
        </label>
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
                <TableHead>Title</TableHead>
                <TableHead>Priority</TableHead>
                <TableHead>Status</TableHead>
                <TableHead>Lead</TableHead>
                <TableHead className="text-right">Incidents</TableHead>
                <TableHead className="text-right">Open tasks</TableHead>
                <TableHead>Updated</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {items?.length === 0 && (
                <TableRow>
                  <TableCell colSpan={7} className="text-center text-sm text-muted-foreground">
                    {trash ? 'The trash is empty.' : 'No cases match.'}
                  </TableCell>
                </TableRow>
              )}
              {items?.map((c) => (
                <TableRow key={c.id}>
                  <TableCell>
                    <Link to="/cases/$caseId" params={{ caseId: c.id }} className="font-medium hover:underline">
                      {c.title}
                    </Link>
                    {c.legalHold && <span className="ml-2 text-xs text-muted-foreground">· legal hold</span>}
                  </TableCell>
                  <TableCell><PriorityBadge priority={c.priority} /></TableCell>
                  <TableCell><StatusBadge status={c.status} /></TableCell>
                  <TableCell className="text-sm">{c.lead ?? '—'}</TableCell>
                  <TableCell className="text-right text-sm tabular-nums">{c.incidentCount}</TableCell>
                  <TableCell className="text-right text-sm tabular-nums">{c.openTasks}</TableCell>
                  <TableCell className="text-sm">{fmtDateTime(c.updatedAt)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </CardContent>
      </Card>
      <div className="flex items-center justify-between text-sm text-muted-foreground">
        <span>{total === undefined ? '' : `${items?.length ?? 0} of ${total}`}</span>
        {cases.hasNextPage && (
          <Button variant="outline" size="sm" disabled={cases.isFetchingNextPage} onClick={() => cases.fetchNextPage()}>
            Load more
          </Button>
        )}
      </div>
    </div>
  );
}

export function OpenCaseDialog({ incidentId, trigger }: { incidentId?: string; trigger?: React.ReactElement }) {
  const [open, setOpen] = useState(false);
  const [title, setTitle] = useState('');
  const [summary, setSummary] = useState('');
  const [priority, setPriority] = useState<string>('Medium');
  const create = useApiMutation({
    mutationFn: () =>
      api.post("/api/cases", {
        title: title.trim(),
        summary: summary.trim() || null,
        priority: enumValue(PRIORITIES, priority),
        incidentIds: incidentId ? [incidentId] : [],
      }),
    invalidate: [['cases']],
    success: 'Case opened',
    onSuccess: () => {
      setOpen(false);
      setTitle('');
      setSummary('');
    },
  });
  return (
    <FormDialog open={open} onOpenChange={setOpen} title="Open a case"
      description="You become the lead. Add members, incidents, people, and evidence from the case page."
      trigger={trigger ?? <Button>Open case</Button>}>
      <div className="space-y-3">
        <div className="space-y-1">
          <Label htmlFor="cs-title">Title</Label>
          <Input id="cs-title" value={title} maxLength={200} onChange={(e) => setTitle(e.target.value)} />
        </div>
        <div className="space-y-1">
          <Label htmlFor="cs-priority">Priority</Label>
          <Select id="cs-priority" value={priority} onChange={(e) => setPriority(e.target.value)}>
            {PRIORITIES.map((p) => <option key={p} value={p}>{p}</option>)}
          </Select>
        </div>
        <div className="space-y-1">
          <Label htmlFor="cs-summary">Summary</Label>
          <Textarea id="cs-summary" rows={3} value={summary} onChange={(e) => setSummary(e.target.value)} />
        </div>
        <Button className="w-full" disabled={!title.trim() || create.isPending} onClick={() => create.mutate()}>
          Open
        </Button>
      </div>
    </FormDialog>
  );
}

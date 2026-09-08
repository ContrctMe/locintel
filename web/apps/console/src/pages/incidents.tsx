import { CATEGORIES, SEVERITIES, categoryLabel, SeverityBadge } from '../features/incidents';
import { SitePicker, type PickedSite, useSiteMetadata } from '../features/sites';
import { FilePicker } from '../features/files';
import { enumValue } from '../lib/enum-value';
import { api } from '@locintel/api';
import { Button, Card, CardContent, FormDialog, Input, Label, Select, Table, TableBody,
  TableCell, TableHead, TableHeader, TableRow, Textarea } from '@locintel/ui';
import { useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useState } from 'react';
import { fmtDateTime } from '../lib/format';
import { useApiMutation } from '../lib/mutation';

import { can, useMe } from '../session';
import { StatusBadge } from '../shell';

/** The fact table (blueprint module 1): what happened, where, on the site's business date. */
export function IncidentsPage() {
  const { data: me } = useMe();
  const [pickedSite, setPickedSite] = useState<PickedSite | null>(null);
  const siteId = pickedSite?.id ?? '';
  const [status, setStatus] = useState('');
  const [category, setCategory] = useState('');
  const [q, setQ] = useState('');
  const [trash, setTrash] = useState(false);

  const params = new URLSearchParams({ limit: '50' });
  if (siteId) params.set('siteId', siteId);
  if (status) params.set('status', status);
  if (category) params.set('category', category);
  if (q.trim()) params.set('q', q.trim());
  if (trash) params.set('trash', 'true');
  const query = params.toString();
  const incidents = useInfiniteQuery({
    queryKey: ['incidents', 'list', query],
    queryFn: ({ pageParam , signal }) =>
      api.get('/api/incidents', { signal, query: { limit: 50, siteId: siteId || undefined, status: status ? enumValue(['Open', 'Closed'], status) : undefined, category: category ? enumValue(CATEGORIES, category) : undefined, q: q.trim() || undefined, trash, offset: pageParam } }),
    initialPageParam: 0,
    getNextPageParam: (last) => last.nextOffset == null ? undefined : Number(last.nextOffset),
  });
  const items = incidents.data?.pages.flatMap((p) => p.items);
  const total = incidents.data?.pages[0]?.total;
  const { data: sites } = useSiteMetadata(items?.map((item) => item.siteId) ?? []);
  const siteName = (id: string) => sites?.find((s) => s.id === id)?.name ?? '—';

  return (
    <div className="max-w-5xl space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold">Incidents</h1>
        <div className="flex gap-2">
          {can(me, 'incidents:manage') && <ImportDialog />}
          {can(me, 'incidents:report') && <ReportDialog />}
        </div>
      </div>
      <div className="flex flex-wrap gap-2">
        <Input className="w-56" placeholder="Search title or narrative" value={q}
          onChange={(e) => setQ(e.target.value)} />
        <SitePicker aria-label="Filter by site" placeholder="All sites" value={pickedSite} onChange={setPickedSite} />
        <Select className="w-32" value={status} onChange={(e) => setStatus(e.target.value)}>
          <option value="">Any status</option>
          <option value="Open">Open</option>
          <option value="Closed">Closed</option>
        </Select>
        <Select className="w-48" value={category} onChange={(e) => setCategory(e.target.value)}>
          <option value="">Any category</option>
          {CATEGORIES.map((c) => <option key={c} value={c}>{categoryLabel(c)}</option>)}
        </Select>
        {can(me, 'incidents:manage') && (
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
                <TableHead>Occurred</TableHead>
                <TableHead>Site</TableHead>
                <TableHead>Title</TableHead>
                <TableHead>Category</TableHead>
                <TableHead>Severity</TableHead>
                <TableHead>Status</TableHead>
                <TableHead className="text-right">Loss</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {items?.length === 0 && (
                <TableRow>
                  <TableCell colSpan={7} className="text-center text-sm text-muted-foreground">
                    {trash ? 'The trash is empty.' : 'No incidents match.'}
                  </TableCell>
                </TableRow>
              )}
              {items?.map((i) => (
                <TableRow key={i.id}>
                  <TableCell className="whitespace-nowrap text-sm">
                    <div>{fmtDateTime(i.occurredAt)}</div>
                    <div className="text-xs text-muted-foreground">day {i.businessDate}</div>
                  </TableCell>
                  <TableCell className="text-sm">{siteName(i.siteId)}</TableCell>
                  <TableCell>
                    <Link to="/incidents/$incidentId" params={{ incidentId: i.id }}
                      className="font-medium hover:underline">
                      {i.title}
                    </Link>
                    {i.legalHold && (
                      <span className="ml-2 text-xs text-muted-foreground">· legal hold</span>
                    )}
                  </TableCell>
                  <TableCell className="text-sm">{categoryLabel(i.category)}</TableCell>
                  <TableCell><SeverityBadge severity={i.severity} /></TableCell>
                  <TableCell><StatusBadge status={i.status} /></TableCell>
                  <TableCell className="text-right text-sm tabular-nums">
                    {i.lossAmount == null ? '—' : i.lossAmount.toLocaleString(undefined, {
                      minimumFractionDigits: 2, maximumFractionDigits: 2 })}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </CardContent>
      </Card>
      <div className="flex items-center justify-between text-sm text-muted-foreground">
        <span>{total === undefined ? '' : `${items?.length ?? 0} of ${total}`}</span>
        {incidents.hasNextPage && (
          <Button variant="outline" size="sm" disabled={incidents.isFetchingNextPage}
            onClick={() => incidents.fetchNextPage()}>
            Load more
          </Button>
        )}
      </div>
    </div>
  );
}

function ReportDialog() {
  const [open, setOpen] = useState(false);
  const [pickedSite, setPickedSite] = useState<PickedSite | null>(null);
  const siteId = pickedSite?.id ?? '';
  const [category, setCategory] = useState<string>('Theft');
  const [severity, setSeverity] = useState<string>('Medium');
  const [title, setTitle] = useState('');
  const [occurredAt, setOccurredAt] = useState(() => new Date().toISOString().slice(0, 16));
  const [narrative, setNarrative] = useState('');
  const [locationDetail, setLocationDetail] = useState('');
  const [lossAmount, setLossAmount] = useState('');

  const report = useApiMutation({
    mutationFn: () =>
      api.post('/api/incidents', {
        siteId,
        category: enumValue(CATEGORIES, category),
        severity: enumValue(SEVERITIES, severity),
        title: title.trim(),
        occurredAt: new Date(occurredAt).toISOString(),
        narrative: narrative.trim() || null,
        locationDetail: locationDetail.trim() || null,
        lossAmount: lossAmount ? Number(lossAmount) : null,
      }),
    invalidate: [['incidents']],
    success: 'Incident reported',
    onSuccess: () => {
      setOpen(false);
      setTitle('');
      setNarrative('');
      setLocationDetail('');
      setLossAmount('');
    },
  });

  return (
    <FormDialog open={open} onOpenChange={setOpen} trigger={<Button>Report incident</Button>}
      title="Report incident"
      description="The business date is stamped on the site's clock from the time it occurred.">
      <div className="space-y-3">
        <div className="space-y-1">
          <Label htmlFor="inc-site">Site</Label>
          <SitePicker id="inc-site" value={pickedSite} onChange={setPickedSite} />
        </div>
        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-1">
            <Label htmlFor="inc-cat">Category</Label>
            <Select id="inc-cat" value={category} onChange={(e) => setCategory(e.target.value)}>
              {CATEGORIES.map((c) => <option key={c} value={c}>{categoryLabel(c)}</option>)}
            </Select>
          </div>
          <div className="space-y-1">
            <Label htmlFor="inc-sev">Severity</Label>
            <Select id="inc-sev" value={severity} onChange={(e) => setSeverity(e.target.value)}>
              {SEVERITIES.map((s) => <option key={s} value={s}>{s}</option>)}
            </Select>
          </div>
        </div>
        <div className="space-y-1">
          <Label htmlFor="inc-title">Title</Label>
          <Input id="inc-title" value={title} maxLength={200}
            onChange={(e) => setTitle(e.target.value)} />
        </div>
        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-1">
            <Label htmlFor="inc-at">Occurred at (your local time)</Label>
            <Input id="inc-at" type="datetime-local" value={occurredAt}
              onChange={(e) => setOccurredAt(e.target.value)} />
          </div>
          <div className="space-y-1">
            <Label htmlFor="inc-loss">Loss amount</Label>
            <Input id="inc-loss" type="number" min="0" step="0.01" value={lossAmount}
              onChange={(e) => setLossAmount(e.target.value)} />
          </div>
        </div>
        <div className="space-y-1">
          <Label htmlFor="inc-loc">Where in the site</Label>
          <Input id="inc-loc" value={locationDetail} placeholder="Electronics aisle"
            onChange={(e) => setLocationDetail(e.target.value)} />
        </div>
        <div className="space-y-1">
          <Label htmlFor="inc-narr">Narrative</Label>
          <Textarea id="inc-narr" value={narrative} rows={4}
            onChange={(e) => setNarrative(e.target.value)} />
        </div>
        <Button className="w-full" disabled={!siteId || !title.trim() || report.isPending}
          onClick={() => report.mutate()}>
          Report
        </Button>
      </div>
    </FormDialog>
  );
}





/** Historical incidents from CSV (stage, preview, commit - ADR 18's shape). Upload on the Files page first. */
function ImportDialog() {
  const [open, setOpen] = useState(false);
  const [fileId, setFileId] = useState('');
  const [batchId, setBatchId] = useState('');
  const { data: detail } = useQuery({
    queryKey: ['incidents', 'import', batchId],
    queryFn: ({ signal }) => api.get("/api/incidents/imports/{id}", { signal, path: { id: batchId } }),
    enabled: !!batchId,
  });
  const stage = useApiMutation({
    mutationFn: () => api.post("/api/incidents/imports", { fileId }),
    onSuccess: (b) => setBatchId(b.id),
    errorFallback: 'Could not stage the file',
  });
  const commit = useApiMutation({
    mutationFn: () => api.post("/api/incidents/imports/{id}/commit", undefined, { path: { id: batchId } }),
    invalidate: [['incidents']],
    success: 'Incidents imported',
    onSuccess: () => { setOpen(false); setBatchId(''); },
  });
  const discard = useApiMutation({
    mutationFn: () => api.post("/api/incidents/imports/{id}/discard", undefined, { path: { id: batchId } }),
    onSuccess: () => setBatchId(''),
  });
  const invalid = detail?.rows.filter((r) => r.errors.length > 0) ?? [];
  return (
    <FormDialog open={open} onOpenChange={(o) => { setOpen(o); if (!o) setBatchId(''); }} title="Import incidents from CSV"
      description="Columns: site, occurred_at, category, severity, title, narrative, loss_amount, police_report, tags. Nothing lands until you commit."
      trigger={<Button variant="outline">Import CSV</Button>}>
      <div className="space-y-3">
        {!batchId ? (
          <>
            <div className="space-y-1">
              <Label htmlFor="imp-file">File (upload on the Files page first)</Label>
              <FilePicker id="imp-file" value={fileId} onChange={setFileId} cleanOnly />
            </div>
            <Button className="w-full" disabled={!fileId || stage.isPending} onClick={() => stage.mutate()}>Stage and preview</Button>
          </>
        ) : detail ? (
          <>
            <p className="text-sm">
              <span className="font-medium">{detail.batch.fileName}</span>: {detail.batch.total} rows · {detail.batch.valid} will land · {detail.batch.invalid} invalid
            </p>
            {invalid.length > 0 && (
              <div className="max-h-48 space-y-1 overflow-auto rounded-md border p-2 text-xs">
                {invalid.slice(0, 50).map((r) => (
                  <div key={r.rowNumber}><span className="font-medium">Row {r.rowNumber}</span> {r.siteRef} · {r.title || '(no title)'}: <span className="text-destructive">{r.errors.join('; ')}</span></div>
                ))}
              </div>
            )}
            <div className="flex gap-2">
              <Button className="flex-1" disabled={detail.batch.valid === 0 || commit.isPending} onClick={() => commit.mutate()}>Commit {detail.batch.valid} incidents</Button>
              <Button variant="outline" disabled={discard.isPending} onClick={() => discard.mutate()}>Discard</Button>
            </div>
          </>
        ) : (
          <p className="text-sm text-muted-foreground">Staging…</p>
        )}
      </div>
    </FormDialog>
  );
}

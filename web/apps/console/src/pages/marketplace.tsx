import { CATEGORIES, URGENCIES, REQUEST_STATUSES, categoryLabel, RequestStatusBadge } from '../features/marketplace';
import { SitePicker, type PickedSite } from '../features/sites';
import { enumValue } from '../lib/enum-value';
import { api } from '@locintel/api';
import { Button, Card, CardContent, FormDialog, Input, Label, Select, Table, TableBody,
  TableCell, TableHead, TableHeader, TableRow, Textarea } from '@locintel/ui';
import { useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useState } from 'react';
import { fmtDateTime } from '../lib/format';
import { apiError, useApiMutation } from '../lib/mutation';

import { can, useMe } from '../session';

export type RequestSummary = {
  id: string; vendorOrgId: string | null; vendorName: string | null; mode: string; requesterName: string; category: string;
  urgency: string; status: string; siteId: string; siteName: string; title: string; startsAt: string;
  endsAt: string | null; budgetAmount: number | null; currency: string; updatedAt: string; responseDueAt: string | null;
};
export type VendorSummary = {
  orgId: string; name: string; description: string; categories: string[]; serviceAreas: string[];
  validCredentials: number; expiredCredentials: number; preferred: boolean; blocked: boolean;
};


/** The buyer's marketplace (blueprint): requests to vendors, direct-to-preferred in v1. */
export function MarketplacePage() {
  const { data: me } = useMe();
  const manage = can(me, 'marketplace:manage');
  const [status, setStatus] = useState('');
  const [q, setQ] = useState('');
  const params = new URLSearchParams({ limit: '50' });
  if (status) params.set('status', status);
  const query = params.toString();
  const requests = useInfiniteQuery({
    queryKey: ['marketplace', 'requests', query],
    queryFn: ({ pageParam , signal }) => api.get('/api/marketplace/requests', { signal, query: { limit: 50, status: status ? enumValue(REQUEST_STATUSES, status) : undefined, offset: pageParam } }),
    initialPageParam: 0,
    getNextPageParam: (last) => last.nextOffset == null ? undefined : Number(last.nextOffset),
  });
  const items = requests.data?.pages.flatMap((p) => p.items).filter((r) => !q.trim() || r.title.toLowerCase().includes(q.toLowerCase()));

  return (
    <div className="max-w-5xl space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold">Marketplace</h1>
        <div className="flex gap-2">
          <Link to="/marketplace/vendors"><Button variant="outline">Vendors</Button></Link>
          {manage && <NewRequestDialog />}
        </div>
      </div>
      <div className="flex flex-wrap gap-2">
        <Input className="w-56" placeholder="Filter by title" value={q} onChange={(e) => setQ(e.target.value)} />
        <Select className="w-40" value={status} onChange={(e) => setStatus(e.target.value)}>
          <option value="">Any status</option>
          {REQUEST_STATUSES.map((s) => <option key={s} value={s}>{s}</option>)}
        </Select>
      </div>
      <Card>
        <CardContent className="p-0">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Request</TableHead>
                <TableHead>Vendor</TableHead>
                <TableHead>Site</TableHead>
                <TableHead>Urgency</TableHead>
                <TableHead>Status</TableHead>
                <TableHead>Starts</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {items?.length === 0 && (
                <TableRow><TableCell colSpan={6} className="text-center text-sm text-muted-foreground">No requests yet.</TableCell></TableRow>
              )}
              {items?.map((r) => (
                <TableRow key={r.id}>
                  <TableCell>
                    <Link to="/marketplace/requests/$requestId" params={{ requestId: r.id }} className="font-medium hover:underline">{r.title}</Link>
                    <div className="text-xs text-muted-foreground">{categoryLabel(r.category)}</div>
                  </TableCell>
                  <TableCell className="text-sm">{r.vendorName ?? (r.mode === 'Broadcast' ? 'Open for quotes' : '—')}</TableCell>
                  <TableCell className="text-sm">{r.siteName}</TableCell>
                  <TableCell className="text-sm">{r.urgency}</TableCell>
                  <TableCell><RequestStatusBadge status={r.status} /></TableCell>
                  <TableCell className="text-sm">{fmtDateTime(r.startsAt)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </CardContent>
      </Card>
      {requests.hasNextPage && (
        <Button variant="outline" size="sm" disabled={requests.isFetchingNextPage} onClick={() => requests.fetchNextPage()}>Load more</Button>
      )}
    </div>
  );
}

export function NewRequestDialog({ siteId: presetSite, incidentId, caseId, trigger }: {
  siteId?: string; incidentId?: string; caseId?: string; trigger?: React.ReactElement;
}) {
  const [open, setOpen] = useState(false);
  const [category, setCategory] = useState<string>('GuardService');
  const [vendorOrgId, setVendorOrgId] = useState('');
  const [pickedSite, setPickedSite] = useState<PickedSite | null>(null);
  const siteId = presetSite ?? pickedSite?.id ?? '';
  const [urgency, setUrgency] = useState<string>('Scheduled');
  const [title, setTitle] = useState('');
  const [details, setDetails] = useState('');
  const [startsAt, setStartsAt] = useState(() => new Date(Date.now() + 3_600_000).toISOString().slice(0, 16));
  const [endsAt, setEndsAt] = useState('');
  const [rrule, setRrule] = useState('');
  const [budget, setBudget] = useState('');
  const [spec, setSpec] = useState('');
  const { data: vendors } = useQuery({
    queryKey: ['marketplace', 'vendors', 'picker', category],
    queryFn: async ({ signal }) => (await api.get('/api/marketplace/vendors', { signal, query: { category: enumValue(CATEGORIES, category), limit: 100 } })).items,
    enabled: open,
  });
  const usable = vendors?.filter((v) => !v.blocked).sort((a, b) => Number(b.preferred) - Number(a.preferred));
  const create = useApiMutation({
    mutationFn: async () => {
      const specObj: Record<string, string> = {};
      for (const line of spec.split('\n')) {
        const i = line.indexOf(':');
        if (i > 0) specObj[line.slice(0, i).trim()] = line.slice(i + 1).trim();
      }
      const created = await api.post("/api/marketplace/requests", {
        vendorOrgId: vendorOrgId || null, category: enumValue(CATEGORIES, category), urgency: enumValue(URGENCIES, urgency), siteId, title: title.trim(), details: details.trim() || null,
        spec: specObj, startsAt: new Date(startsAt).toISOString(),
        endsAt: endsAt ? new Date(endsAt).toISOString() : null, rrule: rrule.trim() || null,
        budgetAmount: budget ? Number(budget) : null, incidentId: incidentId ?? null, caseId: caseId ?? null,
      });
      await api.post("/api/marketplace/requests/{id}/submit", undefined, { path: { id: created.id } });
      return created;
    },
    invalidate: [['marketplace']],
    success: 'Request sent to the vendor',
    errorFallback: 'Could not send the request',
    onSuccess: () => { setOpen(false); setTitle(''); setDetails(''); setSpec(''); },
  });
  return (
    <FormDialog open={open} onOpenChange={setOpen} title="New request"
      description="Direct to a vendor of your choice; preferred vendors list first. Sent on submit."
      trigger={trigger ?? <Button>New request</Button>}>
      <div className="space-y-3">
        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-1">
            <Label htmlFor="rq-cat">Category</Label>
            <Select id="rq-cat" value={category} onChange={(e) => { setCategory(e.target.value); setVendorOrgId(''); }}>
              {CATEGORIES.map((c) => <option key={c} value={c}>{categoryLabel(c)}</option>)}
            </Select>
          </div>
          <div className="space-y-1">
            <Label htmlFor="rq-vendor">Vendor</Label>
            <Select id="rq-vendor" value={vendorOrgId} onChange={(e) => setVendorOrgId(e.target.value)}>
              <option value="">Broadcast: ask every matching vendor for a quote</option>
              {usable?.map((v) => <option key={v.orgId} value={v.orgId}>{v.preferred ? '★ ' : ''}{v.name}</option>)}
            </Select>
          </div>
        </div>
        {!presetSite && (
          <div className="space-y-1">
            <Label htmlFor="rq-site">Site</Label>
            <SitePicker id="rq-site" value={pickedSite} onChange={setPickedSite} />
          </div>
        )}
        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-1">
            <Label htmlFor="rq-urg">Urgency</Label>
            <Select id="rq-urg" value={urgency} onChange={(e) => setUrgency(e.target.value)}>
              {URGENCIES.map((u) => <option key={u} value={u}>{u}</option>)}
            </Select>
          </div>
          <div className="space-y-1">
            <Label htmlFor="rq-budget">Budget (USD)</Label>
            <Input id="rq-budget" type="number" min="0" step="0.01" value={budget} onChange={(e) => setBudget(e.target.value)} />
          </div>
        </div>
        <div className="space-y-1"><Label htmlFor="rq-title">Title</Label><Input id="rq-title" value={title} maxLength={200} onChange={(e) => setTitle(e.target.value)} /></div>
        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-1"><Label htmlFor="rq-start">Starts</Label><Input id="rq-start" type="datetime-local" value={startsAt} onChange={(e) => setStartsAt(e.target.value)} /></div>
          <div className="space-y-1"><Label htmlFor="rq-end">Ends</Label><Input id="rq-end" type="datetime-local" value={endsAt} onChange={(e) => setEndsAt(e.target.value)} /></div>
        </div>
        {urgency === 'Standing' && (
          <div className="space-y-1"><Label htmlFor="rq-rrule">Recurrence (RRULE, site-local)</Label>
            <Input id="rq-rrule" value={rrule} placeholder="FREQ=WEEKLY;BYDAY=FR,SA" onChange={(e) => setRrule(e.target.value)} /></div>
        )}
        <div className="space-y-1"><Label htmlFor="rq-spec">Specifics, one per line as key: value</Label>
          <Textarea id="rq-spec" rows={3} value={spec} placeholder={'headcount: 2\narmed: no'} onChange={(e) => setSpec(e.target.value)} /></div>
        <div className="space-y-1"><Label htmlFor="rq-details">Details</Label><Textarea id="rq-details" rows={3} value={details} onChange={(e) => setDetails(e.target.value)} /></div>
        <Button className="w-full" disabled={!siteId || !title.trim() || create.isPending} onClick={() => create.mutate()}>{vendorOrgId ? 'Send request' : 'Send for quotes'}</Button>
        {create.isError && <p className="text-xs text-destructive">{apiError(create.error, 'Could not send')}</p>}
      </div>
    </FormDialog>
  );
}

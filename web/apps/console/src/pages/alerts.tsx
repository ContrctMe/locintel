import { api } from '@locintel/api';
import { Button, Card, CardContent, CardHeader, CardTitle, FormDialog, Input, Label, Select, Textarea } from '@locintel/ui';
import { useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useState } from 'react';
import { fmtDate, fmtDateTime } from '../lib/format';
import { useApiMutation } from '../lib/mutation';
import type { Page } from '../lib/paging';
import { can, useMe } from '../session';
import { SeverityBadge } from './incidents';

type Alert = {
  id: string; kind: string; severity: string; title: string; body: string; incidentId: string | null;
  bulletinId: string | null; entityId: string | null; createdAt: string; readAt: string | null;
};
type AlertPage = Page<Alert> & { unread: number };
type Bulletin = {
  id: string; kind: string; severity: string; title: string; body: string; scopePath: string | null;
  entityId: string | null; incidentId: string | null; caseId: string | null; issuer: string | null;
  issuedAt: string; expiresAt: string; status: string; active: boolean; acknowledged: boolean; acknowledgements: number;
};
type Hierarchy = { nodes: { id: string; name: string; depth: number; path: string }[] };
type Site = { id: string; name: string };

/** The feed (system alerts) and the board (bulletins / BOLOs), both already scope-filtered by the server. */
export function AlertsPage() {
  const { data: me } = useMe();
  const manage = can(me, 'alerts:manage');
  const [showInactive, setShowInactive] = useState(false);
  const { data: alerts } = useQuery({ queryKey: ['alerts', 'feed'], queryFn: () => api.get<AlertPage>('/api/alerts?limit=100') });
  const { data: bulletins } = useQuery({
    queryKey: ['alerts', 'bulletins', showInactive],
    queryFn: () => api.get<Page<Bulletin>>(`/api/bulletins?limit=100${showInactive ? '&includeInactive=true' : ''}`),
  });
  const { data: sites } = useQuery({
    queryKey: ['sites', 'picker'],
    queryFn: async () => (await api.get<Page<Site>>('/api/sites?limit=200')).items,
  });
  const markRead = useApiMutation({ mutationFn: (id: string) => api.post(`/api/alerts/${id}/read`), invalidate: [['alerts']] });
  const acknowledge = useApiMutation({
    mutationFn: (input: { id: string; siteId: string | null }) => api.post(`/api/bulletins/${input.id}/acknowledge`, { siteId: input.siteId }),
    invalidate: [['alerts']], success: 'Acknowledged',
  });
  const withdraw = useApiMutation({ mutationFn: (id: string) => api.post(`/api/bulletins/${id}/withdraw`), invalidate: [['alerts']], success: 'Withdrawn' });
  const [ackSite, setAckSite] = useState('');

  return (
    <div className="max-w-5xl space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold">Alerts</h1>
        {manage && <IssueBulletinDialog />}
      </div>
      <div className="grid gap-6 md:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center justify-between text-base">
              Bulletins
              <label className="flex items-center gap-2 text-sm font-normal">
                <input type="checkbox" className="size-4 accent-primary" checked={showInactive} onChange={(e) => setShowInactive(e.target.checked)} />
                Include past
              </label>
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-3">
            {sites && sites.length > 1 && (
              <Select className="w-full" value={ackSite} onChange={(e) => setAckSite(e.target.value)}>
                <option value="">Acknowledge as… (no site)</option>
                {sites.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
              </Select>
            )}
            {bulletins?.items.length === 0 && <p className="text-sm text-muted-foreground">Nothing posted.</p>}
            {bulletins?.items.map((b) => (
              <div key={b.id} className={`rounded-md border p-3 text-sm ${b.active ? '' : 'opacity-60'}`}>
                <div className="flex flex-wrap items-center gap-2">
                  <span className="text-xs font-semibold uppercase text-muted-foreground">{b.kind}</span>
                  <SeverityBadge severity={b.severity} />
                  <span className="font-medium">{b.title}</span>
                  {!b.active && <span className="text-xs text-muted-foreground">{b.status === 'Withdrawn' ? 'withdrawn' : 'expired'}</span>}
                </div>
                <p className="mt-1 whitespace-pre-wrap">{b.body}</p>
                <div className="mt-2 flex flex-wrap items-center justify-between gap-2 text-xs text-muted-foreground">
                  <span>
                    {b.issuer ?? 'Issued'} · {fmtDate(b.issuedAt)} → {fmtDate(b.expiresAt)}
                    {b.scopePath ? ' · targeted' : ' · org-wide'} · {b.acknowledgements} ack
                    {b.entityId && <> · <Link to="/entities/$entityId" params={{ entityId: b.entityId }} className="hover:underline">record</Link></>}
                    {b.incidentId && <> · <Link to="/incidents/$incidentId" params={{ incidentId: b.incidentId }} className="hover:underline">incident</Link></>}
                    {b.caseId && <> · <Link to="/cases/$caseId" params={{ caseId: b.caseId }} className="hover:underline">case</Link></>}
                  </span>
                  <span className="flex gap-1">
                    {b.active && !b.acknowledged && (
                      <Button size="sm" variant="outline" onClick={() => acknowledge.mutate({ id: b.id, siteId: ackSite || null })}>Acknowledge</Button>
                    )}
                    {b.acknowledged && <span className="text-emerald-700 dark:text-emerald-300">Acknowledged</span>}
                    {manage && b.active && <Button size="sm" variant="ghost" onClick={() => withdraw.mutate(b.id)}>Withdraw</Button>}
                  </span>
                </div>
              </div>
            ))}
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="flex items-center justify-between text-base">
              Feed <span className="text-sm font-normal text-muted-foreground">{alerts?.unread ?? 0} unread</span>
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-2">
            {alerts?.items.length === 0 && <p className="text-sm text-muted-foreground">Quiet.</p>}
            {alerts?.items.map((a) => (
              <div key={a.id} className={`rounded-md border p-3 text-sm ${a.readAt ? 'opacity-70' : 'border-primary/40'}`}>
                <div className="flex items-center gap-2">
                  <SeverityBadge severity={a.severity} />
                  <span className="font-medium">{a.title}</span>
                </div>
                <p className="mt-1 text-muted-foreground">{a.body}</p>
                <div className="mt-2 flex items-center justify-between text-xs text-muted-foreground">
                  <span>
                    {fmtDateTime(a.createdAt)}
                    {a.incidentId && <> · <Link to="/incidents/$incidentId" params={{ incidentId: a.incidentId }} className="hover:underline">open incident</Link></>}
                  </span>
                  {!a.readAt && <button className="hover:underline" onClick={() => markRead.mutate(a.id)}>Mark read</button>}
                </div>
              </div>
            ))}
          </CardContent>
        </Card>
      </div>
    </div>
  );
}

function IssueBulletinDialog() {
  const [open, setOpen] = useState(false);
  const [kind, setKind] = useState('Bolo');
  const [severity, setSeverity] = useState('High');
  const [title, setTitle] = useState('');
  const [body, setBody] = useState('');
  const [scopePath, setScopePath] = useState('');
  const [days, setDays] = useState('14');
  const { data: hierarchy } = useQuery({ queryKey: ['hierarchy'], queryFn: () => api.get<Hierarchy>('/api/hierarchy'), enabled: open });
  const issue = useApiMutation({
    mutationFn: () => api.post('/api/bulletins', {
      kind, severity, title: title.trim(), body: body.trim(), scopePath: scopePath || null,
      expiresAt: new Date(Date.now() + Number(days) * 86_400_000).toISOString(),
    }),
    invalidate: [['alerts']], success: 'Bulletin issued', onSuccess: () => { setOpen(false); setTitle(''); setBody(''); },
  });
  return (
    <FormDialog open={open} onOpenChange={setOpen} title="Issue a bulletin"
      description="Lands in the feed of everyone in scope and emails the org's managers."
      trigger={<Button>Issue bulletin</Button>}>
      <div className="space-y-3">
        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-1"><Label htmlFor="bl-kind">Kind</Label>
            <Select id="bl-kind" value={kind} onChange={(e) => setKind(e.target.value)}><option>Bolo</option><option>Advisory</option><option>Safety</option></Select></div>
          <div className="space-y-1"><Label htmlFor="bl-sev">Severity</Label>
            <Select id="bl-sev" value={severity} onChange={(e) => setSeverity(e.target.value)}><option>Low</option><option>Medium</option><option>High</option><option>Critical</option></Select></div>
        </div>
        <div className="space-y-1"><Label htmlFor="bl-title">Title</Label><Input id="bl-title" value={title} maxLength={200} onChange={(e) => setTitle(e.target.value)} /></div>
        <div className="space-y-1"><Label htmlFor="bl-body">Body</Label><Textarea id="bl-body" rows={4} value={body} onChange={(e) => setBody(e.target.value)} /></div>
        <div className="grid grid-cols-2 gap-3">
          <div className="space-y-1"><Label htmlFor="bl-scope">Target</Label>
            <Select id="bl-scope" value={scopePath} onChange={(e) => setScopePath(e.target.value)}>
              <option value="">Whole organization</option>
              {hierarchy?.nodes.map((n) => <option key={n.id} value={n.path}>{' '.repeat(n.depth * 2)}{n.name}</option>)}
            </Select></div>
          <div className="space-y-1"><Label htmlFor="bl-days">Days active (max 90)</Label><Input id="bl-days" type="number" min="1" max="90" value={days} onChange={(e) => setDays(e.target.value)} /></div>
        </div>
        <Button className="w-full" disabled={!title.trim() || !body.trim() || issue.isPending} onClick={() => issue.mutate()}>Issue</Button>
      </div>
    </FormDialog>
  );
}

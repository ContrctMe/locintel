import { api, type components } from '@locintel/api';
import { Button, Card, CardContent, CardHeader, CardTitle, ConfirmButton, FormDialog, Input, Label, Select, Textarea } from '@locintel/ui';
import { useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useState } from 'react';
import { fmtDateTime } from '../lib/format';
import { useApiMutation } from '../lib/mutation';

import { weeklySchedule, type DayCode } from '../lib/schedule';
import { can, useMe } from '../session';
import { SeverityBadge } from './incidents';


type Checkpoint = { code: string; label: string; latitude: number | null; longitude: number | null };
type Route = components['schemas']['RouteView'];






const DAYS: { code: DayCode; label: string }[] = [
  { code: 'MO', label: 'Mon' }, { code: 'TU', label: 'Tue' }, { code: 'WE', label: 'Wed' }, { code: 'TH', label: 'Thu' },
  { code: 'FR', label: 'Fri' }, { code: 'SA', label: 'Sat' }, { code: 'SU', label: 'Sun' },
];

/** Guard rounds (blueprint module 8): today's expected runs, live patrol, routes, and the daily activity report. */
export function PatrolsPage() {
  const { data: me } = useMe();
  const manage = can(me, 'patrols:manage');
  const perform = can(me, 'patrols:perform');
  const [siteId, setSiteId] = useState('');
  const [reportDate, setReportDate] = useState('');
  const { data: sites } = useQuery({ queryKey: ['sites', 'picker'], queryFn: async ({ signal }) => (await api.get('/api/sites', { signal, query: { limit: 200 } })).items });
  const activeSite = siteId || sites?.[0]?.id || '';
  const { data: day } = useQuery({ queryKey: ['patrols', 'today', activeSite], queryFn: ({ signal }) => api.get('/api/patrols/today', { signal, query: { siteId: activeSite } }), enabled: !!activeSite, refetchInterval: 30_000 });
  const { data: routes } = useQuery({ queryKey: ['patrols', 'routes', activeSite], queryFn: ({ signal }) => api.get('/api/patrols/routes', { signal, query: { siteId: activeSite } }), enabled: !!activeSite });
  const date = reportDate || day?.businessDate || '';
  const { data: report } = useQuery({ queryKey: ['patrols', 'report', activeSite, date], queryFn: ({ signal }) => api.get('/api/patrols/report', { signal, query: { siteId: activeSite, date } }), enabled: !!activeSite && !!date });

  const start = useApiMutation({
    mutationFn: (input: { routeId: string; scheduledStartLocal: string | null }) => api.post('/api/patrols', input),
    invalidate: [['patrols']], success: 'Patrol started',
  });
  const scan = useApiMutation({
    mutationFn: async (input: { patrolId: string; code: string }) => {
      const pos = await new Promise<{ latitude?: number; longitude?: number }>((resolve) =>
        navigator.geolocation
          ? navigator.geolocation.getCurrentPosition((p) => resolve({ latitude: p.coords.latitude, longitude: p.coords.longitude }), () => resolve({}), { timeout: 4000 })
          : resolve({}),
      );
      return api.post("/api/patrols/{id}/scan", { code: input.code, ...pos }, { path: { id: input.patrolId } });
    },
    invalidate: [['patrols']],
  });
  const end = useApiMutation({ mutationFn: (input: { patrolId: string; summary: string }) => api.post("/api/patrols/{id}/end", { summary: input.summary }, { path: { id: input.patrolId } }), invalidate: [['patrols']], success: 'Patrol completed' });
  const abandon = useApiMutation({ mutationFn: (patrolId: string) => api.post("/api/patrols/{id}/abandon", { reason: 'Abandoned from console' }, { path: { id: patrolId } }), invalidate: [['patrols']] });
  const [summary, setSummary] = useState('');
  const live = day?.patrols.filter((p) => p.status === 'InProgress') ?? [];

  return (
    <div className="max-w-5xl space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold">Patrols</h1>
        <div className="flex gap-2">
          {sites && sites.length > 1 && (
            <Select className="w-56" value={activeSite} onChange={(e) => setSiteId(e.target.value)}>
              {sites.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
            </Select>
          )}
          {manage && activeSite && <RouteDialog siteId={activeSite} />}
        </div>
      </div>
      {day && <p className="text-sm text-muted-foreground">{day.site} · {day.businessDate} (site-local day)</p>}

      {live.map((p) => {
        const route = routes?.items.find((r) => r.id === p.routeId);
        const done = new Set(p.scans.map((s) => s.code));
        return (
          <Card key={p.id} className="border-primary">
            <CardHeader><CardTitle className="text-base">In progress: {p.routeName} <span className="ml-2 text-sm font-normal text-muted-foreground">{p.checkpointsScanned}/{p.checkpointsTotal} checkpoints</span></CardTitle></CardHeader>
            <CardContent className="space-y-3">
              <div className="flex flex-wrap gap-2">
                {route?.checkpoints.map((c) => (
                  <Button key={c.code} size="sm" variant={done.has(c.code) ? 'outline' : 'default'} disabled={!perform || done.has(c.code) || scan.isPending}
                    onClick={() => scan.mutate({ patrolId: p.id, code: c.code })}>
                    {done.has(c.code) ? '✓ ' : ''}{c.label}
                  </Button>
                ))}
              </div>
              <ul className="space-y-1 text-xs text-muted-foreground">
                {p.scans.map((s) => (
                  <li key={s.id}>{fmtDateTime(s.scannedAt)} · {s.label ?? s.code}
                    {s.distanceMeters != null && <span className={s.withinGeofence ? ' text-emerald-700 dark:text-emerald-300' : ' text-destructive'}> · {Math.round(Number(s.distanceMeters))} m</span>}
                    {s.note && ` · ${s.note}`}
                  </li>
                ))}
              </ul>
              {perform && (
                <div className="flex gap-2">
                  <Input placeholder="Summary" value={summary} onChange={(e) => setSummary(e.target.value)} />
                  <Button disabled={end.isPending} onClick={() => { end.mutate({ patrolId: p.id, summary }); setSummary(''); }}>End patrol</Button>
                  <ConfirmButton variant="ghost" onConfirm={() => abandon.mutate(p.id)}>Abandon</ConfirmButton>
                </div>
              )}
            </CardContent>
          </Card>
        );
      })}

      <div className="grid gap-6 md:grid-cols-2">
        <Card>
          <CardHeader><CardTitle className="text-base">Expected today</CardTitle></CardHeader>
          <CardContent className="space-y-2">
            {day?.expected.length === 0 && <p className="text-sm text-muted-foreground">No scheduled rounds today.</p>}
            {day?.expected.map((e) => (
              <div key={`${e.routeId}-${e.startLocal}`} className="flex items-center justify-between rounded-md border p-2 text-sm">
                <span><span className="font-medium">{e.routeName}</span><span className="ml-2 text-muted-foreground">{e.startLocal.slice(0, 5)}</span>
                  {e.status && <span className="ml-2 text-xs text-muted-foreground">{e.status}</span>}</span>
                {perform && !e.patrolId && <Button size="sm" onClick={() => start.mutate({ routeId: e.routeId, scheduledStartLocal: e.startLocal })}>Start</Button>}
              </div>
            ))}
            {perform && routes && routes.items.filter((r) => !r.archived).length > 0 && (
              <div className="pt-2">
                <Label>Unscheduled round</Label>
                <div className="mt-1 flex flex-wrap gap-2">
                  {routes.items.filter((r) => !r.archived).map((r) => (
                    <Button key={r.id} size="sm" variant="outline" onClick={() => start.mutate({ routeId: r.id, scheduledStartLocal: null })}>Start {r.name}</Button>
                  ))}
                </div>
              </div>
            )}
          </CardContent>
        </Card>

        <Card>
          <CardHeader><CardTitle className="text-base">Routes</CardTitle></CardHeader>
          <CardContent className="space-y-2">
            {routes?.items.length === 0 && <p className="text-sm text-muted-foreground">No routes yet.</p>}
            {routes?.items.map((r) => (
              <div key={r.id} className={`rounded-md border p-2 text-sm ${r.archived ? 'opacity-60' : ''}`}>
                <div className="flex items-center justify-between">
                  <span className="font-medium">{r.name}{r.archived && ' (archived)'}</span>
                  {manage && !r.archived && <ScheduleDialog route={r} />}
                </div>
                <div className="text-xs text-muted-foreground">{r.checkpoints.map((c) => c.label).join(' → ')} · ~{r.expectedMinutes} min</div>
                {r.schedules.map((s) => (
                  <div key={s.id} className="mt-1 flex items-center justify-between text-xs text-muted-foreground">
                    <span>{s.rRule} at {s.startLocal.slice(0, 5)}</span>
                    {manage && <RemoveSchedule id={s.id} />}
                  </div>
                ))}
                {manage && !r.archived && <ArchiveRoute route={r} />}
              </div>
            ))}
          </CardContent>
        </Card>
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center justify-between text-base">
            Daily activity report
            <Input type="date" className="w-40" value={date} onChange={(e) => setReportDate(e.target.value)} />
          </CardTitle>
        </CardHeader>
        <CardContent className="space-y-3 text-sm">
          {report && (
            <>
              <p className="text-muted-foreground">{report.completed} of {report.expected} expected rounds completed{report.missed.length > 0 && ` · ${report.missed.length} missed`}</p>
              {report.patrols.map((p) => (
                <div key={p.id} className="rounded-md border p-2">
                  <div className="flex items-center justify-between">
                    <span><span className="font-medium">{p.routeName}</span><span className="ml-2 text-muted-foreground">{p.status} · {p.checkpointsScanned}/{p.checkpointsTotal}</span></span>
                    <span className="text-xs text-muted-foreground">{fmtDateTime(p.startedAt)}{p.endedAt && ` → ${fmtDateTime(p.endedAt)}`} · {p.startedByLabel ?? '—'}</span>
                  </div>
                  {p.summary && <p className="mt-1 text-muted-foreground">{p.summary}</p>}
                </div>
              ))}
              <div className="text-xs uppercase text-muted-foreground">Incidents that day</div>
              {report.incidents.length === 0 && <p className="text-muted-foreground">None.</p>}
              {report.incidents.map((i) => (
                <div key={i.id} className="flex items-center justify-between rounded-md border p-2">
                  <Link to="/incidents/$incidentId" params={{ incidentId: i.id }} className="font-medium hover:underline">{i.title}</Link>
                  <span className="flex items-center gap-2 text-xs text-muted-foreground"><SeverityBadge severity={i.severity} />{fmtDateTime(i.occurredAt)}</span>
                </div>
              ))}
            </>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

function parseCheckpoints(text: string): Checkpoint[] {
  return text.split('\n').map((line) => line.trim()).filter(Boolean).map((line) => {
    const [code = '', label = '', coords = ''] = line.split('|').map((p) => p.trim());
    const [lat, lng] = coords.split(',').map((v) => Number(v.trim()));
    const latitude = lat != null && Number.isFinite(lat) ? lat : null;
    const longitude = lng != null && Number.isFinite(lng) ? lng : null;
    return { code: code.toUpperCase(), label: label || code, latitude, longitude };
  });
}

function RouteDialog({ siteId }: { siteId: string }) {
  const [open, setOpen] = useState(false);
  const [name, setName] = useState('');
  const [text, setText] = useState('');
  const [minutes, setMinutes] = useState('30');
  const create = useApiMutation({
    mutationFn: () => api.post('/api/patrols/routes', { siteId, name: name.trim(), checkpoints: parseCheckpoints(text), expectedMinutes: Number(minutes) }),
    invalidate: [['patrols']], success: 'Route created', onSuccess: () => { setOpen(false); setName(''); setText(''); },
  });
  return (
    <FormDialog open={open} onOpenChange={setOpen} title="New route" description="One checkpoint per line: CODE | Label | lat,lng (coordinates optional)." trigger={<Button>New route</Button>}>
      <div className="space-y-3">
        <div className="space-y-1"><Label htmlFor="rt-name">Name</Label><Input id="rt-name" value={name} onChange={(e) => setName(e.target.value)} /></div>
        <div className="space-y-1"><Label htmlFor="rt-cp">Checkpoints</Label><Textarea id="rt-cp" rows={5} value={text} placeholder={'DOCK | Loading dock | -36.8486,174.7634\nSAFE | Cash office'} onChange={(e) => setText(e.target.value)} /></div>
        <div className="space-y-1"><Label htmlFor="rt-min">Expected minutes</Label><Input id="rt-min" type="number" min="1" value={minutes} onChange={(e) => setMinutes(e.target.value)} /></div>
        <Button className="w-full" disabled={!name.trim() || !text.trim() || create.isPending} onClick={() => create.mutate()}>Create</Button>
      </div>
    </FormDialog>
  );
}

function ScheduleDialog({ route }: { route: Route }) {
  const [open, setOpen] = useState(false);
  const [days, setDays] = useState<Set<DayCode>>(new Set(['MO', 'TU', 'WE', 'TH', 'FR', 'SA', 'SU']));
  const [time, setTime] = useState('22:00');
  const create = useApiMutation({
    mutationFn: () => {
      const { rRule, anchorDate } = weeklySchedule(days, Date.now());
      return api.post("/api/patrols/routes/{id}/schedules", { rRule, anchorDate, startLocal: time }, { path: { id: route.id } });
    },
    invalidate: [['patrols']], success: 'Schedule added', onSuccess: () => setOpen(false),
  });
  const toggle = (d: DayCode) => setDays((s) => { const n = new Set(s); if (n.has(d)) n.delete(d); else n.add(d); return n; });
  return (
    <FormDialog open={open} onOpenChange={setOpen} title={`Schedule: ${route.name}`} description="Site-local time; the rule expands in the site's zone." trigger={<Button size="sm" variant="outline">Add schedule</Button>}>
      <div className="space-y-3">
        <div className="flex flex-wrap gap-2">
          {DAYS.map((d) => (
            <label key={d.code} className="flex items-center gap-1 text-sm"><input type="checkbox" className="size-4 accent-primary" checked={days.has(d.code)} onChange={() => toggle(d.code)} />{d.label}</label>
          ))}
        </div>
        <div className="space-y-1"><Label htmlFor="sc-time">Start (site-local)</Label><Input id="sc-time" type="time" value={time} onChange={(e) => setTime(e.target.value)} /></div>
        <Button className="w-full" disabled={days.size === 0 || create.isPending} onClick={() => create.mutate()}>Add</Button>
      </div>
    </FormDialog>
  );
}

function RemoveSchedule({ id }: { id: string }) {
  const remove = useApiMutation({ mutationFn: () => api.del("/api/patrols/schedules/{id}", { path: { id } }), invalidate: [['patrols']] });
  return <button className="hover:underline" onClick={() => remove.mutate()}>Remove</button>;
}

function ArchiveRoute({ route }: { route: Route }) {
  const archive = useApiMutation({
    mutationFn: () => api.put("/api/patrols/routes/{id}", { name: route.name, checkpoints: route.checkpoints, expectedMinutes: route.expectedMinutes, archived: true }, { path: { id: route.id } }),
    invalidate: [['patrols']], success: 'Route archived',
  });
  return <ConfirmButton size="sm" variant="ghost" className="mt-1" onConfirm={() => archive.mutate()}>Archive</ConfirmButton>;
}

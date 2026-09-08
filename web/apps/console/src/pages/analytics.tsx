import { SitePicker, type PickedSite, useSiteMetadata } from '../features/sites';
import { api } from '@locintel/api';
import { Card, CardContent, CardHeader, CardTitle, Select, Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@locintel/ui';
import { useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useState } from 'react';
import { IncidentMap, type MapPoint } from '../lib/incident-map';

import { categoryLabel } from '../features/incidents';





const WEEKDAYS: Record<string, string> = { '1': 'Mon', '2': 'Tue', '3': 'Wed', '4': 'Thu', '5': 'Fri', '6': 'Sat', '7': 'Sun' };
const isoDate = (d: Date) => d.toISOString().slice(0, 10);

/**
 * Rollups over the STAMPED business date, site-local hour, and weekday
 * (ADR 2/26): every chart here is a group-by on a stamped column. One
 * magnitude hue throughout; identity comes from the row label.
 */
export function AnalyticsPage() {
  const [days, setDays] = useState('30');
  const [pickedSite, setPickedSite] = useState<PickedSite | null>(null);
  const siteId = pickedSite?.id ?? '';
  const to = new Date();
  const from = new Date(to.getTime() - (Number(days) - 1) * 86_400_000);
  const params = new URLSearchParams({ from: isoDate(from), to: isoDate(to) });
  if (siteId) params.set('siteId', siteId);
  const { data: stats } = useQuery({
    queryKey: ['incidents', 'stats', params.toString()],
    queryFn: ({ signal }) => api.get('/api/incidents/stats', { signal, query: { from: isoDate(from), to: isoDate(to), siteId: siteId || undefined } }),
  });
  const { data: sites } = useSiteMetadata(stats?.bySite.map((site) => site.key) ?? []);
  const siteName = (id: string) => sites?.find((s) => s.id === id)?.name ?? id.slice(0, 8);
  const points: MapPoint[] = (stats?.bySite ?? []).flatMap((c) => {
    const site = sites?.find((s) => s.id === c.key);
    return site && site.latitude != null && site.longitude != null
      ? [{ id: site.id, name: site.name, lat: Number(site.latitude), lng: Number(site.longitude), count: Number(c.count), loss: Number(c.loss) }]
      : [];
  });
  const hours = Array.from({ length: 24 }, (_, h) => {
    const key = h.toString().padStart(2, '0');
    return { key, label: `${h}:00`, count: Number(stats?.byHour.find((x) => x.key === key)?.count ?? 0) };
  });
  const weekdays = Object.keys(WEEKDAYS).map((k) => ({ key: k, label: WEEKDAYS[k] ?? k, count: Number(stats?.byWeekday.find((x) => x.key === k)?.count ?? 0) }));

  return (
    <div className="max-w-5xl space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold">Analytics</h1>
        <div className="flex gap-2">
          <Select className="w-40" value={days} onChange={(e) => setDays(e.target.value)}>
            <option value="7">Last 7 days</option>
            <option value="30">Last 30 days</option>
            <option value="90">Last 90 days</option>
            <option value="365">Last year</option>
          </Select>
          <SitePicker aria-label="Filter by site" placeholder="All sites" value={pickedSite} onChange={setPickedSite} />
        </div>
      </div>

      <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        <Tile label="Incidents" value={stats?.total} />
        <Tile label="Open" value={stats?.open} />
        <Tile label="Reported loss" value={stats ? `$${Number(stats.totalLoss).toLocaleString(undefined, { maximumFractionDigits: 0 })}` : undefined} />
        <Tile label="Sites affected" value={stats?.bySite.length} />
      </div>

      {points.length > 0 && (
        <Card>
          <CardHeader><CardTitle className="text-base">Where</CardTitle></CardHeader>
          <CardContent><IncidentMap points={points} /></CardContent>
        </Card>
      )}

      <div className="grid gap-6 md:grid-cols-2">
        <Bars title="By category" rows={(stats?.byCategory ?? []).map((c) => ({ key: c.key, label: categoryLabel(c.key), count: Number(c.count), loss: Number(c.loss) }))} showLoss />
        <Bars title="By severity" rows={(stats?.bySeverity ?? []).map((c) => ({ key: c.key, label: c.key, count: Number(c.count), loss: Number(c.loss) }))} />
        <Bars title="By hour of day (site-local)" rows={hours} dense />
        <Bars title="By weekday (site-local)" rows={weekdays} />
      </div>

      <Card>
        <CardHeader><CardTitle className="text-base">By site</CardTitle></CardHeader>
        <CardContent className="p-0">
          <Table>
            <TableHeader>
              <TableRow><TableHead>Site</TableHead><TableHead className="text-right">Incidents</TableHead><TableHead className="text-right">Loss</TableHead></TableRow>
            </TableHeader>
            <TableBody>
              {stats?.bySite.length === 0 && <TableRow><TableCell colSpan={3} className="text-center text-sm text-muted-foreground">No incidents in this window.</TableCell></TableRow>}
              {stats?.bySite.map((s) => (
                <TableRow key={s.key}>
                  <TableCell><Link to="/incidents" className="hover:underline">{siteName(s.key)}</Link></TableCell>
                  <TableCell className="text-right tabular-nums">{s.count}</TableCell>
                  <TableCell className="text-right tabular-nums">{Number(s.loss) > 0 ? `$${Number(s.loss).toLocaleString(undefined, { maximumFractionDigits: 0 })}` : '—'}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </CardContent>
      </Card>

      <details className="text-sm">
        <summary className="cursor-pointer text-muted-foreground">Daily counts (table)</summary>
        <Table>
          <TableHeader><TableRow><TableHead>Business date</TableHead><TableHead className="text-right">Incidents</TableHead><TableHead className="text-right">Loss</TableHead></TableRow></TableHeader>
          <TableBody>
            {stats?.byBusinessDate.map((d) => (
              <TableRow key={d.key}><TableCell>{d.key}</TableCell><TableCell className="text-right tabular-nums">{d.count}</TableCell><TableCell className="text-right tabular-nums">{Number(d.loss) > 0 ? `$${Number(d.loss).toLocaleString()}` : '—'}</TableCell></TableRow>
            ))}
          </TableBody>
        </Table>
      </details>
    </div>
  );
}

function Tile({ label, value }: { label: string; value: number | string | undefined }) {
  return (
    <Card>
      <CardContent className="pt-5">
        <div className="text-3xl font-semibold tabular-nums">{value ?? '—'}</div>
        <div className="text-sm text-muted-foreground">{label}</div>
      </CardContent>
    </Card>
  );
}

/** Horizontal bars, one magnitude hue; labels in text ink; hover shows the exact value. */
function Bars({ title, rows, showLoss, dense }: {
  title: string; rows: { key: string; label: string; count: number; loss?: number }[]; showLoss?: boolean; dense?: boolean;
}) {
  const max = Math.max(1, ...rows.map((r) => r.count));
  return (
    <Card>
      <CardHeader><CardTitle className="text-base">{title}</CardTitle></CardHeader>
      <CardContent>
        {rows.length === 0 && <p className="text-sm text-muted-foreground">Nothing in this window.</p>}
        <div className={dense ? 'space-y-0.5' : 'space-y-1.5'} role="list">
          {rows.map((r) => (
            <div key={r.key} role="listitem" className="flex items-center gap-2 text-xs"
              title={`${r.label}: ${r.count}${r.loss ? ` · $${r.loss.toLocaleString()}` : ''}`}>
              <span className={`${dense ? 'w-10' : 'w-28'} shrink-0 truncate text-muted-foreground`}>{r.label}</span>
              <span className="h-2.5 flex-1 overflow-hidden rounded-sm bg-muted">
                <span className="block h-full rounded-sm bg-primary" style={{ width: `${(r.count / max) * 100}%`, opacity: r.count === 0 ? 0 : 0.45 + 0.55 * (r.count / max) }} />
              </span>
              <span className="w-8 shrink-0 text-right tabular-nums">{r.count || ''}</span>
              {showLoss && <span className="w-16 shrink-0 text-right tabular-nums text-muted-foreground">{r.loss ? `$${r.loss.toLocaleString(undefined, { maximumFractionDigits: 0 })}` : ''}</span>}
            </div>
          ))}
        </div>
      </CardContent>
    </Card>
  );
}

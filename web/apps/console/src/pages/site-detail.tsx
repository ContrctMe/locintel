import { api } from '@locintel/api';
import { Button, Card, CardContent, CardHeader, CardTitle, ConfirmButton, FormDialog,
  Input, Label, Table, TableBody, TableCell, TableHead, TableHeader, TableRow,
  TimeZoneSelect } from '@locintel/ui';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useParams } from '@tanstack/react-router';
import { useState } from 'react';
import { fmtDayInZone, fmtTimeInZone } from '../lib/format';
import { useApiMutation } from '../lib/mutation';
import { weeklySchedule, type DayCode } from '../lib/schedule';
import { can, useMe } from '../session';
import { StatusBadge } from '../shell';

type Site = {
  id: string; name: string; timeZone: string; status: string; version: number;
  addressLine1: string | null; city: string | null;
  postalCode: string | null; countryCode: string | null;
  latitude: number | null; longitude: number | null;
  attributes: Record<string, string | number | boolean>;
};
type AttributeDefinition = { id: string; key: string; label: string; type: string; public: boolean };
type Schedule = {
  id: string; name: string; rRule: string;
  anchorDate: string; opens: string; closes: string; exDates: string[];
};
type Window = { startsAtUtc: string; endsAtUtc: string; localDate: string };

const DAYS = [
  { code: 'MO', label: 'Mon' }, { code: 'TU', label: 'Tue' }, { code: 'WE', label: 'Wed' },
  { code: 'TH', label: 'Thu' }, { code: 'FR', label: 'Fri' }, { code: 'SA', label: 'Sat' },
  { code: 'SU', label: 'Sun' },
] as const;

function describeRule(rrule: string): string {
  const byday = /BYDAY=([A-Z,]+)/.exec(rrule)?.[1];
  if (rrule.includes('FREQ=DAILY')) return 'Every day';
  if (byday) {
    const codes = byday.split(',');
    if (codes.length === 7) return 'Every day';
    if (codes.join(',') === 'MO,TU,WE,TH,FR') return 'Weekdays';
    if (codes.join(',') === 'SA,SU') return 'Weekends';
    const labels = codes.map((code) => DAYS.find((d) => d.code === code)?.label ?? code);
    return labels.join(', ');
  }
  return rrule;
}

export function SiteDetailPage() {
  const { siteId } = useParams({ strict: false }) as { siteId: string };
  const { data: me } = useMe();
  const queryClient = useQueryClient();
  const manage = can(me, 'sites:manage');

  const { data: site } = useQuery({
    queryKey: ['site', siteId],
    queryFn: () => api.get<Site>(`/api/sites/${siteId}`),
  });
  const { data: schedules } = useQuery({
    queryKey: ['schedules', siteId],
    queryFn: () => api.get<Schedule[]>(`/api/sites/${siteId}/schedules`),
  });
  const { data: windows } = useQuery({
    queryKey: ['windows', siteId],
    queryFn: () => api.get<Window[]>(`/api/sites/${siteId}/windows?days=7`),
  });

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ['site', siteId] });
    void queryClient.invalidateQueries({ queryKey: ['schedules', siteId] });
    // the projection rebuilds async: refresh the preview shortly after
    setTimeout(
      () => void queryClient.invalidateQueries({ queryKey: ['windows', siteId] }),
      1500,
    );
  };

  const update = useApiMutation({
    mutationFn: (
      body: Partial<{
        name: string; timeZone: string; status: string;
        addressLine1: string; city: string; postalCode: string; countryCode: string;
        latitude: number; longitude: number;
        attributes: Record<string, string | number | boolean | null>;
      }>,
    ) =>
      // echo the version we loaded: a 409 means someone else saved first
      api.post(`/api/sites/${siteId}`, { ...body, version: site?.version }),
    success: 'Site updated',
    onSuccess: invalidate,
    onError: invalidate, // a conflict means our copy is stale - reload it
  });
  const addSchedule = useApiMutation({
    mutationFn: (body: object) => api.post(`/api/sites/${siteId}/schedules`, body),
    success: 'Hours added',
    onSuccess: invalidate,
  });
  const removeSchedule = useApiMutation({
    mutationFn: (scheduleId: string) =>
      api.del(`/api/sites/${siteId}/schedules/${scheduleId}`),
    success: 'Hours removed',
    onSuccess: invalidate,
  });

  const [editOpen, setEditOpen] = useState(false);
  const [editName, setEditName] = useState('');
  const [editZone, setEditZone] = useState('');
  const [editAddress, setEditAddress] = useState('');
  const [editCity, setEditCity] = useState('');
  const [editPostal, setEditPostal] = useState('');
  const [editCountry, setEditCountry] = useState('');
  const [editLat, setEditLat] = useState('');
  const [editLng, setEditLng] = useState('');
  const [editAttributes, setEditAttributes] = useState<Record<string, string>>({});
  const { data: definitions } = useQuery({
    queryKey: ['site-attributes'],
    queryFn: () => api.get<AttributeDefinition[]>('/api/sites/attributes'),
  });
  const { data: closures } = useQuery({
    queryKey: ['closures', siteId],
    queryFn: () => api.get<string[]>(`/api/sites/${siteId}/closures`),
  });
  const addClosure = useApiMutation({
    mutationFn: (date: string) => api.post(`/api/sites/${siteId}/closures`, { date }),
    invalidate: [['closures', siteId]],
    success: 'Day closed',
    errorFallback: 'Could not close that day',
    onSuccess: invalidate,
  });
  const removeClosure = useApiMutation({
    mutationFn: (date: string) => api.del(`/api/sites/${siteId}/closures/${date}`),
    invalidate: [['closures', siteId]],
    success: 'Day reopened',
    onSuccess: invalidate,
  });
  const [closureDate, setClosureDate] = useState('');

  const [days, setDays] = useState<string[]>(['MO', 'TU', 'WE', 'TH', 'FR']);
  const [opens, setOpens] = useState('09:00');
  const [closes, setCloses] = useState('17:00');
  const [scheduleName, setScheduleName] = useState('Regular hours');

  if (!site) return <div className="text-muted-foreground">Loading…</div>;
  const closed = site.status === 'Closed';

  return (
    <div className="max-w-3xl space-y-6">
      <Link to="/sites" className="text-sm text-muted-foreground hover:underline">
        ← All sites
      </Link>
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <h1 className="text-2xl font-semibold">{site.name}</h1>
          <StatusBadge status={site.status} />
        </div>
        {manage && (
          <div className="flex gap-2">
            <FormDialog
              open={editOpen}
              onOpenChange={setEditOpen}
              trigger={
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => {
                    setEditName(site.name);
                    setEditZone(site.timeZone);
                    setEditAddress(site.addressLine1 ?? '');
                    setEditCity(site.city ?? '');
                    setEditPostal(site.postalCode ?? '');
                    setEditCountry(site.countryCode ?? '');
                    setEditLat(site.latitude?.toString() ?? '');
                    setEditLng(site.longitude?.toString() ?? '');
                    setEditAttributes(
                      Object.fromEntries(
                        Object.entries(site.attributes ?? {}).map(([k, v]) => [k, String(v)]),
                      ),
                    );
                  }}
                >
                  Edit
                </Button>
              }
              title="Edit site"
              description="Changing the time zone re-anchors the hours projection."
            >
              <div className="space-y-3">
                <div className="space-y-1">
                  <Label htmlFor="edit-site-name">Name</Label>
                  <Input id="edit-site-name" value={editName}
                    onChange={(e) => setEditName(e.target.value)} />
                </div>
                <div className="space-y-1">
                  <Label htmlFor="edit-site-tz">Time zone</Label>
                  <TimeZoneSelect id="edit-site-tz" value={editZone}
                    onChange={(e) => setEditZone(e.target.value)} />
                </div>
                <div className="space-y-1">
                  <Label htmlFor="edit-site-address">Street address</Label>
                  <Input id="edit-site-address" value={editAddress}
                    onChange={(e) => setEditAddress(e.target.value)} />
                </div>
                <div className="grid grid-cols-2 gap-2">
                  <div className="space-y-1">
                    <Label htmlFor="edit-site-city">City</Label>
                    <Input id="edit-site-city" value={editCity}
                      onChange={(e) => setEditCity(e.target.value)} />
                  </div>
                  <div className="space-y-1">
                    <Label htmlFor="edit-site-postal">Postal code</Label>
                    <Input id="edit-site-postal" value={editPostal}
                      onChange={(e) => setEditPostal(e.target.value)} />
                  </div>
                </div>
                <div className="space-y-1">
                  <Label htmlFor="edit-site-country">Country code</Label>
                  <Input id="edit-site-country" value={editCountry} placeholder="US"
                    maxLength={2} onChange={(e) => setEditCountry(e.target.value)} />
                </div>
                <div className="grid grid-cols-2 gap-2">
                  <div className="space-y-1">
                    <Label htmlFor="edit-site-lat">Latitude</Label>
                    <Input id="edit-site-lat" value={editLat} placeholder="42.3601"
                      onChange={(e) => setEditLat(e.target.value)} />
                  </div>
                  <div className="space-y-1">
                    <Label htmlFor="edit-site-lng">Longitude</Label>
                    <Input id="edit-site-lng" value={editLng} placeholder="-71.0589"
                      onChange={(e) => setEditLng(e.target.value)} />
                  </div>
                </div>
                <p className="text-xs text-muted-foreground">
                  Coordinates put this site on the public locator map.
                </p>
                {definitions?.map((d) => (
                  <div key={d.id} className="space-y-1">
                    <Label htmlFor={`attr-${d.key}`}>{d.label}</Label>
                    {d.type === 'Boolean' ? (
                      <label className="flex items-center gap-2 text-sm">
                        <input id={`attr-${d.key}`} type="checkbox" className="size-4 accent-primary"
                          checked={editAttributes[d.key] === 'true'}
                          onChange={(e) =>
                            setEditAttributes({ ...editAttributes, [d.key]: String(e.target.checked) })
                          } />
                        {d.label}
                      </label>
                    ) : (
                      <Input id={`attr-${d.key}`} value={editAttributes[d.key] ?? ''}
                        inputMode={d.type === 'Number' ? 'decimal' : undefined}
                        onChange={(e) =>
                          setEditAttributes({ ...editAttributes, [d.key]: e.target.value })
                        } />
                    )}
                  </div>
                ))}
                <Button className="w-full"
                  disabled={!editName.trim() || update.isPending}
                  onClick={() =>
                    update.mutate(
                      {
                        name: editName.trim(),
                        timeZone: editZone,
                        addressLine1: editAddress.trim(),
                        city: editCity.trim(),
                        postalCode: editPostal.trim(),
                        countryCode: editCountry.trim(),
                        ...(Number.isFinite(Number.parseFloat(editLat)) &&
                        Number.isFinite(Number.parseFloat(editLng))
                          ? {
                              latitude: Number.parseFloat(editLat),
                              longitude: Number.parseFloat(editLng),
                            }
                          : {}),
                        attributes: Object.fromEntries(
                          (definitions ?? []).map((d) => {
                            const raw = editAttributes[d.key] ?? '';
                            if (raw === '') return [d.key, null]; // cleared
                            if (d.type === 'Boolean') return [d.key, raw === 'true'];
                            if (d.type === 'Number') return [d.key, Number.parseFloat(raw)];
                            return [d.key, raw];
                          }),
                        ),
                      },
                      { onSuccess: () => setEditOpen(false) },
                    )
                  }>
                  Save
                </Button>
              </div>
            </FormDialog>
            <ConfirmButton
              variant={closed ? 'default' : 'destructive'}
              size="sm"
              confirmLabel={closed ? 'Reopen?' : 'Close to the public?'}
              disabled={update.isPending}
              onConfirm={() => update.mutate({ status: closed ? 'Open' : 'Closed' })}
            >
              {closed ? 'Reopen site' : 'Close site'}
            </ConfirmButton>
          </div>
        )}
      </div>
      <p className="text-sm text-muted-foreground">
        Time zone: {site.timeZone}
        {site.addressLine1 &&
          ` · ${[site.addressLine1, site.city, site.postalCode, site.countryCode]
            .filter(Boolean)
            .join(', ')}`}
      </p>

      <Card>
        <CardHeader><CardTitle>Operating hours</CardTitle></CardHeader>
        <CardContent className="space-y-4">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Name</TableHead>
                <TableHead>Days</TableHead>
                <TableHead>Hours (local)</TableHead>
                {manage && <TableHead className="w-28" />}
              </TableRow>
            </TableHeader>
            <TableBody>
              {schedules?.map((s) => (
                <TableRow key={s.id}>
                  <TableCell>{s.name}</TableCell>
                  <TableCell className="text-muted-foreground">{describeRule(s.rRule)}</TableCell>
                  <TableCell>
                    {s.opens.slice(0, 5) === '00:00' && s.closes.slice(0, 5) === '23:59'
                      ? 'Open 24 hours'
                      : `${s.opens.slice(0, 5)} – ${s.closes.slice(0, 5)}`}
                  </TableCell>
                  {manage && (
                    <TableCell className="w-28 text-right">
                      <ConfirmButton size="sm" disabled={removeSchedule.isPending}
                        onConfirm={() => removeSchedule.mutate(s.id)}>
                        Remove
                      </ConfirmButton>
                    </TableCell>
                  )}
                </TableRow>
              ))}
              {schedules?.length === 0 && (
                <TableRow>
                  <TableCell colSpan={4} className="text-center text-muted-foreground">
                    No hours defined.
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>

          {manage && (
            <div className="space-y-3 border-t pt-4">
              <div className="flex gap-1">
                {DAYS.map((d) => (
                  <Button
                    key={d.code}
                    size="sm"
                    variant={days.includes(d.code) ? 'default' : 'outline'}
                    onClick={() =>
                      setDays(days.includes(d.code)
                        ? days.filter((x) => x !== d.code)
                        : [...days, d.code])
                    }
                  >
                    {d.label}
                  </Button>
                ))}
              </div>
              <div className="flex items-end gap-3">
                <div className="flex-1 space-y-1">
                  <Label htmlFor="sched-name">Name</Label>
                  <Input id="sched-name" value={scheduleName}
                    onChange={(e) => setScheduleName(e.target.value)} />
                </div>
                <div className="space-y-1">
                  <Label htmlFor="sched-opens">Opens</Label>
                  <Input id="sched-opens" type="time" value={opens}
                    onChange={(e) => setOpens(e.target.value)} />
                </div>
                <div className="space-y-1">
                  <Label htmlFor="sched-closes">Closes</Label>
                  <Input id="sched-closes" type="time" value={closes}
                    onChange={(e) => setCloses(e.target.value)} />
                </div>
                <Button
                  disabled={days.length === 0 || !scheduleName || addSchedule.isPending}
                  onClick={() =>
                    addSchedule.mutate({
                      name: scheduleName,
                      ...weeklySchedule([...days] as DayCode[], Date.now()),
                      opens,
                      closes,
                    })
                  }
                >
                  Add hours
                </Button>
              </div>
            </div>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader><CardTitle>Holiday closures</CardTitle></CardHeader>
        <CardContent className="space-y-3">
          {closures && closures.length > 0 ? (
            <ul className="space-y-1 text-sm">
              {closures.map((date) => (
                <li key={date} className="flex items-center justify-between">
                  <span>
                    {new Date(`${date}T00:00:00`).toLocaleDateString([], {
                      weekday: 'long',
                      month: 'short',
                      day: 'numeric',
                      year: 'numeric',
                    })}
                  </span>
                  {manage && (
                    <ConfirmButton size="sm" confirmLabel="Reopen?"
                      disabled={removeClosure.isPending}
                      onConfirm={() => removeClosure.mutate(date)}>
                      Reopen
                    </ConfirmButton>
                  )}
                </li>
              ))}
            </ul>
          ) : (
            <p className="text-sm text-muted-foreground">No upcoming closures.</p>
          )}
          {manage && (
            <div className="flex items-end gap-2 border-t pt-3">
              <div className="space-y-1">
                <Label htmlFor="closure-date">Close a day</Label>
                <Input id="closure-date" type="date" value={closureDate}
                  onChange={(e) => setClosureDate(e.target.value)} />
              </div>
              <Button size="sm" disabled={!closureDate || addClosure.isPending}
                onClick={() => {
                  addClosure.mutate(closureDate);
                  setClosureDate('');
                }}>
                Close this day
              </Button>
            </div>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader><CardTitle>Open this week</CardTitle></CardHeader>
        <CardContent>
          {windows?.length === 0 ? (
            <p className="text-sm text-muted-foreground">
              No open windows in the next 7 days.
            </p>
          ) : (
            <ul className="space-y-1 text-sm">
              {windows?.map((w) => (
                <li key={w.startsAtUtc} className="flex justify-between">
                  <span>{fmtDayInZone(w.startsAtUtc, site.timeZone)}</span>
                  <span className="text-muted-foreground">
                    {/* the SITE's clock, not the viewer's (UX review P0); the
                        public app's all-day treatment applies here too */}
                    {fmtTimeInZone(w.startsAtUtc, site.timeZone) === '12:00 AM' &&
                    fmtTimeInZone(w.endsAtUtc, site.timeZone) === '11:59 PM'
                      ? 'Open 24 hours'
                      : `${fmtTimeInZone(w.startsAtUtc, site.timeZone)} – ${fmtTimeInZone(w.endsAtUtc, site.timeZone)}`}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

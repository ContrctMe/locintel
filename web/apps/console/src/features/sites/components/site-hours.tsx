import type { components } from '@locintel/api';
import { Button, Card, CardContent, CardHeader, CardTitle, ConfirmButton, Input, Label,
  Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@locintel/ui';
import { useState } from 'react';
import { fmtDayInZone, fmtTimeInZone } from '../../../lib/format';
import { useApiMutation } from '../../../lib/mutation';
import { weeklySchedule, type DayCode } from '../../../lib/schedule';
import { sitesApi } from '../api';
import { useSiteClosures, useSiteSchedules, useSiteWindows, useRefreshSite } from '../hooks';

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

/** A site's hours, exceptions, and projected preview share one mutation lifecycle. */
export function SiteHours({ siteId, timeZone, manage }: {
  siteId: string;
  timeZone: string;
  manage: boolean;
}) {
  const { data: schedules } = useSiteSchedules(siteId);
  const { data: windows } = useSiteWindows(siteId);
  const invalidate = useRefreshSite(siteId);
  const addSchedule = useApiMutation({
    mutationFn: (body: components['schemas']['CreateScheduleRequest']) =>
      sitesApi.createSchedule(siteId, body),
    success: 'Hours added',
    onSuccess: invalidate,
  });
  const removeSchedule = useApiMutation({
    mutationFn: (scheduleId: string) =>
      sitesApi.deleteSchedule(siteId, scheduleId),
    success: 'Hours removed',
    onSuccess: invalidate,
  });

  const { data: closures } = useSiteClosures(siteId);
  const addClosure = useApiMutation({
    mutationFn: (date: string) => sitesApi.addClosure(siteId, date),
    invalidate: [['closures', siteId]],
    success: 'Day closed',
    errorFallback: 'Could not close that day',
    onSuccess: () => { setClosureDate(''); invalidate(); },
  });
  const removeClosure = useApiMutation({
    mutationFn: (date: string) => sitesApi.removeClosure(siteId, date),
    invalidate: [['closures', siteId]],
    success: 'Day reopened',
    onSuccess: invalidate,
  });
  const [closureDate, setClosureDate] = useState('');

  const [days, setDays] = useState<string[]>(['MO', 'TU', 'WE', 'TH', 'FR']);
  const [opens, setOpens] = useState('09:00');
  const [closes, setCloses] = useState('17:00');
  const [scheduleName, setScheduleName] = useState('Regular hours');

  return (
    <>
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
                  <span>{fmtDayInZone(w.startsAtUtc, timeZone)}</span>
                  <span className="text-muted-foreground">
                    {/* the SITE's clock, not the viewer's (UX review P0); the
                        public app's all-day treatment applies here too */}
                    {fmtTimeInZone(w.startsAtUtc, timeZone) === '12:00 AM' &&
                    fmtTimeInZone(w.endsAtUtc, timeZone) === '11:59 PM'
                      ? 'Open 24 hours'
                      : `${fmtTimeInZone(w.startsAtUtc, timeZone)} – ${fmtTimeInZone(w.endsAtUtc, timeZone)}`}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </CardContent>
      </Card>

    </>
  );
}

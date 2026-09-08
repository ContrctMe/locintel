import type { components } from '@locintel/api';
import { Button, ConfirmButton, Field, FieldLabel, Input, type ColumnDef, type DataGridFeatures } from '@locintel/ui';
import { useMemo, useState } from 'react';
import { DateField } from '../../../components/date-field';
import { fmtDayInZone, fmtTimeInZone } from '../../../lib/format';
import { Grid, Panel } from '../../../components/page';
import { useApiMutation } from '../../../lib/mutation';
import { weeklySchedule, type DayCode } from '../../../lib/schedule';
import { sitesApi } from '../api';
import { useSiteClosures, useSiteSchedules, useSiteWindows, useRefreshSite } from '../hooks';

const DAYS = [
  { code: 'MO', label: 'Mon' }, { code: 'TU', label: 'Tue' }, { code: 'WE', label: 'Wed' },
  { code: 'TH', label: 'Thu' }, { code: 'FR', label: 'Fri' }, { code: 'SA', label: 'Sat' },
  { code: 'SU', label: 'Sun' },
] as const;

/** "09:00" (a site-local wall-clock time) as the viewer's clock style: "9:00 AM". */
export function fmtClock(time: string): string {
  const [h, m] = time.split(':').map(Number);
  if (!Number.isFinite(h) || !Number.isFinite(m)) return time;
  return new Date(2000, 0, 1, h, m).toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' });
}

export function describeRule(rrule: string): string {
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
    onSuccess: () => {
      // the form is spent: the next set of hours starts from a blank name
      setScheduleName('');
      setDuplicate(null);
      invalidate();
    },
  });
  const [duplicate, setDuplicate] = useState<string | null>(null);
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

  type ScheduleRow = NonNullable<typeof schedules>[number];
  const scheduleColumns = useMemo<ColumnDef<DataGridFeatures, ScheduleRow>[]>(
    () => [
      { id: 'name', accessorKey: 'name', header: 'Name' },
      { id: 'days', header: 'Days', cell: ({ row }) => <span className="text-muted-foreground">{describeRule(row.original.rRule)}</span> },
      {
        id: 'hours',
        header: 'Hours (local)',
        cell: ({ row }) =>
          row.original.opens.slice(0, 5) === '00:00' && row.original.closes.slice(0, 5) === '23:59'
            ? 'Open 24 hours'
            : `${fmtClock(row.original.opens)} – ${fmtClock(row.original.closes)}`,
      },
      ...(manage
        ? [
            {
              id: 'actions',
              header: () => <span className="sr-only">Actions</span>,
              cell: ({ row }: { row: { original: ScheduleRow } }) => (
                <div className="text-right">
                  <ConfirmButton size="sm" disabled={removeSchedule.isPending} onConfirm={() => removeSchedule.mutate(row.original.id)}>
                    Remove
                  </ConfirmButton>
                </div>
              ),
              meta: { headerClassName: 'w-28' },
            } satisfies ColumnDef<DataGridFeatures, ScheduleRow>,
          ]
        : []),
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [manage],
  );
  const [days, setDays] = useState<string[]>(['MO', 'TU', 'WE', 'TH', 'FR']);
  const [opens, setOpens] = useState('09:00');
  const [closes, setCloses] = useState('17:00');
  const [scheduleName, setScheduleName] = useState('');

  return (
    <>
      <Grid
        title="Operating hours"
        columns={scheduleColumns}
        rows={schedules ?? []}
        getRowId={(s) => s.id}
        isLoading={schedules === undefined}
        emptyMessage="No hours defined."
      >
          {manage && (
            <div className="space-y-3 border-t px-4 py-4">
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
                <Field className="flex-1">
                  <FieldLabel htmlFor="sched-name">Name</FieldLabel>
                  <Input id="sched-name" value={scheduleName} placeholder="Regular hours"
                    onChange={(e) => setScheduleName(e.target.value)} />
                </Field>
                <Field className="w-36">
                  <FieldLabel htmlFor="sched-opens">Opens</FieldLabel>
                  <Input id="sched-opens" type="time" value={opens}
                    onChange={(e) => setOpens(e.target.value)} />
                </Field>
                <Field className="w-36">
                  <FieldLabel htmlFor="sched-closes">Closes</FieldLabel>
                  <Input id="sched-closes" type="time" value={closes}
                    onChange={(e) => setCloses(e.target.value)} />
                </Field>
                <Button
                  disabled={days.length === 0 || !scheduleName.trim() || addSchedule.isPending}
                  onClick={() => {
                    const rule = weeklySchedule([...days] as DayCode[], Date.now());
                    // the same days and times twice is a slip, not a second rule
                    const twin = schedules?.find(
                      (s) => s.rRule === rule.rRule && s.opens.slice(0, 5) === opens && s.closes.slice(0, 5) === closes,
                    );
                    if (twin) {
                      setDuplicate(`"${twin.name}" already covers those days and times.`);
                      return;
                    }
                    setDuplicate(null);
                    addSchedule.mutate({ name: scheduleName.trim(), ...rule, opens, closes });
                  }}
                >
                  Add hours
                </Button>
              </div>
              {duplicate && (
                <p role="alert" className="text-sm text-destructive">
                  {duplicate}
                </p>
              )}
            </div>
          )}
      </Grid>

      <Panel title="Holiday closures" bodyClassName="space-y-3">
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
              <Field>
                <FieldLabel htmlFor="closure-date">Close a day</FieldLabel>
                <DateField id="closure-date" value={closureDate} onChange={setClosureDate} placeholder="Pick a day" />
              </Field>
              <Button size="sm" disabled={!closureDate || addClosure.isPending}
                onClick={() => {
                  addClosure.mutate(closureDate);
                }}>
                Close this day
              </Button>
            </div>
          )}
        </Panel>

      <Panel title="Open this week">
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
        </Panel>

    </>
  );
}

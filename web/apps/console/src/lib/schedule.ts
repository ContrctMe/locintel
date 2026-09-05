/**
 * Weekly-hours builder, pure so the timezone arithmetic stays testable.
 * The anchor sits a week back: BYDAY picks the days, and a UTC-"today"
 * anchor is TOMORROW for an evening-US admin - hours must start now, not
 * after midnight UTC. (This was a real bug caught in a browser smoke.)
 */
const DAY_ORDER = ['MO', 'TU', 'WE', 'TH', 'FR', 'SA', 'SU'] as const;
export type DayCode = (typeof DAY_ORDER)[number];

export function weeklySchedule(days: Iterable<DayCode>, now: number): {
  rRule: string; // the contract's spelling (CreateScheduleRequest)
  anchorDate: string;
} {
  const sorted = [...days].sort((a, b) => DAY_ORDER.indexOf(a) - DAY_ORDER.indexOf(b));
  return {
    rRule: `FREQ=WEEKLY;BYDAY=${sorted.join(',')}`,
    anchorDate: new Date(now - 7 * 86400_000).toISOString().slice(0, 10),
  };
}

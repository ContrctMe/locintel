import { describe, expect, it } from 'vitest';
import { weeklySchedule } from './schedule';

describe('weeklySchedule', () => {
  it('sorts BYDAY into calendar order regardless of click order', () => {
    const { rRule } = weeklySchedule(['FR', 'MO', 'WE'], Date.UTC(2026, 5, 15));
    expect(rRule).toBe('FREQ=WEEKLY;BYDAY=MO,WE,FR');
  });

  it('anchors a full week back so hours start today in every timezone', () => {
    // the regression this guards: a UTC-"today" anchor is TOMORROW for an
    // evening-US admin, silently deferring their hours past midnight UTC
    const now = Date.UTC(2026, 5, 15, 1, 30); // just past midnight UTC
    const { anchorDate } = weeklySchedule(['MO'], now);
    expect(anchorDate).toBe('2026-06-08');
    const daysBack = (now - Date.parse(anchorDate)) / 86400_000;
    expect(daysBack).toBeGreaterThanOrEqual(7);
  });

  it('emits a date-only anchor', () => {
    expect(weeklySchedule(['SU'], Date.now()).anchorDate).toMatch(/^\d{4}-\d{2}-\d{2}$/);
  });
});

import { describe, expect, it } from 'vitest';
import { describeRule, fmtClock } from './site-hours';

describe('hours as people read them', () => {
  it('names the common day sets', () => {
    expect(describeRule('FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR')).toBe('Weekdays');
    expect(describeRule('FREQ=WEEKLY;BYDAY=SA,SU')).toBe('Weekends');
    expect(describeRule('FREQ=DAILY')).toBe('Every day');
    expect(describeRule('FREQ=WEEKLY;BYDAY=MO,WE')).toBe('Mon, Wed');
  });

  it('shows a wall-clock time in the viewer\'s clock style', () => {
    expect(fmtClock('09:00')).toMatch(/9:00/);
    expect(fmtClock('17:30')).toMatch(/5:30|17:30/);
    expect(fmtClock('noon')).toBe('noon');
  });
});

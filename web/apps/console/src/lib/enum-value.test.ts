import { expect, it } from 'vitest';
import { enumValue } from './enum-value';

it('accepts only declared choices, including an explicitly allowed empty choice', () => {
  expect(enumValue(['Open', 'Closed'], 'Open')).toBe('Open');
  expect(enumValue(['', 'Open'], '')).toBe('');
  expect(() => enumValue(['Open', 'Closed'], '')).toThrow('Invalid selection');
  expect(() => enumValue(['Open', 'Closed'], 'open')).toThrow('Invalid selection');
  expect(() => enumValue([], 'Open')).toThrow('Invalid selection');
});

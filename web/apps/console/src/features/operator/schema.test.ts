import { describe, expect, it } from 'vitest';
import { parseEntitlementValue } from './schema';

describe('operator entitlement validation', () => {
  it('normalizes values and rejects blanks', () => {
    expect(parseEntitlementValue(' 100 ')).toBe('100');
    expect(() => parseEntitlementValue('   ')).toThrow('entitlement value is required');
  });
});

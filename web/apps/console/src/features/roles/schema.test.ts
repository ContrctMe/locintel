import { describe, expect, it } from 'vitest';
import { parseGrant } from './schema';

describe('role form validation', () => {
  it('parses complete grants and rejects incomplete values', () => {
    expect(parseGrant('sites:read')).toEqual({ domain: 'sites', action: 'read' });
    expect(() => parseGrant('sites')).toThrow('invalid capability');
  });
});

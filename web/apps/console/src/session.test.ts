import { describe, expect, it } from 'vitest';
import { parseMe } from './session';

describe('session response validation', () => {
  it('accepts a complete user session and rejects invalid capabilities', () => {
    expect(parseMe({
      tier: 'user',
      userId: 'user-1',
      email: 'user@example.test',
      organizations: [{ id: 'org-1', name: 'One', slug: 'one' }],
      capabilities: ['sites:read'],
    })).toMatchObject({ tier: 'user', activeOrg: undefined });

    expect(() => parseMe({
      tier: 'user',
      userId: 'user-1',
      email: 'user@example.test',
      organizations: [],
      capabilities: ['not:a-capability'],
    })).toThrow('invalid session response');
  });
});

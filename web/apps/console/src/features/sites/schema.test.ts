import { describe, expect, it } from 'vitest';
import { parseSiteResponse } from './schema';

describe('site response validation', () => {
  const site = {
    id: 'site-1', nodeId: 'node-1', name: 'Main', timeZone: 'UTC', status: 'Open',
    path: 'root.site', version: '2', addressLine1: null, city: null, postalCode: null,
    countryCode: null, latitude: '40.1', longitude: null,
  };

  it('normalizes generated numeric unions and rejects nested attributes', () => {
    expect(parseSiteResponse({ ...site, attributes: { seats: 10 } })).toMatchObject({
      version: 2,
      latitude: 40.1,
    });
    expect(() => parseSiteResponse({ ...site, attributes: { nested: {} } })).toThrow(
      'invalid site attributes',
    );
  });
});

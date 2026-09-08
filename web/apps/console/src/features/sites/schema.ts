import type { components } from '@locintel/api';

type RawSite = components['schemas']['SiteResponse'];

export type SiteResponse = Omit<RawSite, 'attributes' | 'latitude' | 'longitude' | 'version'> & {
  attributes: Record<string, string | number | boolean>;
  latitude: number | null;
  longitude: number | null;
  version: number;
};

/** Runtime boundary for the server-owned dynamic attribute bag. */
export function parseSiteResponse(value: RawSite): SiteResponse {
  const attributes = value.attributes;
  if (
    typeof attributes !== 'object' ||
    attributes === null ||
    Array.isArray(attributes) ||
    !Object.values(attributes).every((item) =>
      ['string', 'number', 'boolean'].includes(typeof item),
    )
  ) throw new Error('invalid site attributes');

  const { version, latitude, longitude } = value;
  if (![version, latitude, longitude].every((item) => item === null || Number.isFinite(item)))
    throw new Error('invalid site coordinates or version');
  return { ...value, attributes: attributes as SiteResponse['attributes'], latitude, longitude, version };
}

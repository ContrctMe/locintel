import { CAPABILITIES, type components } from '@locintel/api';

// Guests and platform operation are not assignable through an organization role editor.
export const GRANTABLE = CAPABILITIES.filter((c) => c !== 'public:read' && c !== 'platform:operate');
export const grantKey = (grant: components['schemas']['GrantSpec']) => `${grant.domain}:${grant.action}`;

export function parseGrant(value: string): components['schemas']['GrantSpec'] {
  const parts = value.split(':');
  if (parts.length !== 2 || !parts[0] || !parts[1]) throw new Error('invalid capability');
  return { domain: parts[0], action: parts[1] };
}

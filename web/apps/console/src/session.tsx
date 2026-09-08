import { api, CAPABILITIES, type Capability } from '@locintel/api';
import { useQuery } from '@tanstack/react-query';

export type Me =
  | { tier: 'guest'; org?: string }
  | { tier: 'contact'; contactId: string; org: string }
  | {
      tier: 'user';
      userId: string;
      email: string;
      name?: string;
      activeOrg?: string;
      organizations: { id: string; name: string; slug: string }[];
      capabilities: Capability[];
      impersonationExpiresAt?: string | null;
    };

export function parseMe(value: unknown): Me {
  if (typeof value !== 'object' || value === null) throw new Error('invalid session response');
  const row = value as Record<string, unknown>;
  const optionalString = (key: string) =>
    typeof row[key] === 'string' ? row[key] as string : undefined;
  if (row.tier === 'guest') return { tier: 'guest', org: optionalString('org') };
  if (row.tier === 'contact' && typeof row.contactId === 'string' && typeof row.org === 'string')
    return { tier: 'contact', contactId: row.contactId, org: row.org };
  if (
    row.tier === 'user' &&
    typeof row.userId === 'string' &&
    typeof row.email === 'string' &&
    Array.isArray(row.organizations) &&
    row.organizations.every(
      (org) => typeof org === 'object' && org !== null &&
        ['id', 'name', 'slug'].every((key) => typeof (org as Record<string, unknown>)[key] === 'string'),
    ) &&
    Array.isArray(row.capabilities) &&
    row.capabilities.every(
      (capability) => typeof capability === 'string' && CAPABILITIES.includes(capability as Capability),
    )
  ) {
    return {
      tier: 'user',
      userId: row.userId,
      email: row.email,
      name: optionalString('name'),
      activeOrg: optionalString('activeOrg'),
      organizations: row.organizations.map((org) => {
        const item = org as Record<string, string>;
        return { id: item.id!, name: item.name!, slug: item.slug! };
      }),
      capabilities: row.capabilities as Capability[],
      impersonationExpiresAt: optionalString('impersonationExpiresAt'),
    };
  }
  throw new Error('invalid session response');
}

export function useMe() {
  return useQuery({ queryKey: ['me'], queryFn: async ({ signal }) => parseMe(await api.get('/me', { signal })) });
}

/** Hide/disable instead of surfacing 403s: the /me bootstrap drives the UI. */
export function can(me: Me | undefined, capability: Capability): boolean {
  return me?.tier === 'user' && me.capabilities.includes(capability);
}

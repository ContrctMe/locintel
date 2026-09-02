import { api, type Capability } from '@locintel/api';
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

export function useMe() {
  return useQuery({ queryKey: ['me'], queryFn: () => api.get<Me>('/me') });
}

/** Hide/disable instead of surfacing 403s: the /me bootstrap drives the UI. */
export function can(me: Me | undefined, capability: Capability): boolean {
  return me?.tier === 'user' && me.capabilities.includes(capability);
}

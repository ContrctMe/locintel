import { type Capability } from '@locintel/api';
import { Code2, FolderOpen, Home, Layers, ListChecks, MapPin, Network, ScrollText, Server, Settings, Shield, Upload, Users } from 'lucide-react';
import { type ComponentType } from 'react';
import { can, type Me } from '../../session';

export type NavItem = {
  to: string;
  label: string;
  capability: Capability | null;
  icon: ComponentType<{ className?: string }>;
};

// grouped by rhythm of use: daily operations, then org administration,
// then the operator wall (UX review P2)
export const NAV_GROUPS: { label: string | null; items: NavItem[] }[] = [
  {
    label: null,
    items: [
      { to: '/', label: 'Dashboard', capability: null, icon: Home },
      { to: '/sites', label: 'Sites', capability: 'sites:read', icon: MapPin },
      { to: '/overlays', label: 'Overlays', capability: 'overlays:read', icon: Layers },
      { to: '/hierarchy', label: 'Hierarchy', capability: 'hierarchy:manage', icon: Network },
      { to: '/checklists', label: 'Checklists', capability: 'checklists:complete', icon: ListChecks },
      { to: '/files', label: 'Files', capability: 'files:read', icon: FolderOpen },
      { to: '/reports', label: 'Reports', capability: 'files:read', icon: ScrollText },
      { to: '/ingest', label: 'Ingest', capability: 'ingest:manage', icon: Upload },
    ],
  },
  {
    label: 'Administer',
    items: [
      { to: '/members', label: 'Members', capability: 'roles:manage', icon: Users },
      { to: '/roles', label: 'Roles', capability: 'roles:manage', icon: Shield },
      { to: '/developers', label: 'Developers', capability: 'org:manage', icon: Code2 },
      { to: '/settings', label: 'Settings', capability: 'org:manage', icon: Settings },
      { to: '/audit', label: 'Audit', capability: 'audit:read', icon: ScrollText },
    ],
  },
  {
    label: 'Platform',
    items: [{ to: '/operator', label: 'Operator', capability: 'platform:operate', icon: Server }],
  },
];

/** The four areas that earn a slot on the phone's tab bar; the rest live under More. */
export const TAB_BAR = ['/', '/sites', '/checklists', '/files'];

export type User = Extract<Me, { tier: 'user' }>;

export const visibleGroups = (me: User) =>
  NAV_GROUPS.map((g) => ({
    ...g,
    items: g.items.filter((n) => n.capability === null || can(me, n.capability)),
  })).filter((g) => g.items.length > 0);

export const isActive = (path: string, to: string) =>
  to === '/' ? path === '/' : path === to || path.startsWith(to + '/');

/**
 * The pages that read the Scope node (flow review follow-up, 2026-09): the
 * site library and map, the checklists' site choice, and the overlays
 * anchored under a node. Everywhere else the org's administration is the
 * whole org, so the Scope panel stays away instead of promising a filter
 * it does not apply.
 */
export const SCOPED_PAGES = ['/sites', '/checklists', '/overlays'];

export const isScopedPage = (path: string) => SCOPED_PAGES.some((to) => isActive(path, to));

export const pageTitle = (path: string) => {
  if (path.startsWith('/account')) return 'Account';
  const hit = NAV_GROUPS.flatMap((g) => g.items).find((n) => isActive(path, n.to));
  return hit?.label ?? 'LocIntel';
};

import type { Capability } from '@locintel/api';

/**
 * Capability keys in the admin's words (flow review, 2026-09): the generated
 * key stays the value the API speaks; this is only how the console names it.
 * Typed against the generated union, so a new capability fails typecheck
 * here until it has a label and a group.
 */
export const CAPABILITY_LABELS: Record<Capability, { label: string; group: string }> = {
  'public:read': { label: 'See public site info', group: 'Sites' },
  'sites:read': { label: 'See sites', group: 'Sites' },
  'sites:manage': { label: 'Manage sites', group: 'Sites' },
  'hierarchy:manage': { label: 'Manage the hierarchy', group: 'Sites' },
  'ingest:manage': { label: 'Bulk-load sites', group: 'Sites' },
  'reports:generate': { label: 'Generate reports', group: 'Reports' },
  'overlays:read': { label: 'See map overlays', group: 'Map' },
  'overlays:manage': { label: 'Manage map overlays', group: 'Map' },
  'checklists:complete': { label: 'Complete checklists', group: 'Checklists' },
  'checklists:manage': { label: 'Manage checklist templates', group: 'Checklists' },
  'files:read': { label: 'See files', group: 'Files' },
  'files:manage': { label: 'Manage files', group: 'Files' },
  'audit:read': { label: 'Read the audit log', group: 'Audit' },
  'audit:manage': { label: 'Manage audit settings', group: 'Audit' },
  'roles:manage': { label: 'Manage roles and members', group: 'Organization' },
  'entitlements:manage': { label: 'Manage the plan', group: 'Organization' },
  'org:manage': { label: 'Manage organization settings', group: 'Organization' },
  'platform:operate': { label: 'Operate the platform', group: 'Platform' },
};

/** The order groups appear in the editor: the everyday ones first. */
export const CAPABILITY_GROUPS = ['Sites', 'Checklists', 'Files', 'Reports', 'Map', 'Audit', 'Organization'] as const;

export const capabilityLabel = (key: string): string =>
  key === '*:*' ? 'Everything' : (CAPABILITY_LABELS as Record<string, { label: string }>)[key]?.label ?? key;

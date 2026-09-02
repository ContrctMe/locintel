/** Human formatting (UX review P1): raw internals stop leaking to humans. */

export const fmtDate = (value: string | Date): string =>
  new Date(value).toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  });

export const fmtDateTime = (value: string | Date): string =>
  new Date(value).toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  });

/** Weekday + date, rendered in a SPECIFIC zone (site pages show site time). */
export const fmtDayInZone = (value: string | Date, timeZone: string): string =>
  new Date(value).toLocaleDateString(undefined, {
    weekday: 'short',
    month: 'short',
    day: 'numeric',
    timeZone,
  });

export const fmtTimeInZone = (value: string | Date, timeZone: string): string =>
  new Date(value).toLocaleTimeString(undefined, {
    hour: 'numeric',
    minute: '2-digit',
    timeZone,
  });

/** Friendly names for entitlement codes; the raw code stays available as detail. */
export const ENTITLEMENT_LABELS: Record<string, string> = {
  'api.requests_per_minute': 'API requests / minute',
  'audit.read_logging': 'Read-access logging',
  'audit.retention_days': 'Audit retention (days)',
  'contact_links.enabled': 'Contact links',
  'contact_links.monthly': 'Contact links / month',
  'hierarchy.depth': 'Hierarchy depth',
  'marketplace.enabled': 'Marketplace',
  'network.enabled': 'Intelligence sharing',
  'sites.max': 'Site limit',
  'sso.enabled': 'Single sign-on',
};

export const entitlementLabel = (code: string): string => ENTITLEMENT_LABELS[code] ?? code;

/** "billing.subscription_changed" -> "Billing subscription changed" - mechanical, so fork events humanize for free. */
export const eventLabel = (code: string): string => {
  const words = code.replaceAll('.', ' ').replaceAll('_', ' ');
  return words.charAt(0).toUpperCase() + words.slice(1);
};

export function parseEntitlementValue(value: string): string {
  const normalized = value.trim();
  if (!normalized) throw new Error('entitlement value is required');
  return normalized;
}

export const CATEGORIES = ['GuardService', 'MobilePatrol', 'AlarmResponse', 'Investigation', 'CctvInstall',
  'AccessControl', 'BoardUp', 'Restoration', 'LegalSupport', 'EquipmentSupply', 'KeyHolding', 'Other'] as const;
export const URGENCIES = ['Emergency', 'Scheduled', 'Standing'] as const;
export const REQUEST_STATUSES = ['Draft', 'Submitted', 'Accepted', 'Declined', 'InProgress', 'Completed', 'Verified', 'Disputed', 'Cancelled'] as const;
export const categoryLabel = (c: string | null) => (c ?? 'Unspecified').replace(/([a-z])([A-Z])/g, '$1 $2').replace('Cctv', 'CCTV');

export function RequestStatusBadge({ status }: { status: string }) {
  const tone =
    status === 'Verified' || status === 'Completed' ? 'bg-emerald-500/15 text-emerald-700 dark:text-emerald-300'
      : status === 'Declined' || status === 'Cancelled' || status === 'Disputed' ? 'bg-destructive/15 text-destructive'
        : status === 'Draft' ? 'bg-muted text-muted-foreground'
          : 'bg-primary/10 text-primary';
  return <span className={`rounded px-1.5 py-0.5 text-xs font-medium ${tone}`}>{status.replace(/([a-z])([A-Z])/g, '$1 $2')}</span>;
}

import { Badge } from '@locintel/ui';

/**
 * Lifecycle status as a ReUI Badge: the light variants carry the status
 * colour as a tint with readable ink, one per family - live, pending,
 * paused, gone - so every module's statuses read the same way.
 */
export function StatusBadge({ status }: { status: string }) {
  const variant =
    status === 'Open' || status === 'Clean' || status === 'Committed' || status === 'Active'
      ? 'success-light'
      : status === 'ComingSoon' || status === 'Staged' || status === 'Pending' || status === 'Scanning'
        ? 'info-light'
        : status === 'TemporarilyClosed' || status === 'Quarantined' || status === 'Suspended' || status === 'Deleted'
          ? 'warning-light'
          : status === 'Closed' || status === 'Erased' || status === 'Discarded' || status === 'Failed'
            ? 'destructive-light'
            : 'secondary';
  return (
    <Badge variant={variant}>
      {status.replace(/([a-z])([A-Z])/g, '$1 $2')}
    </Badge>
  );
}

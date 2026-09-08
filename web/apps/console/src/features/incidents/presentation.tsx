export const CATEGORIES = [
  'Theft', 'OrganizedRetailCrime', 'InternalTheft', 'Fraud', 'Robbery', 'Burglary',
  'Assault', 'Threat', 'Vandalism', 'Trespass', 'Disturbance', 'Safety', 'Other',
] as const;
export const SEVERITIES = ['Low', 'Medium', 'High', 'Critical'] as const;

export const categoryLabel = (c: string) =>
  c === 'OrganizedRetailCrime' ? 'Organized retail crime'
    : c === 'InternalTheft' ? 'Internal theft'
      : c.replace(/([a-z])([A-Z])/g, '$1 $2');

export function SeverityBadge({ severity }: { severity: string }) {
  const tone =
    severity === 'Critical' ? 'bg-destructive text-destructive-foreground'
      : severity === 'High' ? 'bg-amber-500/20 text-amber-700 dark:text-amber-300'
        : severity === 'Medium' ? 'bg-primary/10 text-primary'
          : 'bg-muted text-muted-foreground';
  return <span className={`rounded px-1.5 py-0.5 text-xs font-medium ${tone}`}>{severity}</span>;
}

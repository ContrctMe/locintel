import { Checkbox, cn, type RowSelectionState } from '@locintel/ui';
import { Link } from '@tanstack/react-router';
import { StatusBadge } from '../../../shell';
import { useSites } from '../hooks';

export type SiteRow = ReturnType<typeof useSites>['data'] extends infer D
  ? D extends { pages: { items: (infer R)[] }[] }
    ? R
    : never
  : never;

export /**
 * The sites as rows a thumb can work: name, city and status, with the
 * shared selection. The map's side list (numbered to match the markers) and
 * the phone's whole Sites page are this one list.
 */
function SiteList({
  sites,
  rowSelection,
  onToggle,
  pending,
  emptyMessage,
  numbered = false,
  className,
  'aria-label': ariaLabel = 'Sites',
}: {
  sites: SiteRow[];
  rowSelection: RowSelectionState;
  onToggle: (id: string) => void;
  pending: boolean;
  emptyMessage: string;
  numbered?: boolean;
  className?: string;
  'aria-label'?: string;
}) {
  return (
    <ul aria-label={ariaLabel} className={className}>
      {sites.map((s, i) => {
        const selected = !!rowSelection[s.id];
        return (
          <li
            key={s.id}
            className={cn('flex items-center gap-2.5 border-b px-3 py-2', selected && 'bg-accent/50')}
          >
            <Checkbox
              checked={selected}
              onCheckedChange={() => onToggle(s.id)}
              aria-label={`Select ${s.name}`}
            />
            {numbered && (
              <span
                className={cn(
                  'flex size-5 shrink-0 items-center justify-center rounded-full border text-[11px] font-semibold tabular-nums',
                  selected
                    ? 'border-primary bg-primary text-primary-foreground'
                    : 'bg-muted text-muted-foreground',
                )}
                aria-hidden
              >
                {i + 1}
              </span>
            )}
            <span className="min-w-0 flex-1 leading-tight">
              <Link
                to="/sites/$siteId"
                params={{ siteId: s.id }}
                className="block truncate text-sm font-medium hover:underline"
              >
                {s.name}
              </Link>
              {s.city && <span className="block truncate text-xs text-muted-foreground">{s.city}</span>}
            </span>
            <StatusBadge status={s.status} />
          </li>
        );
      })}
      {!pending && sites.length === 0 && (
        <li className="px-3 py-6 text-center text-sm text-muted-foreground">{emptyMessage}</li>
      )}
    </ul>
  );
}

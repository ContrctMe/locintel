import { ApiError } from '@locintel/api';
import { cn } from '@locintel/ui';
import { Building2, Check, ChevronRight, MapPin, Network } from 'lucide-react';
import { useMemo, useRef, useState, type ComponentType } from 'react';
import { useVirtualizer } from '@tanstack/react-virtual';
import { can } from '../../session';
import { useScope } from '../scope';
import { useHierarchy } from '../../features/hierarchy/hooks';
import { type User } from './nav';

type ScopeRow = {
  id: string | null;
  name: string;
  depth: number;
  icon: ComponentType<{ className?: string }>;
  hasChildren: boolean;
  expanded: boolean;
};

/**
 * The scope picker: the org's tree with the top level open and everything
 * below it closed until asked for, rendered through a virtualizer so the
 * rows on screen, not the nodes in the org, decide the cost (ADR 51's
 * console half). The chosen node's ancestors stay open so the choice is
 * always visible.
 */
export function ScopeTree({
  me,
  rowClass,
  rowHeight = 32,
  className,
  onPick,
}: {
  me: User;
  rowClass?: string;
  rowHeight?: number;
  className?: string;
  onPick?: () => void;
}) {
  const scope = useScope();
  const { data: hierarchy, isError, error } = useHierarchy();
  // nodes whose default (open at the top, closed below) has been flipped
  const [toggled, setToggled] = useState<Set<string>>(() => new Set());
  const rootIdRef = useRef<string | null>(null);
  const nodes = hierarchy?.nodes ?? [];
  const rows = useMemo<ScopeRow[]>(() => {
    const children = new Map<string | null, typeof nodes>();
    const byId = new Map(nodes.map((n) => [n.id, n]));
    for (const n of nodes) children.set(n.parentId ?? null, [...(children.get(n.parentId ?? null) ?? []), n]);
    const ancestors = new Set<string>();
    for (let cursor = scope.nodeId ? byId.get(scope.nodeId) : undefined; cursor?.parentId; cursor = byId.get(cursor.parentId))
      ancestors.add(cursor.parentId);
    const isOpen = (n: (typeof nodes)[number]) =>
      ancestors.has(n.id) || (n.depth === 0) !== toggled.has(n.id);
    const rootId = nodes.find((n) => n.depth === 0)?.id ?? null;
    // the root node IS "all sites": one row, selected as the whole org
    const out: ScopeRow[] = [];
    const visit = (parent: string | null) => {
      for (const n of children.get(parent) ?? []) {
        const depth = n.depth;
        const kids = children.get(n.id)?.length ?? 0;
        const expanded = kids > 0 && isOpen(n);
        out.push({
          id: depth === 0 ? null : n.id,
          name: n.name,
          depth,
          icon: depth === 0 ? Building2 : depth === 1 ? Network : MapPin,
          hasChildren: kids > 0,
          expanded,
        });
        if (expanded) visit(n.id);
      }
    };
    visit(null);
    rootIdRef.current = rootId;
    if (out.length === 0) out.push({ id: null, name: 'All sites', depth: 0, icon: Building2, hasChildren: false, expanded: false });
    return out;
  }, [nodes, toggled, scope.nodeId]);
  const scroller = useRef<HTMLDivElement>(null);
  const virtualizer = useVirtualizer({
    count: rows.length,
    getScrollElement: () => scroller.current,
    estimateSize: () => rowHeight,
    overscan: 12,
  });
  if (!can(me, 'sites:read')) return null;
  const notProvisioned = error instanceof ApiError && error.status === 404;
  return (
    <div className="flex min-h-0 flex-col">
      <div className="px-2 pb-1 pt-2 text-[11px] font-medium uppercase tracking-wide text-muted-foreground">
        Scope
      </div>
      <div ref={scroller} className={cn('min-h-0 flex-1 overflow-auto', className)}>
        <div
          role="group"
          aria-label="Scope"
          className="relative w-full"
          style={{ height: virtualizer.getTotalSize() }}
        >
          {virtualizer.getVirtualItems().map((item) => {
            const row = rows[item.index]!;
            const selected = scope.nodeId === row.id;
            const Icon = row.icon;
            return (
              <div
                key={row.id ?? 'all'}
                className="absolute left-0 top-0 flex w-full items-center"
                style={{ height: item.size, transform: `translateY(${item.start}px)` }}
              >
                {row.hasChildren ? (
                  <button
                    type="button"
                    aria-label={`${row.expanded ? 'Collapse' : 'Expand'} ${row.name}`}
                    aria-expanded={row.expanded}
                    onClick={() =>
                      setToggled((prev) => {
                        const key = row.id ?? rootIdRef.current ?? '';
                        const next = new Set(prev);
                        if (next.has(key)) next.delete(key);
                        else next.add(key);
                        return next;
                      })
                    }
                    style={{ marginLeft: `${row.depth * 14}px` }}
                    className="flex size-5 shrink-0 items-center justify-center rounded text-muted-foreground hover:bg-sidebar-accent"
                  >
                    <ChevronRight
                      className={cn('size-3.5 transition-transform', row.expanded && 'rotate-90')}
                      aria-hidden
                    />
                  </button>
                ) : (
                  <span className="size-5 shrink-0" style={{ marginLeft: `${row.depth * 14}px` }} />
                )}
                <button
                  type="button"
                  aria-pressed={selected}
                  onClick={() => {
                    scope.setNodeId(row.id);
                    onPick?.();
                  }}
                  className={cn(
                    'flex h-[30px] min-w-0 flex-1 items-center gap-2 rounded-md px-1.5 text-left text-sm text-foreground/80 hover:bg-sidebar-accent',
                    selected && 'bg-sidebar-accent font-medium text-foreground shadow-xs',
                    rowClass,
                  )}
                >
                  <Icon className="size-[14px] shrink-0 text-muted-foreground" />
                  <span className="min-w-0 flex-1 truncate">{row.name}</span>
                  {selected && <Check className="size-4 text-primary" aria-hidden />}
                </button>
              </div>
            );
          })}
        </div>
      </div>
      {isError && !notProvisioned && (
        <p className="px-2 pt-2 text-xs text-muted-foreground">Scope could not be loaded.</p>
      )}
      {(!isError || notProvisioned) && nodes.length === 0 && (
        <p className="px-2 pt-2 text-xs text-muted-foreground">
          No hierarchy yet. Scope narrows the sites, checklists and overlays you see; it filters, it never blocks.
        </p>
      )}
    </div>
  );
}

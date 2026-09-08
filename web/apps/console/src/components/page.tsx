import {
  Empty,
  EmptyContent,
  EmptyDescription,
  EmptyHeader,
  EmptyMedia,
  EmptyTitle,
  Skeleton,
  DataGrid,
  DataGridContainer,
  DataGridScrollArea,
  DataGridTable,
  dataGridFeatures,
  Frame,
  FrameDescription,
  FrameFooter,
  FrameHeader,
  FramePanel,
  FrameTitle,
  IconTile,
  cn,
  useTable,
  type ColumnDef,
  type DataGridFeatures,
  DataGridTableVirtual,
} from '@locintel/ui';
import { Link } from '@tanstack/react-router';
import type { ComponentType, ReactNode } from 'react';

/**
 * The page scaffold every console page shares (direction B): a title row
 * with the page's actions beside it, then Frame panels for its content.
 * Sites set the pattern; the rest of the console follows it here so the
 * surface, spacing and type are one decision, not one per page.
 */
export function PageHeader({
  title,
  count,
  description,
  actions,
  headingLevel = 1,
}: {
  title: ReactNode;
  headingLevel?: 1 | 2;
  /** A total beside the title, muted and tabular (the Sites count). */
  count?: number | string | null;
  description?: ReactNode;
  actions?: ReactNode;
}) {
  const Heading = headingLevel === 2 ? 'h2' : 'h1';
  return (
    <div className="flex flex-wrap items-end justify-between gap-4">
      <div className="min-w-0">
        <Heading className="text-xl font-semibold tracking-tight">
          {title}
          {count !== undefined && count !== null && (
            <span className="ml-2 text-base font-medium tabular-nums text-muted-foreground">{count}</span>
          )}
        </Heading>
        {description && <p className="text-sm text-muted-foreground">{description}</p>}
      </div>
      {actions && <div className="flex flex-wrap items-center gap-3">{actions}</div>}
    </div>
  );
}

/**
 * One content panel on the Frame surface. A title (and optional description
 * and actions) sits in the panel header; the body is padded for text and
 * forms, or edge-to-edge (`flush`) for tables and lists that draw their own
 * rows; a footer takes counts and paging.
 */
export function Panel({
  title,
  description,
  actions,
  footer,
  flush = false,
  className,
  bodyClassName,
  children,
}: {
  title?: ReactNode;
  description?: ReactNode;
  actions?: ReactNode;
  footer?: ReactNode;
  flush?: boolean;
  className?: string;
  bodyClassName?: string;
  children: ReactNode;
}) {
  return (
    <Frame className={className}>
      <FramePanel>
        {(title || actions) && (
          <FrameHeader className="flex-row flex-wrap items-center gap-2">
            <div className="min-w-0 flex-1">
              {title && <FrameTitle>{title}</FrameTitle>}
              {description && <FrameDescription>{description}</FrameDescription>}
            </div>
            {actions && <div className="flex items-center gap-2">{actions}</div>}
          </FrameHeader>
        )}
        <div className={cn(!flush && 'px-4 py-4', flush && (title || actions) && 'border-t', bodyClassName)}>
          {children}
        </div>
        {footer && <FrameFooter className="flex-row flex-wrap items-center gap-3">{footer}</FrameFooter>}
      </FramePanel>
    </Frame>
  );
}

/** A headline number that links to where it comes from (the dashboard): the stats block's tile. */
export function Stat({
  value,
  label,
  to,
  icon: Icon,
  hint,
}: {
  value: ReactNode;
  label: ReactNode;
  to: string;
  icon?: ComponentType<{ className?: string }>;
  /** A second line under the number: a trend, a breakdown, a date. */
  hint?: ReactNode;
}) {
  return (
    <Frame>
      <FramePanel>
        <Link to={to} className="block px-4 py-4">
          <div className="flex items-center gap-2.5">
            {Icon && (
              <IconTile variant="soft" size="sm">
                <Icon />
              </IconTile>
            )}
            <div className="text-sm text-muted-foreground">{label}</div>
          </div>
          <div className="mt-3 text-3xl font-semibold tabular-nums">{value}</div>
          {hint && <div className="mt-1 text-xs text-muted-foreground">{hint}</div>}
        </Link>
      </FramePanel>
    </Frame>
  );
}

/** The one-line status/alert rows pages show while loading or after a failure. */
export function Notice({ kind = 'status', children }: { kind?: 'status' | 'alert'; children: ReactNode }) {
  return (
    <p role={kind} className={cn('text-sm', kind === 'alert' ? 'text-destructive' : 'text-muted-foreground')}>
      {children}
    </p>
  );
}

/**
 * A Data Grid inside a Panel for the console's plain lists (members, files,
 * audit): server-driven rows, the Sites grid's layout, a footer for paging.
 * Columns come from the caller (memoized); everything else is decided here.
 */
export function Grid<TRow extends object>({
  columns,
  rows,
  getRowId,
  isLoading = false,
  loadingMessage,
  emptyMessage,
  onRowClick,
  title,
  description,
  actions,
  footer,
  rowClassName,
  children,
  onFetchMore,
  hasMore,
  isFetchingMore,
}: {
  columns: ColumnDef<DataGridFeatures, TRow>[];
  rows: TRow[];
  getRowId: (row: TRow) => string;
  isLoading?: boolean;
  loadingMessage?: ReactNode;
  emptyMessage?: ReactNode;
  onRowClick?: (row: TRow) => void;
  title?: ReactNode;
  description?: ReactNode;
  actions?: ReactNode;
  footer?: ReactNode;
  /** A class every body row wears (the hover-revealed actions of the members block key on it). */
  rowClassName?: string;
  /** Rendered under the table, inside the panel (a detail pane, a note). */
  children?: ReactNode;
  /** Keyset paging as the grid's own infinite scroll: the next page loads as the end comes into view. */
  onFetchMore?: () => void;
  hasMore?: boolean;
  isFetchingMore?: boolean;
}) {
  const table = useTable({
    features: dataGridFeatures,
    columns,
    data: rows,
    getRowId,
    manualPagination: true,
    manualSorting: true,
    manualFiltering: true,
    pageCount: -1,
  });
  return (
    <Frame>
      <FramePanel>
        <DataGrid
          table={table}
          recordCount={rows.length}
          isLoading={isLoading}
          loadingMode="skeleton"
          loadingMessage={loadingMessage}
          fetchingMoreMessage="Loading more…"
          emptyMessage={emptyMessage}
          onRowClick={onRowClick}
          tableLayout={{ headerBackground: false, headerBorder: true, rowBorder: true }}
          tableClassNames={rowClassName ? { bodyRow: rowClassName } : undefined}
        >
          {(title || actions) && (
            <FrameHeader className="flex-row flex-wrap items-center gap-2">
              <div className="min-w-0 flex-1">
                {title && <FrameTitle>{title}</FrameTitle>}
                {description && <FrameDescription>{description}</FrameDescription>}
              </div>
              {actions && <div className="flex items-center gap-2">{actions}</div>}
            </FrameHeader>
          )}
          <DataGridContainer>
            {onFetchMore ? (
              // its own scroll box, sized to the rows it has (a definite height
              // is what makes "near the end" mean something), capped for long lists
              <DataGridTableVirtual
                height={Math.min(Math.max(rows.length, 3) * 48 + 48, 640)}
                onFetchMore={onFetchMore}
                hasMore={hasMore}
                isFetchingMore={isFetchingMore}
                fetchMoreOffset={5}
              />
            ) : (
              <DataGridScrollArea>
                <DataGridTable />
              </DataGridScrollArea>
            )}
          </DataGridContainer>
          {children}
          {footer && <FrameFooter className="flex-row flex-wrap items-center gap-3">{footer}</FrameFooter>}
        </DataGrid>
      </FramePanel>
    </Frame>
  );
}

/** The shadcn Empty for a panel with nothing in it yet: an icon, a line, and the way in. */
export function EmptyState({
  icon: Icon,
  title,
  description,
  action,
  className,
}: {
  icon: ComponentType<{ className?: string }>;
  title: ReactNode;
  description?: ReactNode;
  action?: ReactNode;
  className?: string;
}) {
  return (
    <Empty className={cn('border-0 bg-transparent py-10', className)}>
      <EmptyHeader>
        <EmptyMedia variant="icon">
          <Icon aria-hidden />
        </EmptyMedia>
        <EmptyTitle>{title}</EmptyTitle>
        {description && <EmptyDescription>{description}</EmptyDescription>}
      </EmptyHeader>
      {action && <EmptyContent>{action}</EmptyContent>}
    </Empty>
  );
}

/**
 * What a page shows while its first data is on the way: skeleton rows in
 * the shape of a panel, plus the one line of text the screen reader (and
 * the browser suite) reads. Text stays; the bars are the picture of it.
 */
export function Loading({ text, rows = 3 }: { text: string; rows?: number }) {
  return (
    <div className="space-y-3" aria-busy>
      <div className="space-y-2">
        {Array.from({ length: rows }, (_, i) => (
          <Skeleton key={i} className="h-9 w-full" style={{ opacity: 1 - i * 0.2 }} />
        ))}
      </div>
      <p role="status" className="text-sm text-muted-foreground">
        {text}
      </p>
    </div>
  );
}

/** The route-level pending state: a page header and a panel, in skeleton. */
export function PageSkeleton() {
  return (
    <div className="space-y-6" aria-busy>
      <div className="space-y-2">
        <Skeleton className="h-6 w-40" />
        <Skeleton className="h-4 w-72" />
      </div>
      <Frame>
        <FramePanel>
          <div className="space-y-3 px-4 py-4">
            <Skeleton className="h-9 w-full" />
            <Skeleton className="h-9 w-full opacity-80" />
            <Skeleton className="h-9 w-full opacity-60" />
            <Skeleton className="h-9 w-full opacity-40" />
          </div>
        </FramePanel>
      </Frame>
      <p role="status" className="sr-only">
        Loading…
      </p>
    </div>
  );
}

import { toast, Button, DataGrid, DataGridColumnVisibility, DataGridContainer, dataGridFeatures, DataGridTableVirtual, DataGridTableRowSelect, DataGridTableRowSelectAll, Frame, FrameFooter, FrameHeader, FramePanel, Select, SiteMap, ToggleGroup, ToggleGroupItem, useIsMobile, useTable, type ColumnDef, type DataGridFeatures, type RowSelectionState, type SiteMapPoint } from '@locintel/ui';
import { Link } from '@tanstack/react-router';
import { Columns3, MapPin, Table2 } from 'lucide-react';
import { useCallback, useMemo, useState } from 'react';
import { useScope } from '../../../app/scope';
import { useTheme } from '../../../app/theme';
import { bboxParam, snapToTileGrid, type Viewport } from '../../../lib/map';
import { useApiMutation } from '../../../lib/mutation';
import { usePreference } from '../../../lib/preference';
import { can, useMe } from '../../../session';
import { ReportGenerationDialog } from '../../reports';
import { StatusBadge } from '../../../shell';
import { sitesApi } from '../api';
import { useHierarchy, useSites } from '../hooks';
import { SiteFilters } from './site-filters';
import { LayersPopover } from './layers-popover';
import { NewSiteDialog } from './new-site-dialog';
import { SiteList, type SiteRow } from './site-list';
import { useMapLayers } from './use-map-layers';

type View = 'table' | 'map';

const toNumber = (v: number | null | undefined): number | null => v ?? null;

/**
 * The site library (direction B, steps 4-5): one scoped, searched list in two
 * views. Table: a ReUI Frame holding the Data Grid. Map: the same frame with
 * a MapLibre map and a list of what is INSIDE the viewport - the box is sent
 * to the server (ADR 49), snapped to the tile grid and debounced, and the
 * server applies scope first. Selection is keyed by site id and shared by
 * both views, so it survives paging, panning, and the toggle.
 */
export function SitesPage() {
  const { data: me } = useMe();
  const [filter, setFilter] = useState('');
  const [statusFilter, setStatusFilter] = useState('');
  const [view, setView] = usePreference<View>('sites.view', 'table');
  const [viewport, setViewport] = useState<Viewport | null>(null);
  const [fitKey, setFitKey] = useState(0);
  const scope = useScope();
  const theme = useTheme();
  // a phone reads a list, not a clipped table (flow review, 2026-09)
  const phone = useIsMobile();

  // the box only exists in the map view: the table is the whole scope
  const box = view === 'map' && viewport ? snapToTileGrid(viewport) : null;
  const mapLayers = useMapLayers({ active: view === 'map', under: scope.nodeId, theme, me });
  const { tiles, statuses, canSeeOverlays, overlays, rasters, basemap } = mapLayers;
  const sitesQuery = useSites(
    filter,
    scope.nodeId,
    box ? bboxParam(box) : undefined,
    box && viewport ? Math.max(0, Math.floor(viewport.zoom)) : undefined,
    statusFilter,
  );
  const sites = useMemo(
    () => sitesQuery.data?.pages.flatMap((p) => p.items) ?? [],
    [sitesQuery.data],
  );
  const firstPage = sitesQuery.data?.pages[0];
  const total = firstPage?.total;
  // a search stops counting past ten thousand hits and says so (ADR 51)
  const totalLabel =
    total === undefined ? '' : firstPage?.totalIsLowerBound ? `${total.toLocaleString()}+` : String(total);
  const withoutCoordinates = toNumber(firstPage?.withoutCoordinates);
  const { data: hierarchy } = useHierarchy();
  const [rowSelection, setRowSelection] = useState<RowSelectionState>({});
  const [reportSites, setReportSites] = useState<Record<string, string>>();
  const selectedCount = Object.values(rowSelection).filter(Boolean).length;
  const selectedIds = useMemo(
    () => Object.keys(rowSelection).filter((id) => rowSelection[id]),
    [rowSelection],
  );
  const toggleSelected = useCallback(
    (id: string) =>
      setRowSelection((current) => {
        const next = { ...current };
        if (next[id]) delete next[id];
        else next[id] = true;
        return next;
      }),
    [],
  );

  const nodeName = (id: string) => hierarchy?.nodes.find((n) => n.id === id)?.name ?? '—';

  // Update the status of every chosen site in one call.
  const bulkStatus = useApiMutation({
    mutationFn: (status: string) =>
      sitesApi.bulkStatus(selectedIds, status as Parameters<typeof sitesApi.bulkStatus>[1]),
    invalidate: [['sites']],
    onSuccess: (result) => {
      const { updated, skipped } = result;
      toast.success(
        skipped > 0
          ? `${updated} updated, ${skipped} outside your scope left alone`
          : `${updated} site${updated === 1 ? '' : 's'} updated`,
      );
      setRowSelection({});
    },
  });

  const columns = useMemo<ColumnDef<DataGridFeatures, SiteRow>[]>(
    () => [
      {
        id: 'select',
        header: () => <DataGridTableRowSelectAll />,
        cell: ({ row }) => <DataGridTableRowSelect row={row} />,
        size: 40,
        enableHiding: false,
        enableResizing: false,
        meta: { headerClassName: 'w-10', cellClassName: 'w-10' },
      },
      {
        id: 'name',
        accessorKey: 'name',
        header: 'Name',
        cell: ({ row }) => (
          <div className="min-w-0">
            <Link
              to="/sites/$siteId"
              params={{ siteId: row.original.id }}
              className="font-medium text-foreground underline-offset-4 hover:underline"
            >
              {row.original.name}
            </Link>
            {row.original.city && (
              <div className="truncate text-xs text-muted-foreground">{row.original.city}</div>
            )}
          </div>
        ),
        size: 260,
        enableHiding: false,
        meta: { headerTitle: 'Name', fillWidth: true },
      },
      {
        id: 'node',
        accessorKey: 'nodeId',
        header: 'Hierarchy node',
        cell: ({ row }) => (
          <span className="text-muted-foreground">{nodeName(row.original.nodeId)}</span>
        ),
        size: 180,
        meta: { headerTitle: 'Hierarchy node' },
      },
      {
        id: 'timeZone',
        accessorKey: 'timeZone',
        header: 'Time zone',
        cell: ({ row }) => (
          <span className="font-mono text-xs text-muted-foreground">{row.original.timeZone}</span>
        ),
        size: 180,
        meta: { headerTitle: 'Time zone' },
      },
      {
        id: 'status',
        accessorKey: 'status',
        header: 'Status',
        cell: ({ row }) => <StatusBadge status={row.original.status} />,
        size: 120,
        meta: { headerTitle: 'Status' },
      },
    ],
    // nodeName reads the hierarchy query; columns rebuild when it arrives
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [hierarchy],
  );

  const table = useTable({
    features: dataGridFeatures,
    columns,
    data: sites,
    getRowId: (row: SiteRow) => row.id,
    state: { rowSelection },
    onRowSelectionChange: setRowSelection,
    enableRowSelection: true,
    manualPagination: true,
    manualSorting: true,
    manualFiltering: true,
    pageCount: -1,
  });

  const points = useMemo<SiteMapPoint[]>(
    () =>
      sites.flatMap((s) => {
        const latitude = toNumber(s.latitude);
        const longitude = toNumber(s.longitude);
        if (latitude === null || longitude === null) return [];
        return [
          {
            id: s.id,
            name: s.name,
            latitude,
            longitude,
            subtitle: s.city ?? undefined,
            selected: !!rowSelection[s.id],
          },
        ];
      }),
    [sites, rowSelection],
  );

  if (sitesQuery.isError)
    return <p className="text-sm text-destructive">Could not load sites.</p>;

  const manage = can(me, 'sites:manage');
  const loaded = sites.length;
  const emptyMessage = filter
    ? 'No sites match the search.'
    : `No sites in scope. ${manage ? 'Create one with "New site".' : ''}`;

  // the filter bar (ReUI Filters): its chips become the list's `q` and `status`
  const search = (
    <div className="min-w-0 flex-1">
      <SiteFilters
        onChange={(values) => {
          setFilter(values.q);
          setStatusFilter(values.statuses.join(','));
        }}
      />
    </div>
  );

  const selection =
    selectedCount > 0 ? (
      <>
        <span className="font-medium tabular-nums">{selectedCount} selected</span>
        {can(me, 'reports:generate') && <Button onClick={() => setReportSites(
          Object.fromEntries(selectedIds.map(id => [id, sites.find(site => site.id === id)?.name ?? id])),
        )}>Generate report</Button>}
        {manage && (
          <Select
            aria-label="Set status for the selected sites"
            className="w-44"
            value=""
            disabled={bulkStatus.isPending}
            onChange={(e) => {
              if (e.target.value) bulkStatus.mutate(e.target.value);
            }}
          >
            <option value="">Set status…</option>
            {SITE_STATUSES.map((status) => (
              <option key={status.key} value={status.key}>
                {status.label}
              </option>
            ))}
          </Select>
        )}
        <Button variant="ghost" size="sm" onClick={() => setRowSelection({})}>
          Clear
        </Button>
      </>
    ) : null;

  const loadMore = sitesQuery.hasNextPage && (
    <Button
      variant="outline"
      size="sm"
      disabled={sitesQuery.isFetchingNextPage}
      onClick={() => void sitesQuery.fetchNextPage()}
    >
      Load more ({loaded} of {totalLabel})
    </Button>
  );

  return (
    <div className="space-y-6">
      <ReportGenerationDialog sites={reportSites} onClose={() => setReportSites(undefined)} />
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">
            Sites
            {view === 'table' && total !== undefined && (
              <span className="ml-2 text-base font-medium tabular-nums text-muted-foreground">
                {totalLabel}
              </span>
            )}
          </h1>
          <p className="mt-1 text-sm text-muted-foreground">
            Every physical location in your scope, on its own clock.
          </p>
        </div>
        <div className="flex items-center gap-3">
          {/* the toggle swaps the whole surface, so it sits with the page actions, not in the toolbar */}
          <ToggleGroup
            aria-label="View"
            variant="outline"
            size="sm"
            spacing={0}
            value={[view]}
            onValueChange={(next) => {
              const key = next[0];
              if (key === 'table' || key === 'map') setView(key);
            }}
          >
            <ToggleGroupItem value="table" aria-label="Table">
              <Table2 aria-hidden />
              Table
            </ToggleGroupItem>
            <ToggleGroupItem value="map" aria-label="Map">
              <MapPin aria-hidden />
              Map
            </ToggleGroupItem>
          </ToggleGroup>
          {manage && <NewSiteDialog />}
        </div>
      </div>

      <Frame>
        <FramePanel>
          {/* the header toolbar and the footer read the grid context (column
              visibility, selection), so the grid wraps the whole panel */}
          <DataGrid
            table={table}
            recordCount={total ?? loaded}
            isLoading={sitesQuery.isPending}
            loadingMode="skeleton"
            loadingMessage="Loading sites…"
            fetchingMoreMessage="Loading more sites…"
            emptyMessage={emptyMessage}
            tableLayout={{
              headerBackground: false,
              headerBorder: true,
              rowBorder: true,
              columnsVisibility: true,
            }}
          >
            <FrameHeader className="flex-row flex-wrap items-center gap-2">
              {search}
              {view === 'table' && (
                <DataGridColumnVisibility
                  table={table}
                  trigger={
                    <Button variant="outline" size="sm" className="ml-auto">
                      <Columns3 className="size-4" aria-hidden />
                      Columns
                    </Button>
                  }
                />
              )}
              {view === 'map' && canSeeOverlays && <LayersPopover layers={mapLayers} />}
            </FrameHeader>

            {view === 'table' && !phone ? (
              <DataGridContainer>
                <DataGridTableVirtual
                  height={Math.min(Math.max(sites.length, 5) * 53 + 48, Math.max(window.innerHeight - 320, 320))}
                  onFetchMore={() => void sitesQuery.fetchNextPage()}
                  hasMore={sitesQuery.hasNextPage}
                  isFetchingMore={sitesQuery.isFetchingNextPage}
                  fetchMoreOffset={5}
                />
              </DataGridContainer>
            ) : view === 'table' ? (
              <SiteList
                sites={sites}
                rowSelection={rowSelection}
                onToggle={toggleSelected}
                pending={sitesQuery.isPending}
                emptyMessage={emptyMessage}
                className="border-t"
              />
            ) : (
              <div className="grid md:grid-cols-[300px_minmax(0,1fr)]">
                {/* the accessible equivalent of the map: what is in view, with the same selection */}
                <div className="order-2 max-h-[360px] overflow-auto border-t md:order-1 md:max-h-[560px] md:border-r md:border-t-0">
                  <div className="flex items-center justify-between border-b px-3 py-2 text-xs text-muted-foreground">
                    <span>
                      <b className="font-medium text-foreground tabular-nums">{loaded} in view</b>
                      {total !== undefined && loaded < total && ` of ${totalLabel}`}
                    </span>
                    <span>List follows the map</span>
                  </div>
                  <SiteList
                    sites={sites}
                    rowSelection={rowSelection}
                    onToggle={toggleSelected}
                    pending={sitesQuery.isPending}
                    emptyMessage="No sites in this part of the map."
                    numbered
                    aria-label="Sites in view"
                  />
                  {withoutCoordinates !== null && withoutCoordinates > 0 && (
                    <p className="px-3 py-3 text-xs text-muted-foreground">
                      {withoutCoordinates} in scope without coordinates, so never on the map.
                    </p>
                  )}
                </div>
                <SiteMap
                  className="order-1 h-[360px] md:order-2 md:h-[560px]"
                  points={points}
                  tiles={tiles}
                  statuses={statuses}
                  selectedIds={selectedIds}
                  overlays={overlays}
                  basemap={basemap}
                  basemaps={rasters}
                  fitKey={fitKey}
                  onViewportChange={setViewport}
                  onPointClick={toggleSelected}
                />
              </div>
            )}

            <FrameFooter className="flex-wrap gap-3">
              <div className="flex items-center gap-2 text-sm" role="status">
                {sitesQuery.isPending ? (
                  <span className="text-muted-foreground">Loading sites…</span>
                ) : (
                  selection ?? (
                    <span className="text-muted-foreground">
                      {total === undefined
                        ? ''
                        : view === 'map'
                          ? `${loaded} in view`
                          : `Showing ${Math.min(loaded, total)} of ${totalLabel}`}
                    </span>
                  )
                )}
              </div>
              <div className="flex items-center gap-2">
                {view === 'map' && (
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={() => {
                      // drop the box (the whole scope loads), then fit the map to it
                      setViewport(null);
                      setFitKey((k) => k + 1);
                    }}
                  >
                    Fit to scope
                  </Button>
                )}
                {(view === 'map' || phone) && loadMore}
              </div>
            </FrameFooter>
          </DataGrid>
        </FramePanel>
      </Frame>
    </div>
  );
}

const SITE_STATUSES = [
  { key: 'ComingSoon', label: 'Coming soon' },
  { key: 'Open', label: 'Open' },
  { key: 'TemporarilyClosed', label: 'Temporarily closed' },
  { key: 'Closed', label: 'Closed' },
] as const;



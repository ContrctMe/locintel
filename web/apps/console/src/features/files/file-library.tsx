import { filesApi, type FileRow } from './api';
import { Link } from '@tanstack/react-router';
import { Button, ConfirmButton, type ColumnDef, type DataGridFeatures } from '@locintel/ui';
import { useInfiniteQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useMemo, useState } from 'react';
import { Grid, PageHeader } from '../../components/page';
import { FileDropzone } from '../../components/file-dropzone';
import { fmtDateTime } from '../../lib/format';
import { useApiMutation } from '../../lib/mutation';
import { uploadFile } from '../../lib/uploads';
import { can, useMe } from '../../session';
import { StatusBadge } from '../../shell';

export function FileLibrary({ siteId }: { siteId?: string }) {
  const { data: me } = useMe();
  const queryClient = useQueryClient();
  const manage = can(me, 'files:manage');
  const [phase, setPhase] = useState('');
  const [trash, setTrash] = useState(false);

  const filesQuery = useInfiniteQuery({
    queryKey: ['files', 'list', siteId, trash],
    queryFn: ({ pageParam, signal }) => filesApi.list({ offset: pageParam, trash, siteId }, signal),
    initialPageParam: 0,
    getNextPageParam: (last) =>
      last.nextOffset ?? undefined,
  });
  const files = filesQuery.data?.pages.flatMap((p) => p.items);
  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: ['files'] });
    if (siteId) void queryClient.invalidateQueries({ queryKey: ['reports', 'photos', siteId] });
  };

  const upload = useMutation({
    mutationFn: (file: File) =>
      uploadFile(file, file.type || 'application/octet-stream', setPhase, siteId),
    onSettled: () => {
      setPhase('');
      refresh();
    },
  });
  const hold = useApiMutation({
    mutationFn: (input: { id: string; hold: boolean }) =>
      filesApi.hold(input.id, input.hold),
    invalidate: [['files']],
    success: 'Legal hold updated',
  });
  const erase = useApiMutation({
    mutationFn: (id: string) => filesApi.trash(id),
    invalidate: [['files']],
    success: 'Moved to trash - restorable for 30 days',
    errorFallback: 'Delete failed',
  });
  const restore = useApiMutation({
    mutationFn: (id: string) => filesApi.restore(id),
    invalidate: [['files']],
    success: 'File restored',
  });

  const download = useApiMutation({ mutationFn: filesApi.download,
    onSuccess: ({ url }) => { window.location.assign(url); } });

  const columns = useMemo<ColumnDef<DataGridFeatures, FileRow>[]>(
    () => [
      {
        id: 'name',
        accessorKey: 'name',
        header: 'Name',
        cell: ({ row }) => (
          <div className="min-w-0">
            <div className="font-medium">{row.original.name}</div>
            <div className="text-xs text-muted-foreground">{row.original.contentType}</div>
            {row.original.origin === 'report' && row.original.originId &&
              <Link to="/reports/$runId" params={{ runId: row.original.originId }} className="text-sm underline">Report run</Link>}
          </div>
        ),
      },
      {
        id: 'status',
        accessorKey: 'status',
        header: 'Status',
        cell: ({ row }) => (
          <>
            <StatusBadge status={row.original.status} />
            {row.original.legalHold && <span className="ml-2 text-xs text-muted-foreground">⚖ hold</span>}
          </>
        ),
      },
      {
        id: 'uploaded',
        accessorKey: 'createdAt',
        header: 'Uploaded',
        cell: ({ row }) => <span className="text-muted-foreground">{fmtDateTime(row.original.createdAt)}</span>,
      },
      {
        id: 'actions',
        header: () => <span className="sr-only">Actions</span>,
        cell: ({ row }) => {
          const f = row.original;
          return (
            <div className="space-x-1 text-right">
              {f.status === 'Clean' && (
                <Button variant="ghost" size="sm" disabled={download.isPending} onClick={() => download.mutate(f.id)}>
                  Download
                </Button>
              )}
              {manage && f.status === 'Deleted' && (
                <Button variant="outline" size="sm" disabled={restore.isPending} onClick={() => restore.mutate(f.id)}>
                  Restore
                </Button>
              )}
              {manage && f.status !== 'Erased' && f.status !== 'Deleted' && (
                <>
                  <Button variant="ghost" size="sm" disabled={hold.isPending}
                    onClick={() => hold.mutate({ id: f.id, hold: !f.legalHold })}>
                    {f.legalHold ? 'Release hold' : 'Hold'}
                  </Button>
                  <ConfirmButton size="sm" disabled={erase.isPending} onConfirm={() => erase.mutate(f.id)}>
                    Delete
                  </ConfirmButton>
                </>
              )}
            </div>
          );
        },
        meta: { headerClassName: 'w-64' },
      },
    ],
    // the mutations are stable hooks; only `manage` decides a cell
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [manage],
  );

  return (
    <div className="space-y-6">
      <PageHeader
        headingLevel={siteId ? 2 : 1}
        title={siteId ? "Site files" : "Files"}
        description="Site reports and uploaded files. Deleted files remain restorable for 30 days."
        actions={<>
          <Button variant={trash ? 'default' : 'ghost'} size="sm"
            onClick={() => setTrash(!trash)}>
            Trash
          </Button>
        </>}
      />
      {manage && !trash && (
        <FileDropzone
          onFile={(file) => upload.mutate(file)}
          busy={upload.isPending}
          phase={phase}
          error={upload.isError ? String(upload.error) : undefined}
          label="Drop a file here, or choose one"
          hint="Scanned before anyone can download it."
          buttonLabel="Upload file"
        />
      )}
      {filesQuery.error && <p role="alert">Files could not load. <Button onClick={() => void filesQuery.refetch()}>Try again</Button></p>}
      {download.error && <p role="alert">{download.error.message}</p>}
      <Grid
        columns={columns}
        rows={files ?? []}
        getRowId={(f) => f.id}
        isLoading={files === undefined}
        loadingMessage="Loading…"
        emptyMessage={`No files yet.${manage ? ' Upload one to get started.' : ''}`}
        onFetchMore={() => void filesQuery.fetchNextPage()}
        hasMore={filesQuery.hasNextPage}
        isFetchingMore={filesQuery.isFetchingNextPage}
        footer={filesQuery.hasNextPage && <Button variant="outline" disabled={filesQuery.isFetchingNextPage}
          onClick={() => void filesQuery.fetchNextPage()}>
          {filesQuery.isFetchingNextPage ? 'Loading files…' : 'Load more files'}
        </Button>}
      />
    </div>
  );
}

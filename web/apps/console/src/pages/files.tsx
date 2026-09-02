import { api } from '@locintel/api';
import { Button, Card, CardContent, ConfirmButton, Table, TableBody, TableCell,
  TableHead, TableHeader, TableRow } from '@locintel/ui';
import { useInfiniteQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useRef, useState } from 'react';
import { fmtDateTime } from '../lib/format';
import { useApiMutation } from '../lib/mutation';
import type { Page } from '../lib/paging';
import { uploadFile } from '../lib/uploads';
import { can, useMe } from '../session';
import { StatusBadge } from '../shell';

type StoredFile = {
  id: string; name: string; contentType: string; status: string;
  legalHold: boolean; hasPreview: boolean; createdAt: string; deletedAt: string | null;
};

export function FilesPage() {
  const { data: me } = useMe();
  const queryClient = useQueryClient();
  const manage = can(me, 'files:manage');
  const [phase, setPhase] = useState('');
  const [trash, setTrash] = useState(false);

  const filesQuery = useInfiniteQuery({
    queryKey: ['files', 'list', trash],
    queryFn: ({ pageParam }) =>
      api.get<Page<StoredFile>>(`/api/files?limit=50&offset=${pageParam}${trash ? '&trash=true' : ''}`),
    initialPageParam: 0,
    getNextPageParam: (last) => last.nextOffset ?? undefined,
  });
  const files = filesQuery.data?.pages.flatMap((p) => p.items);
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['files'] });

  const upload = useMutation({
    mutationFn: (file: File) =>
      uploadFile(file, file.type || 'application/octet-stream', setPhase),
    onSettled: () => {
      setPhase('');
      refresh();
    },
  });
  const hold = useApiMutation({
    mutationFn: (input: { id: string; hold: boolean }) =>
      api.post(`/api/files/${input.id}/hold`, { hold: input.hold }),
    invalidate: [['files']],
    success: 'Legal hold updated',
  });
  const erase = useApiMutation({
    mutationFn: (id: string) => api.del(`/api/files/${id}`),
    invalidate: [['files']],
    success: 'Moved to trash - restorable for 30 days',
    errorFallback: 'Delete failed',
  });
  const restore = useApiMutation({
    mutationFn: (id: string) => api.post(`/api/files/${id}/restore`),
    invalidate: [['files']],
    success: 'File restored',
  });
  const fileInput = useRef<HTMLInputElement>(null);

  const download = async (id: string) => {
    const { url } = await api.get<{ url: string }>(`/api/files/${id}/download`);
    window.open(url, '_blank');
  };

  return (
    <div className="max-w-4xl space-y-6">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <h1 className="text-2xl font-semibold">Files</h1>
          <Button variant={trash ? 'default' : 'ghost'} size="sm"
            onClick={() => setTrash(!trash)}>
            Trash
          </Button>
        </div>
        {manage && (
          <div className="flex items-center gap-3">
            {phase && <span className="text-sm text-muted-foreground">{phase}</span>}
            {upload.isError && (
              <span className="text-sm text-destructive">{String(upload.error)}</span>
            )}
            <input
              ref={fileInput}
              type="file"
              className="hidden"
              onChange={(e) => {
                const file = e.target.files?.[0];
                if (file) upload.mutate(file);
                e.target.value = '';
              }}
            />
            <Button disabled={upload.isPending} onClick={() => fileInput.current?.click()}>
              Upload file
            </Button>
          </div>
        )}
      </div>
      <Card>
        <CardContent className="pt-4">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Name</TableHead>
                <TableHead>Status</TableHead>
                <TableHead>Uploaded</TableHead>
                <TableHead><span className="sr-only">Actions</span></TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {files?.map((f) => (
                <TableRow key={f.id}>
                  <TableCell>
                    <div className="font-medium">{f.name}</div>
                    <div className="text-xs text-muted-foreground">{f.contentType}</div>
                  </TableCell>
                  <TableCell>
                    <StatusBadge status={f.status} />
                    {f.legalHold && (
                      <span className="ml-2 text-xs text-muted-foreground">⚖ hold</span>
                    )}
                  </TableCell>
                  <TableCell className="text-muted-foreground">{fmtDateTime(f.createdAt)}</TableCell>
                  <TableCell className="space-x-1 text-right">
                    {f.status === 'Clean' && (
                      <Button variant="ghost" size="sm" onClick={() => void download(f.id)}>
                        Download
                      </Button>
                    )}
                    {manage && f.status === 'Deleted' && (
                      <Button variant="outline" size="sm" disabled={restore.isPending}
                        onClick={() => restore.mutate(f.id)}>
                        Restore
                      </Button>
                    )}
                    {manage && f.status !== 'Erased' && f.status !== 'Deleted' && (
                      <>
                        <Button variant="ghost" size="sm" disabled={hold.isPending}
                          onClick={() => hold.mutate({ id: f.id, hold: !f.legalHold })}>
                          {f.legalHold ? 'Release hold' : 'Hold'}
                        </Button>
                        <ConfirmButton size="sm" disabled={erase.isPending}
                          onConfirm={() => erase.mutate(f.id)}>
                          Delete
                        </ConfirmButton>
                      </>
                    )}
                  </TableCell>
                </TableRow>
              ))}
              {files === undefined && (
                <TableRow>
                  <TableCell colSpan={4} className="text-center text-muted-foreground">
                    Loading…
                  </TableCell>
                </TableRow>
              )}
              {files?.length === 0 && (
                <TableRow>
                  <TableCell colSpan={4} className="text-center text-muted-foreground">
                    No files yet.{manage && ' Upload one to get started.'}
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
          {filesQuery.hasNextPage && (
            <div className="pt-3 text-center">
              <Button variant="outline" size="sm"
                disabled={filesQuery.isFetchingNextPage}
                onClick={() => void filesQuery.fetchNextPage()}>
                Load more
              </Button>
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

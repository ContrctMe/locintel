import { Button, Select } from '@locintel/ui';
import { useInfiniteQuery } from '@tanstack/react-query';
import { filesApi } from './api';

/** Every accessible file is reachable; eligibility never hides the next page. */
export function FilePicker({ id, value, onChange, cleanOnly = false, 'aria-label': ariaLabel = 'File' }: {
  id?: string;
  value: string;
  onChange: (id: string) => void;
  cleanOnly?: boolean;
  'aria-label'?: string;
}) {
  const files = useInfiniteQuery({
    queryKey: ['files', 'pick'],
    queryFn: ({ pageParam, signal }) => filesApi.list({ offset: pageParam, trash: false }, signal),
    initialPageParam: 0,
    getNextPageParam: (page) => page.nextOffset ?? undefined,
  });
  const items = files.data?.pages.flatMap((page) => page.items).filter((file) => !cleanOnly || file.status === 'Clean') ?? [];
  return (
    <div className="space-y-2">
      <Select id={id} aria-label={ariaLabel} value={value} onChange={(event) => onChange(event.target.value)}>
        <option value="">Choose a file…</option>
        {items.map((file) => <option key={file.id} value={file.id}>{file.name} ({file.status})</option>)}
      </Select>
      {files.isPending && <p role="status">Loading files…</p>}
      {files.isError && <p role="alert">Could not load files. <Button type="button" variant="outline" onClick={() => void (files.isFetchNextPageError ? files.fetchNextPage() : files.refetch())}>Retry files</Button></p>}
      {files.isSuccess && items.length === 0 && <p>No eligible files on the loaded pages.</p>}
      {files.hasNextPage && <Button type="button" variant="outline" disabled={files.isFetching} onClick={() => void files.fetchNextPage()}>Load more files</Button>}
    </div>
  );
}

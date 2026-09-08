import { Button, Checkbox, Field, FieldLabel, FieldLegend, FieldSet } from '@locintel/ui';
import { useInfiniteQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { reportsApi } from './api';

const MAX_PHOTOS = 10;
const IMAGES = ['image/jpeg', 'image/png'];

/**
 * One site's own clean photographs. Scoped per site on purpose: the picker used
 * to fetch the whole organization's files once and render that same list under
 * every selected site, so fifty sites meant fifty identical copies.
 */
export function SitePhotoPicker({
  siteId,
  siteName,
  chosen,
  onChange,
}: {
  siteId: string;
  siteName: string;
  chosen: string[];
  onChange: (ids: string[]) => void;
}) {
  const files = useInfiniteQuery({
    queryKey: ['reports', 'photos', siteId],
    queryFn: ({ pageParam, signal }) => reportsApi.photos(siteId, pageParam, signal),
    initialPageParam: 0,
    getNextPageParam: (last) => last.nextOffset ?? undefined,
  });
  const photos =
    files.data?.pages
      .flatMap((page) => page.items)
      .filter((file) => file.status === 'Clean' && IMAGES.includes(file.contentType)) ?? [];

  return (
    <FieldSet>
      <FieldLegend>
        Photographs for {siteName} (up to {MAX_PHOTOS})
      </FieldLegend>
      {files.isPending && <p role="status">Loading photographs…</p>}
      {files.error && <p role="alert">Photographs for {siteName} could not load.</p>}
      {files.isSuccess && photos.length === 0 && (
        <p className="text-sm text-muted-foreground">
          No photographs on this site yet.{' '}
          <Link to="/sites/$siteId" params={{ siteId }} className="underline">
            Upload one to its files
          </Link>{' '}
          to include it in a report.
        </p>
      )}
      {photos.length > 0 && (
        <div className="max-h-36 overflow-auto">
          {photos.map((file) => (
            <Field key={file.id} orientation="horizontal">
              <Checkbox
                id={`photo-${siteId}-${file.id}`}
                checked={chosen.includes(file.id)}
                disabled={!chosen.includes(file.id) && chosen.length >= MAX_PHOTOS}
                onCheckedChange={() =>
                  onChange(
                    chosen.includes(file.id)
                      ? chosen.filter((id) => id !== file.id)
                      : [...chosen, file.id],
                  )
                }
              />
              <FieldLabel htmlFor={`photo-${siteId}-${file.id}`} className="font-normal">
                {file.name}
              </FieldLabel>
            </Field>
          ))}
        </div>
      )}
      {files.hasNextPage && (
        <Button
          type="button"
          variant="outline"
          disabled={files.isFetchingNextPage}
          onClick={() => void files.fetchNextPage()}
        >
          More photographs for {siteName}
        </Button>
      )}
    </FieldSet>
  );
}

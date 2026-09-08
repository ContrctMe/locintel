import {
  Button,
  Combobox,
  ComboboxContent,
  ComboboxEmpty,
  ComboboxInput,
  ComboboxItem,
  ComboboxList,
} from '@locintel/ui';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useDebounced } from '../../../lib/debounce';
import { sitesApi } from '../api';

export type PickedSite = { id: string; name: string; city?: string | null };

/** What to search for: what was typed, unless it is only the chosen site's own name showing in the input. */
export const searchTerm = (typed: string, selectedName: string | undefined): string =>
  typed.trim() === selectedName ? '' : typed.trim();

/**
 * One site out of however many the org has: the shadcn Combobox over the
 * server's word search (ADR 51), so the list is the twenty best matches for
 * what was typed, never a page of fifty with a "load more". The chosen
 * site stays shown when the query changes.
 */
export function SitePicker({
  id,
  value,
  onChange,
  placeholder = 'Search sites…',
  under,
  'aria-label': ariaLabel,
}: {
  id?: string;
  value: PickedSite | null;
  onChange: (site: PickedSite | null) => void;
  placeholder?: string;
  /** The console's scope node: the search stays inside it. */
  under?: string | null;
  'aria-label'?: string;
}) {
  const [typed, setTyped] = useState('');
  const q = useDebounced(searchTerm(typed, value?.name), 250);
  const results = useQuery({
    queryKey: ['sites', 'pick', q, under ?? null],
    queryFn: ({ signal }) => sitesApi.list(20, undefined, q || undefined, under ?? undefined, undefined, undefined, signal),
    staleTime: 30_000,
  });
  const items: PickedSite[] = results.data?.items.map((s) => ({ id: s.id, name: s.name, city: s.city })) ?? [];
  // the server already filtered; keep every item the list holds
  return (
    <div className="space-y-2">
    <Combobox<PickedSite>
      items={items}
      value={value}
      onValueChange={(next) => onChange(next ?? null)}
      onInputValueChange={(text) => setTyped(text)}
      itemToStringLabel={(s) => s.name}
      isItemEqualToValue={(a, b) => a.id === b.id}
      filter={null}
    >
      <ComboboxInput id={id} placeholder={placeholder} aria-label={ariaLabel} showClear={value !== null} />
      <ComboboxContent>
        <ComboboxEmpty>{results.isPending ? 'Searching…' : results.isError ? 'Search unavailable.' : 'No sites match.'}</ComboboxEmpty>
        <ComboboxList>
          {(site: PickedSite) => (
            <ComboboxItem key={site.id} value={site}>
              <span className="flex min-w-0 flex-col">
                <span className="truncate">{site.name}</span>
                {site.city && <span className="truncate text-xs text-muted-foreground">{site.city}</span>}
              </span>
            </ComboboxItem>
          )}
        </ComboboxList>
      </ComboboxContent>
    </Combobox>
    {results.isError && <p role="alert">Could not search sites. <Button type="button" variant="outline" onClick={() => void results.refetch()}>Retry site search</Button></p>}
    </div>
  );
}

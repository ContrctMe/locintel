import {
  Button,
  Command,
  CommandDialog,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from '@locintel/ui';
import { useQuery } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { MapPin, Search } from 'lucide-react';
import { useEffect, useState } from 'react';
import { sitesApi } from '../features/sites/api';
import { useDebounced } from '../lib/debounce';
import { StatusBadge } from '../shell';

/**
 * Command-K search on the shadcn Command palette (cmdk), wired to sites:
 * open with the shortcut or the header button, type, pick a site, land on
 * it. Results come from the server through the same scoped list endpoint
 * the Sites page uses, so cmdk's own filtering is off - what the server
 * returns is what is shown.
 */
export function CommandSearch() {
  const [open, setOpen] = useState(false);
  const [q, setQ] = useState('');
  const navigate = useNavigate();

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if (event.key.toLowerCase() === 'k' && (event.metaKey || event.ctrlKey)) {
        event.preventDefault();
        setOpen(true);
      }
    }
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, []);

  // a keystroke is not a query: the server sees the term once typing pauses
  const term = useDebounced(q.trim(), 250);
  const results = useQuery({
    queryKey: ['sites', 'search', term],
    queryFn: ({ signal }) => sitesApi.list(8, undefined, term, undefined, undefined, undefined, signal),
    enabled: open && term.length > 0,
  });
  const items = results.data?.items ?? [];

  const go = (id: string) => {
    setOpen(false);
    setQ('');
    void navigate({ to: '/sites/$siteId', params: { siteId: id } });
  };

  return (
    <>
      <Button
        type="button"
        variant="ghost"
        size="icon"
        aria-label="Open search"
        aria-haspopup="dialog"
        aria-expanded={open}
        onClick={() => setOpen(true)}
      >
        <Search className="size-4.5" aria-hidden />
      </Button>

      <CommandDialog
        open={open}
        onOpenChange={(next) => {
          setOpen(next);
          if (!next) setQ('');
        }}
        title="Search sites"
        description="Type a site name or city, then pick one to open it."
      >
        <Command shouldFilter={false} className="**:data-[selected=true]:bg-muted">
          <CommandInput
            placeholder="Search sites by a word of the name or city…"
            aria-label="Search sites"
            value={q}
            onValueChange={setQ}
          />
          <CommandList>
            {term.length === 0 && (
              <CommandEmpty>Start typing to search the sites in your scope.</CommandEmpty>
            )}
            {term.length > 0 && results.isPending && <CommandEmpty>Searching…</CommandEmpty>}
            {term.length > 0 && results.data && items.length === 0 && (
              <CommandEmpty>No sites match.</CommandEmpty>
            )}
            {items.length > 0 && (
              <CommandGroup heading="Sites">
                {items.map((s) => (
                  <CommandItem key={s.id} value={s.id} onSelect={() => go(s.id)} className="gap-2.5">
                    <MapPin className="size-4 shrink-0 text-muted-foreground" aria-hidden />
                    <div className="flex min-w-0 flex-1 flex-col">
                      <span className="truncate font-medium">{s.name}</span>
                      {s.city && <span className="truncate text-xs text-muted-foreground">{s.city}</span>}
                    </div>
                    <div className="ml-auto" data-slot="command-shortcut">
                      <StatusBadge status={s.status} />
                    </div>
                  </CommandItem>
                ))}
              </CommandGroup>
            )}
          </CommandList>
        </Command>
      </CommandDialog>
    </>
  );
}

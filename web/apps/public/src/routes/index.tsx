import { createFileRoute, Link, useNavigate, useRouter } from '@tanstack/react-router';
import { createServerFn } from '@tanstack/react-start';
import { useEffect, useState } from 'react';
import { publicLocator, publicLocatorPage, publicSignOut, type PublicSite } from '../api';
import { SiteMap } from '../SiteMap';

const fetchLocator = createServerFn({ method: 'GET' })
  .validator((near?: string) => near)
  .handler(({ data: near }) => publicLocator(near));

// the next alphabetical page (nearest-first pages have no next: the point of "near" is the closest ones)
const fetchMore = createServerFn({ method: 'GET' })
  .validator((after: string) => after)
  .handler(({ data: after }) => publicLocatorPage(after));

const signOut = createServerFn({ method: 'POST' }).handler(publicSignOut);

export const Route = createFileRoute('/')({
  validateSearch: (search: Record<string, unknown>): { near?: string } =>
    typeof search.near === 'string' ? { near: search.near } : {},
  loaderDeps: ({ search }) => ({ near: search.near }),
  loader: ({ deps }) => fetchLocator({ data: deps.near }),
  component: Locator,
});

function Locator() {
  const { sites: firstPage, next: firstNext, me } = Route.useLoaderData();
  const { near } = Route.useSearch();
  const navigate = useNavigate();
  const router = useRouter();
  const [signingOut, setSigningOut] = useState(false);
  const [signOutError, setSignOutError] = useState<string>();
  // pages after the first are appended in place; a new search starts over
  const [more, setMore] = useState<{ items: PublicSite[]; next: string | null }>({ items: [], next: firstNext });
  const [loadingMore, setLoadingMore] = useState(false);
  useEffect(() => setMore({ items: [], next: firstNext }), [firstPage, firstNext]);
  const sites = firstPage === undefined ? undefined : [...firstPage, ...more.items];

  if (sites === undefined) {
    return (
      <main className="mx-auto max-w-2xl space-y-4 px-6 py-16">
        <h1 className="text-3xl font-semibold tracking-tight">Temporarily unavailable</h1>
        <p className="text-muted-foreground">
          We couldn&apos;t load locations right now. Please try again in a moment.
        </p>
      </main>
    );
  }

  return (
    <main className="mx-auto max-w-2xl space-y-8 px-6 py-12">
      {me.tier === 'contact' && me.email && (
        <p className="flex items-center justify-between rounded-md border bg-card px-4 py-2 text-sm text-muted-foreground">
          <span>
            You&apos;re viewing as{' '}
            <span className="font-medium text-foreground">{me.email}</span>
          </span>
          <button
            type="button"
            disabled={signingOut}
            className="underline-offset-4 hover:underline"
            onClick={async () => {
              setSigningOut(true);
              setSignOutError(undefined);
              try {
                const result = await signOut();
                if (!result.ok) setSignOutError(result.error);
                else await router.invalidate();
              } catch {
                setSignOutError('We could not confirm sign-out. Your session may still be active. Please try again.');
              } finally {
                setSigningOut(false);
              }
            }}
          >
            {signingOut ? 'Signing out…' : 'Sign out'}
          </button>
        </p>
      )}
      {signOutError && <p role="alert">{signOutError}</p>}
      <div className="space-y-2">
        <h1 className="text-3xl font-semibold tracking-tight">Our locations</h1>
        <p className="text-muted-foreground">
          {sites.length === 0
            ? 'No locations to show for this address.'
            : 'Find a location and check today’s hours.'}
        </p>
        {sites.length > 1 && (
          <button
            type="button"
            className="text-sm underline-offset-4 hover:underline"
            onClick={() => {
              if (near) {
                void navigate({ to: '/', search: {} });
                return;
              }
              navigator.geolocation?.getCurrentPosition((position) => {
                const at = `${position.coords.latitude.toFixed(4)},${position.coords.longitude.toFixed(4)}`;
                void navigate({ to: '/', search: { near: at } });
              });
            }}
          >
            {near ? 'Clear distance sort' : 'Sort by distance from me'}
          </button>
        )}
      </div>
      <SiteMap sites={sites} />
      <ul className="divide-y rounded-lg border bg-card">
        {sites.map((site) => (
          <li key={site.id}>
            <Link
              to="/sites/$siteId"
              params={{ siteId: site.id }}
              className="flex items-center justify-between px-4 py-3 hover:bg-accent"
            >
              <span>
                <span className="font-medium">{site.name}</span>
                {site.city && (
                  <span className="ml-2 text-sm text-muted-foreground">{site.city}</span>
                )}
                {site.distanceKm != null && (
                  <span className="ml-2 text-sm text-muted-foreground">
                    {site.distanceKm} km
                  </span>
                )}
              </span>
              <span
                className={`text-sm ${site.openNow ? 'text-primary' : 'text-muted-foreground'}`}
              >
                {site.status === 'ComingSoon'
                  ? 'Coming soon'
                  : site.openNow
                    ? 'Open now'
                    : 'Closed'}
              </span>
            </Link>
          </li>
        ))}
      </ul>
      {more.next && (
        <button
          type="button"
          disabled={loadingMore}
          className="w-full rounded-lg border bg-card px-4 py-3 text-sm hover:bg-accent disabled:opacity-60"
          onClick={async () => {
            if (!more.next) return;
            setLoadingMore(true);
            try {
              const page = await fetchMore({ data: more.next });
              setMore((current) => ({ items: [...current.items, ...page.items], next: page.next }));
            } finally {
              setLoadingMore(false);
            }
          }}
        >
          {loadingMore ? 'Loading…' : 'Show more locations'}
        </button>
      )}
    </main>
  );
}

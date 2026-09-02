import { createFileRoute, Link, useNavigate, useRouter } from '@tanstack/react-router';
import { createServerFn } from '@tanstack/react-start';
import { getRequestHeader, setCookie } from '@tanstack/react-start/server';
import { publicApi, publicApiMaybe, type PublicMe, type PublicSite } from '../api';
import { SiteMap } from '../SiteMap';

const fetchLocator = createServerFn({ method: 'GET' })
  .inputValidator((near?: string) => near)
  .handler(async ({ data: near }) => ({
    // maybe-variant: an unreachable API must not masquerade as an empty org
    sites: await publicApiMaybe<PublicSite[]>(
      near ? `/public/sites?near=${encodeURIComponent(near)}` : '/public/sites',
    ),
    me: await publicApi<PublicMe>('/me', { tier: 'guest' }),
  }));

const signOut = createServerFn({ method: 'POST' }).handler(async () => {
  const apiBase = process.env.LOCINTEL_API ?? 'http://localhost:5293';
  const cookie = getRequestHeader('cookie');
  try {
    const response = await fetch(`${apiBase}/auth/logout`, {
      method: 'POST',
      headers: cookie ? { cookie } : {},
    });
    // relay the API's cookie deletions onto THIS host, mirroring the redeem relay
    for (const raw of response.headers.getSetCookie()) {
      const pair = raw.split(';')[0] ?? '';
      const eq = pair.indexOf('=');
      if (eq > 0) setCookie(pair.slice(0, eq), '', { path: '/', maxAge: 0 });
    }
  } catch {
    // the API being down must not trap the visitor in the session
  }
});

export const Route = createFileRoute('/')({
  validateSearch: (search: Record<string, unknown>): { near?: string } =>
    typeof search.near === 'string' ? { near: search.near } : {},
  loaderDeps: ({ search }) => ({ near: search.near }),
  loader: ({ deps }) => fetchLocator({ data: deps.near }),
  component: Locator,
});

function Locator() {
  const { sites, me } = Route.useLoaderData();
  const { near } = Route.useSearch();
  const navigate = useNavigate();
  const router = useRouter();

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
            className="underline-offset-4 hover:underline"
            onClick={async () => {
              await signOut();
              await router.invalidate();
            }}
          >
            Sign out
          </button>
        </p>
      )}
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
    </main>
  );
}

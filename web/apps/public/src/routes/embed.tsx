import { createFileRoute, Link } from '@tanstack/react-router';
import { createServerFn } from '@tanstack/react-start';
import { publicApi, type PublicSiteList } from '../api';
import { SiteMap } from '../SiteMap';

const fetchSites = createServerFn({ method: 'GET' }).handler(() =>
  publicApi<PublicSiteList>('/public/sites', { items: [], next: null }).then((page) => page.items),
);

/**
 * The embeddable locator (ADR 43): map + compact list, no chrome, meant for
 * an iframe on the org's own website (the console's Settings page hands out
 * the snippet). Site links open the full public page in a new tab - the
 * iframe is a window, not a cage.
 */
export const Route = createFileRoute('/embed')({
  loader: () => fetchSites(),
  component: Embed,
});

function Embed() {
  const sites = Route.useLoaderData();
  return (
    <main className="space-y-3 p-3">
      <SiteMap sites={sites} />
      <ul className="divide-y rounded-lg border bg-card">
        {sites.map((site) => (
          <li key={site.id}>
            <Link
              to="/sites/$siteId"
              params={{ siteId: site.id }}
              target="_blank"
              className="flex items-center justify-between px-3 py-2 text-sm hover:bg-accent"
            >
              <span className="font-medium">
                {site.name}
                {site.city && (
                  <span className="ml-2 font-normal text-muted-foreground">{site.city}</span>
                )}
              </span>
              <span className={site.openNow ? 'text-primary' : 'text-muted-foreground'}>
                {site.status === 'ComingSoon' ? 'Coming soon' : site.openNow ? 'Open now' : 'Closed'}
              </span>
            </Link>
          </li>
        ))}
      </ul>
    </main>
  );
}

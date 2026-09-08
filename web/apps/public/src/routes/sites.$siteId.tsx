import { createFileRoute, Link } from '@tanstack/react-router';
import { createServerFn } from '@tanstack/react-start';
import { publicApi, type PublicSiteDetail } from '../api';
import { isTodayInZone, spanLabel } from '../lib/hours';

const fetchSite = createServerFn({ method: 'GET' })
  .validator((siteId: string) => siteId)
  .handler(({ data }) => publicApi<PublicSiteDetail | null>(`/public/sites/${data}`, null));

export const Route = createFileRoute('/sites/$siteId')({
  loader: ({ params }) => fetchSite({ data: params.siteId }),
  head: ({ loaderData: site }) => ({
    meta: site ? [{ title: site.name }] : [],
  }),
  component: SitePage,
});

function SitePage() {
  const site = Route.useLoaderData();
  if (!site) {
    return (
      <main className="mx-auto max-w-2xl space-y-4 px-6 py-16">
        <h1 className="text-2xl font-semibold">Location not found</h1>
        <Link to="/" className="text-sm underline underline-offset-4">
          Back to all locations
        </Link>
      </main>
    );
  }

  // group this week's windows by site-local date
  const byDate = new Map<string, string[]>();
  for (const w of site.windows) {
    const list = byDate.get(w.localDate) ?? [];
    list.push(spanLabel(w.startsAtUtc, w.endsAtUtc, site.timeZone));
    byDate.set(w.localDate, list);
  }
  const address = [site.addressLine1, site.city, site.postalCode, site.countryCode]
    .filter(Boolean)
    .join(', ');

  return (
    <main className="mx-auto max-w-2xl space-y-8 px-6 py-12">
      <Link to="/" className="text-sm text-muted-foreground underline-offset-4 hover:underline">
        ← All locations
      </Link>
      <div className="space-y-2">
        <h1 className="text-3xl font-semibold tracking-tight">{site.name}</h1>
        <p className={site.openNow ? 'font-medium text-primary' : 'text-muted-foreground'}>
          {site.status === 'ComingSoon' ? 'Coming soon' : site.openNow ? 'Open now' : 'Closed'}
        </p>
        {address && (
          <p className="text-sm text-muted-foreground">
            <a
              href={`https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(`${site.name}, ${address}`)}`}
              target="_blank"
              rel="noreferrer"
              className="underline-offset-4 hover:underline"
            >
              {address}
            </a>
          </p>
        )}
        {site.attributes?.length > 0 && (
          <ul className="flex flex-wrap gap-x-4 gap-y-1 text-sm text-muted-foreground">
            {site.attributes.map((a) => (
              <li key={a.key}>
                {typeof a.value === 'boolean'
                  ? `${a.value ? '✓' : '✗'} ${a.label}`
                  : `${a.label}: ${a.value}`}
              </li>
            ))}
          </ul>
        )}
      </div>
      <section className="space-y-3">
        <h2 className="text-lg font-medium">Hours this week</h2>
        {byDate.size === 0 ? (
          <p className="text-sm text-muted-foreground">Hours are not published.</p>
        ) : (
          <ul className="divide-y rounded-lg border bg-card text-sm">
            {[...byDate.entries()].map(([date, spans]) => {
              const today = isTodayInZone(date, site.timeZone, Date.now());
              return (
                <li
                  key={date}
                  className={`flex justify-between px-4 py-2.5 ${today ? 'font-semibold' : ''}`}
                >
                  <span>
                    {today
                      ? 'Today'
                      : new Date(`${date}T00:00:00`).toLocaleDateString([], {
                          weekday: 'long',
                          month: 'short',
                          day: 'numeric',
                        })}
                  </span>
                  <span className={today ? '' : 'text-muted-foreground'}>
                    {spans.join(', ')}
                  </span>
                </li>
              );
            })}
          </ul>
        )}
        {site.closures.length > 0 && (
          <p className="text-sm">
            <span className="font-medium">Upcoming closures:</span>{' '}
            <span className="text-muted-foreground">
              {site.closures
                .map((date) =>
                  new Date(`${date}T00:00:00`).toLocaleDateString([], {
                    weekday: 'short',
                    month: 'short',
                    day: 'numeric',
                  }),
                )
                .join(' · ')}
            </span>
          </p>
        )}
        <p className="text-xs text-muted-foreground">
          All times local to the location ({site.timeZone.replaceAll('_', ' ')}).
        </p>
      </section>
    </main>
  );
}

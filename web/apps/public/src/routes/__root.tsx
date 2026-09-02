import { createRootRoute, HeadContent, Link, Outlet, Scripts, useRouterState } from '@tanstack/react-router';
import { createServerFn } from '@tanstack/react-start';
import type { ReactNode } from 'react';
import { publicApiMaybe, type PublicOrg } from '../api';
import css from '../styles.css?url';

const fetchOrg = createServerFn({ method: 'GET' }).handler(() =>
  publicApiMaybe<PublicOrg>('/public/org'),
);

export const Route = createRootRoute({
  // the org's identity dresses the shell; pages render fine without it
  loader: () => fetchOrg(),
  head: ({ loaderData: org }) => ({
    meta: [
      { charSet: 'utf-8' },
      { name: 'viewport', content: 'width=device-width, initial-scale=1' },
      { title: org ? `${org.name} — Locations` : 'LocIntel' },
      {
        name: 'description',
        content: org
          ? `Locations and opening hours for ${org.name}.`
          : 'Locations and opening hours.',
      },
    ],
    links: [{ rel: 'stylesheet', href: css }],
  }),
  component: RootComponent,
});

function RootComponent() {
  const org = Route.useLoaderData();
  // the embed route lives inside someone ELSE's page: no chrome (ADR 43)
  const embedded = useRouterState({ select: (s) => s.location.pathname }) === '/embed';
  if (embedded) {
    return (
      <RootDocument brandColor={org?.brandColor ?? undefined}>
        <Outlet />
      </RootDocument>
    );
  }
  return (
    <RootDocument brandColor={org?.brandColor ?? undefined}>
      <header className="border-b">
        <div className="mx-auto flex max-w-2xl items-center justify-between px-6 py-4">
          <Link to="/" className="font-semibold tracking-tight">
            {org?.name ?? 'Locations'}
          </Link>
        </div>
      </header>
      <Outlet />
      <footer className="mt-16 border-t">
        <div className="mx-auto flex max-w-2xl items-center justify-between px-6 py-6 text-xs text-muted-foreground">
          <span>{org?.name ?? ''}</span>
          <a href="/tips" className="underline-offset-2 hover:underline">Report something</a>
          <span>Powered by LocIntel</span>
        </div>
      </footer>
    </RootDocument>
  );
}

function RootDocument({
  children,
  brandColor,
}: {
  children: ReactNode;
  brandColor?: string;
}) {
  return (
    // brand.color (an org setting) overrides the accent: the template-sized
    // theming hook - forks that want more start here
    <html lang="en" style={brandColor ? { ['--primary' as never]: brandColor } : undefined}>
      <head>
        <HeadContent />
      </head>
      <body>
        {children}
        <Scripts />
      </body>
    </html>
  );
}

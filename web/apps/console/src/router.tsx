import { createRootRoute, createRoute, createRouter, lazyRouteComponent, Outlet } from '@tanstack/react-router';
import { Shell } from './shell';

const rootRoute = createRootRoute({
  component: () => (
    <Shell>
      <Outlet />
    </Shell>
  ),
});

const routes = [
  createRoute({ getParentRoute: () => rootRoute, path: '/',
    component: lazyRouteComponent(() => import('./pages/dashboard'), 'DashboardPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/sites',
    component: lazyRouteComponent(() => import('./features/sites'), 'SitesPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/sites/$siteId',
    component: lazyRouteComponent(() => import('./features/sites'), 'SiteDetailPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/checklists',
    component: lazyRouteComponent(() => import('./features/checklists'), 'ChecklistsPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/files',
    component: lazyRouteComponent(() => import('./pages/files'), 'FilesPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/hierarchy',
    component: lazyRouteComponent(() => import('./pages/hierarchy'), 'HierarchyPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/ingest',
    component: lazyRouteComponent(() => import('./pages/ingest'), 'IngestPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/members',
    component: lazyRouteComponent(() => import('./pages/members'), 'MembersPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/roles',
    component: lazyRouteComponent(() => import('./features/roles'), 'RolesPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/audit',
    component: lazyRouteComponent(() => import('./pages/audit'), 'AuditPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/operator',
    component: lazyRouteComponent(() => import('./features/operator'), 'OperatorPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/settings',
    component: lazyRouteComponent(() => import('./pages/settings'), 'SettingsPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/developers',
    component: lazyRouteComponent(() => import('./pages/developers'), 'DevelopersPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/account',
    component: lazyRouteComponent(() => import('./pages/account'), 'AccountPage') }),
];

export const router = createRouter({
  routeTree: rootRoute.addChildren(routes),
  defaultPendingComponent: () => <p className="text-sm text-muted-foreground">Loading…</p>,
});

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router;
  }
}

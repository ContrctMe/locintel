import { PageSkeleton } from './components/page';
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
  createRoute({ getParentRoute: () => rootRoute, path: '/reports/$runId', component: lazyRouteComponent(() => import('./features/reports'), 'ReportRunPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/reports', component: lazyRouteComponent(() => import('./features/reports'), 'ReportsPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/overlays', component: lazyRouteComponent(() => import('./features/overlays'), 'OverlaysPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/', component: lazyRouteComponent(() => import('./pages/dashboard'), 'DashboardPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/sites', component: lazyRouteComponent(() => import('./features/sites'), 'SitesPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/sites/$siteId', component: lazyRouteComponent(() => import('./features/sites'), 'SiteDetailPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/checklists', component: lazyRouteComponent(() => import('./features/checklists'), 'ChecklistsPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/patrols', component: lazyRouteComponent(() => import('./pages/patrols'), 'PatrolsPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/incidents', component: lazyRouteComponent(() => import('./pages/incidents'), 'IncidentsPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/incidents/$incidentId', component: lazyRouteComponent(() => import('./pages/incident-detail'), 'IncidentDetailPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/alerts', component: lazyRouteComponent(() => import('./pages/alerts'), 'AlertsPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/analytics', component: lazyRouteComponent(() => import('./pages/analytics'), 'AnalyticsPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/cases', component: lazyRouteComponent(() => import('./pages/cases'), 'CasesPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/cases/$caseId', component: lazyRouteComponent(() => import('./pages/case-detail'), 'CaseDetailPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/cases/$caseId/package', component: lazyRouteComponent(() => import('./pages/case-package'), 'CasePackagePage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/entities', component: lazyRouteComponent(() => import('./pages/entities'), 'EntitiesPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/entities/$entityId', component: lazyRouteComponent(() => import('./pages/entity-detail'), 'EntityDetailPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/network', component: lazyRouteComponent(() => import('./pages/network'), 'NetworkPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/marketplace', component: lazyRouteComponent(() => import('./pages/marketplace'), 'MarketplacePage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/marketplace/vendors', component: lazyRouteComponent(() => import('./pages/marketplace-vendors'), 'MarketplaceVendorsPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/marketplace/requests/$requestId', component: lazyRouteComponent(() => import('./pages/request-detail'), 'RequestDetailPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/vendor', component: lazyRouteComponent(() => import('./pages/vendor-portal'), 'VendorPortalPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/vendor/requests/$requestId', component: lazyRouteComponent(() => import('./pages/vendor-portal'), 'VendorRequestPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/files', component: lazyRouteComponent(() => import('./pages/files'), 'FilesPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/hierarchy', component: lazyRouteComponent(() => import('./pages/hierarchy'), 'HierarchyPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/ingest', component: lazyRouteComponent(() => import('./pages/ingest'), 'IngestPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/members', component: lazyRouteComponent(() => import('./pages/members'), 'MembersPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/roles', component: lazyRouteComponent(() => import('./features/roles'), 'RolesPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/audit', component: lazyRouteComponent(() => import('./pages/audit'), 'AuditPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/operator', component: lazyRouteComponent(() => import('./features/operator'), 'OperatorPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/settings', component: lazyRouteComponent(() => import('./pages/settings'), 'SettingsPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/developers', component: lazyRouteComponent(() => import('./pages/developers'), 'DevelopersPage') }),
  createRoute({ getParentRoute: () => rootRoute, path: '/account', component: lazyRouteComponent(() => import('./pages/account'), 'AccountPage') }),
];

export const router = createRouter({ routeTree: rootRoute.addChildren(routes), defaultPendingComponent: PageSkeleton });

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router;
  }
}

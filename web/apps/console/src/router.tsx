import { createRootRoute, createRoute, createRouter, Outlet } from '@tanstack/react-router';
import { Shell } from './shell';
import { AccountPage } from './pages/account';
import { AlertsPage } from './pages/alerts';
import { AuditPage } from './pages/audit';
import { CaseDetailPage } from './pages/case-detail';
import { CasesPage } from './pages/cases';
import { ChecklistsPage } from './pages/checklists';
import { DashboardPage } from './pages/dashboard';
import { DevelopersPage } from './pages/developers';
import { EntitiesPage } from './pages/entities';
import { EntityDetailPage } from './pages/entity-detail';
import { HierarchyPage } from './pages/hierarchy';
import { IncidentDetailPage } from './pages/incident-detail';
import { IncidentsPage } from './pages/incidents';
import { IngestPage } from './pages/ingest';
import { MarketplacePage } from './pages/marketplace';
import { MarketplaceVendorsPage } from './pages/marketplace-vendors';
import { MembersPage } from './pages/members';
import { RequestDetailPage } from './pages/request-detail';
import { VendorPortalPage, VendorRequestPage } from './pages/vendor-portal';
import { OperatorPage } from './pages/operator';
import { RolesPage } from './pages/roles';
import { SettingsPage } from './pages/settings';
import { SiteDetailPage } from './pages/site-detail';
import { FilesPage } from './pages/files';
import { SitesPage } from './pages/sites';

const rootRoute = createRootRoute({
  component: () => (
    <Shell>
      <Outlet />
    </Shell>
  ),
});

const routes = [
  createRoute({ getParentRoute: () => rootRoute, path: '/', component: DashboardPage }),
  createRoute({ getParentRoute: () => rootRoute, path: '/sites', component: SitesPage }),
  createRoute({ getParentRoute: () => rootRoute, path: '/sites/$siteId', component: SiteDetailPage }),
  createRoute({ getParentRoute: () => rootRoute, path: '/checklists', component: ChecklistsPage }),
  createRoute({ getParentRoute: () => rootRoute, path: '/incidents', component: IncidentsPage }),
  createRoute({ getParentRoute: () => rootRoute, path: '/incidents/$incidentId', component: IncidentDetailPage }),
  createRoute({ getParentRoute: () => rootRoute, path: '/alerts', component: AlertsPage }),
  createRoute({ getParentRoute: () => rootRoute, path: '/cases', component: CasesPage }),
  createRoute({ getParentRoute: () => rootRoute, path: '/cases/$caseId', component: CaseDetailPage }),
  createRoute({ getParentRoute: () => rootRoute, path: '/entities', component: EntitiesPage }),
  createRoute({ getParentRoute: () => rootRoute, path: '/entities/$entityId', component: EntityDetailPage }),
  createRoute({ getParentRoute: () => rootRoute, path: '/marketplace', component: MarketplacePage }),
  createRoute({ getParentRoute: () => rootRoute, path: '/marketplace/vendors', component: MarketplaceVendorsPage }),
  createRoute({ getParentRoute: () => rootRoute, path: '/marketplace/requests/$requestId', component: RequestDetailPage }),
  createRoute({ getParentRoute: () => rootRoute, path: '/vendor', component: VendorPortalPage }),
  createRoute({ getParentRoute: () => rootRoute, path: '/vendor/requests/$requestId', component: VendorRequestPage }),
  createRoute({ getParentRoute: () => rootRoute, path: '/files', component: FilesPage }),
  createRoute({ getParentRoute: () => rootRoute, path: '/hierarchy', component: HierarchyPage }),
  createRoute({ getParentRoute: () => rootRoute, path: '/ingest', component: IngestPage }),
  createRoute({ getParentRoute: () => rootRoute, path: '/members', component: MembersPage }),
  createRoute({ getParentRoute: () => rootRoute, path: '/roles', component: RolesPage }),
  createRoute({ getParentRoute: () => rootRoute, path: '/audit', component: AuditPage }),
  createRoute({ getParentRoute: () => rootRoute, path: '/operator', component: OperatorPage }),
  createRoute({ getParentRoute: () => rootRoute, path: '/settings', component: SettingsPage }),
  createRoute({ getParentRoute: () => rootRoute, path: '/developers', component: DevelopersPage }),
  createRoute({ getParentRoute: () => rootRoute, path: '/account', component: AccountPage }),
];

export const router = createRouter({ routeTree: rootRoute.addChildren(routes) });

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router;
  }
}

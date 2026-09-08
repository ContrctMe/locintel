import { api } from '@locintel/api';
import { Breadcrumb, BreadcrumbItem, BreadcrumbList, BreadcrumbPage, BreadcrumbSeparator, Button, Select, SidebarTrigger } from '@locintel/ui';
import { useQuery } from '@tanstack/react-query';
import { useRouterState } from '@tanstack/react-router';
import { MoonStar, Sun } from 'lucide-react';
import { useEffect, useState } from 'react';
import { can } from '../../session';
import { CommandSearch } from '../../components/command-search';
import { UserMenu } from '../../components/user-menu';
import { persisted } from '../persisted';
import { useScope } from '../scope';
import { useHierarchy } from '../../features/hierarchy/hooks';
import { useSessionTransition } from '../session-boundary';
import { currentTheme, toggleTheme } from '../theme';
import { type User, pageTitle } from './nav';
import { ScopeSheet } from './phone';

export function Topbar({ me, scoped, orgName }: { me: User; scoped: boolean; orgName: string }) {
  const path = useRouterState({ select: (s) => s.location.pathname });
  const scope = useScope();
  const { data: hierarchy } = useHierarchy();
  const scopeName =
    scope.nodeId === null
      ? 'All sites'
      : hierarchy?.nodes.find((n) => n.id === scope.nodeId)?.name ?? 'All sites';
  const title = pageTitle(path);
  return (
    <header className="sticky top-0 z-10 flex h-[52px] shrink-0 items-center gap-3 border-b bg-background px-4 md:px-5">
      {/* desktop: the org (a switcher when there is a choice), then the
          breadcrumb - the scope node only on the pages that read it */}
      <div className="hidden min-w-0 items-center gap-2 md:flex">
        {scoped && <SidebarTrigger className="-ml-1" />}
        <span className="flex size-6 shrink-0 items-center justify-center rounded-md bg-primary text-xs font-bold text-primary-foreground" aria-hidden>
          {orgName.charAt(0).toUpperCase()}
        </span>
        {me.organizations.length > 1 ? (
          <OrgSwitcher me={me} className="w-auto max-w-56" />
        ) : (
          <span className="truncate text-sm font-semibold">{orgName}</span>
        )}
        <Breadcrumb>
          <BreadcrumbList>
            <BreadcrumbSeparator />
            {scoped && (
              <>
                <BreadcrumbItem>
                  <span className="truncate">{scopeName}</span>
                </BreadcrumbItem>
                <BreadcrumbSeparator />
              </>
            )}
            <BreadcrumbItem>
              <BreadcrumbPage>{title}</BreadcrumbPage>
            </BreadcrumbItem>
          </BreadcrumbList>
        </Breadcrumb>
      </div>
      {/* phone: the page, then the scope chip where it applies */}
      <div className="flex min-w-0 flex-1 items-center gap-2 md:hidden">
        <span className="shrink-0 text-base font-semibold">{title}</span>
        {scoped && <ScopeSheet me={me} scopeName={scopeName} />}
      </div>
      <div className="ml-auto flex items-center gap-1">
        <CommandSearch />
        <ThemeToggle />
        <UserMenu email={me.email} name={me.name} canManageOrg={can(me, 'org:manage')} footer={<ApiVersion />} />
      </div>
    </header>
  );
}

export function OrgSwitcher({ me, className = 'w-full' }: { me: User; className?: string }) {
  const changeSession = useSessionTransition();
  if (me.organizations.length <= 1) return null;
  return (
    <Select
      aria-label="Active organization"
      className={className}
      value={me.activeOrg ?? ''}
      onChange={async (e) => {
        const orgId = e.target.value;
        await changeSession(() => api.post('/auth/switch-org', { orgId }));
      }}
    >
      {me.organizations.map((o) => (
        <option key={o.id} value={o.id}>
          {o.name}
        </option>
      ))}
    </Select>
  );
}

export function ThemeToggle() {
  const [theme, setTheme] = useState(() => currentTheme());
  const next = theme === 'dark' ? 'light' : 'dark';
  return (
    <Button
      variant="ghost"
      size="icon"
      aria-label={`Switch to ${next} theme`}
      onClick={() => setTheme(toggleTheme())}
    >
      {theme === 'dark' ? <Sun className="size-4" /> : <MoonStar className="size-4" />}
    </Button>
  );
}

/** "What version are you running?" - answerable from any screenshot (maturity review, hole 4). */
export function ApiVersion() {
  // one read per tab: the version changes on deploy, and a deploy reloads the tab
  const store = persisted<{ version?: string }>('healthz');
  const stored = store.read();
  const { data, isFetched } = useQuery({
    queryKey: ['healthz'],
    queryFn: ({ signal }) => api.get('/healthz', { signal }),
    staleTime: Infinity,
    initialData: stored?.data,
    initialDataUpdatedAt: stored?.at,
  });
  useEffect(() => {
    if (data && isFetched) store.write(data);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [data, isFetched]);
  if (!data?.version) return null;
  // muted at full opacity and 12px: the 10px/70% version of this failed contrast on every page (axe)
  return <span className="block truncate text-xs text-muted-foreground">{data.version}</span>;
}

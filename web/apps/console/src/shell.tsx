import { useAlertPings } from './lib/alert-pings';
import { Button, Card, CardDescription, CardHeader, CardTitle, SidebarInset, SidebarProvider, Toaster, TooltipProvider } from '@locintel/ui';
import { useRouterState } from '@tanstack/react-router';
import { type CSSProperties, type ReactNode } from 'react';
import { useMe } from './session';
import { ScopeProvider } from './app/scope';
import { useTheme } from './app/theme';
import { CreateOrgScreen, SignInScreen } from './app/shell/auth-screens';
import { ImpersonationBanner } from './app/shell/impersonation-banner';
import { isScopedPage } from './app/shell/nav';
import { TabBar } from './app/shell/phone';
import { AppSidebar } from './app/shell/rail';
import { Topbar } from './app/shell/topbar';

/**
 * Direction B (2026-09-05): a 76px icon rail for the areas, a 240px inner
 * panel that holds SCOPE - the hierarchy node the whole console reads under -
 * and the page. Scope is chosen once here and every list asks under it
 * (`under=`, ADR 49); the server's gate 3 still filters on top. On a phone the
 * rail becomes a bottom tab bar (four areas plus More) and the scope panel
 * becomes a chip that opens a sheet.
 */
export function Shell({ children }: { children: ReactNode }) {
  const theme = useTheme();
  useAlertPings();
  const { data: me, isLoading, error, refetch } = useMe();
  const path = useRouterState({ select: (s) => s.location.pathname });
  const scoped = isScopedPage(path);

  if (isLoading) return <div className="p-12 text-muted-foreground">Loading session…</div>;

  if (error) return (
    <main className="p-12 space-y-4">
      <p role="alert">Could not verify your session. {error.message}</p>
      <Button onClick={() => { void refetch(); }}>Retry session verification</Button>
    </main>
  );

  if (me?.tier !== 'user') {
    return <SignInScreen />;
  }

  if (me.organizations.length === 0) {
    return <CreateOrgScreen />;
  }

  const activeOrg = me.organizations.find((o) => o.id === me.activeOrg);
  if (activeOrg && (activeOrg as { status?: string }).status === 'Suspended') {
    return (
      <main className="flex min-h-screen items-center justify-center bg-background p-4">
        <Card className="w-full max-w-sm text-center">
          <CardHeader>
            <CardTitle role="heading" aria-level={1}>{activeOrg.name} is suspended</CardTitle>
            <CardDescription>Contact support to restore access. Your data is retained.</CardDescription>
          </CardHeader>
        </Card>
      </main>
    );
  }

  return (
    <TooltipProvider>
      <ScopeProvider orgId={me.activeOrg ?? 'none'}>
        {/* shadcn Sidebar (the app-shell block's layout): the sidebar is fixed
            and scrolls itself, the page scrolls on its own, Cmd/Ctrl-B folds
            the Scope panel away to the icon rail */}
        <SidebarProvider
          className="bg-background"
          style={{ '--sidebar-width': scoped ? '316px' : '77px', '--sidebar-width-icon': '76px' } as CSSProperties}
        >
          <AppSidebar me={me} scoped={scoped} />
          <SidebarInset className="min-w-0">
            {me.impersonationExpiresAt && (
              <ImpersonationBanner
                orgName={activeOrg?.name ?? 'organization'}
                expiresAt={me.impersonationExpiresAt}
              />
            )}
            <Topbar me={me} scoped={scoped} orgName={activeOrg?.name ?? 'No organization'} />
            {/* SidebarInset IS the page's <main>; this is only its padding */}
            <div className="min-w-0 flex-1 p-4 pb-24 md:p-8 md:pb-8">{children}</div>
          </SidebarInset>
          <TabBar me={me} />
          <Toaster theme={theme} />
        </SidebarProvider>
      </ScopeProvider>
    </TooltipProvider>
  );
}

export { StatusBadge } from './components/status-badge';

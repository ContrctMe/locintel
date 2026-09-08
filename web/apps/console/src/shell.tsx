import { api } from '@locintel/api';
import { Badge, Button, cn, Input, Label, Toaster } from '@locintel/ui';
import { useQuery } from '@tanstack/react-query';
import { Link, useRouterState } from '@tanstack/react-router';
import { useState, type ReactNode } from 'react';
import { useAlertPings } from './lib/alert-pings';
import { can, parseMe, useMe } from './session';
import { useSessionTransition } from './app/session-boundary';

// grouped by rhythm of use: daily operations, then org administration,
// then the operator wall (UX review P2)
const NAV_GROUPS = [
  {
    label: null,
    items: [
      { to: '/', label: 'Dashboard', capability: null },
      { to: '/sites', label: 'Sites', capability: 'sites:read' },
      { to: '/hierarchy', label: 'Hierarchy', capability: 'hierarchy:manage' },
      { to: '/alerts', label: 'Alerts', capability: 'alerts:read' },
      { to: '/incidents', label: 'Incidents', capability: 'incidents:read' },
      { to: '/analytics', label: 'Analytics', capability: 'incidents:read' },
      { to: '/cases', label: 'Cases', capability: 'cases:read' },
      { to: '/entities', label: 'People & vehicles', capability: 'entities:read' },
      { to: '/checklists', label: 'Checklists', capability: 'checklists:complete' },
      { to: '/patrols', label: 'Patrols', capability: 'patrols:read' },
      { to: '/network', label: 'Network', capability: 'network:read' },
      { to: '/marketplace', label: 'Marketplace', capability: 'marketplace:read' },
      { to: '/vendor', label: 'Vendor portal', capability: 'vendor:fulfill' },
      { to: '/files', label: 'Files', capability: 'files:read' },
      { to: '/ingest', label: 'Ingest', capability: 'ingest:manage' },
    ],
  },
  {
    label: 'Administer',
    items: [
      { to: '/members', label: 'Members', capability: 'roles:manage' },
      { to: '/roles', label: 'Roles', capability: 'roles:manage' },
      { to: '/developers', label: 'Developers', capability: 'org:manage' },
      { to: '/settings', label: 'Settings', capability: 'org:manage' },
      { to: '/audit', label: 'Audit', capability: 'audit:read' },
    ],
  },
  {
    label: 'Platform',
    items: [{ to: '/operator', label: 'Operator', capability: 'platform:operate' }],
  },
] as const;

export function Shell({ children }: { children: ReactNode }) {
  useAlertPings();
  const { data: me, isLoading, error, refetch } = useMe();
  const changeSession = useSessionTransition();
  const path = useRouterState({ select: (s) => s.location.pathname });
  const [navOpen, setNavOpen] = useState(false);

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
      <main className="flex min-h-screen items-center justify-center bg-background">
        <div className="w-full max-w-sm space-y-3 rounded-lg border bg-card p-8 text-center">
          <h1 className="text-xl font-semibold">{activeOrg.name} is suspended</h1>
          <p className="text-sm text-muted-foreground">
            Contact support to restore access. Your data is retained.
          </p>
        </div>
      </main>
    );
  }
  return (
    <div className="flex min-h-screen flex-col bg-background">
      {me.impersonationExpiresAt && (
        <ImpersonationBanner
          orgName={activeOrg?.name ?? 'organization'}
          expiresAt={me.impersonationExpiresAt}
        />
      )}
      <div className="flex flex-1">
      {/* the sidebar content renders twice: a fixed rail on md+, a drawer below */}
      <aside className="hidden w-56 flex-col border-r bg-card md:flex">
        <div className="border-b p-4">
          <div className="font-semibold">LocIntel</div>
          <div className="text-xs text-muted-foreground">{activeOrg?.name ?? 'No organization'}</div>
        </div>
        <nav className="flex-1 space-y-4 p-2">
          {NAV_GROUPS.map((group) => {
            const items = group.items.filter(
              (n) => n.capability === null || can(me, n.capability),
            );
            if (items.length === 0) return null;
            return (
              <div key={group.label ?? 'operate'} className="space-y-1">
                {group.label && (
                  <div className="px-3 pt-1 text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                    {group.label}
                  </div>
                )}
                {items.map((n) => (
                  <Link
                    key={n.to}
                    to={n.to}
                    onClick={() => setNavOpen(false)}
                    className={cn(
                      'block rounded-md px-3 py-2 text-sm hover:bg-accent',
                      path === n.to && 'bg-accent font-medium',
                    )}
                  >
                    {n.label}
                  </Link>
                ))}
              </div>
            );
          })}
        </nav>
        <div className="space-y-2 border-t p-3">
          {me.organizations.length > 1 && (
            <select
              aria-label="Active organization"
              className="w-full rounded-md border bg-background px-2 py-1.5 text-sm"
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
            </select>
          )}
          <div className="flex items-center justify-between gap-2">
            <span className="min-w-0">
              <Link
                to="/account"
                className="truncate text-xs text-muted-foreground hover:text-foreground hover:underline"
              >
                {me.email}
              </Link>
              <ApiVersion />
            </span>
            <Button
              variant="ghost"
              size="sm"
              onClick={async () => {
                await changeSession(() => api.post('/auth/logout'));
              }}
            >
              Sign out
            </Button>
          </div>
        </div>
      </aside>
      {navOpen && (
        <div className="fixed inset-0 z-50 flex md:hidden">
          <div
            className="absolute inset-0 bg-black/40"
            onClick={() => setNavOpen(false)}
            aria-hidden
          />
          <aside className="relative flex w-64 flex-col overflow-y-auto border-r bg-card">
        <div className="border-b p-4">
          <div className="font-semibold">LocIntel</div>
          <div className="text-xs text-muted-foreground">{activeOrg?.name ?? 'No organization'}</div>
        </div>
        <nav className="flex-1 space-y-4 p-2">
          {NAV_GROUPS.map((group) => {
            const items = group.items.filter(
              (n) => n.capability === null || can(me, n.capability),
            );
            if (items.length === 0) return null;
            return (
              <div key={group.label ?? 'operate'} className="space-y-1">
                {group.label && (
                  <div className="px-3 pt-1 text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                    {group.label}
                  </div>
                )}
                {items.map((n) => (
                  <Link
                    key={n.to}
                    to={n.to}
                    onClick={() => setNavOpen(false)}
                    className={cn(
                      'block rounded-md px-3 py-2 text-sm hover:bg-accent',
                      path === n.to && 'bg-accent font-medium',
                    )}
                  >
                    {n.label}
                  </Link>
                ))}
              </div>
            );
          })}
        </nav>
        <div className="space-y-2 border-t p-3">
          {me.organizations.length > 1 && (
            <select
              aria-label="Active organization"
              className="w-full rounded-md border bg-background px-2 py-1.5 text-sm"
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
            </select>
          )}
          <div className="flex items-center justify-between gap-2">
            <span className="min-w-0">
              <Link
                to="/account"
                className="truncate text-xs text-muted-foreground hover:text-foreground hover:underline"
              >
                {me.email}
              </Link>
              <ApiVersion />
            </span>
            <Button
              variant="ghost"
              size="sm"
              onClick={async () => {
                await changeSession(() => api.post('/auth/logout'));
              }}
            >
              Sign out
            </Button>
          </div>
        </div>
          </aside>
        </div>
      )}
      <main className="min-w-0 flex-1 overflow-auto p-4 md:p-8">
        <div className="mb-4 flex items-center gap-3 md:hidden">
          <Button variant="outline" size="sm" aria-label="Open navigation"
            onClick={() => setNavOpen(true)}>
            ☰
          </Button>
          <div>
            <span className="font-semibold">LocIntel</span>
            <span className="ml-2 text-xs text-muted-foreground">{activeOrg?.name}</span>
          </div>
        </div>
        {children}
      </main>
      </div>
      <Toaster />
    </div>
  );
}

/** "What version are you running?" - answerable from any screenshot (maturity review, hole 4). */
function ApiVersion() {
  const { data } = useQuery({
    queryKey: ['healthz'],
    queryFn: ({ signal }) => api.get('/healthz', { signal }),
    staleTime: Infinity,
  });
  if (!data?.version) return null;
  // muted at full opacity and 12px: the 10px/70% version of this failed contrast on every page (axe)
  return <span className="block truncate text-xs text-muted-foreground">{data.version}</span>;
}

function ImpersonationBanner({ orgName, expiresAt }: { orgName: string; expiresAt: string }) {
  const changeSession = useSessionTransition();
  const ends = new Date(expiresAt).toLocaleTimeString(undefined, {
    hour: 'numeric',
    minute: '2-digit',
  });
  return (
    <div className="flex items-center justify-between gap-3 bg-warning px-4 py-2 text-sm text-warning-foreground">
      <span>
        <span className="font-semibold">Support session:</span> impersonating {orgName} · ends{' '}
        {ends}
      </span>
      <Button
        variant="outline"
        size="sm"
        onClick={async () => {
          await changeSession(() => api.post('/auth/impersonation/stop'));
        }}
      >
        Stop impersonating
      </Button>
    </div>
  );
}

function SignInScreen() {
  const authError = new URLSearchParams(location.search).get('authError');
  const [signupEmail, setSignupEmail] = useState<string | null>(null);
  return (
    <main className="flex min-h-screen items-center justify-center bg-background">
      <div className="w-full max-w-sm space-y-4 rounded-lg border bg-card p-8 text-center">
        <h1 className="text-xl font-semibold">LocIntel Console</h1>
        <p className="text-sm text-muted-foreground">Sign in to manage your organization.</p>
        {authError && (
          <p className="rounded-md bg-destructive/10 px-3 py-2 text-sm text-destructive">
            {authError === 'user_not_found'
              ? 'No account for that email yet — use Create account below.'
              : `Sign-in didn't complete (${authError.replaceAll('_', ' ')}). Try again.`}
          </p>
        )}
        <Button asChild className="w-full">
          <a href={`/auth/login?returnUrl=${encodeURIComponent(location.pathname)}`}>Sign in</a>
        </Button>
        {signupEmail === null ? (
          <button
            type="button"
            className="text-sm text-muted-foreground underline-offset-4 hover:underline"
            onClick={() => setSignupEmail('')}
          >
            Create account
          </button>
        ) : (
          <form method="post" action="/auth/signup" className="space-y-2 text-left">
            <Label htmlFor="signup-email">Email for your new account</Label>
            <Input
              id="signup-email"
              name="email"
              type="email"
              required
              maxLength={320}
              value={signupEmail}
              onChange={(e) => setSignupEmail(e.target.value)}
            />
            <Button type="submit" className="w-full" variant="secondary" disabled={!signupEmail.trim()}>
              Create account
            </Button>
          </form>
        )}
      </div>
    </main>
  );
}

function CreateOrgScreen() {
  const changeSession = useSessionTransition();
  const signOut = async () => {
    await changeSession(() => api.post('/auth/logout'));
  };
  const [name, setName] = useState('');
  const [slug, setSlug] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);

  const create = async () => {
    setCreating(true);
    setError(null);
    try {
      const { orgId } = await api.post('/api/orgs', { name, slug });
      // founder membership arrives via the outbox: poll, then switch in
      for (let attempt = 0; attempt < 50; attempt++) {
        const me = parseMe(await api.get('/me'));
        if (me.tier === 'user' && me.organizations.some((o) => o.id === orgId)) break;
        await new Promise((resolve) => setTimeout(resolve, 200));
      }
      await changeSession(() => api.post('/auth/switch-org', { orgId }));
    } catch (e) {
      setError(
        String((e as { body?: { error?: string } }).body?.error ?? 'could not create organization'),
      );
      setCreating(false);
    }
  };

  return (
    <main className="flex min-h-screen items-center justify-center bg-background">
      <div className="w-full max-w-sm space-y-4 rounded-lg border bg-card p-8">
        <div>
          <h1 className="text-xl font-semibold">Create your organization</h1>
          <p className="text-sm text-muted-foreground">
            You&apos;re signed in but don&apos;t belong to an organization yet.
          </p>
        </div>
        <div className="space-y-1">
          <Label htmlFor="org-name">Organization name</Label>
          <Input
            id="org-name"
            value={name}
            onChange={(e) => {
              setName(e.target.value);
              setSlug(
                e.target.value
                  .toLowerCase()
                  .replace(/[^a-z0-9]+/g, '-')
                  .replace(/^-|-$/g, ''),
              );
            }}
          />
        </div>
        <div className="space-y-1">
          <Label htmlFor="org-slug">URL slug</Label>
          <Input id="org-slug" value={slug} onChange={(e) => setSlug(e.target.value)} />
        </div>
        <Button className="w-full" disabled={!name || slug.length < 3 || creating} onClick={create}>
          {creating ? 'Setting up…' : 'Create organization'}
        </Button>
        {error && <p className="text-sm text-destructive">{error}</p>}
        <button
          type="button"
          className="w-full text-center text-sm text-muted-foreground underline-offset-4 hover:underline"
          onClick={() => void signOut()}
        >
          Sign out
        </button>
      </div>
    </main>
  );
}

export function StatusBadge({ status }: { status: string }) {
  const variant =
    status === 'Open' || status === 'Clean' || status === 'Committed'
      ? 'success'
      : status === 'Closed' || status === 'Quarantined'
        ? 'destructive'
        : 'secondary';
  return <Badge variant={variant as never}>{status}</Badge>;
}

import { api } from '@locintel/api';
import { Button, Card, CardContent, CardHeader, CardTitle, ConfirmButton, Input,
  Label } from '@locintel/ui';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { fmtDateTime } from '../lib/format';
import { useApiMutation } from '../lib/mutation';
import { useMe } from '../session';

type Session = { id: string; userAgent: string | null; createdAt: string; current: boolean };
type Prefs = { phone: string | null; smsAlerts: boolean; smsMarketplace: boolean; smsNetwork: boolean; browserAlerts: boolean; smsAvailable: boolean };

/** Best-effort browser/OS from the UA - a label, not a fingerprint. */
function describeAgent(ua: string | null): string {
  if (!ua) return 'Unknown browser';
  const browser = /Edg\//.test(ua)
    ? 'Edge'
    : /OPR\//.test(ua)
      ? 'Opera'
      : /Firefox\//.test(ua)
        ? 'Firefox'
        : /Chrome\//.test(ua)
          ? 'Chrome'
          : /Safari\//.test(ua)
            ? 'Safari'
            : 'Browser';
  const os = /Windows/.test(ua)
    ? 'Windows'
    : /Mac OS X|Macintosh/.test(ua)
      ? 'macOS'
      : /iPhone|iPad/.test(ua)
        ? 'iOS'
        : /Android/.test(ua)
          ? 'Android'
          : /Linux/.test(ua)
            ? 'Linux'
            : null;
  return os ? `${browser} on ${os}` : browser;
}

/** The user acting on themselves: profile, credentials entry points, sessions, deletion. */
export function AccountPage() {
  const { data: me } = useMe();
  const [name, setName] = useState<string | null>(null);
  const [deleteError, setDeleteError] = useState<string | null>(null);

  const { data: sessions } = useQuery({
    queryKey: ['sessions'],
    queryFn: () => api.get<Session[]>('/auth/sessions'),
  });

  const rename = useApiMutation({
    mutationFn: (value: string) => api.put('/auth/profile', { name: value }),
    invalidate: [['me']],
    success: 'Name updated',
  });
  const passwordReset = useApiMutation({
    mutationFn: () => api.post('/auth/password-reset'),
    success: 'Reset email sent',
  });
  const revoke = useApiMutation({
    mutationFn: (id: string) => api.del(`/auth/sessions/${id}`),
    invalidate: [['sessions']],
    success: 'Session revoked',
  });
  const revokeOthers = useApiMutation({
    mutationFn: () => api.post('/auth/sessions/revoke-others'),
    invalidate: [['sessions']],
    success: 'Other sessions signed out',
  });
  // stays on useMutation semantics via wrapper except the rich 409 body:
  // last-manager refusal needs the org list, so it renders inline
  const deleteAccount = useApiMutation({
    mutationFn: async () => {
      try {
        return await api.del('/auth/account');
      } catch (e) {
        const body = (e as { body?: { code?: string; organizations?: string[] } }).body;
        setDeleteError(
          body?.code === 'last_manager'
            ? `You are the last manager of: ${(body.organizations ?? []).join(', ')}. Transfer management or offboard first.`
            : null,
        );
        throw e;
      }
    },
    errorFallback: 'Deletion failed',
    onSuccess: () => {
      location.href = '/';
    },
  });

  if (!me || me.tier !== 'user') return null;
  const draft = name ?? me.name ?? '';
  return (
    <div className="max-w-lg space-y-6">
      <h1 className="text-2xl font-semibold">Account</h1>
      <NotificationsCard />
      <Card>
        <CardHeader><CardTitle>Profile</CardTitle></CardHeader>
        <CardContent className="space-y-3">
          <div className="space-y-1">
            <Label htmlFor="account-email">Email</Label>
            <Input id="account-email" value={me.email} disabled />
          </div>
          <div className="space-y-1">
            <Label htmlFor="account-name">Name</Label>
            <Input id="account-name" value={draft} onChange={(e) => setName(e.target.value)} />
          </div>
          <Button
            disabled={draft === (me.name ?? '') || !draft.trim() || rename.isPending}
            onClick={() => rename.mutate(draft.trim())}
          >
            Save
          </Button>
        </CardContent>
      </Card>
      <Card>
        <CardHeader><CardTitle>Sign-in &amp; security</CardTitle></CardHeader>
        <CardContent className="space-y-2">
          <p className="text-sm text-muted-foreground">
            Your password and multi-factor sign-in are managed by your identity provider.
          </p>
          <Button
            variant="outline"
            disabled={passwordReset.isPending}
            onClick={() => passwordReset.mutate()}
          >
            {passwordReset.isSuccess ? 'Reset email sent' : 'Send password reset email'}
          </Button>
        </CardContent>
      </Card>
      <Card>
        <CardHeader><CardTitle>Sessions</CardTitle></CardHeader>
        <CardContent className="space-y-2">
          {sessions?.map((s) => (
            <div key={s.id} className="flex items-center justify-between gap-2 text-sm">
              <div className="min-w-0">
                <div className="truncate" title={s.userAgent ?? undefined}>
                  {describeAgent(s.userAgent)}
                </div>
                <div className="text-xs text-muted-foreground">
                  {fmtDateTime(s.createdAt)}
                  {s.current && ' · this session'}
                </div>
              </div>
              {!s.current && (
                <ConfirmButton size="sm" disabled={revoke.isPending}
                  onConfirm={() => revoke.mutate(s.id)}>
                  Revoke
                </ConfirmButton>
              )}
            </div>
          ))}
          {sessions && sessions.length > 1 && (
            <ConfirmButton variant="outline" size="sm" disabled={revokeOthers.isPending}
              onConfirm={() => revokeOthers.mutate()}>
              Sign out other sessions
            </ConfirmButton>
          )}
        </CardContent>
      </Card>
      <Card>
        <CardHeader><CardTitle className="text-destructive">Danger zone</CardTitle></CardHeader>
        <CardContent className="space-y-2">
          <p className="text-sm text-muted-foreground">
            Deleting your account removes your access everywhere and your identity provider
            record. Organizations you manage alone must be handed over or offboarded first.
          </p>
          {deleteError && <p className="text-sm text-destructive">{deleteError}</p>}
          <ConfirmButton
            variant="destructive"
            confirmLabel="Permanently delete?"
            disabled={deleteAccount.isPending}
            onConfirm={() => deleteAccount.mutate()}
          >
            Delete account
          </ConfirmButton>
        </CardContent>
      </Card>
    </div>
  );
}

/** Reachability beyond email: SMS opt-in per kind (needs the SMS transport on) and browser notifications for alerts. */
function NotificationsCard() {
  const { data: prefs } = useQuery({ queryKey: ['me', 'notifications'], queryFn: () => api.get<Prefs>('/api/me/notifications') });
  if (!prefs) return null;
  return <NotificationsForm key={JSON.stringify(prefs)} prefs={prefs} />;
}

function NotificationsForm({ prefs }: { prefs: Prefs }) {
  const [phone, setPhone] = useState(prefs.phone ?? '');
  const [smsAlerts, setSmsAlerts] = useState(prefs.smsAlerts);
  const [smsMarketplace, setSmsMarketplace] = useState(prefs.smsMarketplace);
  const [smsNetwork, setSmsNetwork] = useState(prefs.smsNetwork);
  const [browserAlerts, setBrowserAlerts] = useState(prefs.browserAlerts);
  const save = useApiMutation({
    mutationFn: async () => {
      if (browserAlerts && typeof Notification !== 'undefined' && Notification.permission === 'default') await Notification.requestPermission();
      return api.put('/api/me/notifications', { phone: phone.trim() || null, smsAlerts, smsMarketplace, smsNetwork, browserAlerts });
    },
    invalidate: [['me', 'notifications']], success: 'Notification settings saved',
  });
  const smsOn = prefs.smsAvailable;
  return (
    <Card>
      <CardHeader><CardTitle>Notifications</CardTitle></CardHeader>
      <CardContent className="space-y-3 text-sm">
        <div className="space-y-1">
          <Label htmlFor="nt-phone">Mobile number (E.164, e.g. +14155551234)</Label>
          <Input id="nt-phone" value={phone} placeholder="+1…" onChange={(e) => setPhone(e.target.value)} disabled={!smsOn} />
          {!smsOn && <p className="text-xs text-muted-foreground">SMS is not configured on this deployment; email and browser notifications still work.</p>}
        </div>
        <label className="flex items-center gap-2"><input type="checkbox" className="size-4 accent-primary" checked={smsAlerts} disabled={!smsOn} onChange={(e) => setSmsAlerts(e.target.checked)} />Text me critical incidents, bulletins, and repeat offenders</label>
        <label className="flex items-center gap-2"><input type="checkbox" className="size-4 accent-primary" checked={smsMarketplace} disabled={!smsOn} onChange={(e) => setSmsMarketplace(e.target.checked)} />Text me marketplace dispatches and updates</label>
        <label className="flex items-center gap-2"><input type="checkbox" className="size-4 accent-primary" checked={smsNetwork} disabled={!smsOn} onChange={(e) => setSmsNetwork(e.target.checked)} />Text me intelligence network activity</label>
        <label className="flex items-center gap-2"><input type="checkbox" className="size-4 accent-primary" checked={browserAlerts} onChange={(e) => setBrowserAlerts(e.target.checked)} />Show browser notifications for new alerts while the console is open</label>
        <Button size="sm" disabled={save.isPending} onClick={() => save.mutate()}>Save</Button>
      </CardContent>
    </Card>
  );
}

import { api } from '@locintel/api';
import { Alert, AlertDescription, Button, Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle, Field, FieldLabel, Input } from '@locintel/ui';
import { useState } from 'react';
import { parseMe } from '../../session';
import { useSessionTransition } from '../session-boundary';

export function SignInScreen() {
  const authError = new URLSearchParams(location.search).get('authError');
  const [signupEmail, setSignupEmail] = useState<string | null>(null);
  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-6 bg-background p-4">
      {/* the auth block's shape: a wordmark header, one card, one primary action */}
      <div className="flex items-center gap-2">
        <span className="flex size-8 items-center justify-center rounded-lg bg-foreground text-sm font-extrabold text-background">
          P
        </span>
        <span className="text-lg font-semibold">LocIntel</span>
      </div>
      <Card className="w-full max-w-sm">
        <CardHeader className="text-center">
          <CardTitle role="heading" aria-level={1}>LocIntel Console</CardTitle>
          <CardDescription>Sign in to manage your organization.</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          {authError && (
            <Alert variant="destructive">
              <AlertDescription>
                {authError === 'user_not_found'
                  ? 'No account for that email yet - use Create account below.'
                  : `Sign-in didn't complete (${authError.replaceAll('_', ' ')}). Try again.`}
              </AlertDescription>
            </Alert>
          )}
          <Button
            className="w-full"
            render={<a href={`/auth/login?returnUrl=${encodeURIComponent(location.pathname)}`} />}
          >
            Sign in
          </Button>
          {signupEmail !== null && (
            <form method="post" action="/auth/signup"><Field>
              <FieldLabel htmlFor="signup-email">Email for your new account</FieldLabel>
              <Input
                id="signup-email"
                name="email"
                required
                maxLength={320}
                type="email"
                value={signupEmail}
                onChange={(e) => setSignupEmail(e.target.value)}
              />
              <Button className="w-full" variant="secondary" type="submit" disabled={!signupEmail.trim()}>
                Create account
              </Button>
            </Field></form>
          )}
        </CardContent>
        {signupEmail === null && (
          <CardFooter className="justify-center">
            <Button variant="link" size="sm" onClick={() => setSignupEmail('')}>
              Create account
            </Button>
          </CardFooter>
        )}
      </Card>
    </main>
  );
}

export function CreateOrgScreen() {
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
      const message = String(
        (e as { body?: { error?: string } }).body?.error ?? 'could not create organization',
      );
      // a taken slug is the slug field's problem, with a free one to hand
      if (/slug/i.test(message)) {
        setSlugError(message.replace(/^slug '([^']+)' is taken$/, "'$1' is already in use."));
        setSuggestion(`${slug.replace(/-\d+$/, '')}-${Math.floor(Math.random() * 900 + 100)}`);
      } else setError(message);
      setCreating(false);
    }
  };
  const [slugError, setSlugError] = useState<string | null>(null);
  const [suggestion, setSuggestion] = useState<string | null>(null);

  return (
    <main className="flex min-h-screen items-center justify-center bg-background p-4">
      <Card className="w-full max-w-sm">
        <CardHeader>
          <CardTitle role="heading" aria-level={1}>Create your organization</CardTitle>
          <CardDescription>You&apos;re signed in but don&apos;t belong to an organization yet.</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
        <Field>
          <FieldLabel htmlFor="org-name">Organization name</FieldLabel>
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
        </Field>
        <Field data-invalid={slugError ? true : undefined}>
          <FieldLabel htmlFor="org-slug">URL slug</FieldLabel>
          <Input
            id="org-slug"
            value={slug}
            aria-invalid={slugError ? true : undefined}
            aria-describedby={slugError ? 'org-slug-error' : undefined}
            onChange={(e) => {
              setSlug(e.target.value);
              setSlugError(null);
            }}
          />
          {slugError && (
            <p id="org-slug-error" role="alert" className="text-sm text-destructive">
              {slugError}{' '}
              {suggestion && (
                <Button
                  variant="link"
                  size="sm"
                  className="h-auto px-0"
                  onClick={() => {
                    setSlug(suggestion);
                    setSlugError(null);
                  }}
                >
                  Use {suggestion}
                </Button>
              )}
            </p>
          )}
        </Field>
        <Button className="w-full" disabled={!name || slug.length < 3 || creating} onClick={create}>
          {creating ? 'Setting up…' : 'Create organization'}
        </Button>
        {error && <p role="alert" className="text-sm text-destructive">{error}</p>}
        </CardContent>
        <CardFooter className="justify-center">
          <Button variant="link" size="sm" onClick={() => void signOut()}>
            Sign out
          </Button>
        </CardFooter>
      </Card>
    </main>
  );
}

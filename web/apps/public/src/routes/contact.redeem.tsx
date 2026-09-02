import { createFileRoute, Link, redirect } from '@tanstack/react-router';
import { createServerFn } from '@tanstack/react-start';
import { getRequestHeader, setCookie } from '@tanstack/react-start/server';

/**
 * The contact-link landing: relays the token to the API and the API's
 * session cookie back onto THIS host (the browser only ever talks to the
 * public app), then sends the now-identified contact to the locator.
 */
const redeem = createServerFn({ method: 'GET' })
  .inputValidator((token: string) => token)
  .handler(async ({ data: token }) => {
    const apiBase = process.env.LOCINTEL_API ?? 'http://localhost:5293';
    const host = getRequestHeader('host');
    try {
      const response = await fetch(
        `${apiBase}/contact/redeem?token=${encodeURIComponent(token)}`,
        {
          headers: host ? { 'X-Forwarded-Host': host } : {},
          redirect: 'manual',
        },
      );
      if (response.status !== 302) {
        const body = (await response.json().catch(() => null)) as { error?: string } | null;
        return { ok: false as const, error: body?.error ?? 'this link is not valid' };
      }
      for (const raw of response.headers.getSetCookie()) {
        const pair = raw.split(';')[0] ?? '';
        const eq = pair.indexOf('=');
        if (eq <= 0) continue;
        setCookie(pair.slice(0, eq), pair.slice(eq + 1), {
          path: '/',
          httpOnly: true,
          sameSite: 'lax',
        });
      }
      return { ok: true as const };
    } catch {
      return { ok: false as const, error: 'we could not verify this link right now - try again shortly' };
    }
  });

export const Route = createFileRoute('/contact/redeem')({
  validateSearch: (search: Record<string, unknown>) => ({
    token: String(search.token ?? ''),
  }),
  loaderDeps: ({ search }) => ({ token: search.token }),
  loader: async ({ deps }) => {
    const result = await redeem({ data: deps.token });
    if (result.ok) throw redirect({ to: '/' });
    return { error: result.error ?? 'this link is not valid' };
  },
  component: RedeemError,
});

function RedeemError() {
  const result = Route.useLoaderData();
  return (
    <main className="mx-auto max-w-2xl space-y-4 px-6 py-16">
      <h1 className="text-2xl font-semibold">This link didn&apos;t work</h1>
      <p className="text-muted-foreground">{result.error}</p>
      <p className="text-sm text-muted-foreground">
        Links expire after 30 minutes and can be revoked. Ask your contact at the
        organization to send a fresh one.
      </p>
      <Link to="/" className="text-sm underline underline-offset-4">
        Browse locations
      </Link>
    </main>
  );
}

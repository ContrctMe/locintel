import { createFileRoute, Link, redirect } from '@tanstack/react-router';
import { createServerFn } from '@tanstack/react-start';
import { publicRedeem } from '../api';

const redeem = createServerFn({ method: 'GET' })
  .validator((token: unknown) => token)
  .handler(({ data }) => publicRedeem(data));

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

import { createFileRoute, Link } from '@tanstack/react-router';
import { createServerFn } from '@tanstack/react-start';
import { useState } from 'react';
import { publicApi, publicApiPost, type PublicSite } from '../api';

type TipInput = {
  siteId: string;
  category: string;
  description: string;
  occurredAt?: string;
  contact?: string;
  website?: string;
};

const CATEGORIES: { value: string; label: string }[] = [
  { value: 'Theft', label: 'Theft or shoplifting' },
  { value: 'OrganizedRetailCrime', label: 'Organized group theft' },
  { value: 'Fraud', label: 'Fraud or scam' },
  { value: 'Vandalism', label: 'Vandalism or damage' },
  { value: 'Trespass', label: 'Trespassing or loitering' },
  { value: 'Threat', label: 'Threats or harassment' },
  { value: 'Safety', label: 'Safety hazard' },
  { value: 'Other', label: 'Something else' },
];

const loadSites = createServerFn({ method: 'GET' }).handler(async () => ({
  sites: await publicApi<PublicSite[]>('/public/sites', []),
}));

const submitTip = createServerFn({ method: 'POST' })
  .inputValidator((input: TipInput) => input)
  .handler(async ({ data }) => publicApiPost<{ receipt: string }>('/public/tips', data));

export const Route = createFileRoute('/tips')({
  loader: () => loadSites(),
  component: TipsPage,
});

/** Anonymous tip intake: the guest tier's one write (blueprint: incident intake via tips). */
function TipsPage() {
  const { sites } = Route.useLoaderData();
  const [siteId, setSiteId] = useState(sites[0]?.id ?? '');
  const [category, setCategory] = useState('Theft');
  const [description, setDescription] = useState('');
  const [occurredAt, setOccurredAt] = useState('');
  const [contact, setContact] = useState('');
  const [website, setWebsite] = useState('');
  const [receipt, setReceipt] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function send() {
    setBusy(true);
    setError(null);
    const result = await submitTip({
      data: {
        siteId,
        category,
        description: description.trim(),
        occurredAt: occurredAt ? new Date(occurredAt).toISOString() : undefined,
        contact: contact.trim() || undefined,
        website,
      },
    });
    setBusy(false);
    if (result.ok && result.data) setReceipt(result.data.receipt);
    else setError((result.data as { error?: string } | undefined)?.error ?? 'We could not send that right now. Please try again.');
  }

  if (receipt) {
    return (
      <main className="mx-auto max-w-xl space-y-4 px-6 py-16">
        <h1 className="text-3xl font-semibold tracking-tight">Thank you</h1>
        <p className="text-muted-foreground">
          Your tip was received. Your receipt is <span className="font-mono font-semibold">{receipt}</span>.
          Keep it if you want to refer to this report later.
        </p>
        <Link to="/" className="text-sm underline">Back to locations</Link>
      </main>
    );
  }

  return (
    <main className="mx-auto max-w-xl space-y-6 px-6 py-12">
      <div>
        <h1 className="text-3xl font-semibold tracking-tight">Report something</h1>
        <p className="mt-2 text-muted-foreground">
          Saw something at one of our locations? Tell us here. You do not have to identify yourself.
          If someone is in danger right now, call your local emergency number first.
        </p>
      </div>
      <form
        className="space-y-4"
        onSubmit={(e) => {
          e.preventDefault();
          void send();
        }}
      >
        <label className="block space-y-1 text-sm">
          <span className="font-medium">Location</span>
          <select className="w-full rounded-md border bg-background px-3 py-2" value={siteId} onChange={(e) => setSiteId(e.target.value)} required>
            {sites.length === 0 && <option value="">No locations listed</option>}
            {sites.map((s) => (
              <option key={s.id} value={s.id}>{s.name}{s.city ? ` · ${s.city}` : ''}</option>
            ))}
          </select>
        </label>
        <label className="block space-y-1 text-sm">
          <span className="font-medium">What kind of thing</span>
          <select className="w-full rounded-md border bg-background px-3 py-2" value={category} onChange={(e) => setCategory(e.target.value)}>
            {CATEGORIES.map((c) => <option key={c.value} value={c.value}>{c.label}</option>)}
          </select>
        </label>
        <label className="block space-y-1 text-sm">
          <span className="font-medium">What happened</span>
          <textarea className="min-h-32 w-full rounded-md border bg-background px-3 py-2" value={description} maxLength={4000} required minLength={10}
            placeholder="Who, what, where in the store, any descriptions or plates you noticed."
            onChange={(e) => setDescription(e.target.value)} />
        </label>
        <label className="block space-y-1 text-sm">
          <span className="font-medium">When (optional)</span>
          <input type="datetime-local" className="w-full rounded-md border bg-background px-3 py-2" value={occurredAt} onChange={(e) => setOccurredAt(e.target.value)} />
        </label>
        <label className="block space-y-1 text-sm">
          <span className="font-medium">How to reach you (optional)</span>
          <input className="w-full rounded-md border bg-background px-3 py-2" value={contact} maxLength={320} placeholder="Email or phone, only if you want a follow-up"
            onChange={(e) => setContact(e.target.value)} />
        </label>
        {/* honeypot: hidden from people, filled by bots */}
        <div className="hidden" aria-hidden="true">
          <label>Website<input tabIndex={-1} autoComplete="off" value={website} onChange={(e) => setWebsite(e.target.value)} /></label>
        </div>
        {error && <p className="text-sm text-red-600">{error}</p>}
        <button type="submit" disabled={busy || !siteId || description.trim().length < 10}
          className="rounded-md bg-primary px-4 py-2 text-sm font-medium text-primary-foreground disabled:opacity-50">
          {busy ? 'Sending…' : 'Send tip'}
        </button>
      </form>
    </main>
  );
}

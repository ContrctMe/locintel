import { getRequest, getRequestHeader, setResponseHeader } from '@tanstack/react-start/server';
import { fetchWithHost } from './upstream';

/** Revoke upstream before relaying cookie deletions to this public host. */
export async function publicSignOut(): Promise<{ ok: boolean; error?: string }> {
  const cookie = getRequestHeader('cookie');
  const host = getRequestHeader('host');
  try {
    const response = await fetchWithHost(`${process.env.LOCINTEL_API ?? 'http://localhost:5293'}/auth/logout`, {
      method: 'POST',
      signal: AbortSignal.any([getRequest().signal, AbortSignal.timeout(30_000)]),
      headers: { ...(host ? { Host: host } : {}), ...(cookie ? { cookie } : {}) },
      redirect: 'manual',
    });
    if (response.status !== 204) throw new Error('Logout not confirmed');
    relaySessionCookies(response.headers);
  } catch {
    // A lost response is not proof of revocation, nor of local cookie removal.
    return { ok: false, error: 'We could not confirm sign-out. Your session may still be active. Please try again.' };
  }
  return { ok: true };
}

/** Only the API's host-only session cookies cross this relay; preserve all attributes/chunks. */
function relaySessionCookies(headers: Headers) {
  const cookies = headers.getSetCookie().filter((raw) => /^locintel_session(?:C\d+)?=/.test(raw));
  if (!cookies.some((raw) => raw.startsWith('locintel_session='))) throw new Error('Session cookie missing');
  setResponseHeader('Set-Cookie', cookies.map((raw) => {
    const hostOnly = raw.replace(/;\s*Domain=[^;]*/gi, '');
    return process.env.NODE_ENV === 'production' && !/;\s*Secure(?:;|$)/i.test(hostOnly)
      ? `${hostOnly}; Secure` : hostOnly;
  }));
}

export async function publicRedeem(token: unknown): Promise<{ ok: boolean; error?: string }> {
  if (typeof token !== 'string' || token.length === 0 || token.length > 8192)
    return { ok: false, error: 'this link is not valid' };
  try {
    const host = getRequestHeader('host');
    const response = await fetchWithHost(
      `${process.env.LOCINTEL_API ?? 'http://localhost:5293'}/contact/redeem?token=${encodeURIComponent(token)}`,
      { signal: AbortSignal.any([getRequest().signal, AbortSignal.timeout(30_000)]),
        headers: host ? { Host: host } : {}, redirect: 'manual' },
    );
    await response.body?.cancel();
    if (response.status !== 302) return { ok: false, error: 'this link is not valid' };
    relaySessionCookies(response.headers);
    return { ok: true };
  } catch {
    return { ok: false, error: 'we could not verify this link right now - try again shortly' };
  }
}

export function publicSite(siteId: unknown) {
  if (typeof siteId !== 'string' || !/^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(siteId))
    throw new TypeError('Invalid site ID');
  return publicApi<PublicSiteDetail | null>(`/public/sites/${encodeURIComponent(siteId)}`, null);
}

/**
 * Server-side fetch to the API, forwarding the browser's host so the guest
 * pipeline derives the org from {org-slug}.domain (ADR 7). API down or org
 * unknown degrades to empty - the public page always renders.
 */
export async function publicApi<T>(path: string, fallback: T): Promise<T> {
  const value = await publicApiMaybe<T>(path);
  return value === undefined ? fallback : value;
}

export type PublicSite = {
  id: string;
  name: string;
  city?: string;
  timeZone: string;
  status: string;
  openNow: boolean;
  lat?: number | null;
  lng?: number | null;
  distanceKm?: number | null;
};

export type PublicSiteDetail = PublicSite & {
  attributes: { key: string; label: string; value: string | number | boolean }[];
  addressLine1?: string;
  postalCode?: string;
  countryCode?: string;
  windows: { startsAtUtc: string; endsAtUtc: string; localDate: string }[];
  closures: string[];
};

/**
 * Like publicApi, but undefined when the API is unreachable or refuses -
 * callers that must tell "org has nothing" from "backend is down" use this.
 */
export async function publicApiMaybe<T>(path: string, budget?: AbortSignal): Promise<T | undefined> {
  const apiBase = process.env.LOCINTEL_API ?? 'http://localhost:5293';
  try {
    const host = getRequestHeader('host');
    const cookie = getRequestHeader('cookie');
    const signal = AbortSignal.any([getRequest().signal, budget ?? AbortSignal.timeout(30_000)]);
    signal.throwIfAborted();
    const response = await fetchWithHost(`${apiBase}${path}`, {
      signal,
      headers: {
        ...(host ? { Host: host } : {}),
        ...(cookie ? { cookie } : {}),
      },
    });
    if (!response.ok) {
      await response.body?.cancel();
      return undefined;
    }
    return (await response.json()) as T;
  } catch {
    return undefined;
  }
}

export type PublicMe = { tier: string; email?: string };

/** A page of the locator: the sites and the cursor for the next page (null on a nearest-first page and at the end). */
export type PublicSiteList = { items: PublicSite[]; next: string | null };

const locatorPath = (near?: string, after?: string) => {
  const query = new URLSearchParams();
  if (near) query.set('near', near);
  if (after) query.set('after', after);
  const text = query.toString();
  return text ? `/public/sites?${text}` : '/public/sites';
};

/** Independent locator reads share a 30-second concurrent upstream budget. */
export async function publicLocator(near?: string) {
  const [page, me] = await Promise.all([
    publicApiMaybe<PublicSiteList>(locatorPath(near)),
    publicApi<PublicMe>('/me', { tier: 'guest' }),
  ]);
  return { sites: page?.items, next: page?.next ?? null, me };
}

/** The next page of the alphabetical list; empty when the API is away. */
export const publicLocatorPage = (after: string) =>
  publicApi<PublicSiteList>(locatorPath(undefined, after), { items: [], next: null });

/** Bounded sitemap work: one request deadline, at most 250 pages, no partial cacheable success. */
export async function publicSitesAll(): Promise<PublicSite[]> {
  const signal = AbortSignal.any([getRequest().signal, AbortSignal.timeout(30_000)]);
  const all: PublicSite[] = [];
  let after: string | undefined;
  const seen = new Set<string>();
  for (let pageNumber = 0; pageNumber < 250; pageNumber++) {
    signal.throwIfAborted();
    const page = await publicApiMaybe<PublicSiteList>(
      `${locatorPath(undefined, after)}${after ? '&' : '?'}limit=200`, signal,
    );
    if (!page || !Array.isArray(page.items) || page.items.length > 200)
      throw new Error('Sitemap unavailable');
    all.push(...page.items);
    if (!page.next) return all;
    if (seen.has(page.next)) throw new Error('Repeated sitemap cursor');
    seen.add(page.next);
    after = page.next;
  }
  throw new Error('Sitemap page budget exceeded');
}

export type PublicOrg = { name: string; slug: string; brandColor?: string | null };

/**
 * Server-side POST to the API on the visitor's behalf (anonymous tips):
 * host forwarded so the guest pipeline derives the org; the body is the
 * visitor's, the response is the API's status and JSON.
 */
export async function publicApiPost<T>(
  path: string,
  body: unknown,
): Promise<{ ok: boolean; status: number; data: T | undefined }> {
  const apiBase = process.env.LOCINTEL_API ?? 'http://localhost:5293';
  try {
    const host = getRequestHeader('host');
    const response = await fetch(`${apiBase}${path}`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...(host ? { 'X-Forwarded-Host': host } : {}),
      },
      body: JSON.stringify(body),
    });
    const text = await response.text();
    return {
      ok: response.ok,
      status: response.status,
      data: text ? (JSON.parse(text) as T) : undefined,
    };
  } catch {
    return { ok: false, status: 0, data: undefined };
  }
}

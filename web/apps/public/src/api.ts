import { getRequestHeader } from '@tanstack/react-start/server';

/**
 * Server-side fetch to the API, forwarding the browser's host so the guest
 * pipeline derives the org from {org-slug}.domain (ADR 7). API down or org
 * unknown degrades to empty - the public page always renders.
 */
export async function publicApi<T>(path: string, fallback: T): Promise<T> {
  const apiBase = process.env.LOCINTEL_API ?? 'http://localhost:5293';
  try {
    const host = getRequestHeader('host');
    // the browser's cookie rides along: an identified contact stays
    // identified through the SSR hop
    const cookie = getRequestHeader('cookie');
    const response = await fetch(`${apiBase}${path}`, {
      headers: {
        ...(host ? { 'X-Forwarded-Host': host } : {}),
        ...(cookie ? { cookie } : {}),
      },
    });
    if (!response.ok) return fallback;
    return (await response.json()) as T;
  } catch {
    return fallback;
  }
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
export async function publicApiMaybe<T>(path: string): Promise<T | undefined> {
  const apiBase = process.env.LOCINTEL_API ?? 'http://localhost:5293';
  try {
    const host = getRequestHeader('host');
    const cookie = getRequestHeader('cookie');
    const response = await fetch(`${apiBase}${path}`, {
      headers: {
        ...(host ? { 'X-Forwarded-Host': host } : {}),
        ...(cookie ? { cookie } : {}),
      },
    });
    if (!response.ok) return undefined;
    return (await response.json()) as T;
  } catch {
    return undefined;
  }
}

export type PublicMe = { tier: string; email?: string };

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

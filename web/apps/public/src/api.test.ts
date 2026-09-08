import { afterEach, expect, it, vi } from 'vitest';
import { publicApi, publicApiMaybe, publicSignOut, publicLocator, publicRedeem, publicSite, publicSitesAll } from './api';
import { setResponseHeader } from '@tanstack/react-start/server';

const requestContext = vi.hoisted(() => ({ controller: new AbortController() }));

vi.mock('./upstream', () => ({ fetchWithHost: (...args: Parameters<typeof fetch>) => fetch(...args) }));

vi.mock('@tanstack/react-start/server', () => ({
  getRequestHeader: (name: string) => name === 'host' ? 'tenant.example.test' : 'test-cookie',
  getRequest: () => ({ signal: requestContext.controller.signal }),
  setResponseHeader: vi.fn(),
}));

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
  vi.clearAllMocks();
  vi.unstubAllEnvs();
  requestContext.controller = new AbortController();
});

it.each(['network', 'http', 'missing-cookie'])('does not report logout success after %s failure', async (failure) => {
  const fetchMock = failure === 'network'
    ? vi.fn().mockRejectedValue(new TypeError('offline'))
    : vi.fn().mockResolvedValue(new Response(null, { status: failure === 'http' ? 503 : 204 }));
  vi.stubGlobal('fetch', fetchMock);
  expect(await publicSignOut()).toMatchObject({ ok: false, error: expect.stringContaining('could not confirm') });
  expect(setResponseHeader).not.toHaveBeenCalled();
});

it('revokes with the current cookie and relays successful logout deletions', async () => {
  const fetchMock = vi.fn().mockResolvedValue(new Response(null, {
    status: 204, headers: { 'Set-Cookie': 'locintel_session=; Path=/; Max-Age=0' },
  }));
  vi.stubGlobal('fetch', fetchMock);
  expect(await publicSignOut()).toEqual({ ok: true });
  expect(fetchMock).toHaveBeenCalledWith(expect.stringContaining('/auth/logout'), expect.objectContaining({
    method: 'POST', headers: { Host: 'tenant.example.test', cookie: 'test-cookie' }, signal: expect.any(AbortSignal),
  }));
  expect(setResponseHeader).toHaveBeenCalledWith('Set-Cookie', ['locintel_session=; Path=/; Max-Age=0']);
});

it.each([false, true])('bounds public response bodies and preserves fallback semantics: %s', async (maybe) => {
  const deadline = new AbortController();
  const timeout = vi.spyOn(AbortSignal, 'timeout').mockReturnValue(deadline.signal);
  const body = new ReadableStream({
    start(controller) {
      deadline.signal.addEventListener('abort', () => controller.error(deadline.signal.reason));
    },
  });
  const fetchMock = vi.fn().mockResolvedValue(new Response(body));
  vi.stubGlobal('fetch', fetchMock);
  const request = maybe ? publicApiMaybe('/public/sites') : publicApi('/public/sites', []);
  await Promise.resolve();
  deadline.abort(new DOMException('deadline', 'TimeoutError'));
  expect(await request).toEqual(maybe ? undefined : []);
  expect(timeout).toHaveBeenCalledWith(30_000);
  expect(fetchMock.mock.calls[0]?.[1]?.signal.aborted).toBe(true);
});

it('forwards host and cookie and returns a successful response', async () => {
  const fetchMock = vi.fn().mockResolvedValue(Response.json([{ id: 'site' }]));
  vi.stubGlobal('fetch', fetchMock);
  expect(await publicApi('/public/sites', [])).toEqual([{ id: 'site' }]);
  expect(fetchMock.mock.calls[0]?.[1]).toMatchObject({
    headers: { Host: 'tenant.example.test', cookie: 'test-cookie' },
  });
});

it('starts both locator reads before either completes and preserves unavailable locations', async () => {
  const pending: ((response: Response) => void)[] = [];
  const fetchMock = vi.fn((_input: RequestInfo | URL) => new Promise<Response>((resolve) => pending.push(resolve)));
  vi.stubGlobal('fetch', fetchMock);
  const result = publicLocator('1,2');
  expect(fetchMock).toHaveBeenCalledTimes(2);
  expect(fetchMock.mock.calls[0]?.[0]).toContain('/public/sites?near=1%2C2');
  pending[0]!(new Response(null, { status: 503 }));
  pending[1]!(Response.json({ tier: 'contact', email: 'visitor@example.test' }));
  expect(await result).toEqual({ sites: undefined, next: null, me: { tier: 'contact', email: 'visitor@example.test' } });
});


it.each(['../../api/settings', '%2e%2e/%2e%2e/api/hierarchy', '../org', 'abc?x=1', 'abc#x', {}, null])(
  'rejects non-UUID site identifiers before contacting the API: %s', (value) => {
    const fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);
    expect(() => publicSite(value)).toThrow('Invalid site ID');
    expect(fetchMock).not.toHaveBeenCalled();
  },
);

it('requests the exact public site route for a valid UUID', async () => {
  const fetchMock = vi.fn().mockResolvedValue(Response.json({ id: '01990000-0000-7000-8000-000000000001' }));
  vi.stubGlobal('fetch', fetchMock);
  await publicSite('01990000-0000-7000-8000-000000000001');
  expect(fetchMock.mock.calls[0]?.[0]).toBe('http://localhost:5293/public/sites/01990000-0000-7000-8000-000000000001');
});

it.each(['development', 'production'])('preserves cookie attributes and session chunks on redemption: %s', async (environment) => {
  vi.stubEnv('NODE_ENV', environment);
  const headers = new Headers();
  headers.append('Set-Cookie', 'locintel_session=chunks-1; Path=/; Secure; HttpOnly; SameSite=Lax; Expires=Fri, 01 Jan 2038 00:00:00 GMT');
  headers.append('Set-Cookie', 'locintel_sessionC1=opaque; Path=/; Secure; HttpOnly; SameSite=Lax');
  headers.append('Set-Cookie', 'unrelated=blocked; Path=/');
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 302, headers })));
  expect(await publicRedeem('synthetic-token')).toEqual({ ok: true });
  expect(setResponseHeader).toHaveBeenCalledWith('Set-Cookie', headers.getSetCookie().slice(0, 2));
});

it.each(['development', 'production'])('keeps sessions host-only with an explicit production Secure floor: %s', async (environment) => {
  vi.stubEnv('NODE_ENV', environment);
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, {
    status: 302, headers: { 'Set-Cookie': 'locintel_session=opaque; Path=/; HttpOnly; SameSite=Lax; Domain=api.example.test' },
  })));
  expect(await publicRedeem('synthetic-token')).toEqual({ ok: true });
  expect(setResponseHeader).toHaveBeenCalledWith('Set-Cookie', [
    `locintel_session=opaque; Path=/; HttpOnly; SameSite=Lax${environment === 'production' ? '; Secure' : ''}`,
  ]);
});

it('does not confirm redemption when the session cookie is missing', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 302 })));
  expect(await publicRedeem('synthetic-token')).toMatchObject({ ok: false });
  expect(setResponseHeader).not.toHaveBeenCalled();
});

it('shares one deadline across every sitemap page and stops when it expires', async () => {
  const deadline = new AbortController();
  vi.spyOn(AbortSignal, 'timeout').mockReturnValue(deadline.signal);
  let calls = 0;
  vi.stubGlobal('fetch', vi.fn(async () => {
    calls++;
    if (calls === 2) deadline.abort(new DOMException('deadline', 'TimeoutError'));
    return Response.json({ items: [], next: `cursor-${calls}` });
  }));
  await expect(publicSitesAll()).rejects.toThrow();
  expect(calls).toBe(2);
  expect(AbortSignal.timeout).toHaveBeenCalledTimes(1);
});

it('refuses repeated sitemap cursors and unavailable pages instead of returning partial success', async () => {
  const fetchMock = vi.fn().mockImplementation(async () => Response.json({ items: [], next: 'repeat' }));
  vi.stubGlobal('fetch', fetchMock);
  await expect(publicSitesAll()).rejects.toThrow('Repeated sitemap cursor');
  expect(fetchMock).toHaveBeenCalledTimes(2);
  fetchMock.mockImplementation(async () => new Response(null, { status: 503 }));
  await expect(publicSitesAll()).rejects.toThrow('Sitemap unavailable');
});

it('stops sitemap enumeration at its page budget', async () => {
  let calls = 0;
  vi.stubGlobal('fetch', vi.fn(async () => Response.json({ items: [], next: String(++calls) })));
  await expect(publicSitesAll()).rejects.toThrow('Sitemap page budget exceeded');
  expect(calls).toBe(250);
});


it('does not start upstream work after the client disconnects', async () => {
  const fetchMock = vi.fn();
  vi.stubGlobal('fetch', fetchMock);
  requestContext.controller.abort(new DOMException('client disconnected', 'AbortError'));
  await expect(publicSitesAll()).rejects.toThrow('client disconnected');
  await expect(publicApiMaybe('/public/org')).resolves.toBeUndefined();
  expect(fetchMock).not.toHaveBeenCalled();
});

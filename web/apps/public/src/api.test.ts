import { afterEach, expect, it, vi } from 'vitest';
import { publicApi, publicApiMaybe, publicSignOut, publicLocator } from './api';
import { setCookie } from '@tanstack/react-start/server';

vi.mock('@tanstack/react-start/server', () => ({
  getRequestHeader: (name: string) => name === 'host' ? 'tenant.example.test' : 'test-cookie',
  setCookie: vi.fn(),
}));

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
  vi.clearAllMocks();
});

it.each(['network', 'http', 'missing-cookie'])('does not report logout success after %s failure', async (failure) => {
  const fetchMock = failure === 'network'
    ? vi.fn().mockRejectedValue(new TypeError('offline'))
    : vi.fn().mockResolvedValue(new Response(null, { status: failure === 'http' ? 503 : 204 }));
  vi.stubGlobal('fetch', fetchMock);
  expect(await publicSignOut()).toMatchObject({ ok: false, error: expect.stringContaining('could not confirm') });
  expect(setCookie).not.toHaveBeenCalled();
});

it('revokes with the current cookie and relays successful logout deletions', async () => {
  const fetchMock = vi.fn().mockResolvedValue(new Response(null, {
    status: 204, headers: { 'Set-Cookie': 'locintel.session=; Path=/; Max-Age=0' },
  }));
  vi.stubGlobal('fetch', fetchMock);
  expect(await publicSignOut()).toEqual({ ok: true });
  expect(fetchMock).toHaveBeenCalledWith(expect.stringContaining('/auth/logout'), expect.objectContaining({
    method: 'POST', headers: { cookie: 'test-cookie' }, signal: expect.any(AbortSignal),
  }));
  expect(setCookie).toHaveBeenCalledWith('locintel.session', '', { path: '/', maxAge: 0 });
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
  expect(fetchMock.mock.calls[0]?.[1]).toMatchObject({ signal: deadline.signal });
});

it('forwards host and cookie and returns a successful response', async () => {
  const fetchMock = vi.fn().mockResolvedValue(Response.json([{ id: 'site' }]));
  vi.stubGlobal('fetch', fetchMock);
  expect(await publicApi('/public/sites', [])).toEqual([{ id: 'site' }]);
  expect(fetchMock.mock.calls[0]?.[1]).toMatchObject({
    headers: { 'X-Forwarded-Host': 'tenant.example.test', cookie: 'test-cookie' },
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
  expect(await result).toEqual({ sites: undefined, me: { tier: 'contact', email: 'visitor@example.test' } });
});

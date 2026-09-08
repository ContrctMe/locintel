import { createServer } from 'node:http';
import { afterAll, beforeAll, expect, it } from 'vitest';
import { fetchWithHost } from './upstream';

const server = createServer((request, response) => {
  if (request.url === '/oversized') {
    response.writeHead(200);
    response.end(Buffer.alloc(4 * 1024 * 1024 + 1));
    return;
  }
  if (request.url === '/slow') {
    response.writeHead(200); response.flushHeaders();
    return; // The client's abort must also terminate a pending response body.
  }
  response.writeHead(request.url === '/logout' ? 204 : 302, {
    'Set-Cookie': ['first=; Max-Age=0', 'second=; Max-Age=0'],
    'X-Observed-Host': request.headers.host!, 'X-Observed-Cookie': request.headers.cookie ?? '',
    Location: '/must-not-follow',
  });
  response.end();
});
let base: string;
beforeAll(async () => {
  await new Promise<void>(resolve => server.listen(0, '127.0.0.1', resolve));
  const address = server.address();
  if (!address || typeof address === 'string') throw new Error('TCP listener required');
  base = `http://127.0.0.1:${address.port}`;
});
afterAll(() => { server.closeAllConnections(); server.close(); });

it.each(['/logout', '/redirect'])('preserves Host, cookies, status and separate Set-Cookie headers: %s', async path => {
  const response = await fetchWithHost(base + path, { method: 'POST',
    headers: { Host: 'tenant.example.test', Cookie: 'session=test' }, signal: AbortSignal.timeout(1000) });
  expect(response.status).toBe(path === '/logout' ? 204 : 302);
  expect(response.headers.get('x-observed-host')).toBe('tenant.example.test');
  expect(response.headers.get('x-observed-cookie')).toBe('session=test');
  expect(response.headers.getSetCookie()).toEqual(['first=; Max-Age=0', 'second=; Max-Age=0']);
  await response.text();
});

it('aborts the body after headers have arrived', async () => {
  const cancellation = new AbortController();
  const response = await fetchWithHost(base + '/slow', { headers: {}, signal: cancellation.signal });
  const body = response.text();
  cancellation.abort();
  await expect(body).rejects.toThrow();
});


it('rejects a chunked response beyond the byte ceiling', async () => {
  const response = await fetchWithHost(base + '/oversized', { headers: {}, signal: AbortSignal.timeout(1000) });
  await expect(response.text()).rejects.toThrow('Upstream response exceeds 4 MiB');
});

import { expect, it } from 'vitest';
import { consoleSecurityHeaders } from './security-headers';

it('blocks inline scripts and framing in production while allowing map workers and HTTPS tile providers', () => {
  const headers = consoleSecurityHeaders();
  expect(headers['Content-Security-Policy'].split('; ')[1]).toBe("script-src 'self'");
  expect(headers['Content-Security-Policy']).toContain("worker-src 'self' blob:");
  expect(headers['Content-Security-Policy']).toContain("frame-ancestors 'none'");
  expect(headers['X-Frame-Options']).toBe('DENY');
});

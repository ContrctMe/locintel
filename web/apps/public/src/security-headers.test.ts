import { expect, it } from 'vitest';
import { publicSecurityHeaders } from './security-headers';

it('allows embedding only on the intentional embed route and requires a nonce for inline scripts', () => {
  const standard = publicSecurityHeaders('/', 'random-per-request');
  const embedded = publicSecurityHeaders('/embed', 'other-request');
  expect(standard['Content-Security-Policy']).toContain("script-src 'self' 'nonce-random-per-request'");
  expect(standard['Content-Security-Policy']).toContain("frame-ancestors 'none'");
  expect(embedded['Content-Security-Policy']).toContain('frame-ancestors https: http:');
  expect(publicSecurityHeaders('/embed/other', 'nonce')['Content-Security-Policy']).toContain("frame-ancestors 'none'");
  expect(standard['Content-Security-Policy']).not.toContain('unsafe-eval');
  expect(standard['Cache-Control']).toBe('no-store');
  expect(standard['Referrer-Policy']).toBe('no-referrer');
});

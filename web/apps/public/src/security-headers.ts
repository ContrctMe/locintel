/** /embed is deliberately frameable; every other public document is a top-level page. */
export function publicSecurityHeaders(pathname: string, nonce: string, development = false) {
  return {
    'Content-Security-Policy': [
      "default-src 'self'",
      `script-src 'self' 'nonce-${nonce}'`,
      "style-src 'self' 'unsafe-inline'",
      "img-src 'self' https: data: blob:",
      `connect-src 'self'${development ? ' ws: wss:' : ''}`,
      "object-src 'none'", "base-uri 'none'", "form-action 'self'",
      `frame-ancestors ${pathname === '/embed' ? 'https: http:' : "'none'"}`,
    ].join('; '),
    'X-Content-Type-Options': 'nosniff',
    'Referrer-Policy': 'no-referrer',
    'Permissions-Policy': 'camera=(), microphone=(), geolocation=(self)',
    'Cache-Control': 'no-store', // HTML includes the visitor's identity and a fresh script nonce.
  };
}

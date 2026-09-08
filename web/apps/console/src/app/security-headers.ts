/** The preview host sends headers; the static build also carries the script policy in HTML. */
export function consoleSecurityHeaders(development = false) {
  return {
    'Content-Security-Policy': [
      "default-src 'self'",
      `script-src 'self'${development ? " 'unsafe-inline'" : ''}`,
      "style-src 'self' 'unsafe-inline'", "img-src 'self' https: data: blob:",
      `connect-src 'self' https:${development ? ' ws: wss:' : ''}`,
      "worker-src 'self' blob:", "object-src 'none'", "base-uri 'none'",
      "form-action 'self'", "frame-ancestors 'none'",
    ].join('; '),
    'X-Content-Type-Options': 'nosniff',
    'X-Frame-Options': 'DENY',
    'Referrer-Policy': 'no-referrer',
    'Permissions-Policy': 'camera=(), microphone=(), geolocation=(self)',
  };
}

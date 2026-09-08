import { randomBytes } from 'node:crypto';
import { createStartHandler, defaultStreamHandler } from '@tanstack/react-start/server';
import { createServerEntry } from '@tanstack/react-start/server-entry';
import { publicSecurityHeaders } from './security-headers';

const fetch = createStartHandler((context) => {
  const nonce = randomBytes(18).toString('base64');
  context.router.update({ ssr: { nonce } });
  const headers = publicSecurityHeaders(new URL(context.request.url).pathname, nonce, process.env.NODE_ENV !== 'production');
  for (const [name, value] of Object.entries(headers)) context.responseHeaders.set(name, value);
  return defaultStreamHandler(context);
});

export default createServerEntry({ fetch });

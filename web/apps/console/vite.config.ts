import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

// dev proxy: the console and API share an origin so the HttpOnly session
// cookie (ADR 21) just works. Point LOCINTEL_API at the running API.
const apiTarget = process.env.LOCINTEL_API ?? 'http://localhost:5293';
const proxy = Object.fromEntries(
  // '^/me$' is a regex EXACT match: a plain '/me' prefix would swallow
  // hard navigations to /members (found by an E2E smoke - the page
  // proxied to the API and rendered a black 404)
  ['/api', '/auth', '^/me$', '/objects', '/openapi', '/contact-links', '/contact', '/billing', '/healthz'].map(
    (p) => [p, { target: apiTarget, changeOrigin: false }],
  ),
);

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: Number(process.env.PORT ?? 5173), // Aspire assigns via PORT
    strictPort: true,
    proxy,
  },
  preview: { strictPort: true, proxy },
});

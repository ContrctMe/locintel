import { createFileRoute } from '@tanstack/react-router';
import { getRequestHeader } from '@tanstack/react-start/server';
import { publicSitesAll } from '../api';

/** The one surface where SEO is table stakes: the org's locator + site pages. */
export const Route = createFileRoute('/sitemap.xml')({
  server: {
    handlers: {
      GET: async () => {
        const host = getRequestHeader('host') ?? 'localhost';
        let sites;
        try { sites = await publicSitesAll(); }
        catch { return new Response('Sitemap temporarily unavailable', { status: 503, headers: { 'Cache-Control': 'no-store' } }); }
        const urls = [
          `https://${host}/`,
          ...sites.slice(0, 49_999).map((s) => `https://${host}/sites/${s.id}`),
        ];
        const xml =
          '<?xml version="1.0" encoding="UTF-8"?>\n' +
          '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n' +
          urls.map((u) => `  <url><loc>${u.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;')}</loc></url>`).join('\n') +
          '\n</urlset>\n';
        // Upstream tenant resolution also considers the visitor's session.
        return new Response(xml, { headers: { 'Content-Type': 'application/xml', 'Cache-Control': 'private, no-store' } });
      },
    },
  },
});

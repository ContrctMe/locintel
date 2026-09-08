import { useEffect, useRef } from 'react';
import type { PublicSite } from './api';

/**
 * The locator map (ADR 43): Leaflet over OpenStreetMap tiles - no API key,
 * no vendor account, works the moment a fork deploys. Circle markers on
 * purpose: Leaflet's default icon assets fight every bundler, and a dot in
 * the brand color reads better anyway. Client-only; the effect guards SSR.
 * Forks with volume swap the tile URL for a commercial provider (OSM's usage
 * policy is fine for development and small deployments, not for heavy load).
 */
export function SiteMap({
  sites,
  onSelect,
}: {
  sites: PublicSite[];
  onSelect?: (siteId: string) => void;
}) {
  const containerRef = useRef<HTMLDivElement>(null);
  const located = sites.filter((s) => s.lat != null && s.lng != null);

  useEffect(() => {
    if (!containerRef.current || located.length === 0) return;
    let disposed = false;
    let map: import('leaflet').Map | undefined;
    void (async () => {
      const L = (await import('leaflet')).default;
      await import('leaflet/dist/leaflet.css');
      if (disposed || !containerRef.current) return;
      map = L.map(containerRef.current, { scrollWheelZoom: false });
      L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
        attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>',
      }).addTo(map);
      const markers = located.map((site) => {
        const marker = L.circleMarker([site.lat!, site.lng!], {
          radius: 9,
          weight: 2,
          color: '#8c1d54',
          fillColor: '#8c1d54',
          fillOpacity: 0.75,
        }).addTo(map!);
        const label = document.createElement('span');
        label.textContent = site.name;
        marker.bindTooltip(label);
        if (onSelect) marker.on('click', () => onSelect(site.id));
        return marker;
      });
      map.fitBounds(L.featureGroup(markers).getBounds().pad(0.25), { maxZoom: 14 });
    })();
    return () => {
      disposed = true;
      map?.remove();
    };
    // re-render only when the located set genuinely changes
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [JSON.stringify(located.map((s) => s.id)), onSelect]);

  if (located.length === 0) return null;
  return <div ref={containerRef} role="region" className="h-72 w-full rounded-lg border" aria-label="Map of locations" />;
}

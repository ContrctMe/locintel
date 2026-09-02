import { useEffect, useRef } from 'react';

export type MapPoint = { id: string; name: string; lat: number; lng: number; count: number; loss: number };

/**
 * Incident density per site (ADR 43's Leaflet-over-OSM, reused in the
 * console). One hue; radius encodes count (sqrt scale), so magnitude is
 * read from size and the tooltip, never from a rainbow. Client-only.
 */
export function IncidentMap({ points, onSelect }: { points: MapPoint[]; onSelect?: (siteId: string) => void }) {
  const containerRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!containerRef.current || points.length === 0) return;
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
      const max = Math.max(1, ...points.map((p) => p.count));
      const markers = points.map((p) => {
        const marker = L.circleMarker([p.lat, p.lng], {
          radius: 6 + 18 * Math.sqrt(p.count / max),
          weight: 2,
          color: '#ffffff',
          fillColor: '#8c1d54',
          fillOpacity: 0.55 + 0.35 * (p.count / max),
        }).addTo(map!);
        marker.bindTooltip(`${p.name}: ${p.count} incident${p.count === 1 ? '' : 's'}${p.loss > 0 ? `, $${p.loss.toLocaleString()} loss` : ''}`);
        if (onSelect) marker.on('click', () => onSelect(p.id));
        return marker;
      });
      map.fitBounds(L.featureGroup(markers).getBounds().pad(0.25), { maxZoom: 13 });
    })();
    return () => {
      disposed = true;
      map?.remove();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [JSON.stringify(points.map((p) => [p.id, p.count])), onSelect]);
  if (points.length === 0) return null;
  return <div ref={containerRef} role="region" aria-label="Map of incidents by site" className="h-80 w-full rounded-lg border" />;
}

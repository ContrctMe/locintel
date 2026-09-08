// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { expect, it, vi } from 'vitest';
import { SiteMap } from './SiteMap';

const { labels } = vi.hoisted(() => ({ labels: [] as unknown[] }));
vi.mock('leaflet', () => ({ default: {
  map: () => ({ fitBounds() {}, remove() {} }),
  tileLayer: () => ({ addTo() {} }),
  circleMarker: () => ({ addTo() { return this; }, bindTooltip(label: unknown) { labels.push(label); } }),
  featureGroup: () => ({ getBounds: () => ({ pad() { return this; } }) }),
} }));

it('renders an untrusted site name as tooltip text instead of HTML', async () => {
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  const container = document.createElement('div');
  document.body.append(container);
  const root = createRoot(container);
  const name = '<img src=x onerror="window.compromised=1">';
  try {
    await act(async () => root.render(<SiteMap sites={[{
      id: 'site', name, lat: 1, lng: 2, timeZone: 'Etc/UTC', status: 'Open', openNow: true,
    }]} />));
    await vi.waitFor(() => expect(labels).toHaveLength(1));
    expect(labels[0]).toBeInstanceOf(HTMLElement);
    const tooltip = labels[0] as HTMLElement;
    expect(tooltip.textContent).toBe(name);
    expect(tooltip.querySelector('img')).toBeNull();
  } finally {
    await act(async () => root.unmount());
    container.remove();
  }
});

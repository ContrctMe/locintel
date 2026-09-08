import { cleanup } from '@testing-library/react';
import { afterEach } from 'vitest';

/**
 * What jsdom lacks that Base UI and the virtualizer ask for on mount, and
 * the unmount between tests. Import first in a component test (with
 * `// @vitest-environment jsdom` at the top).
 */
afterEach(cleanup);
if (typeof window !== 'undefined') {
  if (!globalThis.CSS) (globalThis as { CSS?: unknown }).CSS = { escape: (v: string) => v.replace(/[^a-zA-Z0-9_-]/g, (c) => `\\${c}`) };
  else if (!CSS.escape) CSS.escape = (v: string) => v.replace(/[^a-zA-Z0-9_-]/g, (c) => `\\${c}`);
  if (!window.matchMedia)
    window.matchMedia = (query: string) =>
      ({ matches: false, media: query, onchange: null, addEventListener() {}, removeEventListener() {}, addListener() {}, removeListener() {}, dispatchEvent: () => false }) as MediaQueryList;
  if (!window.ResizeObserver)
    window.ResizeObserver = class { observe() {} unobserve() {} disconnect() {} } as unknown as typeof ResizeObserver;
  if (!Element.prototype.scrollIntoView) Element.prototype.scrollIntoView = () => {};
  // Base UI waits for a popup's animations to finish before it settles
  if (!Element.prototype.getAnimations) Element.prototype.getAnimations = () => [];
  if (!Element.prototype.animate)
    Element.prototype.animate = (() =>
      ({ finished: Promise.resolve(), cancel() {}, finish() {}, addEventListener() {}, removeEventListener() {} })) as unknown as typeof Element.prototype.animate;
}

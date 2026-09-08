// @vitest-environment jsdom
import { expect, it } from 'vitest';
import { clearPrivateCache, persisted } from './persisted';

it('removes old private hierarchy data for every account while retaining non-sensitive preferences', () => {
  sessionStorage.clear();
  persisted('hierarchy.org-a').write({ nodes: [{ name: 'Private A' }] });
  persisted('hierarchy.org-b').write({ nodes: [{ name: 'Private B' }] });
  persisted('healthz').write({ version: 'test' });
  sessionStorage.setItem('unrelated-preference', 'keep');
  clearPrivateCache();
  expect(persisted('hierarchy.org-a').read()).toBeNull();
  expect(persisted('hierarchy.org-b').read()).toBeNull();
  expect(persisted<{ version: string }>('healthz').read()?.data.version).toBe('test');
  expect(sessionStorage.getItem('unrelated-preference')).toBe('keep');
});

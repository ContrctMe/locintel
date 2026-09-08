import { describe, expect, it } from 'vitest';
import { setupSteps, type SetupFacts } from './dashboard';

const fresh: SetupFacts = {
  levels: ['Region', 'Market'],
  nodeCount: 1,
  siteCount: 0,
  memberCount: 1,
  pendingInvites: 0,
  manageHierarchy: true,
  manageSites: true,
  manageMembers: true,
};

describe('the dashboard setup steps', () => {
  it('starts with every step open for a fresh org', () => {
    const steps = setupSteps(fresh);
    expect(steps.map((s) => s.key)).toEqual(['levels', 'node', 'site', 'invite']);
    expect(steps.every((s) => !s.done)).toBe(true);
  });

  it('counts renamed levels, a node, a site, and an invite as done', () => {
    const steps = setupSteps({
      ...fresh,
      levels: ['Division', 'District'],
      nodeCount: 2,
      siteCount: 1,
      pendingInvites: 1,
    });
    expect(steps.every((s) => s.done)).toBe(true);
  });

  it('treats a node under the default levels as accepting them', () => {
    const [levels] = setupSteps({ ...fresh, nodeCount: 2 });
    expect(levels?.done).toBe(true);
  });

  it('only offers the steps the member may take', () => {
    const steps = setupSteps({ ...fresh, manageHierarchy: false, manageMembers: false });
    expect(steps.map((s) => s.key)).toEqual(['site']);
  });
});

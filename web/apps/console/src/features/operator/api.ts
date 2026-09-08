import { api } from '@locintel/api';

export const operatorApi = {
  orgs: (signal?: AbortSignal) => api.get('/api/operator/orgs', { signal }),
  transition: (orgId: string, action: 'suspend' | 'reactivate') =>
    action === 'suspend'
      ? api.post('/api/operator/orgs/{orgId}/suspend', undefined, { path: { orgId } })
      : api.post('/api/operator/orgs/{orgId}/reactivate', undefined, { path: { orgId } }),
  exportOrg: (orgId: string) =>
    api.post('/api/operator/orgs/{orgId}/export', undefined, { path: { orgId } }),
  impersonate: (orgId: string) =>
    api.post('/api/operator/orgs/{orgId}/impersonate', undefined, { path: { orgId } }),
  offboard: (orgId: string) =>
    api.post('/api/operator/orgs/{orgId}/offboard', undefined, { path: { orgId } }),
  health: (signal?: AbortSignal) => api.get('/api/operator/health', { signal }),
  suppressions: (q?: string, signal?: AbortSignal) => api.get('/api/operator/suppressions', { query: { q }, signal }),
  unsuppress: (id: string) =>
    api.del('/api/operator/suppressions/{id}', { path: { id } }),
  users: (q: string, signal?: AbortSignal) => api.get('/api/operator/users', { query: { q }, signal }),
  overview: (signal?: AbortSignal) => api.get('/api/operator/overview', { signal }),
  deadLetters: (signal?: AbortSignal) => api.get('/api/operator/dead-letters', { signal }),
  replayDeadLetter: (id: string) =>
    api.post('/api/operator/dead-letters/{id}/replay', undefined, { path: { id } }),
  discardDeadLetter: (id: string) =>
    api.del('/api/operator/dead-letters/{id}', { path: { id } }),
  entitlements: (orgId: string, signal?: AbortSignal) =>
    api.get('/api/operator/orgs/{orgId}/entitlements', { path: { orgId }, signal }),
  setEntitlement: (orgId: string, code: string, value: string) =>
    api.put('/api/operator/orgs/{orgId}/entitlements/{code}', { value }, {
      path: { orgId, code },
    }),
};

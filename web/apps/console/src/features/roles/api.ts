import { api, type components } from '@locintel/api';

type Grant = components['schemas']['GrantSpec'];

export const rolesApi = {
  list: (signal?: AbortSignal) => api.get('/api/roles', { signal }),
  members: async (signal?: AbortSignal) => (await api.get('/api/members', { query: { limit: 200 }, signal })).items,
  hierarchy: (signal?: AbortSignal) => api.get('/api/hierarchy', { signal }),
  save: (id: string | null, name: string, grants: Grant[]) =>
    id
      ? api.put('/api/roles/{id}', { name, grants }, { path: { id } })
      : api.post('/api/roles', { name, grants }),
  remove: (id: string) => api.del('/api/roles/{id}', { path: { id } }),
  assign: (roleId: string, userId: string, scopePath: string | null) =>
    api.post('/api/roles/{id}/assign', { userId, scopePath }, { path: { id: roleId } }),
  exceptions: (signal?: AbortSignal) => api.get('/api/grant-exceptions', { signal }),
  addException: (
    userId: string,
    domain: string,
    action: string,
    reason: string,
    expiresAt: string,
    scopePath: string | null,
  ) => api.post('/api/grant-exceptions', {
    userId, domain, action, reason, expiresAt, scopePath,
  }),
  removeException: (id: string) =>
    api.del('/api/grant-exceptions/{id}', { path: { id } }),
};

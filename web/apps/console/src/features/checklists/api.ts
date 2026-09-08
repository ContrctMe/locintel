import { api } from '@locintel/api';

export const checklistsApi = {
  today: (siteId: string, signal?: AbortSignal) =>
    api.get('/api/checklists/today', { query: { siteId }, signal }),
  check: (body: { siteId: string; templateId: string; itemIndex: number; done: boolean }) =>
    api.post('/api/checklists/check', body),
  templates: (signal?: AbortSignal) => api.get('/api/checklists/templates', { signal }),
  create: (body: { name: string; items: string[] }) => api.post('/api/checklists/templates', body),
  remove: (id: string) => api.del('/api/checklists/templates/{id}', { path: { id } }),
};

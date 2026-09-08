import { api, type components } from '@locintel/api';

export type FileRow = components['schemas']['FileSummary'];
export const filesApi = {
  list: (query: { offset: number; trash: boolean; siteId?: string }, signal?: AbortSignal) =>
    api.get('/api/files', { query: { ...query, limit: 50 }, signal }),
  download: (id: string) => api.get('/api/files/{id}/download', { path: { id } }),
  hold: (id: string, hold: boolean) => api.post('/api/files/{id}/hold', { hold }, { path: { id } }),
  trash: (id: string) => api.del('/api/files/{id}', { path: { id } }),
  restore: (id: string) => api.post('/api/files/{id}/restore', undefined, { path: { id } }),
};

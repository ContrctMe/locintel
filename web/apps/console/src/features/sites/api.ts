import { api, type components } from '@locintel/api';
import { parseSiteResponse } from './schema';

type CreateSite = components['schemas']['CreateSiteRequest'];
type UpdateSite = components['schemas']['UpdateSiteRequest'];
type CreateSchedule = components['schemas']['CreateScheduleRequest'];

export const sitesApi = {
  list: (limit: number, offset: number, q?: string, signal?: AbortSignal) =>
    api.get('/api/sites', { query: { limit, offset, q }, signal }),
  hierarchy: (signal?: AbortSignal) => api.get('/api/hierarchy', { signal }),
  create: (body: CreateSite) => api.post('/api/sites', body),
  get: async (id: string, signal?: AbortSignal) =>
    parseSiteResponse(await api.get('/api/sites/{id}', { path: { id }, signal })),
  update: (id: string, body: UpdateSite) =>
    api.post('/api/sites/{id}', body, { path: { id } }),
  schedules: (id: string, signal?: AbortSignal) => api.get('/api/sites/{id}/schedules', { path: { id }, signal }),
  createSchedule: (id: string, body: CreateSchedule) =>
    api.post('/api/sites/{id}/schedules', body, { path: { id } }),
  deleteSchedule: (id: string, scheduleId: string) =>
    api.del('/api/sites/{id}/schedules/{scheduleId}', { path: { id, scheduleId } }),
  windows: (id: string, signal?: AbortSignal) =>
    api.get('/api/sites/{id}/windows', { path: { id }, query: { days: 7 }, signal }),
  attributes: (signal?: AbortSignal) => api.get('/api/sites/attributes', { signal }),
  closures: (id: string, signal?: AbortSignal) => api.get('/api/sites/{id}/closures', { path: { id }, signal }),
  addClosure: (id: string, date: string) =>
    api.post('/api/sites/{id}/closures', { date }, { path: { id } }),
  removeClosure: (id: string, date: string) =>
    api.del('/api/sites/{id}/closures/{date}', { path: { id, date } }),
};

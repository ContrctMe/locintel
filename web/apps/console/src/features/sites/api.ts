import { api, type components } from '@locintel/api';
import { parseSiteResponse } from './schema';

type CreateSite = components['schemas']['CreateSiteRequest'];
type UpdateSite = components['schemas']['UpdateSiteRequest'];
type CreateSchedule = components['schemas']['CreateScheduleRequest'];

export const sitesApi = {
  // `under` is the console's global scope (direction B): the node the list
  // asks under; the server's own scope gate still applies on top (ADR 49)
  // bbox/zoom are the map view's viewport (ADR 49): one more predicate on
  // the same list, applied after scope; absent in the table view
  // `after` is the keyset cursor the previous page returned as `next` (ADR 51)
  list: (
    limit: number,
    after: string | undefined,
    q?: string,
    under?: string,
    bbox?: string,
    zoom?: number,
    signal?: AbortSignal,
    status?: string,
  ) => api.get('/api/sites', { query: { limit, after, q, under, bbox, zoom, status }, signal }),
  hierarchy: (signal?: AbortSignal) => api.get('/api/hierarchy', { signal }),
  /** The org's raster basemaps (ADR 50 §3), provider keys already in the URLs. */
  basemaps: (signal?: AbortSignal) => api.get('/api/map/basemaps', { signal }),
  /** The data layers this principal may draw (ADR 50 §3): registered queries served as tiles. */
  layers: (signal?: AbortSignal) => api.get('/api/map/layers', { signal }),
  /** The tile URL template for a data layer; `under` narrows to the console's scope node. */
  tiles: (layer: string, under: string | null) =>
    `${window.location.origin}/api/tiles/${layer}/{z}/{x}/{y}${
      under ? `?under=${encodeURIComponent(under)}` : ''
    }`,
  create: (body: CreateSite) => api.post('/api/sites', body),
  get: async (id: string, signal?: AbortSignal) =>
    parseSiteResponse(await api.get('/api/sites/{id}', { path: { id }, signal })),
  update: (id: string, body: UpdateSite) =>
    api.post('/api/sites/{id}', body, { path: { id } }),
  /** One status for the selected sites; the server counts what scope left alone. */
  bulkStatus: (ids: string[], status: components['schemas']['SiteStatus']) =>
    api.post('/api/sites/bulk-status', { ids, status }),
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

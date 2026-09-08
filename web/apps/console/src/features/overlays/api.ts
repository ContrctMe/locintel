import { api, type components } from '@locintel/api';

export type OverlayLayer = components['schemas']['OverlayLayerSummary'];
type CreateLayer = components['schemas']['CreateOverlayLayerRequest'];
type UpdateLayer = components['schemas']['UpdateOverlayLayerRequest'];

/** The Spatial module's overlays (ADR 50 §3): the org's own shapes, served as tiles. */
export const overlaysApi = {
  list: (deleted?: boolean, signal?: AbortSignal) =>
    api.get('/api/overlays', { query: { deleted }, signal }),
  create: (body: CreateLayer) => api.post('/api/overlays', body),
  update: (id: string, body: UpdateLayer) => api.post('/api/overlays/{id}', body, { path: { id } }),
  /** The upload IS the feature set: GeoJSON in, the layer's shapes replaced wholesale. */
  replaceFeatures: (id: string, geoJson: unknown) =>
    api.put('/api/overlays/{id}/features', { geoJson }, { path: { id } }),
  remove: (id: string) => api.del('/api/overlays/{id}', { path: { id } }),
  restore: (id: string) => api.post('/api/overlays/{id}/restore', undefined, { path: { id } }),
  /** The tile URL template the map draws a layer from (scope is the server's). */
  tiles: (id: string) => `${window.location.origin}/api/tiles/overlays/${id}/{z}/{x}/{y}`,
};

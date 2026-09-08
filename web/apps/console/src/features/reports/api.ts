import { api, type components } from '@locintel/api';
import { filesApi } from '../files';

export type ReportJob = components['schemas']['JobResponse'];
export type ReportSubmission = components['schemas']['SubmitRequest'];
export type ReportArtifact = components['schemas']['ArtifactResponse'];
export type ReportItem = components['schemas']['ItemResponse'];
export type ReportJobState = components['schemas']['ReportJobState'];

/**
 * The one place submission rules live. The dialog reports what this throws
 * rather than repeating the rules with its own wording.
 */
export function validateSubmission(value: ReportSubmission): ReportSubmission {
  if (!value.reportType) throw new Error('Choose a report.');
  if (value.selection === 'selected' && value.siteIds.length === 0)
    throw new Error('Select at least one site.');
  if (value.mode === 'single' && (value.selection !== 'selected' || value.siteIds.length !== 1))
    throw new Error('A single report covers exactly one selected site.');
  if (new Set(value.siteIds).size !== value.siteIds.length)
    throw new Error('Select each site only once.');
  if (!value.options || typeof value.options !== 'object' || Array.isArray(value.options))
    throw new Error('Report options must be an object.');
  return value;
}

export const reportsApi = {
  quota: (signal?: AbortSignal) => api.get('/api/reports/quota', { signal }),
  list: (signal?: AbortSignal) => api.get('/api/reports', { signal }),
  get: (id: string, signal?: AbortSignal) => api.get('/api/reports/{id}', { path: { id }, signal }),
  types: (signal?: AbortSignal) => api.get('/api/reports/types', { signal }),
  basemaps: (signal?: AbortSignal) => api.get('/api/reports/basemaps', { signal }),
  overlays: (signal?: AbortSignal) => api.get('/api/overlays', { signal }),
  /** Photographs a report may include: this site's own clean images. */
  photos: (siteId: string, offset: number, signal?: AbortSignal) =>
    filesApi.list({ offset, trash: false, siteId }, signal),
  submit: (body: ReportSubmission, idempotencyKey: string) =>
    api.post('/api/reports', validateSubmission(body), { idempotencyKey }),
  cancel: (id: string) => api.post('/api/reports/{id}/cancel', undefined, { path: { id } }),
  retry: (id: string) => api.post('/api/reports/{id}/retry', undefined, { path: { id } }),
  /** The run's own bundle. A generated PDF is a site file - download it with `file`. */
  bundle: (id: string, artifactId: string) =>
    api.get('/api/reports/{id}/artifacts/{artifactId}/download', {
      path: { id, artifactId },
    }),
  file: (fileId: string) => filesApi.download(fileId),
};

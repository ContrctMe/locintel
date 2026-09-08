import { api, type components } from '@locintel/api';

type Attribute = components['schemas']['CreateAttributeDefinitionRequest'];
type Basemaps = components['schemas']['PutBasemapsRequest']['basemaps'];

export const settingsApi = {
  rename: (name: string) => api.put('/api/org', { name }),
  exportData: () => api.post('/api/org/export'),
  billing: (signal?: AbortSignal) => api.get('/api/billing', { signal }),
  checkout: (planId: string) => api.post('/api/billing/checkout', { planId, returnPath: '/settings' }),
  billingPortal: () => api.post('/api/billing/portal', { returnPath: '/settings' }),
  sso: (signal?: AbortSignal) => api.get('/api/org/sso', { signal }),
  publicUrl: (signal?: AbortSignal) => api.get('/api/org/public-url', { signal }),
  closure: (signal?: AbortSignal) => api.get('/api/org/closure', { signal }),
  requestClose: () => api.post('/api/org/close'),
  cancelClose: () => api.post('/api/org/close/cancel'),
  ssoPortal: (intent: 'sso' | 'dsync') => api.post('/api/org/sso/portal', { intent, returnPath: '/settings' }),
  attributes: (signal?: AbortSignal) => api.get('/api/sites/attributes', { signal }),
  createAttribute: (body: Attribute) => api.post('/api/sites/attributes', body),
  removeAttribute: (id: string) => api.del('/api/sites/attributes/{id}', { path: { id } }),
  basemaps: (signal?: AbortSignal) => api.get('/api/map/basemaps/settings', { signal }),
  saveBasemaps: (basemaps: Basemaps) => api.put('/api/map/basemaps', { basemaps }),
};

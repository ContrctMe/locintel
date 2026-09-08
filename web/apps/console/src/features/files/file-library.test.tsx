// @vitest-environment jsdom
import '../../test/dom';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { beforeEach, expect, it, vi } from 'vitest';
import { filesApi } from './api';
import { FileLibrary } from './file-library';
import { uploadFile } from '../../lib/uploads';

const session = vi.hoisted(() => ({ manage: true }));
vi.mock('../../session', async importOriginal => ({
  ...await importOriginal<typeof import('../../session')>(),
  useMe: () => ({ data: { tier: 'user', capabilities: ['files:read', ...(session.manage ? ['files:manage'] : [])] } }),
}));
vi.mock('../../lib/uploads', () => ({ uploadFile: vi.fn() }));
const siteId = '01990000-0000-7000-8000-000000000001';

beforeEach(() => {
  vi.restoreAllMocks();
  session.manage = true;
  vi.mocked(uploadFile).mockReset().mockResolvedValue('file-1');
  vi.spyOn(filesApi, 'list').mockResolvedValue({ items: [], total: 0, nextOffset: null });
});

function mount(site?: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  client.setQueryData(['reports', 'photos', siteId], { pages: [] });
  const view = render(<QueryClientProvider client={client}><FileLibrary siteId={site} /></QueryClientProvider>);
  return { ...view, client };
}

it.each([undefined, siteId])('uploads from file library for site %s and refreshes its choices', async (site) => {
  const { container, client } = mount(site);
  await screen.findByRole('button', { name: 'Upload file' });
  await waitFor(() => expect(filesApi.list).toHaveBeenCalledTimes(1));
  const photo = new File(['photo'], 'site.png', { type: 'image/png' });
  await userEvent.setup({ applyAccept: false }).upload(container.querySelector<HTMLInputElement>('input[type="file"]')!, photo);
  await waitFor(() => expect(uploadFile).toHaveBeenCalledWith(photo, 'image/png', expect.any(Function), site));
  await waitFor(() => expect(filesApi.list).toHaveBeenCalledTimes(2));
  expect(client.getQueryState(['reports', 'photos', siteId])?.isInvalidated).toBe(!!site);
});

it('shows upload failure and keeps the site upload available for retry', async () => {
  vi.mocked(uploadFile).mockRejectedValue(new Error('Upload refused'));
  const { container } = mount(siteId);
  await userEvent.setup({ applyAccept: false }).upload(container.querySelector<HTMLInputElement>('input[type="file"]')!, new File(['photo'], 'site.png', { type: 'image/png' }));
  expect((await screen.findByRole('alert')).textContent).toContain('Upload refused');
  expect(screen.getByRole('button', { name: 'Upload file' })).toHaveProperty('disabled', false);
});

it('hides site upload for readers and while viewing trash', async () => {
  session.manage = false;
  const view = mount(siteId);
  expect(screen.queryByRole('button', { name: 'Upload file' })).toBeNull();
  view.unmount();
  session.manage = true;
  mount(siteId);
  await userEvent.click(screen.getByRole('button', { name: 'Trash' }));
  expect(screen.queryByRole('button', { name: 'Upload file' })).toBeNull();
});

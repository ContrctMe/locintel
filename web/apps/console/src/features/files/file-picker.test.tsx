// @vitest-environment jsdom
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useState } from 'react';
import { afterEach, expect, test, vi } from 'vitest';
import { FilePicker } from './file-picker';
import { filesApi } from './api';

afterEach(() => { cleanup(); vi.restoreAllMocks(); });

test('clean file 201 remains reachable through empty eligible pages and a failed next page', async () => {
  let failNext = true;
  vi.spyOn(filesApi, 'list').mockImplementation(async ({ offset }) => {
    if (offset === 200 && failNext) throw new Error('offline');
    return { items: [{ id: String(offset + 1), name: `File ${offset + 1}`, status: offset === 200 ? 'Clean' : 'Quarantined' }],
      nextOffset: offset === 200 ? null : offset + 50 } as Awaited<ReturnType<typeof filesApi.list>>;
  });
  function Harness() {
    const [id, setId] = useState('');
    return <FilePicker value={id} onChange={setId} cleanOnly />;
  }
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(<QueryClientProvider client={client}><Harness /></QueryClientProvider>);
  const user = userEvent.setup();
  for (let page = 0; page < 4; page++) {
    await screen.findByText('No eligible files on the loaded pages.');
    await user.click(await screen.findByRole('button', { name: 'Load more files' }));
  }
  await screen.findByRole('alert');
  failNext = false;
  await user.click(screen.getByRole('button', { name: 'Retry files' }));
  await screen.findByRole('option', { name: 'File 201 (Clean)' });
  await user.selectOptions(screen.getByRole('combobox', { name: 'File' }), '201');
  expect((screen.getByRole('combobox', { name: 'File' }) as HTMLSelectElement).value).toBe('201');
  expect(screen.queryByRole('button', { name: 'Load more files' })).toBeNull();
  client.clear();
});

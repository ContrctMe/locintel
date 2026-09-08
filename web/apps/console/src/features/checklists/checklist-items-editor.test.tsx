// @vitest-environment jsdom
import '../../test/dom';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { ChecklistItemsEditor } from './checklists-page';

describe('ChecklistItemsEditor', () => {
  it('adds the next item on Enter and never removes the last one', async () => {
    const onChange = vi.fn();
    render(<ChecklistItemsEditor items={[{ id: 'a', text: 'Unlock doors' }]} onChange={onChange} />);
    expect(screen.getByRole('button', { name: 'Remove item 1' })).toHaveProperty('disabled', true);
    await userEvent.type(screen.getByRole('textbox', { name: 'Item 1' }), '{Enter}');
    expect(onChange).toHaveBeenCalledTimes(1);
    expect(onChange.mock.calls[0]?.[0]).toHaveLength(2);
  });

  it('removes an item when there is another', async () => {
    const onChange = vi.fn();
    render(
      <ChecklistItemsEditor
        items={[{ id: 'a', text: 'Unlock doors' }, { id: 'b', text: 'Count the till' }]}
        onChange={onChange}
      />,
    );
    await userEvent.click(screen.getByRole('button', { name: 'Remove item 1' }));
    expect(onChange).toHaveBeenCalledWith([{ id: 'b', text: 'Count the till' }]);
  });
});

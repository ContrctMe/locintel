// @vitest-environment jsdom
import '../../test/dom';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { LevelsFields } from './levels-fields';

describe('LevelsFields', () => {
  it('shows one field per level and adds one up to the plan', async () => {
    const onChange = vi.fn();
    render(<LevelsFields levels={['Region', 'Market']} onChange={onChange} max={3} />);
    expect(screen.getByLabelText('Top level')).toHaveProperty('value', 'Region');
    expect(screen.getByLabelText('Level 2')).toHaveProperty('value', 'Market');
    await userEvent.click(screen.getByRole('button', { name: 'Add a level' }));
    expect(onChange).toHaveBeenCalledWith(['Region', 'Market', '']);
  });

  it('stops adding at the plan depth and keeps a level a node uses', () => {
    render(<LevelsFields levels={['Region', 'Market']} onChange={() => {}} max={2} min={1} />);
    expect(screen.getByRole('button', { name: 'Add a level' })).toHaveProperty('disabled', true);
    expect(screen.getByRole('button', { name: 'Remove top level' })).toHaveProperty('disabled', true);
    expect(screen.getByRole('button', { name: 'Remove level 2' })).toHaveProperty('disabled', false);
  });

  it('edits a level in place', async () => {
    const onChange = vi.fn();
    render(<LevelsFields levels={['Region']} onChange={onChange} max={4} />);
    await userEvent.type(screen.getByLabelText('Top level'), 's');
    expect(onChange).toHaveBeenLastCalledWith(['Regions']);
  });
});

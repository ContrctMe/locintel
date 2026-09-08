import { describe, expect, it } from 'vitest';
import { searchTerm } from './site-picker';

// the popup itself is the browser suite's job (checklist-picker.spec); this
// is the one decision the picker makes on its own
describe('the site picker\'s search term', () => {
  it('searches what was typed', () => {
    expect(searchTerm('  pike ', undefined)).toBe('pike');
  });
  it('does not search the chosen site\'s own name', () => {
    expect(searchTerm('Pike Place', 'Pike Place')).toBe('');
    expect(searchTerm('Pike Pl', 'Pike Place')).toBe('Pike Pl');
  });
});

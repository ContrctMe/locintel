// @vitest-environment jsdom
import '../test/dom';
import { render, screen } from '@testing-library/react';
import { expect, it, vi } from 'vitest';
import { FileDropzone } from './file-dropzone';

it('labels each native file input with its visible upload prompt', () => {
  render(<>
    <FileDropzone onFile={vi.fn()} />
    <FileDropzone label="Upload a GeoJSON overlay" onFile={vi.fn()} />
  </>);
  const files = screen.getByLabelText('Drop a file here, or choose one');
  const overlay = screen.getByLabelText('Upload a GeoJSON overlay');
  expect(files).toHaveProperty('type', 'file');
  expect(overlay).toHaveProperty('type', 'file');
  expect(files).not.toBe(overlay);
});

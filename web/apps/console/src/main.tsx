import { RouterProvider } from '@tanstack/react-router';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { router } from './router';
import { SessionBoundary } from './app/session-boundary';
import './styles.css';

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <SessionBoundary>
      <RouterProvider router={router} />
    </SessionBoundary>
  </StrictMode>,
);

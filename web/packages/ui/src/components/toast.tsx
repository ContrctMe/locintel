import { toast as sonnerToast } from 'sonner';
import { Toaster as SonnerToaster } from './sonner';

/**
 * Toasts are sonner, as in every ReUI block, behind the barrel's one
 * `toast` object (ADR 20): success and error, one line each, the server's
 * message on failure. The Toaster reads the console's own theme (the
 * `dark` class on the root, ADR 20 direction B) rather than next-themes.
 */
export const toast = {
  success: (message: string) => {
    sonnerToast.success(message);
  },
  error: (message: string) => {
    sonnerToast.error(message);
  },
  info: (message: string) => {
    sonnerToast.info(message);
  },
};

export function Toaster({ theme }: { theme?: 'light' | 'dark' }) {
  return <SonnerToaster theme={theme} position="bottom-right" duration={4000} closeButton={false} richColors={false} />;
}

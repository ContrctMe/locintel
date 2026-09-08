import { useState, type ComponentProps, type ReactNode } from 'react';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from './alert-dialog';
import { Button } from './button';

/**
 * A button whose action needs a second, deliberate step. The step is the
 * shadcn AlertDialog: the question is the title, the consequence (when the
 * caller names one) is the description, and the confirming button repeats
 * the question so the ask reads the same in both places. Destructive
 * callers get a destructive confirm; everything else the default.
 */
export function ConfirmButton({
  onConfirm,
  children,
  confirmLabel = 'Sure?',
  description,
  variant = 'ghost',
  ...props
}: Omit<ComponentProps<typeof Button>, 'onClick'> & {
  onConfirm: () => void;
  /** The question the dialog asks, and the label of the button that answers it. */
  confirmLabel?: string;
  /** What happens if they say yes; shown under the question. */
  description?: ReactNode;
}) {
  const [open, setOpen] = useState(false);
  return (
    <AlertDialog open={open} onOpenChange={setOpen}>
      <AlertDialogTrigger render={<Button variant={variant} {...props} />}>{children}</AlertDialogTrigger>
      <AlertDialogContent size="sm">
        <AlertDialogHeader>
          <AlertDialogTitle>{confirmLabel}</AlertDialogTitle>
          {description ? (
            <AlertDialogDescription>{description}</AlertDialogDescription>
          ) : (
            <AlertDialogDescription className="sr-only">Confirm: {children}</AlertDialogDescription>
          )}
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>Cancel</AlertDialogCancel>
          <AlertDialogAction
            variant={variant === 'destructive' ? 'destructive' : 'default'}
            onClick={() => {
              setOpen(false);
              onConfirm();
            }}
          >
            {confirmLabel}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}

import { Button, cn, Progress, useFileUpload } from '@locintel/ui';
import { UploadCloud } from 'lucide-react';
import { useId, type ReactNode } from 'react';

/**
 * One way to hand the console a file: ReUI's file-upload hook behind a
 * dropzone (drag in, or choose), with the upload's phase and progress shown
 * where the file landed. Files, Ingest and Overlays all use it, so the
 * accepted types and the copy are the only things that differ.
 */
export function FileDropzone({
  accept,
  onFile,
  busy = false,
  phase,
  error,
  label = 'Drop a file here, or choose one',
  hint,
  buttonLabel = 'Choose file',
  className,
}: {
  accept?: string;
  onFile: (file: File) => void;
  busy?: boolean;
  /** What the upload is doing right now ("Scanning…"); shown with a bar while busy. */
  phase?: ReactNode;
  error?: ReactNode;
  label?: ReactNode;
  hint?: ReactNode;
  buttonLabel?: string;
  className?: string;
}) {
  const labelId = useId();
  const [{ isDragging, errors }, { handleDragEnter, handleDragLeave, handleDragOver, handleDrop, openFileDialog, getInputProps }] =
    useFileUpload({
      accept,
      multiple: false,
      onFilesAdded: (added) => {
        const file = added[0]?.file;
        if (file instanceof File) onFile(file);
      },
    });
  return (
    <div
      role="group"
      aria-busy={busy}
      onDragEnter={handleDragEnter}
      onDragLeave={handleDragLeave}
      onDragOver={handleDragOver}
      onDrop={handleDrop}
      className={cn(
        'flex flex-col items-center gap-2 rounded-lg border border-dashed px-4 py-6 text-center transition-colors',
        isDragging && 'border-primary bg-primary/5',
        className,
      )}
    >
      <input {...getInputProps()} aria-labelledby={labelId} className="sr-only" />
      <UploadCloud className="size-5 text-muted-foreground" aria-hidden />
      <p id={labelId} className="text-sm">{label}</p>
      {hint && <p className="text-xs text-muted-foreground">{hint}</p>}
      <Button type="button" size="sm" variant="outline" disabled={busy} onClick={openFileDialog}>
        {buttonLabel}
      </Button>
      {busy && (
        <div className="w-full max-w-xs space-y-1" aria-live="polite">
          {phase && <p className="text-xs text-muted-foreground">{phase}</p>}
          <Progress value={null} />
        </div>
      )}
      {(error || errors[0]) && (
        <p role="alert" className="text-sm text-destructive">
          {error ?? errors[0]}
        </p>
      )}
    </div>
  );
}

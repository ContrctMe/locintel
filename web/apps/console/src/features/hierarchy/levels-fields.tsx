import { Button, Field, FieldDescription, FieldLabel, Input } from '@locintel/ui';
import { Plus, X } from 'lucide-react';

/** How a level is addressed in the form: the top one, then each one under it. */
const levelLabel = (index: number) => (index === 0 ? 'Top level' : `Level ${index + 1}`);

/**
 * The hierarchy's level names as one field per level (flow review, 2026-09):
 * "Region", then "Market", with add and remove - not a comma-separated line
 * to get right. `max` is the plan's depth; `min` is the deepest level a node
 * already uses, which cannot be removed from here.
 */
export function LevelsFields({
  levels,
  onChange,
  max,
  min = 1,
}: {
  levels: string[];
  onChange: (levels: string[]) => void;
  max: number;
  min?: number;
}) {
  return (
    <div className="space-y-2">
      {levels.map((level, index) => (
        <Field key={index}>
          <FieldLabel htmlFor={`level-${index}`}>{levelLabel(index)}</FieldLabel>
          <div className="flex items-center gap-1.5">
            <Input
              id={`level-${index}`}
              value={level}
              placeholder={index === 0 ? 'Region' : 'Market'}
              onChange={(e) => onChange(levels.map((l, i) => (i === index ? e.target.value : l)))}
            />
            <Button
              type="button"
              variant="ghost"
              size="icon-sm"
              aria-label={`Remove ${levelLabel(index).toLowerCase()}`}
              disabled={levels.length <= 1 || index < min}
              onClick={() => onChange(levels.filter((_, i) => i !== index))}
            >
              <X className="size-4" />
            </Button>
          </div>
        </Field>
      ))}
      <div className="flex items-center justify-between gap-3">
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={levels.length >= max}
          onClick={() => onChange([...levels, ''])}
        >
          <Plus className="size-4" aria-hidden />
          Add a level
        </Button>
        <FieldDescription className="m-0">
          Sites sit on any level, the root included. Your plan allows {max}.
        </FieldDescription>
      </div>
    </div>
  );
}

import { Button, Calendar, Popover, PopoverContent, PopoverTrigger } from '@locintel/ui';
import { CalendarIcon } from 'lucide-react';
import { useState } from 'react';

const toIso = (d: Date) =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
const fromIso = (s: string) => {
  const [y, m, d] = s.split('-').map(Number);
  return y && m && d ? new Date(y, m - 1, d) : undefined;
};

/**
 * A calendar date as a form field (the shadcn date-picker pattern: a button
 * that opens the Calendar in a Popover), holding the value as the API's
 * ISO date string. Replaces the browser's own date input, which draws its
 * own chrome in every browser.
 */
export function DateField({
  id,
  value,
  onChange,
  placeholder = 'Pick a date',
  min,
}: {
  id?: string;
  /** ISO date, "YYYY-MM-DD", or empty. */
  value: string;
  onChange: (iso: string) => void;
  placeholder?: string;
  min?: Date;
}) {
  const [open, setOpen] = useState(false);
  const selected = value ? fromIso(value) : undefined;
  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger render={<Button id={id} variant="outline" className="w-full justify-start font-normal" />}>
        <CalendarIcon className="size-4 text-muted-foreground" aria-hidden />
        {selected ? selected.toLocaleDateString() : <span className="text-muted-foreground">{placeholder}</span>}
      </PopoverTrigger>
      <PopoverContent className="w-auto p-0" align="start">
        <Calendar
          mode="single"
          selected={selected}
          defaultMonth={selected}
          disabled={min ? { before: min } : undefined}
          onSelect={(next) => {
            onChange(next ? toIso(next) : '');
            setOpen(false);
          }}
        />
      </PopoverContent>
    </Popover>
  );
}

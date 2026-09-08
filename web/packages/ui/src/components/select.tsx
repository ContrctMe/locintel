import type { ComponentProps } from 'react';
import { cn } from '../lib/utils';
import { NativeSelect } from './native-select';

/**
 * The console's pickers are NATIVE selects on purpose: zero-surprise, right
 * on phones, driven by the browser suite with selectOption. They wear
 * shadcn's own native-select styling; `className` sizes the control, as it
 * always did (the wrapper takes it, the select fills the wrapper).
 */
export function Select({ className, ...props }: Omit<ComponentProps<'select'>, 'size'>) {
  return <NativeSelect className={cn('w-full', className)} {...props} />;
}

const TIME_ZONES: string[] = (() => {
  try {
    return Intl.supportedValuesOf('timeZone');
  } catch {
    return ['Etc/UTC'];
  }
})();

/** Every IANA zone the runtime knows - no more freetext "America/New_York". */
export function TimeZoneSelect(props: Omit<ComponentProps<'select'>, 'children' | 'size'>) {
  return (
    <Select {...props}>
      {TIME_ZONES.map((zone) => (
        <option key={zone} value={zone}>
          {zone.replaceAll('_', ' ')}
        </option>
      ))}
    </Select>
  );
}

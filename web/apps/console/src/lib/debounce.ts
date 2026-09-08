import { useEffect, useState } from 'react';

/**
 * The value once it has stopped changing for `ms`. Typing stays instant in
 * the input; what the server is asked settles after the pause.
 */
export function useDebounced<T>(value: T, ms: number): T {
  const [settled, setSettled] = useState(value);
  useEffect(() => {
    const handle = setTimeout(() => setSettled(value), ms);
    return () => clearTimeout(handle);
  }, [value, ms]);
  return settled;
}

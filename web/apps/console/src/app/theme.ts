import { useSyncExternalStore } from 'react';

/**
 * The .dark class IS the theme switch (ADR 20/47): tokens are class-switched,
 * so toggling one class re-themes everything and nothing is half-themed.
 * The console is dark-first (index.html ships the class); a viewer's choice
 * is remembered in this browser and applied before first paint from main.tsx.
 */
const key = 'locintel.theme';

export type Theme = 'dark' | 'light';

export function currentTheme(): Theme {
  return document.documentElement.classList.contains('dark') ? 'dark' : 'light';
}

export function applyStoredTheme() {
  try {
    const stored = localStorage.getItem(key);
    if (stored === 'light') document.documentElement.classList.remove('dark');
    if (stored === 'dark') document.documentElement.classList.add('dark');
  } catch {
    // no storage: stay with the document default
  }
}

export function toggleTheme(): Theme {
  const next: Theme = currentTheme() === 'dark' ? 'light' : 'dark';
  document.documentElement.classList.toggle('dark', next === 'dark');
  try {
    localStorage.setItem(key, next);
  } catch {
    // preference not remembered; the toggle still worked
  }
  return next;
}


const subscribe = (onChange: () => void) => {
  const observer = new MutationObserver(onChange);
  observer.observe(document.documentElement, { attributes: true, attributeFilter: ['class'] });
  return () => observer.disconnect();
};

/** The live theme, for components that paint outside the CSS tokens (the map's basemap). */
export function useTheme(): Theme {
  return useSyncExternalStore(subscribe, currentTheme, () => 'dark');
}

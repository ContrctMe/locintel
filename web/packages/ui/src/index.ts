// The @locintel/ui barrel (ADR 20): app code imports ONLY from here, never
// from component files directly - a lint rule enforces it. This indirection
// is the real seam: reskin via tokens.css, replace a component behind the
// barrel without touching call sites.
export * from './components/alert';
export * from './components/badge';
export * from './components/button';
export * from './components/card';
export * from './components/input';
export * from './components/label';
export * from './components/table';
export * from './components/textarea';
export * from './components/confirm-button';
export * from './components/dialog';
export * from './components/select';
export * from './components/toast';
export { cn } from './lib/utils';

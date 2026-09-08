// The @locintel/ui barrel (ADR 20): app code imports ONLY from here, never
// from component files directly - a lint rule enforces it. This indirection
// is the real seam: reskin via tokens.css, replace a component behind the
// barrel without touching call sites.
export * from './components/alert';
export * from './components/badge';
export * from './components/button';
export * from './components/calendar';
export * from './components/card';
export * from './components/input';
export * from './components/label';
export * from './components/table';
export * from './components/textarea';
export * from './components/confirm-button';
export * from './components/dialog';
export * from './components/select';
export * from './components/toast';

// ReUI surface + shadcn primitives the ReUI app shells build on (direction B,
// 2026-09-05). Installed via the shadcn CLI from this package; imports inside
// them are relative because the apps' Vite has no `@` alias. Not exported on
// purpose: components/reui/badge (the template's Badge stays the one above)
// and components/select-menu (Radix listbox the data grid uses internally;
// the template's Select stays the native control).
export * from './components/reui/frame';
export * from './components/reui/data-grid/data-grid';
export * from './components/reui/data-grid/data-grid-table';
export * from './components/reui/data-grid/data-grid-table-virtual';
export * from './components/reui/data-grid/data-grid-scroll-area';
export * from './components/reui/data-grid/data-grid-pagination';
export * from './components/reui/data-grid/data-grid-column-header';
export * from './components/reui/data-grid/data-grid-column-visibility';
export * from './components/reui/data-grid/data-grid-column-filter';
export * from './components/sidebar';
export * from './components/sheet';
export * from './components/tooltip';
export * from './components/dropdown-menu';
export * from './components/popover';
export * from './components/checkbox';
export * from './components/separator';
export * from './components/skeleton';
export * from './components/spinner';
// the console map (ADR 50 §5): MapLibre GL behind the barrel, loaded on demand
export * from './components/site-map';
export { useIsMobile } from './hooks/use-mobile';
// The grid is headless TanStack Table underneath; apps wire it through the
// barrel too, so no app takes a direct dependency on the table library.
export {
  useTable,
  type ColumnDef,
  type RowSelectionState,
  type SortingState,
} from '@tanstack/react-table';
export { cn } from './lib/utils';

// UI/UX review (2026-09-06): the shadcn defaults and free ReUI components the
// console now composes instead of hand-rolled equivalents. ConfirmButton is
// the AlertDialog; toast is sonner; empty and loading states are Empty,
// Skeleton and Icon Stack; segmented choices are ToggleGroup and Tabs.
export * from './components/alert-dialog';
export * from './components/avatar';
export * from './components/breadcrumb';
export * from './components/button-group';
export * from './components/collapsible';
export * from './components/empty';
export * from './components/field';
export * from './components/input-group';
export * from './components/item';
export * from './components/kbd';
export * from './components/command';
export * from './components/progress';
export * from './components/radio-group';
export * from './components/scroll-area';
export * from './components/switch';
export * from './components/tabs';
export * from './components/toggle-group';
export * from './components/toggle';
export * from './components/reui/icon-stack';
export * from './components/reui/icon-tile';
export * from './components/reui/timeline';
export * from './components/reui/tree';
export * from './components/reui/filters/filters';
export * from './components/combobox';
export * from './components/reui/autocomplete';
export * from './components/reui/stepper';
export * from './components/reui/sortable';
export * from './components/reui/code-block/code-block';
export * from './components/reui/cascader/cascader';
export { CascaderItems } from './components/reui/cascader/cascader-item';
export { CascaderBreadcrumb, CascaderInput, CascaderNav, CascaderValue } from './components/reui/cascader/cascader-nav';
export type { CascaderNode } from './components/reui/cascader/cascader-types';
export { useFileUpload, type FileWithPreview, type FileUploadOptions } from './hooks/use-file-upload';

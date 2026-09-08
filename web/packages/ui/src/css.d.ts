// CSS imported for its side effect (MapLibre's stylesheet), loaded on demand
declare module '*.css';

// Vite's `?url` import: the asset's public URL (MapLibre's worker file)
declare module '*?url' {
  const url: string;
  export default url;
}

// Vite's `?worker&url` import: the URL of a worker entry built on its own
declare module '*?worker&url' {
  const url: string;
  export default url;
}

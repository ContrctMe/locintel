// The typed client over the generated OpenAPI types (ADR 16). Paths are the
// contract's path templates, path and query parameters and request bodies are
// the operation's, and the response is the operation's 2xx JSON - so a
// contract change fails the typecheck at every affected call instead of at
// runtime. Session rides the HttpOnly cookie (ADR 21): credentials always
// included, no tokens in JS.
//
// Endpoints the API has not typed yet (the typed-response ratchet) describe
// their body as an empty object; for those the caller's generic stands in,
// exactly as before. For a typed endpoint the caller's generic is ignored and
// the contract's shape is what comes back.
import type { paths } from './types';

export type ApiPaths = paths;

const sessionHeader = 'X-LocIntel-Session-Context';
export const SESSION_CONTEXT_CHANGED = 'locintel:session-context-changed';
export const SESSION_CONTEXT_OBSERVED = 'locintel:session-context-observed';
let sessionContext: string | undefined;
let sessionGeneration = 0;

/** Called when the console discards its session tree, not on ordinary queries. */
export function resetSessionContext() {
  sessionContext = undefined;
  sessionGeneration++;
}

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly body: unknown,
    cause?: unknown,
    public readonly outcomeUnknown = false,
    /** Seconds the server asked the caller to wait, from Retry-After. */
    public readonly retryAfterSeconds?: number,
  ) {
    super(apiErrorMessage(status, body), { cause });
    this.name = 'ApiError';
  }
}

/**
 * A refusal the server itself says is transient. It must name a Retry-After: a
 * bare 503 can come from something in front of the API, after the work was
 * already done, and repeating a write on that guess is how a request lands twice.
 */
export function isTransient(error: unknown): error is ApiError {
  return (
    error instanceof ApiError &&
    error.status === 503 &&
    error.retryAfterSeconds !== undefined
  );
}

export type ApiProblem = {
  error?: string;
  traceId?: string;
  errors?: Record<string, string | string[]>;
};

export function apiProblem(value: unknown): ApiProblem | undefined {
  if (typeof value !== 'object' || value === null) return undefined;
  const candidate = value as Record<string, unknown>;
  const errors = candidate.errors;
  if (
    candidate.error !== undefined &&
    typeof candidate.error !== 'string'
  )
    return undefined;
  if (
    candidate.traceId !== undefined &&
    typeof candidate.traceId !== 'string'
  )
    return undefined;
  if (
    errors !== undefined &&
    (typeof errors !== 'object' || errors === null || Array.isArray(errors) ||
      !Object.values(errors).every((value) =>
        typeof value === 'string' ||
        (Array.isArray(value) && value.every((item) => typeof item === 'string')),
      ))
  )
    return undefined;
  return candidate as ApiProblem;
}

/**
 * Gate 1's single 402 body (GateResults). A plan limit is an upsell, not a
 * failure, so callers can offer the plan instead of only reporting an error.
 * `limit`/`current` are absent when the plan omits the feature entirely.
 */
export type ApiPlanLimit = {
  code: string;
  limit?: number;
  current?: number;
};

export function apiPlanLimit(
  status: number,
  body: unknown,
): ApiPlanLimit | undefined {
  if (status !== 402 || typeof body !== 'object' || body === null)
    return undefined;
  const candidate = body as Record<string, unknown>;
  if (typeof candidate.code !== 'string') return undefined;
  const limit = candidate.limit;
  const current = candidate.current;
  return {
    code: candidate.code,
    limit: typeof limit === 'number' ? limit : undefined,
    current: typeof current === 'number' ? current : undefined,
  };
}

function apiErrorMessage(status: number, body: unknown): string {
  const plan = apiPlanLimit(status, body);
  if (plan)
    return plan.limit === undefined
      ? 'Your plan does not include this feature'
      : `Plan limit reached: ${plan.current ?? plan.limit} of ${plan.limit} used`;
  const problem = apiProblem(body);
  if (problem?.error) return problem.error;
  const validation = problem?.errors
    ? Object.values(problem.errors)
        .flatMap((value) => value)
        .find((value) => value.length > 0)
    : undefined;
  if (validation) return validation;
  if (typeof body === 'string' && body.trim()) return body.trim();
  if (status === 0) return 'Unable to reach the server';
  if (status === 401) return 'Sign in required';
  if (status === 403) return 'Permission denied';
  if (status === 409) return 'The request conflicts with a newer change';
  if (status === 503) return 'The server is busy. Please try again.';
  return `API ${status}`;
}

type Method = 'get' | 'post' | 'put' | 'delete';

/** Paths that offer the method. */
type PathsFor<M extends Method> = {
  [P in keyof paths]: paths[P][M] extends undefined | never ? never : P;
}[keyof paths];

type Op<P extends keyof paths, M extends Method> = NonNullable<paths[P][M]>;

type Params<O> = O extends { parameters: infer Pa } ? Pa : never;
type PathParams<O> = Params<O> extends { path: infer T } ? T : undefined;
type QueryParams<O> = Params<O> extends { query?: infer T }
  ? [T] extends [never]
    ? undefined
    : T
  : undefined;
type RequestBody<O> = O extends { requestBody: { content: { 'application/json': infer B } } }
  ? B
  : O extends { requestBody?: { content: { 'application/json': infer B } } }
    ? B | undefined
    : undefined;

type Json<C> = C extends { content: { 'application/json': infer J } } ? J : unknown;
type Response<O> = O extends { responses: infer R }
  ? R extends { 200: infer A }
    ? Json<A> extends Record<string, never>
      ? R extends { 201: infer B }
        ? Json<B>
        : R extends { 202: infer B }
          ? Json<B>
          : R extends { 204: unknown }
            ? undefined
            : unknown
      : Json<A>
    : R extends { 201: infer A }
      ? Json<A>
      : R extends { 202: infer A }
        ? Json<A>
        : R extends { 204: unknown }
          ? undefined
          : unknown
  : unknown;
type Result<O> = [Response<O>] extends [never]
  ? unknown
  : Response<O> extends Record<string, never>
    ? unknown
    : Response<O>;

type RequiredKeys<T> = T extends object
  ? {
      [K in keyof T]-?: {} extends Pick<T, K> ? never : K;
    }[keyof T]
  : never;
type HasRequiredKeys<T> = [RequiredKeys<T>] extends [never] ? false : true;
type HasRequiredInit<O> = [PathParams<O>] extends [undefined]
  ? HasRequiredKeys<QueryParams<O>>
  : true;

/** Options an operation accepts; required path/query values make the whole argument required. */
type Init<O> = ([PathParams<O>] extends [undefined]
  ? { path?: undefined }
  : { path: PathParams<O> }) &
  ([QueryParams<O>] extends [undefined]
    ? { query?: undefined }
    : HasRequiredKeys<QueryParams<O>> extends true
      ? { query: QueryParams<O> }
      : { query?: QueryParams<O> }) & { signal?: AbortSignal; idempotencyKey?: string };
type InitArgs<O> = HasRequiredInit<O> extends true ? [init: Init<O>] : [init?: Init<O>];
type WriteArgs<O> = O extends { requestBody: { content: { 'application/json': unknown } } }
  ? [body: RequestBody<O>, ...init: InitArgs<O>]
  : HasRequiredInit<O> extends true
    ? [body: RequestBody<O>, init: Init<O>]
    : [body?: RequestBody<O>, init?: Init<O>];

// Vite injects env at build; plain node (Start server) falls back cleanly
const base: string =
  (import.meta as { env?: { VITE_API_URL?: string } }).env?.VITE_API_URL ?? '';

function url(template: string, init?: { path?: unknown; query?: unknown }): string {
  const filled = template.replace(/\{(\w+)\}/g, (_, key: string) => {
    const value = (init?.path as Record<string, string | number> | undefined)?.[key];
    if (value === undefined) throw new Error(`missing path parameter '${key}' for ${template}`);
    return encodeURIComponent(String(value));
  });
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries((init?.query as Record<string, unknown>) ?? {}))
    if (value !== undefined && value !== null) search.set(key, String(value));
  const qs = search.toString();
  return `${base}${filled}${qs ? `?${qs}` : ''}`;
}

async function request<T>(
  method: string,
  template: string,
  init?: { path?: unknown; query?: unknown; signal?: AbortSignal; idempotencyKey?: string },
  body?: unknown,
): Promise<T> {
  const generation = sessionGeneration;
  // A cancelled query must not start a request; cancellation after dispatch
  // cannot establish whether a write committed on the server.
  init?.signal?.throwIfAborted();
  const deadline = AbortSignal.timeout(30_000);
  const signal = init?.signal ? AbortSignal.any([init.signal, deadline]) : deadline;
  let response: globalThis.Response;
  let text: string;
  try {
    response = await fetch(url(template, init), {
      method,
      signal,
      credentials: 'include',
      headers: {
        ...(init?.idempotencyKey ? { 'Idempotency-Key': init.idempotencyKey } : {}),
        ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
        ...(sessionContext && template !== '/me' ? { [sessionHeader]: sessionContext } : {}),
      },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    text = await response.text();
  } catch (cause) {
    if (method === 'GET' && init?.signal?.aborted) throw init.signal.reason;
    const outcomeUnknown = method !== 'GET';
    const error = outcomeUnknown
      ? 'Connection interrupted. This operation may have completed. Refresh before retrying.'
      : deadline.aborted ? 'Request timed out. Please try again.' : 'Unable to reach the server';
    throw new ApiError(0, { error }, cause, outcomeUnknown);
  }
  let parsed: unknown;
  if (text.length > 0) {
    try {
      parsed = JSON.parse(text);
    } catch {
      parsed = text;
    }
  }
  if (typeof window !== 'undefined') {
    if (generation !== sessionGeneration)
      throw new ApiError(409, { error: 'This response belongs to a previous session.' });
    const observed = template === '/me' ? response.headers.get(sessionHeader) : null;
    if (template === '/me' && response.ok && !observed) {
      // A proxy or mismatched API must not silently disable stale-write checks.
      // Only an established session needs a reset; failed bootstrap stays failed.
      if (sessionContext) window.dispatchEvent(new Event(SESSION_CONTEXT_CHANGED));
      throw new ApiError(502, { error: 'Session verification unavailable. Reload; if this continues, contact support.' });
    }
    const mismatch = response.status === 409 && typeof parsed === 'object' && parsed !== null
      && (parsed as Record<string, unknown>).code === 'session_context_changed';
    if (mismatch || (observed && sessionContext && observed !== sessionContext)) {
      window.dispatchEvent(new Event(SESSION_CONTEXT_CHANGED));
      throw new ApiError(409, { error: 'Your session changed. Please retry in the current organization.' });
    }
    if (observed && response.ok && observed !== sessionContext) {
      sessionContext = observed;
      window.dispatchEvent(new CustomEvent(SESSION_CONTEXT_OBSERVED, { detail: observed }));
    }
  }
  if (!response.ok) {
    // Number(null) is 0, which would have made every bare 503 look like the
    // server had asked for an immediate retry.
    const header = response.headers.get('Retry-After');
    const after = header === null || header.trim() === '' ? Number.NaN : Number(header);
    throw new ApiError(
      response.status,
      parsed,
      undefined,
      false,
      Number.isFinite(after) && after >= 0 ? after : undefined,
    );
  }
  return parsed as T;
}

export const api = {
  get: <P extends PathsFor<'get'>>(path: P, ...args: InitArgs<Op<P, 'get'>>) =>
    request<Result<Op<P, 'get'>>>('GET', path, args[0]),
  post: <P extends PathsFor<'post'>>(
    path: P,
    ...args: WriteArgs<Op<P, 'post'>>
  ) => request<Result<Op<P, 'post'>>>('POST', path, args[1], args[0]),
  put: <P extends PathsFor<'put'>>(
    path: P,
    ...args: WriteArgs<Op<P, 'put'>>
  ) => request<Result<Op<P, 'put'>>>('PUT', path, args[1], args[0]),
  del: <P extends PathsFor<'delete'>>(path: P, ...args: InitArgs<Op<P, 'delete'>>) =>
    request<Result<Op<P, 'delete'>>>('DELETE', path, args[0]),
};

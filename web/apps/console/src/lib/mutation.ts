import { toast } from '@locintel/ui';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { ApiError, apiProblem, isTransient } from '@locintel/api';

/** The server's error body, when it sent one - with the trace id support can quote (maturity review, hole 1). */
export function apiError(error: unknown, fallback: string): string {
  if (!(error instanceof ApiError)) return fallback;
  const problem = apiProblem(error.body);
  const message = error.message.startsWith('API ') ? fallback : error.message;
  return problem?.traceId ? `${message} (trace ${problem.traceId})` : message;
}

/**
 * The one feedback contract (UX review P1): every mutation toasts its error
 * with the server's message, optionally toasts success, and invalidates the
 * query keys it dirtied. Pages stop hand-rolling onError/alert/invalidate.
 */
export function useApiMutation<TVariables = void, TData = unknown>(options: {
  mutationFn: (variables: TVariables) => Promise<TData>;
  invalidate?: string[][];
  success?: string;
  errorFallback?: string;
  onSuccess?: (data: TData, variables: TVariables) => void;
  onError?: () => void;
}) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: options.mutationFn,
    // Admission contention is transient by contract (ADR 55): the server never
    // waits on a pooled connection, it refuses and names a delay. Absorb one
    // round of that so two people submitting at once do not both see a toast.
    retry: (failureCount, error) => failureCount < 1 && isTransient(error),
    retryDelay: (_, error) =>
      (error instanceof ApiError ? (error.retryAfterSeconds ?? 1) : 1) * 1000,
    onSuccess: (data, variables) => {
      for (const key of options.invalidate ?? [])
        void queryClient.invalidateQueries({ queryKey: key });
      if (options.success) toast.success(options.success);
      options.onSuccess?.(data, variables);
    },
    onError: (error) => {
      toast.error(apiError(error, options.errorFallback ?? 'Request failed'));
      options.onError?.();
    },
  });
}

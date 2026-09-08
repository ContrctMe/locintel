import { useQuery } from '@tanstack/react-query';
import { operatorApi } from './api';

export const useOperatorOrgs = () =>
  useQuery({ queryKey: ['operator-orgs'], queryFn: ({ signal }) => operatorApi.orgs(signal) });

export const useOperatorHealth = () =>
  useQuery({ queryKey: ['operator-health'], queryFn: ({ signal }) => operatorApi.health(signal), refetchInterval: 60_000 });

export const useSuppressions = (q: string) =>
  useQuery({
    queryKey: ['suppressions', q],
    queryFn: ({ signal }) => operatorApi.suppressions(q.trim() || undefined, signal),
  });

export const useOperatorUsers = (q: string) =>
  useQuery({
    queryKey: ['operator-users', q],
    queryFn: ({ signal }) => operatorApi.users(q, signal),
    enabled: q.trim().length >= 2,
  });

export const useOperatorOverview = () =>
  useQuery({ queryKey: ['operator-overview'], queryFn: ({ signal }) => operatorApi.overview(signal), refetchInterval: 30_000 });

export const useDeadLetters = () =>
  useQuery({ queryKey: ['dead-letters'], queryFn: ({ signal }) => operatorApi.deadLetters(signal), refetchInterval: 30_000 });

export const useOperatorEntitlements = (orgId: string) =>
  useQuery({
    queryKey: ['operator-entitlements', orgId],
    queryFn: ({ signal }) => operatorApi.entitlements(orgId, signal),
  });

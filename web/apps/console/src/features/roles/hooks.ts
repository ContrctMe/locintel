import { useQuery } from '@tanstack/react-query';
import { rolesApi } from './api';

export const useRoles = () =>
  useQuery({ queryKey: ['roles'], queryFn: ({ signal }) => rolesApi.list(signal) });

export const useRoleMembers = () =>
  useQuery({ queryKey: ['members', 'picker'], queryFn: ({ signal }) => rolesApi.members(signal) });

// the one hierarchy query (features/hierarchy); the scope picker reads it
export { useHierarchy as useRoleHierarchy } from '../hierarchy/hooks';

export const useGrantExceptions = () =>
  useQuery({ queryKey: ['grant-exceptions'], queryFn: ({ signal }) => rolesApi.exceptions(signal) });

import { useQuery } from '@tanstack/react-query';
import { rolesApi } from './api';

export const useRoles = () =>
  useQuery({ queryKey: ['roles'], queryFn: ({ signal }) => rolesApi.list(signal) });

export const useRoleMembers = () =>
  useQuery({ queryKey: ['members', 'picker'], queryFn: ({ signal }) => rolesApi.members(signal) });

export const useRoleHierarchy = () =>
  useQuery({ queryKey: ['hierarchy'], queryFn: ({ signal }) => rolesApi.hierarchy(signal), retry: false });

export const useGrantExceptions = () =>
  useQuery({ queryKey: ['grant-exceptions'], queryFn: ({ signal }) => rolesApi.exceptions(signal) });

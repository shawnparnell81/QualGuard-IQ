import { createContext, useContext, type ReactNode } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { get, post, resetCsrf } from './api';

export interface Grant { permission: string; siteId: string | null; departmentId: string | null }
export interface Me { id: string; email: string; displayName: string; grants: Grant[] }

interface AuthState {
  me: Me | null;
  loading: boolean;
  can: (permission: string) => boolean;
  login: (email: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
}

const Ctx = createContext<AuthState>(null!);

export function AuthProvider({ children }: { children: ReactNode }) {
  const qc = useQueryClient();
  const q = useQuery({
    queryKey: ['me'],
    queryFn: async () => {
      try { return await get<Me>('/api/auth/me'); } catch { return null; }
    },
    staleTime: 60_000,
  });
  const me = q.data ?? null;
  const state: AuthState = {
    me,
    loading: q.isLoading,
    can: (p) => !!me?.grants.some((g) => g.permission === p),
    login: async (email, password) => {
      await post('/api/auth/login', { email, password });
      resetCsrf();
      await qc.invalidateQueries();
    },
    logout: async () => {
      await post('/api/auth/logout');
      resetCsrf();
      qc.clear();
      await qc.invalidateQueries({ queryKey: ['me'] });
    },
  };
  return <Ctx.Provider value={state}>{children}</Ctx.Provider>;
}

export const useAuth = () => useContext(Ctx);

export interface Branding { applicationName: string; organizationName: string; primaryColor: string | null }
export const useBranding = () =>
  useQuery({ queryKey: ['branding'], queryFn: () => get<Branding>('/api/organization/branding'), staleTime: 300_000 });

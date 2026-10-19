import { createContext, useContext, useEffect, type ReactNode } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { get, post, resetCsrf } from './api';
import { setQueueUser } from './offline';
import { clearPickWork } from './offlinePicks';
import { clearVerifyWork } from './offlineVerifications';

export interface Grant { permission: string; siteId: string | null; departmentId: string | null }
export interface Me { id: string; email: string; displayName: string; grants: Grant[]; portalKind?: string | null }

interface AuthState {
  me: Me | null;
  loading: boolean;
  can: (permission: string) => boolean;
  login: (email: string, password: string) => Promise<void>;
  /** Called after the last sign-in step (code, set-up or new password) set the session cookie. */
  signedIn: () => Promise<void>;
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
  useEffect(() => setQueueUser(me?.id ?? null), [me?.id]);
  const state: AuthState = {
    me,
    loading: q.isLoading,
    can: (p) => !!me?.grants.some((g) => g.permission === p),
    login: async (email, password) => {
      await post('/api/auth/login', { email, password });
      resetCsrf();
      await qc.invalidateQueries();
    },
    signedIn: async () => {
      resetCsrf(); // the CSRF token is bound to the signed-in user
      await qc.invalidateQueries();
    },
    logout: async () => {
      await post('/api/auth/logout');
      resetCsrf();
      clearPickWork(); clearVerifyWork(); // downloaded pick and verification work never outlives the session on a shared device
      // Signed out: show the sign-in page at once (me = null), then drop every cached query so no record data outlives the session.
      // Clearing the cache alone left the mounted shell on screen with its queries failing with 401.
      await qc.cancelQueries();
      qc.setQueryData(['me'], null);
      qc.removeQueries({ predicate: (x) => x.queryKey[0] !== 'me' });
    },
  };
  return <Ctx.Provider value={state}>{children}</Ctx.Provider>;
}

export const useAuth = () => useContext(Ctx);

export interface Branding { applicationName: string; organizationName: string; primaryColor: string | null; hasLogo?: boolean }
export const useBranding = () =>
  useQuery({ queryKey: ['branding'], queryFn: () => get<Branding>('/api/organization/branding'), staleTime: 300_000 });

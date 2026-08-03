import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';
import { adminFetch, sessionExpiredEvent } from '../api/client';

export interface UserSession {
  authenticated: true;
  username: string;
  issuer: string;
}

interface AuthState {
  session: UserSession | null;
  loading: boolean;
  error: string | null;
  login: () => void;
  logout: () => Promise<void>;
}

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<UserSession | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    fetch('/api/v1/auth/session', { credentials: 'include', signal: controller.signal })
      .then(async (response) => {
        if (response.status === 401) {
          return null;
        }
        if (!response.ok) {
          throw new Error(`Session request failed with status ${response.status}`);
        }
        return (await response.json()) as UserSession;
      })
      .then((value) => setSession(value))
      .catch((reason: unknown) => {
        if (!controller.signal.aborted) {
          setError(reason instanceof Error ? reason.message : 'Unable to load session');
        }
      })
      .finally(() => {
        if (!controller.signal.aborted) {
          setLoading(false);
        }
      });

    return () => controller.abort();
  }, []);

  useEffect(() => {
    const handleSessionExpired = () => setSession(null);
    window.addEventListener(sessionExpiredEvent, handleSessionExpired);
    return () => window.removeEventListener(sessionExpiredEvent, handleSessionExpired);
  }, []);

  const login = () => {
    const returnUrl = `${window.location.pathname}${window.location.search}`;
    window.location.assign(`/api/v1/auth/login?returnUrl=${encodeURIComponent(returnUrl)}`);
  };

  const logout = async () => {
    const response = await adminFetch('/api/v1/auth/logout', { method: 'POST' });
    if (response.ok || response.redirected) {
      setSession(null);
    }
  };

  return (
    <AuthContext.Provider value={{ session, loading, error, login, logout }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth(): AuthState {
  const state = useContext(AuthContext);
  if (!state) {
    throw new Error('useAuth must be used within AuthProvider');
  }
  return state;
}
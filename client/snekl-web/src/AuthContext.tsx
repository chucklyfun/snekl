import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';
import { api } from './api';
import type { UserPresentation } from './types';

interface AuthContextValue {
  user: UserPresentation | null;
  loading: boolean;
  login: (username: string, password: string) => Promise<void>;
  register: (username: string, email: string, password: string, displayName: string) => Promise<void>;
  logout: () => void;
  refreshMe: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserPresentation | null>(null);
  const [loading, setLoading] = useState(true);

  const refreshMe = async () => {
    const token = localStorage.getItem('snekl_token');
    if (!token) {
      setUser(null);
      return;
    }
    const me = await api.me();
    setUser(me);
  };

  useEffect(() => {
    refreshMe()
      .catch(() => localStorage.removeItem('snekl_token'))
      .finally(() => setLoading(false));
  }, []);

  const login = async (username: string, password: string) => {
    const result = await api.login(username, password);
    localStorage.setItem('snekl_token', result.token);
    await refreshMe();
  };

  const register = async (username: string, email: string, password: string, displayName: string) => {
    const result = await api.register(username, email, password, displayName);
    localStorage.setItem('snekl_token', result.token);
    await refreshMe();
  };

  const logout = () => {
    localStorage.removeItem('snekl_token');
    setUser(null);
  };

  return (
    <AuthContext.Provider value={{ user, loading, login, register, logout, refreshMe }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}

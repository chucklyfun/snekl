import {
  createContext,
  useContext,
  useEffect,
  useState,
  type ReactNode,
} from 'react';
import { Navigate, useLocation } from 'react-router';
import { adminApi } from './adminApi';
import type { SiteAdmin } from './types';

interface AdminAuthContextValue {
  admin: SiteAdmin | null;
  loading: boolean;
  login: (username: string, password: string) => Promise<void>;
  logout: () => void;
}

const AdminAuthContext = createContext<AdminAuthContextValue | null>(null);

export function AdminAuthProvider({ children }: { children: ReactNode }) {
  const [admin, setAdmin] = useState<SiteAdmin | null>(null);
  const [loading, setLoading] = useState(true);

  const refresh = async () => {
    const token = localStorage.getItem('snekl_admin_token');
    if (!token) {
      setAdmin(null);
      setLoading(false);
      return;
    }
    try {
      setAdmin(await adminApi.me());
    } catch {
      localStorage.removeItem('snekl_admin_token');
      setAdmin(null);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void refresh();
  }, []);

  const login = async (username: string, password: string) => {
    const result = await adminApi.login(username, password);
    localStorage.setItem('snekl_admin_token', result.token);
    setAdmin(result.admin);
  };

  const logout = () => {
    localStorage.removeItem('snekl_admin_token');
    setAdmin(null);
  };

  return (
    <AdminAuthContext.Provider value={{ admin, loading, login, logout }}>
      {children}
    </AdminAuthContext.Provider>
  );
}

export function useAdminAuth() {
  const ctx = useContext(AdminAuthContext);
  if (!ctx) throw new Error('useAdminAuth must be used within AdminAuthProvider');
  return ctx;
}

export function RequireAdmin({ children }: { children: ReactNode }) {
  const { admin, loading } = useAdminAuth();
  const location = useLocation();

  if (loading) return <p>Loading admin session...</p>;
  if (!admin) return <Navigate to="/admin/login" replace state={{ from: location.pathname }} />;
  return children;
}

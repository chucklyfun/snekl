import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';
import { api, authApi } from './api';
import { buildRegisterChallenge, signMultibaseProof } from './did/didKey';
import { clearDidKey, getOrCreateDidKey, getStoredDid } from './did/keyStore';
import { useServerRegistry } from './ServerRegistryContext';
import type { UserPresentation } from './types';

interface AuthContextValue {
  user: UserPresentation | null;
  did: string | null;
  isSuperUser: boolean;
  serverPermissions: string[];
  loading: boolean;
  login: (username: string, password: string) => Promise<void>;
  register: (username: string, email: string, password: string, displayName: string) => Promise<string>;
  logout: () => void;
  refreshMe: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const { refresh: refreshServers } = useServerRegistry();
  const [user, setUser] = useState<UserPresentation | null>(null);
  const [isSuperUser, setIsSuperUser] = useState(false);
  const [serverPermissions, setServerPermissions] = useState<string[]>([]);
  const [did, setDid] = useState<string | null>(getStoredDid());
  const [loading, setLoading] = useState(true);

  const refreshMe = async () => {
    const token = localStorage.getItem('snekl_token');
    if (!token) {
      setUser(null);
      setIsSuperUser(false);
      setServerPermissions([]);
      return;
    }
    const me = await api.me();
    setUser(me.user);
    setIsSuperUser(me.isSuperUser);
    setServerPermissions(me.serverPermissions);
    setDid(getStoredDid());
  };

  useEffect(() => {
    refreshMe()
      .catch(() => localStorage.removeItem('snekl_token'))
      .finally(() => setLoading(false));
  }, []);

  const login = async (username: string, password: string) => {
    const result = await authApi.login(username, password);
    localStorage.setItem('snekl_token', result.accessToken);
    setDid(result.person.did);
    await refreshServers();
    await refreshMe();
  };

  const register = async (username: string, email: string, password: string, displayName: string) => {
    const keyMaterial = await getOrCreateDidKey();
    const normalizedUsername = username.trim().toLowerCase();
    const challenge = buildRegisterChallenge(normalizedUsername, keyMaterial.did);
    const keyOwnershipProof = await signMultibaseProof(keyMaterial.privateKey, challenge);

    const result = await authApi.register({
      username: normalizedUsername,
      email,
      password,
      displayName,
      did: keyMaterial.did,
      keyOwnershipProof,
    });

    localStorage.setItem('snekl_token', result.accessToken);
    setDid(result.person.did);
    await refreshServers();
    await refreshMe();
    return result.person.did;
  };

  const logout = () => {
    localStorage.removeItem('snekl_token');
    setUser(null);
    setIsSuperUser(false);
    setServerPermissions([]);
    clearDidKey();
    setDid(null);
    void refreshServers();
  };

  return (
    <AuthContext.Provider
      value={{ user, did, isSuperUser, serverPermissions, loading, login, register, logout, refreshMe }}
    >
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}

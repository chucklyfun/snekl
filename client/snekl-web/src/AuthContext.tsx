import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';
import { authApi, createContentClient } from './api';
import { buildRegisterChallenge, signMultibaseProof } from './did/didKey';
import { clearDidKey, getOrCreateDidKey, getStoredDid } from './did/keyStore';
import { useServerRegistry } from './ServerRegistryContext';
import type { PersonCluster, UserPresentation } from './types';

interface AuthContextValue {
  user: UserPresentation | null;
  person: PersonCluster | null;
  did: string | null;
  isSuperUser: boolean;
  isServerOwner: boolean;
  serverPermissions: string[];
  loading: boolean;
  login: (email: string, password: string) => Promise<void>;
  reauthenticate: (password: string) => Promise<void>;
  register: (username: string, email: string, password: string, displayName: string) => Promise<string>;
  logout: () => void;
  refreshMe: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

function personToPresentation(person: PersonCluster): UserPresentation {
  return {
    id: person.id,
    username: person.canonicalUsername,
    displayName: person.displayName,
    bio: person.bio,
    isFederated: false,
    effectiveDisplayName: person.displayName || person.canonicalUsername,
    annotations: [],
    did: person.did,
  };
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const { refresh: refreshServers } = useServerRegistry();
  const [user, setUser] = useState<UserPresentation | null>(null);
  const [person, setPerson] = useState<PersonCluster | null>(null);
  const [isSuperUser, setIsSuperUser] = useState(false);
  const [isServerOwner, setIsServerOwner] = useState(false);
  const [serverPermissions, setServerPermissions] = useState<string[]>([]);
  const [did, setDid] = useState<string | null>(getStoredDid());
  const [loading, setLoading] = useState(true);

  const refreshMe = async () => {
    const token = localStorage.getItem('snekl_token');
    if (!token) {
      setUser(null);
      setPerson(null);
      setIsSuperUser(false);
      setIsServerOwner(false);
      setServerPermissions([]);
      return;
    }
    try {
      const account = await authApi.me();
      setPerson(account);
      setUser(personToPresentation(account));
      setDid(account.did);
      setIsSuperUser(false);
      setIsServerOwner(false);
      setServerPermissions([]);

      const memberships = await authApi.getContentServers().catch(() => []);
      const origin = memberships.find((s) => s.status === 'active' || s.status === 'favorite');
      if (origin) {
        try {
          const me = await createContentClient(origin.baseUrl).me();
          setUser(me.user);
          setIsSuperUser(me.isSuperUser);
          setIsServerOwner(Boolean(me.isServerOwner));
          setServerPermissions(me.serverPermissions);
        } catch {
          // Auth identity is enough when a content node is unreachable.
        }
      }
    } catch {
      localStorage.removeItem('snekl_token');
      setUser(null);
      setPerson(null);
      setIsSuperUser(false);
      setIsServerOwner(false);
      setServerPermissions([]);
    }
  };

  useEffect(() => {
    refreshMe()
      .catch(() => localStorage.removeItem('snekl_token'))
      .finally(() => setLoading(false));
  }, []);

  const login = async (email: string, password: string) => {
    const result = await authApi.login(email, password);
    localStorage.setItem('snekl_token', result.accessToken);
    setDid(result.person.did);
    await refreshServers();
    await refreshMe();
  };

  const reauthenticate = async (password: string) => {
    const result = await authApi.reauthenticate(password);
    localStorage.setItem('snekl_token', result.accessToken);
    setDid(result.person.did);
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
    setPerson(null);
    setIsSuperUser(false);
    setIsServerOwner(false);
    setServerPermissions([]);
    clearDidKey();
    setDid(null);
    void refreshServers();
  };

  return (
    <AuthContext.Provider
      value={{
        user,
        person,
        did,
        isSuperUser,
        isServerOwner,
        serverPermissions,
        loading,
        login,
        reauthenticate,
        register,
        logout,
        refreshMe,
      }}
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

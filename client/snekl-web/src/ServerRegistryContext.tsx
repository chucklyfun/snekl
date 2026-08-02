import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react';
import { authApi, CONTENT_API } from './api';
import type { ContentServerMembership, ContentServerStatusPreset } from './types';

interface ServerRegistryContextValue {
  servers: ContentServerMembership[];
  activeServers: ContentServerMembership[];
  presets: ContentServerStatusPreset[];
  loading: boolean;
  refresh: () => Promise<void>;
  subscribe: (baseUrl: string) => Promise<ContentServerMembership>;
  unsubscribe: (serverDid: string) => Promise<void>;
  setStatus: (serverDid: string, status: string) => Promise<void>;
  resolveBaseUrl: (serverDid: string) => string | undefined;
}

const ServerRegistryContext = createContext<ServerRegistryContextValue | null>(null);

function isFeedVisible(status: string): boolean {
  return status === 'active' || status === 'favorite';
}

export function ServerRegistryProvider({ children }: { children: ReactNode }) {
  const [servers, setServers] = useState<ContentServerMembership[]>([]);
  const [presets, setPresets] = useState<ContentServerStatusPreset[]>([]);
  const [loading, setLoading] = useState(true);

  const refresh = useCallback(async () => {
    const token = localStorage.getItem('snekl_token');
    if (!token) {
      setServers([]);
      setLoading(false);
      return;
    }

    setLoading(true);
    try {
      let list = await authApi.getContentServers();
      if (list.length === 0) {
        await authApi.subscribeContentServer(CONTENT_API);
        list = await authApi.getContentServers();
      }
      setServers(list);
    } catch {
      setServers([]);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    authApi.getContentServerPresets().then(setPresets).catch(() => {});
  }, []);

  useEffect(() => {
    refresh();
  }, [refresh]);

  useEffect(() => {
    const onStorage = (e: StorageEvent) => {
      if (e.key === 'snekl_token') refresh();
    };
    window.addEventListener('storage', onStorage);
    return () => window.removeEventListener('storage', onStorage);
  }, [refresh]);

  const subscribe = useCallback(async (baseUrl: string) => {
    const membership = await authApi.subscribeContentServer(baseUrl);
    setServers((prev) => {
      if (prev.some((s) => s.serverDid === membership.serverDid)) return prev;
      return [...prev, membership];
    });
    return membership;
  }, []);

  const unsubscribe = useCallback(async (serverDid: string) => {
    await authApi.unsubscribeContentServer(serverDid);
    setServers((prev) => prev.filter((s) => s.serverDid !== serverDid));
  }, []);

  const setStatus = useCallback(async (serverDid: string, status: string) => {
    const updated = await authApi.updateContentServerStatus(serverDid, status);
    setServers((prev) => prev.map((s) => (s.serverDid === serverDid ? updated : s)));
  }, []);

  const activeServers = useMemo(
    () => servers.filter((s) => isFeedVisible(s.status)),
    [servers],
  );

  const resolveBaseUrl = useCallback(
    (serverDid: string) => servers.find((s) => s.serverDid === serverDid)?.baseUrl,
    [servers],
  );

  return (
    <ServerRegistryContext.Provider
      value={{
        servers,
        activeServers,
        presets,
        loading,
        refresh,
        subscribe,
        unsubscribe,
        setStatus,
        resolveBaseUrl,
      }}
    >
      {children}
    </ServerRegistryContext.Provider>
  );
}

export function useServerRegistry() {
  const ctx = useContext(ServerRegistryContext);
  if (!ctx) throw new Error('useServerRegistry must be used within ServerRegistryProvider');
  return ctx;
}

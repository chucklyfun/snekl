import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react';
import { authApi } from './api';
import { useAuth } from './AuthContext';
import type { Follow, FollowAlertMode } from './types';

interface FollowsContextValue {
  follows: Follow[];
  loading: boolean;
  isFollowing: (did: string | null | undefined) => boolean;
  follow: (payload: { did: string; displayName?: string | null; username?: string | null }) => Promise<Follow>;
  unfollow: (did: string) => Promise<void>;
  setFollowAlert: (did: string, mode: FollowAlertMode) => Promise<Follow>;
  refresh: () => Promise<void>;
}

const FollowsContext = createContext<FollowsContextValue | null>(null);

export function FollowsProvider({ children }: { children: ReactNode }) {
  const { user } = useAuth();
  const [follows, setFollows] = useState<Follow[]>([]);
  const [loading, setLoading] = useState(false);

  const refresh = useCallback(async () => {
    if (!user) {
      setFollows([]);
      return;
    }
    setLoading(true);
    try {
      setFollows(await authApi.getFollows());
    } catch {
      setFollows([]);
    } finally {
      setLoading(false);
    }
  }, [user]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const isFollowing = useCallback(
    (did: string | null | undefined) => Boolean(did && follows.some((f) => f.did === did)),
    [follows],
  );

  const follow = useCallback(
    async (payload: { did: string; displayName?: string | null; username?: string | null }) => {
      const created = await authApi.upsertFollow(payload);
      setFollows((current) => {
        const without = current.filter((f) => f.did !== created.did);
        return [...without, created];
      });
      return created;
    },
    [],
  );

  const unfollow = useCallback(async (did: string) => {
    await authApi.removeFollow(did);
    setFollows((current) => current.filter((f) => f.did !== did));
  }, []);

  const setFollowAlert = useCallback(async (did: string, mode: FollowAlertMode) => {
    const updated = await authApi.setFollowAlert(did, mode);
    setFollows((current) => current.map((person) => (person.did === updated.did ? updated : person)));
    return updated;
  }, []);

  const value = useMemo(
    () => ({ follows, loading, isFollowing, follow, unfollow, setFollowAlert, refresh }),
    [follows, loading, isFollowing, follow, unfollow, setFollowAlert, refresh],
  );

  return <FollowsContext.Provider value={value}>{children}</FollowsContext.Provider>;
}

export function useFollows() {
  const ctx = useContext(FollowsContext);
  if (!ctx) throw new Error('useFollows must be used within FollowsProvider');
  return ctx;
}

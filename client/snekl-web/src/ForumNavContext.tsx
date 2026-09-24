import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react';
import { aggregateForums, authApi, createContentClient } from './api';
import { useAuth } from './AuthContext';
import { toForumNavItem, type ForumNavItem } from './forumNav';
import { isForumNavGroupOpen, readForumNavOpenState, writeForumNavOpenState } from './forumNavCollapse';
import {
  emptyForumNavLayout,
  type ForumNavLayout,
} from './forumNavFolders';
import { readForumRecents, recordForumVisit } from './forumRecents';
import { useServerRegistry } from './ServerRegistryContext';
import type { ForumFavorite } from './types';

interface ForumNavContextValue {
  forums: ForumNavItem[];
  recents: ReturnType<typeof readForumRecents>;
  layout: ForumNavLayout;
  isGroupOpen: (id: string, defaultOpen?: boolean) => boolean;
  setGroupOpen: (id: string, open: boolean) => void;
  saveLayout: (next: ForumNavLayout) => Promise<void>;
  loading: boolean;
  isFavorite: (serverDid: string, forumId: string) => boolean;
  toggleFavorite: (
    item: Pick<
      ForumNavItem,
      'serverDid' | 'forumId' | 'name' | 'slug' | 'parentSlug' | 'serverName' | 'serverBaseUrl' | 'viewerIsMember'
    >,
  ) => Promise<void>;
  recordVisit: (item: Pick<ForumNavItem, 'serverDid' | 'forumId' | 'name' | 'slug' | 'parentSlug' | 'serverName' | 'serverBaseUrl'>) => void;
  refresh: () => Promise<void>;
}

const ForumNavContext = createContext<ForumNavContextValue | null>(null);

function toSavedLayout(layout: ForumNavLayout): ForumNavLayout {
  return {
    folders: layout.folders ?? [],
    items: layout.items ?? [],
    hidden: layout.hidden ?? [],
    groupAxes: layout.groupAxes,
    peers: layout.peers,
  };
}

export function ForumNavProvider({ children }: { children: ReactNode }) {
  const { user, did } = useAuth();
  const { activeServers, loading: serversLoading } = useServerRegistry();
  const [forums, setForums] = useState<ForumNavItem[]>([]);
  const [recents, setRecents] = useState<ReturnType<typeof readForumRecents>>([]);
  const [layout, setLayout] = useState<ForumNavLayout>(emptyForumNavLayout);
  const [openState, setOpenState] = useState<Record<string, boolean>>({});
  const [loading, setLoading] = useState(false);

  const refresh = useCallback(async () => {
    if (!user) {
      setForums([]);
      setRecents([]);
      setLayout(emptyForumNavLayout());
      setOpenState({});
      return;
    }

    setLoading(true);
    try {
      const [authFavs, groups, originForums] = await Promise.all([
        authApi.getForumFavorites().catch(() => [] as ForumFavorite[]),
        authApi.getForumGroups().catch(() => emptyForumNavLayout()),
        activeServers.length === 0 || serversLoading
          ? Promise.resolve([])
          : aggregateForums(activeServers).catch(() => []),
      ]);
      setLayout(toSavedLayout(groups));
      const items = originForums.map(toForumNavItem);
      const migrated = [...items];
      for (const fav of authFavs) {
        const item = migrated.find(
          (forum) => forum.serverDid === fav.serverDid && forum.forumId === fav.forumId,
        );
        if (item?.viewerIsMember && !item.viewerIsFavorite && item.serverBaseUrl) {
          try {
            const updated = await createContentClient(item.serverBaseUrl).setForumFavorite(
              item.slug,
              true,
              item.parentSlug ?? undefined,
            );
            item.viewerIsFavorite = Boolean(updated.viewerIsFavorite);
          } catch {
            // Origin may reject leftover pins that are no longer joined.
          }
        }
        try {
          await authApi.removeForumFavorite(fav.serverDid, fav.forumId);
        } catch {
          // Ignore stale auth pins.
        }
      }
      setForums(migrated);
      setRecents(readForumRecents(did));
      setOpenState(readForumNavOpenState(did));
    } finally {
      setLoading(false);
    }
  }, [user, did, activeServers, serversLoading]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const isGroupOpen = useCallback(
    (id: string, defaultOpen = true) => isForumNavGroupOpen(openState, id, defaultOpen),
    [openState],
  );

  const setGroupOpen = useCallback(
    (id: string, open: boolean) => {
      setOpenState((prev) => {
        const next = { ...prev, [id]: open };
        writeForumNavOpenState(did, next);
        return next;
      });
    },
    [did],
  );

  const saveLayout = useCallback(async (next: ForumNavLayout) => {
    setLayout(next);
    const saved = await authApi.saveForumGroups(next);
    setLayout(toSavedLayout(saved));
  }, []);

  const isFavorite = useCallback(
    (serverDid: string, forumId: string) =>
      forums.some(
        (forum) => forum.serverDid === serverDid && forum.forumId === forumId && forum.viewerIsFavorite,
      ),
    [forums],
  );

  const toggleFavorite = useCallback(
    async (
      item: Pick<
        ForumNavItem,
        'serverDid' | 'forumId' | 'name' | 'slug' | 'parentSlug' | 'serverName' | 'serverBaseUrl' | 'viewerIsMember'
      >,
    ) => {
      if (!item.viewerIsMember || !item.serverBaseUrl) return;
      const nextFavorite = !isFavorite(item.serverDid, item.forumId);
      const updated = await createContentClient(item.serverBaseUrl).setForumFavorite(
        item.slug,
        nextFavorite,
        item.parentSlug ?? undefined,
      );
      setForums((prev) =>
        prev.map((forum) =>
          forum.serverDid === item.serverDid && forum.forumId === item.forumId
            ? {
                ...forum,
                viewerIsFavorite: Boolean(updated.viewerIsFavorite),
                viewerIsMember: Boolean(updated.viewerIsMember),
              }
            : forum,
        ),
      );
    },
    [isFavorite],
  );

  const recordVisit = useCallback(
    (item: Pick<ForumNavItem, 'serverDid' | 'forumId' | 'name' | 'slug' | 'parentSlug' | 'serverName' | 'serverBaseUrl'>) => {
      setRecents(
        recordForumVisit(did, {
          serverDid: item.serverDid,
          forumId: item.forumId,
          name: item.name,
          slug: item.slug,
          parentSlug: item.parentSlug,
          serverName: item.serverName,
          serverBaseUrl: item.serverBaseUrl,
        }),
      );
    },
    [did],
  );

  const value = useMemo(
    () => ({
      forums,
      recents,
      layout,
      isGroupOpen,
      setGroupOpen,
      saveLayout,
      loading: loading || serversLoading,
      isFavorite,
      toggleFavorite,
      recordVisit,
      refresh,
    }),
    [
      forums,
      recents,
      layout,
      isGroupOpen,
      setGroupOpen,
      saveLayout,
      loading,
      serversLoading,
      isFavorite,
      toggleFavorite,
      recordVisit,
      refresh,
    ],
  );

  return <ForumNavContext.Provider value={value}>{children}</ForumNavContext.Provider>;
}

export function useForumNav() {
  const ctx = useContext(ForumNavContext);
  if (!ctx) throw new Error('useForumNav must be used within ForumNavProvider');
  return ctx;
}

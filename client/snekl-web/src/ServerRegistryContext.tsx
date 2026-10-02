import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react';
import { authApi, createContentClient, hubApi } from './api';
import { readBoundToken } from './deviceKey';
import { rewriteLoopbackForPage } from './hosts';
import {
  federationOriginMembership,
  homeSubscriptionUrls,
  isFeedVisibleStatus,
  mergeNetworkServers,
  originForNav,
  shouldBootstrapHomeSubscriptions,
} from './serverRegistry';
import type {
  ContentServerMembership,
  ContentServerStatusPreset,
  FederationMembership,
  FederationOriginGroup,
} from './types';

interface ServerRegistryContextValue {
  servers: ContentServerMembership[];
  activeServers: ContentServerMembership[];
  federations: FederationMembership[];
  activeFederations: FederationMembership[];
  federationOriginGroups: FederationOriginGroup[];
  presets: ContentServerStatusPreset[];
  loading: boolean;
  refresh: () => Promise<void>;
  subscribe: (baseUrl: string) => Promise<ContentServerMembership>;
  unsubscribe: (serverDid: string) => Promise<void>;
  setStatus: (serverDid: string, status: string) => Promise<void>;
  subscribeFederation: (baseUrl: string) => Promise<FederationMembership>;
  unsubscribeFederation: (baseUrl: string) => Promise<void>;
  setFederationStatus: (baseUrl: string, status: string) => Promise<void>;
  resolveBaseUrl: (serverDid: string) => string | undefined;
}

const ServerRegistryContext = createContext<ServerRegistryContextValue | null>(null);

function withReachableBaseUrl(server: ContentServerMembership): ContentServerMembership {
  return { ...server, baseUrl: rewriteLoopbackForPage(server.baseUrl) };
}

/** Hubs the account subscribed to, plus hubs accepted by subscribed content origins. */
async function collectHubBaseUrls(
  feds: FederationMembership[],
  contentServers: ContentServerMembership[],
): Promise<string[]> {
  const hubs = new Set<string>();
  for (const fed of feds.filter((f) => isFeedVisibleStatus(f.status))) {
    hubs.add(rewriteLoopbackForPage(fed.baseUrl));
  }

  const visibleContent = contentServers.filter((s) => isFeedVisibleStatus(s.status));
  await Promise.all(
    visibleContent.map(async (server) => {
      try {
        const node = await createContentClient(server.baseUrl).getNode();
        if (node.hubBaseUrl) hubs.add(rewriteLoopbackForPage(node.hubBaseUrl));
      } catch {
        // Origin unreachable or older node without hubBaseUrl — skip.
      }
    }),
  );

  return [...hubs];
}

async function fetchFederationOriginState(
  feds: FederationMembership[],
  contentServers: ContentServerMembership[],
): Promise<{ origins: ContentServerMembership[]; groups: FederationOriginGroup[] }> {
  const hubUrls = await collectHubBaseUrls(feds, contentServers);
  const nameByUrl = new Map(feds.map((f) => [rewriteLoopbackForPage(f.baseUrl), f.name]));
  const configs = await Promise.all(
    hubUrls.map(async (hubUrl) => {
      const config = await hubApi.getConfig(hubUrl).catch(() => null);
      return { hubUrl, config };
    }),
  );

  const origins: ContentServerMembership[] = [];
  const seenFlat = new Set<string>();
  const groups: FederationOriginGroup[] = [];

  for (const { hubUrl, config } of configs) {
    if (!config) continue;
    const groupOrigins: ContentServerMembership[] = [];
    const seenInGroup = new Set<string>();
    for (const origin of config.servers ?? []) {
      const membership = originForNav(
        federationOriginMembership({
          ...origin,
          baseUrl: rewriteLoopbackForPage(origin.baseUrl),
        }),
        contentServers,
      );
      if (!membership || seenInGroup.has(membership.serverDid)) continue;
      seenInGroup.add(membership.serverDid);
      groupOrigins.push(membership);
      if (!seenFlat.has(membership.serverDid)) {
        seenFlat.add(membership.serverDid);
        origins.push(membership);
      }
    }
    groups.push({
      baseUrl: rewriteLoopbackForPage(config.baseUrl || hubUrl),
      name: config.name || nameByUrl.get(hubUrl) || hubUrl,
      origins: groupOrigins,
    });
  }

  return { origins, groups };
}

export function ServerRegistryProvider({ children }: { children: ReactNode }) {
  const [servers, setServers] = useState<ContentServerMembership[]>([]);
  const [federations, setFederations] = useState<FederationMembership[]>([]);
  const [federationOrigins, setFederationOrigins] = useState<ContentServerMembership[]>([]);
  const [federationOriginGroups, setFederationOriginGroups] = useState<FederationOriginGroup[]>([]);
  const [presets, setPresets] = useState<ContentServerStatusPreset[]>([]);
  const [loading, setLoading] = useState(true);
  const homeBootstrapAttempted = useRef(false);

  const applyFederationState = useCallback(
    async (nextFeds: FederationMembership[], nextServers: ContentServerMembership[]) => {
      const state = await fetchFederationOriginState(nextFeds, nextServers);
      setFederationOrigins(state.origins);
      setFederationOriginGroups(state.groups);
    },
    [],
  );

  const refresh = useCallback(async () => {
    const token = readBoundToken('snekl_token');
    if (!token) {
      homeBootstrapAttempted.current = false;
      setServers([]);
      setFederations([]);
      setFederationOrigins([]);
      setFederationOriginGroups([]);
      setLoading(false);
      return;
    }

    setLoading(true);
    try {
      const [list, feds] = await Promise.all([
        authApi.getContentServers(),
        authApi.getFederations().catch(() => [] as FederationMembership[]),
      ]);
      setServers(list);
      setFederations(feds);
      await applyFederationState(feds, list);
      const visible = list.filter((s) => isFeedVisibleStatus(s.status));
      await Promise.all(
        visible.map((server) =>
          createContentClient(rewriteLoopbackForPage(server.baseUrl)).syncHubMemberships().catch(() => undefined),
        ),
      );
    } catch (err) {
      const message = err instanceof Error ? err.message : '';
      if (message.includes('(401)')) {
        localStorage.removeItem('snekl_token');
      }
      setServers([]);
      setFederations([]);
      setFederationOrigins([]);
      setFederationOriginGroups([]);
    } finally {
      setLoading(false);
    }
  }, [applyFederationState]);

  useEffect(() => {
    authApi.getContentServerPresets().then(setPresets).catch(() => {});
  }, []);

  useEffect(() => {
    refresh();
  }, [refresh]);

  useEffect(() => {
    const onStorage = (e: StorageEvent) => {
      if (e.key === 'snekl_token' || e.key === 'snekl_auth_url') refresh();
    };
    window.addEventListener('storage', onStorage);
    return () => window.removeEventListener('storage', onStorage);
  }, [refresh]);

  const subscribe = useCallback(async (baseUrl: string) => {
    const membership = await authApi.subscribeContentServer(baseUrl);
    let nextServers: ContentServerMembership[] = [];
    setServers((prev) => {
      nextServers = prev.some((s) => s.serverDid === membership.serverDid)
        ? prev
        : [...prev, membership];
      return nextServers;
    });
    await applyFederationState(federations, nextServers);
    return membership;
  }, [federations, applyFederationState]);

  const unsubscribe = useCallback(async (serverDid: string) => {
    await authApi.unsubscribeContentServer(serverDid);
    let nextServers: ContentServerMembership[] = [];
    setServers((prev) => {
      nextServers = prev.filter((s) => s.serverDid !== serverDid);
      return nextServers;
    });
    await applyFederationState(federations, nextServers);
  }, [federations, applyFederationState]);

  const setStatus = useCallback(async (serverDid: string, status: string) => {
    const updated = await authApi.updateContentServerStatus(serverDid, status);
    let nextServers: ContentServerMembership[] = [];
    setServers((prev) => {
      nextServers = prev.map((s) => (s.serverDid === serverDid ? updated : s));
      return nextServers;
    });
    await applyFederationState(federations, nextServers);
  }, [federations, applyFederationState]);

  const subscribeFederation = useCallback(async (baseUrl: string) => {
    const membership = await authApi.subscribeFederation(baseUrl);
    let next: FederationMembership[] = [];
    setFederations((prev) => {
      next = prev.some((s) => s.baseUrl === membership.baseUrl) ? prev : [...prev, membership];
      return next;
    });
    await applyFederationState(next, servers);
    return membership;
  }, [servers, applyFederationState]);

  const unsubscribeFederation = useCallback(async (baseUrl: string) => {
    await authApi.unsubscribeFederation(baseUrl);
    let next: FederationMembership[] = [];
    setFederations((prev) => {
      next = prev.filter((s) => s.baseUrl !== baseUrl);
      return next;
    });
    await applyFederationState(next, servers);
  }, [servers, applyFederationState]);

  const setFederationStatus = useCallback(async (baseUrl: string, status: string) => {
    const updated = await authApi.updateFederationStatus(baseUrl, status);
    let next: FederationMembership[] = [];
    setFederations((prev) => {
      next = prev.map((s) => (s.baseUrl === updated.baseUrl ? updated : s));
      return next;
    });
    await applyFederationState(next, servers);
  }, [servers, applyFederationState]);

  useEffect(() => {
    if (
      !shouldBootstrapHomeSubscriptions({
        hasToken: Boolean(readBoundToken('snekl_token')),
        loading,
        serverCount: servers.length,
        federationCount: federations.length,
        alreadyAttempted: homeBootstrapAttempted.current,
      })
    ) {
      return;
    }

    homeBootstrapAttempted.current = true;
    const urls = homeSubscriptionUrls();
    void (async () => {
      try {
        await subscribe(rewriteLoopbackForPage(urls.content));
      } catch {
        // Home origin missing or already added — Browse still explains an empty list.
      }
      try {
        await subscribeFederation(rewriteLoopbackForPage(urls.hub));
      } catch {
        // Hub is optional; a direct content subscription is enough to list forums.
      }
    })();
  }, [loading, servers.length, federations.length, subscribe, subscribeFederation]);

  const activeServers = useMemo(
    () => mergeNetworkServers(servers.map(withReachableBaseUrl), federationOrigins).map(withReachableBaseUrl),
    [servers, federationOrigins],
  );

  const activeFederations = useMemo(
    () => federations.filter((s) => isFeedVisibleStatus(s.status)),
    [federations],
  );

  const resolveBaseUrl = useCallback(
    (serverDid: string) => {
      const raw =
        servers.find((s) => s.serverDid === serverDid)?.baseUrl ??
        federationOrigins.find((s) => s.serverDid === serverDid)?.baseUrl;
      return raw ? rewriteLoopbackForPage(raw) : undefined;
    },
    [servers, federationOrigins],
  );

  return (
    <ServerRegistryContext.Provider
      value={{
        servers,
        activeServers,
        federations,
        activeFederations,
        federationOriginGroups,
        presets,
        loading,
        refresh,
        subscribe,
        unsubscribe,
        setStatus,
        subscribeFederation,
        unsubscribeFederation,
        setFederationStatus,
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

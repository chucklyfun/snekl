import type {
  AggregatedForum,
  ContentServerMembership,
  ContentServerStatusPreset,
  CuratedList,
  Forum,
  ListAnnotation,
  NodeInfo,
  Peer,
  Post,
  RingSession,
  StatusPreset,
  UserPresentation,
  UserStatus,
} from './types';

export const CONTENT_API = import.meta.env.VITE_API_URL ?? 'http://localhost:5001';
export const AUTH_API = import.meta.env.VITE_AUTH_URL ?? 'http://localhost:5100';

function getToken(): string | null {
  return localStorage.getItem('snekl_token');
}

async function request<T>(baseUrl: string, path: string, options: RequestInit = {}): Promise<T> {
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    ...(options.headers as Record<string, string> | undefined),
  };

  const token = getToken();
  if (token) headers.Authorization = `Bearer ${token}`;

  const response = await fetch(`${baseUrl}${path}`, { ...options, headers });

  if (!response.ok) {
    const body = await response.json().catch(() => ({}));
    throw new Error(body.error ?? `Request failed (${response.status})`);
  }

  if (response.status === 204) return undefined as T;
  return response.json();
}

export const authApi = {
  register: (payload: {
    username: string;
    email: string;
    password: string;
    displayName: string;
    did: string;
    keyOwnershipProof: string;
  }) =>
    request<RingSession>(AUTH_API, '/api/auth/register', {
      method: 'POST',
      body: JSON.stringify(payload),
    }),

  login: (username: string, password: string) =>
    request<RingSession>(AUTH_API, '/api/auth/login', {
      method: 'POST',
      body: JSON.stringify({ username, password }),
    }),

  me: () => request<RingSession['person']>(AUTH_API, '/api/me'),

  getContentServers: () =>
    request<ContentServerMembership[]>(AUTH_API, '/api/me/content-servers'),

  getContentServerPresets: () =>
    request<ContentServerStatusPreset[]>(AUTH_API, '/api/me/content-servers/presets'),

  subscribeContentServer: (baseUrl: string) =>
    request<ContentServerMembership>(AUTH_API, '/api/me/content-servers', {
      method: 'POST',
      body: JSON.stringify({ baseUrl }),
    }),

  updateContentServerStatus: (serverDid: string, status: string) =>
    request<ContentServerMembership>(
      AUTH_API,
      `/api/me/content-servers/${encodeURIComponent(serverDid)}`,
      {
        method: 'PUT',
        body: JSON.stringify({ status }),
      },
    ),

  unsubscribeContentServer: (serverDid: string) =>
    request<void>(AUTH_API, `/api/me/content-servers/${encodeURIComponent(serverDid)}`, {
      method: 'DELETE',
    }),
};

export function createContentClient(baseUrl: string) {
  const root = baseUrl.replace(/\/$/, '');
  return {
    me: () => request<UserPresentation>(root, '/api/auth/me'),
    getUser: (username: string) => request<UserPresentation>(root, `/api/users/${username}`),
    updateFlair: (flair: {
      emoji?: string;
      iconUrl?: string;
      label?: string;
      nameColor?: string;
      nameStyle?: string;
    }) =>
      request<UserPresentation>(root, '/api/users/me/flair', {
        method: 'PUT',
        body: JSON.stringify(flair),
      }),
    getStatusPresets: () => request<StatusPreset[]>(root, '/api/users/status/presets'),
    setStatus: (status: {
      preset: string;
      message?: string;
      emoji?: string;
      iconUrl?: string;
      linkUrl?: string;
      eventName?: string;
      expiresAt: string;
      avatarOverlay?: {
        kind: string;
        emoji?: string;
        color?: string;
        ariaLabel?: string;
      };
      displayNameSuffix?: string;
      nameColorOverride?: string;
    }) =>
      request<UserStatus>(root, '/api/users/me/status', {
        method: 'PUT',
        body: JSON.stringify(status),
      }),
    clearStatus: () => request<void>(root, '/api/users/me/status', { method: 'DELETE' }),
    getNode: () => request<NodeInfo>(root, '/api/node'),
    getForums: () => request<Forum[]>(root, '/api/forums'),
    getForum: (slug: string) => request<Forum>(root, `/api/forums/${slug}`),
    createForum: (name: string, slug: string, description: string) =>
      request<Forum>(root, '/api/forums', {
        method: 'POST',
        body: JSON.stringify({ name, slug, description }),
      }),
    getForumThreads: (forumId: string) => request<Post[]>(root, `/api/posts/forum/${forumId}`),
    getThread: (postId: string) => request<Post>(root, `/api/posts/${postId}`),
    createPost: (forumId: string, message: string, parentPostId?: string) =>
      request<Post>(root, '/api/posts', {
        method: 'POST',
        body: JSON.stringify({ forumId, message, parentPostId: parentPostId ?? null }),
      }),
    getPeers: () => request<Peer[]>(root, '/api/federation/peers'),
    subscribePeer: (peerBaseUrl: string) =>
      request<Peer>(root, '/api/federation/peers', {
        method: 'POST',
        body: JSON.stringify({ baseUrl: peerBaseUrl }),
      }),
    unsubscribePeer: (peerId: string) =>
      request<void>(root, `/api/federation/peers/${peerId}`, { method: 'DELETE' }),
    getLists: () => request<CuratedList[]>(root, '/api/lists'),
    createList: (data: {
      name: string;
      description: string;
      defaultBehavior: string;
      defaultFlairEmoji?: string;
      isPublic: boolean;
    }) =>
      request<CuratedList>(root, '/api/lists', {
        method: 'POST',
        body: JSON.stringify(data),
      }),
    subscribeList: (listId: string, appliedBehavior: string) =>
      request<void>(root, `/api/lists/${listId}/subscribe`, {
        method: 'POST',
        body: JSON.stringify({ appliedBehavior }),
      }),
    unsubscribeList: (listId: string) =>
      request<void>(root, `/api/lists/${listId}/subscribe`, { method: 'DELETE' }),
    addListEntry: (
      listId: string,
      data: {
        targetUserId: string;
        severity: number;
        tooltipText: string;
        flairEmoji?: string;
        usernameColor?: string;
      },
    ) =>
      request<ListAnnotation>(root, `/api/lists/${listId}/entries`, {
        method: 'POST',
        body: JSON.stringify(data),
      }),
  };
}

export async function aggregateForums(
  servers: ContentServerMembership[],
): Promise<AggregatedForum[]> {
  const results = await Promise.allSettled(
    servers.map(async (server) => {
      const client = createContentClient(server.baseUrl);
      const forums = await client.getForums();
      return forums.map(
        (forum): AggregatedForum => ({
          ...forum,
          serverDid: server.serverDid,
          serverName: server.name,
          serverBaseUrl: server.baseUrl,
          serverStatus: server.status,
        }),
      );
    }),
  );

  const merged: AggregatedForum[] = [];
  for (const result of results) {
    if (result.status === 'fulfilled') merged.push(...result.value);
  }

  return merged.sort((a, b) => {
    const aFav = a.serverStatus === 'favorite' ? 0 : 1;
    const bFav = b.serverStatus === 'favorite' ? 0 : 1;
    if (aFav !== bFav) return aFav - bFav;
    return new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime();
  });
}

const defaultClient = createContentClient(CONTENT_API);

export const api = {
  register: authApi.register,
  login: authApi.login,
  me: () => defaultClient.me(),
  getUser: (username: string) => defaultClient.getUser(username),
  updateFlair: defaultClient.updateFlair,
  getStatusPresets: defaultClient.getStatusPresets,
  setStatus: defaultClient.setStatus,
  clearStatus: defaultClient.clearStatus,
  getNode: defaultClient.getNode,
  getForums: defaultClient.getForums,
  getForum: defaultClient.getForum,
  createForum: defaultClient.createForum,
  getForumThreads: defaultClient.getForumThreads,
  getThread: defaultClient.getThread,
  createPost: defaultClient.createPost,
  getPeers: defaultClient.getPeers,
  subscribePeer: defaultClient.subscribePeer,
  unsubscribePeer: defaultClient.unsubscribePeer,
  getLists: defaultClient.getLists,
  createList: defaultClient.createList,
  subscribeList: defaultClient.subscribeList,
  unsubscribeList: defaultClient.unsubscribeList,
  addListEntry: defaultClient.addListEntry,
};

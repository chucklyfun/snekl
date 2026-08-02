import type {
  AggregatedForum,
  AuthMeResponse,
  ContentServerMembership,
  ContentServerStatusPreset,
  CuratedList,
  Forum,
  ForumFlairTemplate,
  ForumMember,
  ForumRole,
  ListAnnotation,
  NodeInfo,
  Peer,
  Post,
  RingSession,
  ServerMember,
  ServerPermissionsInfo,
  ServerRole,
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

export function hasPermission(permissions: string[] | undefined, name: string): boolean {
  return permissions?.includes(name) ?? false;
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

  login: (email: string, password: string) =>
    request<RingSession>(AUTH_API, '/api/auth/login', {
      method: 'POST',
      body: JSON.stringify({ email, password }),
    }),

  forgotPassword: (email: string) =>
    request<{ message: string }>(AUTH_API, '/api/auth/forgot-password', {
      method: 'POST',
      body: JSON.stringify({ email }),
    }),

  resetPassword: (token: string, newPassword: string) =>
    request<{ message: string }>(AUTH_API, '/api/auth/reset-password', {
      method: 'POST',
      body: JSON.stringify({ token, newPassword }),
    }),

  verifyEmail: (token: string) =>
    request<{ message: string }>(AUTH_API, '/api/auth/verify-email', {
      method: 'POST',
      body: JSON.stringify({ token }),
    }),

  me: () => request<RingSession['person']>(AUTH_API, '/api/me'),

  updateProfile: (payload: {
    username: string;
    email: string;
    displayName: string;
    bio: string;
    disambiguationTag?: string | null;
  }) =>
    request<{ person: RingSession['person']; accessToken: string }>(AUTH_API, '/api/me/profile', {
      method: 'PUT',
      body: JSON.stringify({
        username: payload.username,
        email: payload.email,
        displayName: payload.displayName,
        bio: payload.bio,
        disambiguationTag: payload.disambiguationTag ?? null,
      }),
    }),

  resendVerificationEmail: () =>
    request<{ message: string }>(AUTH_API, '/api/me/send-verification-email', {
      method: 'POST',
      body: JSON.stringify({}),
    }),

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

  function forumBase(slug: string, parentSlug?: string) {
    const qs = parentSlug ? `?parentSlug=${encodeURIComponent(parentSlug)}` : '';
    return `/api/forums/${slug}${qs}`;
  }

  return {
    me: () => request<AuthMeResponse>(root, '/api/auth/me'),
    getServerPermissions: () => request<ServerPermissionsInfo>(root, '/api/server/me/permissions'),
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
    getForum: (slug: string, parentSlug?: string) =>
      request<Forum>(root, forumBase(slug, parentSlug)),
    createForum: (name: string, slug: string, description: string, parentForumId?: string) =>
      request<Forum>(root, '/api/forums', {
        method: 'POST',
        body: JSON.stringify({ name, slug, description, parentForumId: parentForumId ?? null }),
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

    getServerRoles: () => request<ServerRole[]>(root, '/api/server/roles'),
    createServerRole: (data: { name: string; color?: string; position: number; permissions: string[] }) =>
      request<ServerRole>(root, '/api/server/roles', { method: 'POST', body: JSON.stringify(data) }),
    updateServerRole: (roleId: string, data: { name: string; color?: string; position: number; permissions: string[] }) =>
      request<ServerRole>(root, `/api/server/roles/${roleId}`, { method: 'PUT', body: JSON.stringify(data) }),
    deleteServerRole: (roleId: string) =>
      request<void>(root, `/api/server/roles/${roleId}`, { method: 'DELETE' }),
    getServerMembers: () => request<ServerMember[]>(root, '/api/server/members'),
    assignServerRoles: (userId: string, roleIds: string[]) =>
      request<void>(root, `/api/server/members/${userId}/roles`, {
        method: 'PUT',
        body: JSON.stringify({ roleIds }),
      }),
    assignServerRolesByUsername: (username: string, roleIds: string[]) =>
      request<void>(root, `/api/server/members/by-username/${encodeURIComponent(username)}/roles`, {
        method: 'PUT',
        body: JSON.stringify({ roleIds }),
      }),

    getForumRoles: (slug: string, parentSlug?: string) =>
      request<ForumRole[]>(root, `${forumBase(slug, parentSlug)}/roles`),
    createForumRole: (slug: string, data: { name: string; color?: string; position: number; permissions: string[] }, parentSlug?: string) =>
      request<ForumRole>(root, `${forumBase(slug, parentSlug)}/roles`, { method: 'POST', body: JSON.stringify(data) }),
    getForumMembers: (slug: string, parentSlug?: string) =>
      request<ForumMember[]>(root, `${forumBase(slug, parentSlug)}/members`),
    assignForumRoles: (slug: string, userId: string, roleIds: string[], parentSlug?: string) =>
      request<void>(root, `${forumBase(slug, parentSlug)}/members/${userId}/roles`, {
        method: 'PUT',
        body: JSON.stringify({ roleIds }),
      }),
    getFlairTemplates: (slug: string, parentSlug?: string) =>
      request<ForumFlairTemplate[]>(root, `${forumBase(slug, parentSlug)}/flair-templates`),
    createFlairTemplate: (
      slug: string,
      data: { emoji?: string; label?: string; nameColor?: string; position: number },
      parentSlug?: string,
    ) =>
      request<ForumFlairTemplate>(root, `${forumBase(slug, parentSlug)}/flair-templates`, {
        method: 'POST',
        body: JSON.stringify(data),
      }),
    setMyAlias: (slug: string, alias: string, parentSlug?: string) =>
      request<void>(root, `${forumBase(slug, parentSlug)}/members/me/alias`, {
        method: 'PUT',
        body: JSON.stringify({ alias }),
      }),
    updateForumSettings: (
      slug: string,
      data: { name?: string; description?: string; allowAliases?: boolean },
      parentSlug?: string,
    ) =>
      request<void>(root, `${forumBase(slug, parentSlug)}/settings`, {
        method: 'PUT',
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

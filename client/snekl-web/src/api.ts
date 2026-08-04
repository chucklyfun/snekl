import type {
  AggregatedForum,
  AggregatedPost,
  AuthMeResponse,
  ContentServerMembership,
  ContentServerStatusPreset,
  CuratedList,
  Forum,
  ForumBan,
  ForumFlairTemplate,
  ForumMember,
  ForumRole,
  ListAnnotation,
  NodeInfo,
  NetworkServer,
  Peer,
  Post,
  PostReport,
  RecentThread,
  RingSession,
  ServerMember,
  ServerPermissionsInfo,
  ServerRole,
  StatusPreset,
  UserPresentation,
  UserStatus,
} from './types';
import { type PostSort } from './ranking';

function recentThreadsPath(sort: PostSort, page: number, pageSize: number, servers?: string[]): string {
  const params = new URLSearchParams({
    sort,
    page: String(page),
    pageSize: String(pageSize),
  });
  for (const server of servers ?? []) {
    params.append('servers', server);
  }
  return `/api/posts/recent?${params}`;
}

export const CONTENT_API = (import.meta.env.VITE_API_URL || 'http://localhost:5001').replace(/\/$/, '');
export const AUTH_API = (import.meta.env.VITE_AUTH_URL || 'http://localhost:5100').replace(/\/$/, '');

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

  const url = `${baseUrl}${path}`;
  let response: Response;
  try {
    response = await fetch(url, { ...options, headers });
  } catch {
    throw new Error(
      `Can't reach ${baseUrl}. Is the API running? Try: dotnet run --project src/Snekl.Server --no-launch-profile -- status`,
    );
  }

  if (!response.ok) {
    const body = await response.json().catch(() => ({}));
    throw new Error(body.error ?? `Request failed (${response.status}) at ${url}`);
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
    request<{ message: string; resetUrl?: string | null }>(AUTH_API, '/api/auth/forgot-password', {
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

  devResetPassword: (email: string, newPassword: string) =>
    request<{ message: string }>(AUTH_API, '/api/auth/dev/reset-password', {
      method: 'POST',
      body: JSON.stringify({ email, newPassword }),
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

  function forumApiPath(slug: string, suffix = '') {
    return `/api/forums/${slug}${suffix}`;
  }

  function forumParentQuery(parentSlug?: string) {
    return parentSlug ? `?parentSlug=${encodeURIComponent(parentSlug)}` : '';
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
      request<Forum>(root, `${forumApiPath(slug)}${forumParentQuery(parentSlug)}`),
    createForum: (name: string, slug: string, description: string, parentForumId?: string) =>
      request<Forum>(root, '/api/forums', {
        method: 'POST',
        body: JSON.stringify({ name, slug, description, parentForumId: parentForumId ?? null }),
      }),
    getForumThreads: (forumId: string, sort: PostSort = 'hot') =>
      request<Post[]>(root, `/api/posts/forum/${forumId}?sort=${sort}`),
    getRecentThreads: (sort: PostSort = 'hot', page = 1, pageSize = 20, servers?: string[]) =>
      request<RecentThread[]>(root, recentThreadsPath(sort, page, pageSize, servers)),
    getNetworkServers: () => request<NetworkServer[]>(root, '/api/network/servers'),
    getThread: (postId: string) => request<Post>(root, `/api/posts/${postId}`),
    getPostRevisions: (postId: string) => request<import('./types').PostRevision[]>(root, `/api/posts/${postId}/revisions`),
    updatePost: (postId: string, message: string) =>
      request<Post>(root, `/api/posts/${postId}`, {
        method: 'PUT',
        body: JSON.stringify({ message }),
      }),
    deletePost: (postId: string, hard = false) =>
      request<Post | void>(root, `/api/posts/${postId}${hard ? '?hard=true' : ''}`, {
        method: 'DELETE',
      }),
    setThreadLocked: (postId: string, locked: boolean) =>
      request<Post>(root, `/api/posts/${postId}/lock?locked=${locked}`, { method: 'PUT' }),
    setThreadPinned: (postId: string, pinned: boolean) =>
      request<Post>(root, `/api/posts/${postId}/pin?pinned=${pinned}`, { method: 'PUT' }),
    reportPost: (postId: string, reason: string) =>
      request<PostReport>(root, `/api/posts/${postId}/report`, {
        method: 'POST',
        body: JSON.stringify({ reason }),
      }),
    toggleUpvote: (postId: string) =>
      request<{ upvoteCount: number; viewerHasUpvoted: boolean }>(root, `/api/posts/${postId}/upvote`, {
        method: 'POST',
      }),
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
        label: string;
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
      request<ForumRole[]>(root, `${forumApiPath(slug, '/roles')}${forumParentQuery(parentSlug)}`),
    createForumRole: (slug: string, data: { name: string; color?: string; position: number; permissions: string[] }, parentSlug?: string) =>
      request<ForumRole>(root, `${forumApiPath(slug, '/roles')}${forumParentQuery(parentSlug)}`, {
        method: 'POST',
        body: JSON.stringify(data),
      }),
    getForumMembers: (slug: string, parentSlug?: string) =>
      request<ForumMember[]>(root, `${forumApiPath(slug, '/members')}${forumParentQuery(parentSlug)}`),
    assignForumRoles: (slug: string, userId: string, roleIds: string[], parentSlug?: string) =>
      request<void>(root, `${forumApiPath(slug, `/members/${userId}/roles`)}${forumParentQuery(parentSlug)}`, {
        method: 'PUT',
        body: JSON.stringify({ roleIds }),
      }),
    assignForumRolesByUsername: (slug: string, username: string, roleIds: string[], parentSlug?: string) =>
      request<void>(root, `${forumApiPath(slug, `/members/by-username/${encodeURIComponent(username)}/roles`)}${forumParentQuery(parentSlug)}`, {
        method: 'PUT',
        body: JSON.stringify({ roleIds }),
      }),
    getForumBans: (slug: string, parentSlug?: string) =>
      request<ForumBan[]>(root, `${forumApiPath(slug, '/bans')}${forumParentQuery(parentSlug)}`),
    banForumMember: (slug: string, data: { username: string; reason?: string; expiresAt?: string }, parentSlug?: string) =>
      request<void>(root, `${forumApiPath(slug, '/bans')}${forumParentQuery(parentSlug)}`, {
        method: 'POST',
        body: JSON.stringify(data),
      }),
    unbanForumMember: (slug: string, userId: string, parentSlug?: string) =>
      request<void>(root, `${forumApiPath(slug, `/bans/${userId}`)}${forumParentQuery(parentSlug)}`, {
        method: 'DELETE',
      }),
    getForumReports: (slug: string, parentSlug?: string) =>
      request<PostReport[]>(root, `${forumApiPath(slug, '/reports')}${forumParentQuery(parentSlug)}`),
    dismissForumReport: (slug: string, reportId: string, parentSlug?: string) =>
      request<void>(root, `${forumApiPath(slug, `/reports/${reportId}/dismiss`)}${forumParentQuery(parentSlug)}`, {
        method: 'POST',
      }),
    resolveForumReport: (slug: string, reportId: string, parentSlug?: string) =>
      request<void>(root, `${forumApiPath(slug, `/reports/${reportId}/resolve`)}${forumParentQuery(parentSlug)}`, {
        method: 'POST',
      }),
    getFlairTemplates: (slug: string, parentSlug?: string) =>
      request<ForumFlairTemplate[]>(root, `${forumApiPath(slug, '/flair-templates')}${forumParentQuery(parentSlug)}`),
    createFlairTemplate: (
      slug: string,
      data: { emoji?: string; label?: string; nameColor?: string; position: number },
      parentSlug?: string,
    ) =>
      request<ForumFlairTemplate>(root, `${forumApiPath(slug, '/flair-templates')}${forumParentQuery(parentSlug)}`, {
        method: 'POST',
        body: JSON.stringify(data),
      }),
    setMyAlias: (slug: string, alias: string, parentSlug?: string) =>
      request<void>(root, `${forumApiPath(slug, '/members/me/alias')}${forumParentQuery(parentSlug)}`, {
        method: 'PUT',
        body: JSON.stringify({ alias }),
      }),
    updateForumSettings: (
      slug: string,
      data: { name?: string; description?: string; allowAliases?: boolean },
      parentSlug?: string,
    ) =>
      request<void>(root, `${forumApiPath(slug, '/settings')}${forumParentQuery(parentSlug)}`, {
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

/** Query one server with the list of servers the user cares about; it aggregates owned posts. */
export async function fetchAggregatedRecentPosts(
  memberships: ContentServerMembership[],
  sort: PostSort = 'hot',
  pageSize = 20,
): Promise<AggregatedPost[]> {
  if (memberships.length === 0) return [];

  const servers = memberships.map((s) => s.baseUrl);
  const client = createContentClient(servers[0]);
  const nameByDid = new Map(memberships.map((s) => [s.serverDid, s.name]));
  const nameByUrl = new Map(memberships.map((s) => [s.baseUrl.replace(/\/$/, ''), s.name]));

  const threads = await client.getRecentThreads(sort, 1, pageSize, servers);
  return threads.map(
    (thread): AggregatedPost => ({
      ...thread.post,
      serverDid: thread.ownerDid,
      serverName:
        nameByDid.get(thread.ownerDid) ??
        nameByUrl.get(thread.ownerBaseUrl.replace(/\/$/, '')) ??
        thread.ownerBaseUrl,
      serverBaseUrl: thread.ownerBaseUrl,
      forumSlug: thread.forumSlug,
      forumName: thread.forumName,
      parentForumSlug: thread.parentForumSlug,
    }),
  );
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
  getRecentThreads: defaultClient.getRecentThreads,
  getNetworkServers: defaultClient.getNetworkServers,
  getThread: defaultClient.getThread,
  toggleUpvote: defaultClient.toggleUpvote,
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

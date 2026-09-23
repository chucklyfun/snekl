import type {
  AggregatedForum,
  AggregatedPost,
  AuthMeResponse,
  ContentServerMembership,
  ContentServerStatusPreset,
  CuratedList,
  FederationConfig,
  FederationMembership,
  Follow,
  Forum,
  ForumBan,
  ForumFavorite,
  ForumFlairTemplate,
  ForumMember,
  ForumRole,
  HubFeedCard,
  HubSearchHit,
  AggregatedSearchHit,
  DirectConversation,
  DirectConversationRequest,
  DirectMessage,
  DirectMessageEligibility,
  DirectMessageForumPreference,
  DirectMessageForumPreferences,
  DirectMessageInbox,
  ListEntry,
  UserListEntryHit,
  MediaAttachment,
  PagedResult,
  NetworkServer,
  NodeInfo,
  NsfwAgeCheckStatus,
  Post,
  PostReaction,
  PostReport,
  RecentThread,
  RingSession,
  SearchHit,
  ServerMember,
  ServerPermissionsInfo,
  ServerRole,
  StatusPreset,
  UserPresentation,
  UserStatus,
} from './types';
import { type PostSort } from './ranking';
import { getAuthBaseUrl, normalizeBaseUrl, rewriteLoopbackForPage, rewriteLoopbackUrlFields } from './hosts';
import type { ForumNavLayout } from './forumNavFolders';

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

export function authBase(): string {
  return getAuthBaseUrl();
}

export type HubInfo = {
  name: string;
  baseUrl: string;
  authBaseUrl: string;
  mapVersion: number;
};

export type HubRegisteredServer = {
  serverDid: string;
  baseUrl: string;
  name: string;
  registeredAt: string;
  ackedMapVersion: number;
};

export type HubFederationMap = {
  mapVersion: number;
  servers: HubRegisteredServer[];
};

export const hubApi = {
  getInfo: (baseUrl: string) => hubRequest<HubInfo>(baseUrl, '/api/hub'),
  getConfig: (baseUrl: string) => hubRequest<FederationConfig>(baseUrl, '/.well-known/snekl-federation.json'),
    getFeed: (baseUrl: string, opts?: { sort?: string; limit?: number; offset?: number }) => {
    const query = new URLSearchParams();
    if (opts?.sort) query.set('sort', opts.sort);
    if (opts?.limit) query.set('limit', String(opts.limit));
    if (opts?.offset) query.set('offset', String(opts.offset));
    const suffix = query.size > 0 ? `?${query.toString()}` : '';
    const token = getToken();
    const headers: Record<string, string> = {};
    if (token) headers.Authorization = `Bearer ${token}`;
    return hubRequest<HubFeedCard[]>(baseUrl, `/api/hub/feed${suffix}`, { headers });
  },
  search: (baseUrl: string, q: string, opts?: { limit?: number; offset?: number }) => {
    const query = new URLSearchParams({ q });
    if (opts?.limit) query.set('limit', String(opts.limit));
    if (opts?.offset) query.set('offset', String(opts.offset));
    const token = getToken();
    const headers: Record<string, string> = {};
    if (token) headers.Authorization = `Bearer ${token}`;
    return hubRequest<HubSearchHit[]>(baseUrl, `/api/hub/search?${query}`, { headers });
  },
  getMap: (baseUrl: string) => hubRequest<HubFederationMap>(baseUrl, '/api/hub/map'),
  getServers: (baseUrl: string) => hubRequest<HubRegisteredServer[]>(baseUrl, '/api/hub/servers'),
  registerServer: (
    baseUrl: string,
    payload: { serverDid: string; baseUrl: string; name: string },
  ) =>
    hubRequest<HubRegisteredServer>(baseUrl, '/api/hub/servers', {
      method: 'POST',
      body: JSON.stringify(payload),
    }),
  ackMap: (baseUrl: string, serverDid: string, mapVersion: number) =>
    hubRequest<void>(baseUrl, '/api/hub/map/ack', {
      method: 'POST',
      body: JSON.stringify({ serverDid, mapVersion }),
    }),
  unregisterServer: (baseUrl: string, serverDid: string) => {
    const encoded = encodeURIComponent(serverDid);
    return hubRequest<void>(baseUrl, `/api/hub/servers/${encoded}?serverDid=${encoded}`, {
      method: 'DELETE',
    });
  },
};

const HUB_FEED_MAX_LIMIT = 100;

/** Walk hub feed pages until `limit` cards or a short page. */
export async function fetchHubFeedWindow(
  baseUrl: string,
  limit: number,
  sort = 'hot',
): Promise<HubFeedCard[]> {
  const cards: HubFeedCard[] = [];
  let remaining = Math.max(0, limit);
  let offset = 0;
  while (remaining > 0) {
    const take = Math.min(HUB_FEED_MAX_LIMIT, remaining);
    const chunk = await hubApi.getFeed(baseUrl, { sort, limit: take, offset });
    cards.push(...chunk);
    if (chunk.length < take) break;
    offset += chunk.length;
    remaining -= chunk.length;
  }
  return cards;
}

/** Hub registry endpoints stay anonymous. Feed may attach the member JWT. */
async function hubRequest<T>(baseUrl: string, path: string, options: RequestInit = {}): Promise<T> {
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    ...(options.headers as Record<string, string> | undefined),
  };

  const root = rewriteLoopbackForPage(baseUrl);
  const url = `${root}${path}`;
  let response: Response;
  try {
    response = await fetch(url, { cache: 'no-store', ...options, headers });
  } catch {
    throw new Error(
      `Can't reach hub at ${root}. Is Snekl.Hub running? Try: snekl status`,
    );
  }

  if (!response.ok) {
    const body = await response.json().catch(() => ({}));
    throw new Error(body.error ?? `Request failed (${response.status}) at ${url}`);
  }

  if (response.status === 204) return undefined as T;
  return rewriteLoopbackUrlFields(await response.json());
}

function getToken(): string | null {
  return localStorage.getItem('snekl_token');
}

async function request<T>(baseUrl: string, path: string, options: RequestInit = {}): Promise<T> {
  const headers: Record<string, string> = {
    ...(options.headers as Record<string, string> | undefined),
  };

  const isFormData = typeof FormData !== 'undefined' && options.body instanceof FormData;
  if (!isFormData && !headers['Content-Type']) {
    headers['Content-Type'] = 'application/json';
  }

  const token = getToken();
  if (token) headers.Authorization = `Bearer ${token}`;

  const root = rewriteLoopbackForPage(baseUrl);
  const url = `${root}${path}`;
  let response: Response;
  try {
    response = await fetch(url, { ...options, headers });
  } catch {
    throw new Error(
      `Can't reach ${root}. Is the API running? Try: snekl status`,
    );
  }

  if (!response.ok) {
    const body = await response.json().catch(() => ({}));
    throw new Error(body.error ?? `Request failed (${response.status}) at ${url}`);
  }

  if (response.status === 204) return undefined as T;
  return rewriteLoopbackUrlFields(await response.json());
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
    request<RingSession>(getAuthBaseUrl(), '/api/auth/register', {
      method: 'POST',
      body: JSON.stringify(payload),
    }),

  login: (email: string, password: string) =>
    request<RingSession>(getAuthBaseUrl(), '/api/auth/login', {
      method: 'POST',
      body: JSON.stringify({ email, password }),
    }),

  reauthenticate: (password: string) =>
    request<RingSession>(getAuthBaseUrl(), '/api/auth/reauthenticate', {
      method: 'POST',
      body: JSON.stringify({ password }),
    }),

  forgotPassword: (email: string) =>
    request<{ message: string; resetUrl?: string | null }>(getAuthBaseUrl(), '/api/auth/forgot-password', {
      method: 'POST',
      body: JSON.stringify({ email }),
    }),

  resetPassword: (token: string, newPassword: string) =>
    request<{ message: string }>(getAuthBaseUrl(), '/api/auth/reset-password', {
      method: 'POST',
      body: JSON.stringify({ token, newPassword }),
    }),

  verifyEmail: (token: string) =>
    request<{ message: string }>(getAuthBaseUrl(), '/api/auth/verify-email', {
      method: 'POST',
      body: JSON.stringify({ token }),
    }),

  devResetPassword: (email: string, newPassword: string) =>
    request<{ message: string }>(getAuthBaseUrl(), '/api/auth/dev/reset-password', {
      method: 'POST',
      body: JSON.stringify({ email, newPassword }),
    }),

  me: () => request<RingSession['person']>(getAuthBaseUrl(), '/api/me'),

  updateProfile: (payload: {
    username: string;
    email: string;
    displayName: string;
    bio: string;
    disambiguationTag?: string | null;
  }) =>
    request<{ person: RingSession['person']; accessToken: string }>(getAuthBaseUrl(), '/api/me/profile', {
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
    request<{ message: string }>(getAuthBaseUrl(), '/api/me/send-verification-email', {
      method: 'POST',
      body: JSON.stringify({}),
    }),

  getContentServers: () =>
    request<ContentServerMembership[]>(getAuthBaseUrl(), '/api/me/content-servers'),

  getContentServerPresets: () =>
    request<ContentServerStatusPreset[]>(getAuthBaseUrl(), '/api/me/content-servers/presets'),

  subscribeContentServer: (baseUrl: string) =>
    request<ContentServerMembership>(getAuthBaseUrl(), '/api/me/content-servers', {
      method: 'POST',
      body: JSON.stringify({ baseUrl }),
    }),

  updateContentServerStatus: (serverDid: string, status: string) =>
    request<ContentServerMembership>(
      getAuthBaseUrl(),
      `/api/me/content-servers/${encodeURIComponent(serverDid)}`,
      {
        method: 'PUT',
        body: JSON.stringify({ status }),
      },
    ),

  unsubscribeContentServer: (serverDid: string) =>
    request<void>(getAuthBaseUrl(), `/api/me/content-servers/${encodeURIComponent(serverDid)}`, {
      method: 'DELETE',
    }),

  getFederations: () =>
    request<FederationMembership[]>(getAuthBaseUrl(), '/api/me/federations'),

  getFederationPresets: () =>
    request<ContentServerStatusPreset[]>(getAuthBaseUrl(), '/api/me/federations/presets'),

  subscribeFederation: (baseUrl: string) =>
    request<FederationMembership>(getAuthBaseUrl(), '/api/me/federations', {
      method: 'POST',
      body: JSON.stringify({ baseUrl }),
    }),

  updateFederationStatus: (baseUrl: string, status: string) =>
    request<FederationMembership>(
      getAuthBaseUrl(),
      `/api/me/federations?baseUrl=${encodeURIComponent(normalizeBaseUrl(baseUrl))}`,
      {
        method: 'PUT',
        body: JSON.stringify({ status }),
      },
    ),

  unsubscribeFederation: (baseUrl: string) =>
    request<void>(
      getAuthBaseUrl(),
      `/api/me/federations?baseUrl=${encodeURIComponent(normalizeBaseUrl(baseUrl))}`,
      { method: 'DELETE' },
    ),

  getForumFavorites: () => request<ForumFavorite[]>(getAuthBaseUrl(), '/api/me/forum-favorites'),

  upsertForumFavorite: (payload: {
    serverDid: string;
    forumId: string;
    name: string;
    slug: string;
    parentSlug?: string | null;
    serverName: string;
  }) =>
    request<ForumFavorite>(getAuthBaseUrl(), '/api/me/forum-favorites', {
      method: 'POST',
      body: JSON.stringify(payload),
    }),

  removeForumFavorite: (serverDid: string, forumId: string) =>
    request<void>(
      getAuthBaseUrl(),
      `/api/me/forum-favorites?serverDid=${encodeURIComponent(serverDid)}&forumId=${encodeURIComponent(forumId)}`,
      { method: 'DELETE' },
    ),

  getFollows: () => request<Follow[]>(getAuthBaseUrl(), '/api/me/follows'),

  upsertFollow: (payload: { did: string; displayName?: string | null; username?: string | null }) =>
    request<Follow>(getAuthBaseUrl(), '/api/me/follows', {
      method: 'POST',
      body: JSON.stringify(payload),
    }),

  removeFollow: (did: string) =>
    request<void>(
      getAuthBaseUrl(),
      `/api/me/follows?did=${encodeURIComponent(did)}`,
      { method: 'DELETE' },
    ),

  getForumGroups: () => request<ForumNavLayout>(getAuthBaseUrl(), '/api/me/forum-groups'),

  saveForumGroups: (layout: ForumNavLayout) =>
    request<ForumNavLayout>(getAuthBaseUrl(), '/api/me/forum-groups', {
      method: 'PUT',
      body: JSON.stringify(layout),
    }),
};

export function createContentClient(baseUrl: string) {
  const root = rewriteLoopbackForPage(baseUrl);

  function forumApiPath(slug: string, suffix = '') {
    return `/api/forums/${slug}${suffix}`;
  }

  function forumParentQuery(parentSlug?: string) {
    return parentSlug ? `?parentSlug=${encodeURIComponent(parentSlug)}` : '';
  }

  return {
    me: () => request<AuthMeResponse>(root, '/api/auth/me'),
    getServerPermissions: () => request<ServerPermissionsInfo>(root, '/api/server/me/permissions'),
    transferServerOwnership: (username: string) =>
      request<NodeInfo>(root, '/api/server/transfer-ownership', {
        method: 'POST',
        body: JSON.stringify({ username }),
      }),
    getNsfwAgeCheck: (forumId?: string) =>
      request<NsfwAgeCheckStatus>(
        root,
        forumId
          ? `/api/me/nsfw-age-check?forumId=${encodeURIComponent(forumId)}`
          : '/api/me/nsfw-age-check',
      ),
    submitNsfwAgeCheck: (data: { dateOfBirth: string; forumId?: string }) =>
      request<NsfwAgeCheckStatus>(root, '/api/me/nsfw-age-check', {
        method: 'POST',
        body: JSON.stringify({
          dateOfBirth: data.dateOfBirth,
          forumId: data.forumId,
        }),
      }),
    updateServerNsfwAgeCheck: (data: { required: boolean; minimumAge?: number }) =>
      request<NsfwAgeCheckStatus>(root, '/api/server/nsfw-age-check', {
        method: 'PUT',
        body: JSON.stringify(data),
      }),
    getUser: (username: string) => {
      const handle = username.trim().replace(/^@+/, '');
      return request<UserPresentation>(root, `/api/users/${encodeURIComponent(handle)}`);
    },
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
    syncHubMemberships: () =>
      request<void>(root, '/api/forums/hub-memberships/sync', { method: 'POST' }),
    getForum: (slug: string, parentSlug?: string) =>
      request<Forum>(root, `${forumApiPath(slug)}${forumParentQuery(parentSlug)}`),
    transferForumOwnership: (slug: string, username: string, parentSlug?: string) =>
      request<Forum>(root, `${forumApiPath(slug, '/transfer-ownership')}${forumParentQuery(parentSlug)}`, {
        method: 'POST',
        body: JSON.stringify({ username }),
      }),
    createForum: (name: string, slug: string, description: string, parentForumId?: string) =>
      request<Forum>(root, '/api/forums', {
        method: 'POST',
        body: JSON.stringify({ name, slug, description, parentForumId: parentForumId ?? null }),
      }),
    approveForum: (slug: string, parentSlug?: string) =>
      request<Forum>(root, `${forumApiPath(slug, '/approve')}${forumParentQuery(parentSlug)}`, {
        method: 'POST',
      }),
    rejectForum: (slug: string, parentSlug?: string) =>
      request<void>(root, `${forumApiPath(slug, '/reject')}${forumParentQuery(parentSlug)}`, {
        method: 'POST',
      }),
    deleteForum: (slug: string, parentSlug?: string) =>
      request<void>(root, `${forumApiPath(slug)}${forumParentQuery(parentSlug)}`, {
        method: 'DELETE',
      }),
    joinForum: (slug: string, parentSlug?: string) =>
      request<Forum>(root, `${forumApiPath(slug, '/join')}${forumParentQuery(parentSlug)}`, {
        method: 'POST',
      }),
    leaveForum: (slug: string, parentSlug?: string) =>
      request<Forum>(root, `${forumApiPath(slug, '/leave')}${forumParentQuery(parentSlug)}`, {
        method: 'POST',
      }),
    setForumFavorite: (slug: string, isFavorite: boolean, parentSlug?: string) =>
      request<Forum>(root, `${forumApiPath(slug, '/favorite')}${forumParentQuery(parentSlug)}`, {
        method: 'PUT',
        body: JSON.stringify({ isFavorite }),
      }),
    getForumThreads: (forumId: string, sort: PostSort = 'hot', page = 1, pageSize = 20) => {
      const params = new URLSearchParams({
        sort,
        page: String(page),
        pageSize: String(pageSize),
      });
      return request<Post[]>(root, `/api/posts/forum/${forumId}?${params}`);
    },
    getWatercoolerStream: (watercoolerId: string, sort: PostSort = 'new', page = 1, pageSize = 40) => {
      const params = new URLSearchParams({
        sort,
        page: String(page),
        pageSize: String(pageSize),
      });
      return request<Post[]>(root, `/api/posts/watercooler/${watercoolerId}?${params}`);
    },
    listServerWatercoolers: () => request<import('./types').Watercooler[]>(root, '/api/watercoolers'),
    getWatercooler: (slug: string, parentForumId?: string) => {
      const q = parentForumId ? `?parentForumId=${encodeURIComponent(parentForumId)}` : '';
      return request<import('./types').Watercooler>(root, `/api/watercoolers/${encodeURIComponent(slug)}${q}`);
    },
    getWatercoolerHistory: (slug: string, parentForumId?: string) => {
      const q = parentForumId ? `?parentForumId=${encodeURIComponent(parentForumId)}` : '';
      return request<import('./types').ChangeLogEntry[]>(
        root,
        `/api/watercoolers/${encodeURIComponent(slug)}/history${q}`,
      );
    },
    createWatercooler: (
      name: string,
      slug: string,
      description: string,
      parentForumId?: string,
      options?: { allowUpvotes?: boolean; newestAtBottom?: boolean },
    ) =>
      request<import('./types').Watercooler>(root, '/api/watercoolers', {
        method: 'POST',
        body: JSON.stringify({
          name,
          slug,
          description,
          parentForumId: parentForumId ?? null,
          allowUpvotes: options?.allowUpvotes,
          newestAtBottom: options?.newestAtBottom,
        }),
      }),
    updateWatercooler: (
      slug: string,
      data: { name?: string; description?: string; allowUpvotes?: boolean; newestAtBottom?: boolean },
      parentForumId?: string,
    ) => {
      const q = parentForumId ? `?parentForumId=${encodeURIComponent(parentForumId)}` : '';
      return request<import('./types').Watercooler>(root, `/api/watercoolers/${encodeURIComponent(slug)}${q}`, {
        method: 'PUT',
        body: JSON.stringify(data),
      });
    },
    approveWatercooler: (slug: string, parentForumId?: string) => {
      const q = parentForumId ? `?parentForumId=${encodeURIComponent(parentForumId)}` : '';
      return request<import('./types').Watercooler>(root, `/api/watercoolers/${encodeURIComponent(slug)}/approve${q}`, {
        method: 'POST',
      });
    },
    rejectWatercooler: (slug: string, parentForumId?: string) => {
      const q = parentForumId ? `?parentForumId=${encodeURIComponent(parentForumId)}` : '';
      return request<void>(root, `/api/watercoolers/${encodeURIComponent(slug)}/reject${q}`, {
        method: 'POST',
      });
    },
    deleteWatercooler: (slug: string, parentForumId?: string) => {
      const q = parentForumId ? `?parentForumId=${encodeURIComponent(parentForumId)}` : '';
      return request<void>(root, `/api/watercoolers/${encodeURIComponent(slug)}${q}`, {
        method: 'DELETE',
      });
    },
    replaceForumContentBlocks: (
      forumSlug: string,
      blocks: { typeKey: string; watercoolerId?: string | null; slot: string; position: number }[],
      parentSlug?: string,
    ) =>
      request<import('./types').ForumContentBlock[]>(
        root,
        `${forumApiPath(forumSlug, '/content-blocks')}${forumParentQuery(parentSlug)}`,
        { method: 'PUT', body: JSON.stringify({ blocks }) },
      ),
    getRecentThreads: (sort: PostSort = 'hot', page = 1, pageSize = 20, servers?: string[]) =>
      request<RecentThread[]>(root, recentThreadsPath(sort, page, pageSize, servers)),
    search: (q: string, page = 1, pageSize = 20) => {
      const params = new URLSearchParams({
        q,
        page: String(page),
        pageSize: String(pageSize),
      });
      return request<SearchHit[]>(root, `/api/search?${params}`);
    },
    getNetworkServers: () => request<NetworkServer[]>(root, '/api/network/servers'),
    getContentBlockTypes: () => request<import('./types').ContentBlockType[]>(root, '/api/content-blocks'),
    getExtensions: () => request<import('./types').ExtensionInstall[]>(root, '/api/extensions'),
    getExtensionInstallOptions: (clientId?: string) => {
      const query = clientId ? `?clientId=${encodeURIComponent(clientId)}` : '';
      return request<import('./types').ExtensionInstallOptions>(root, `/api/extensions/install-options${query}`);
    },
    createExtensionInstall: (payload: {
      clientId: string;
      appName: string;
      adminUrl?: string | null;
      iframeUrl?: string | null;
      resourceType: string;
      forumId?: string | null;
      serverPermissions: string[];
      forumPermissions: string[];
    }) =>
      request<import('./types').ExtensionInstall>(root, '/api/extensions/installs', {
        method: 'POST',
        body: JSON.stringify(payload),
      }),
    getThread: (postId: string) => request<Post>(root, `/api/posts/${postId}`),
    getPostRevisions: (postId: string) => request<import('./types').PostRevision[]>(root, `/api/posts/${postId}/revisions`),
    getServerHistory: () => request<import('./types').ChangeLogEntry[]>(root, '/api/server/history'),
    getForumHistory: (slug: string, parentSlug?: string) =>
      request<import('./types').ChangeLogEntry[]>(root, `${forumApiPath(slug, '/history')}${forumParentQuery(parentSlug)}`),
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
    setReplyPolicy: (postId: string, replyPolicy: string) =>
      request<Post>(root, `/api/posts/${postId}/reply-policy`, {
        method: 'PUT',
        body: JSON.stringify({ replyPolicy }),
      }),
    reportPost: (postId: string, reason: string) =>
      request<PostReport>(root, `/api/posts/${postId}/report`, {
        method: 'POST',
        body: JSON.stringify({ reason }),
      }),
    toggleUpvote: (postId: string) =>
      request<{ upvoteCount: number; viewerHasUpvoted: boolean }>(root, `/api/posts/${postId}/upvote`, {
        method: 'POST',
      }),
    toggleReaction: (postId: string, payload: { emoteId?: string; emoji?: string }) =>
      request<PostReaction[]>(root, `/api/posts/${postId}/reactions`, {
        method: 'POST',
        body: JSON.stringify(payload),
      }),
    getEmotes: (forumId?: string, options?: { allViewable?: boolean }) => {
      const params = new URLSearchParams();
      if (forumId) params.set('forumId', forumId);
      if (options?.allViewable) params.set('allViewable', 'true');
      const query = params.toString();
      return request<import('./types').CatalogEmote[]>(root, `/api/emotes${query ? `?${query}` : ''}`);
    },
    createEmote: (payload: {
      code: string;
      mediaAssetId?: string;
      forumId?: string | null;
      name?: string;
      skinsUnicode?: string | null;
      highlighted?: boolean;
    }) =>
      request<import('./types').CatalogEmote>(root, '/api/emotes', {
        method: 'POST',
        body: JSON.stringify(payload),
      }),
    updateEmote: (emoteId: string, payload: {
      name?: string;
      code?: string;
      mediaAssetId?: string;
      skinsUnicode?: string | null;
      clearSkin?: boolean;
      highlighted?: boolean;
    }) =>
      request<import('./types').CatalogEmote>(root, `/api/emotes/${emoteId}`, {
        method: 'PUT',
        body: JSON.stringify(payload),
      }),
    reorderEmotes: (ids: string[], forumId?: string | null) =>
      request<void>(root, '/api/emotes/order', {
        method: 'PUT',
        body: JSON.stringify({ ids, forumId: forumId ?? null }),
      }),
    deleteEmote: (emoteId: string) =>
      request<void>(root, `/api/emotes/${emoteId}`, { method: 'DELETE' }),
    createPost: (
      forumId: string | undefined,
      message: string,
      parentPostId?: string,
      title?: string,
      mediaAssetIds?: string[],
      watercoolerId?: string,
      quotedPostId?: string,
      nsfwMedia?: { mediaAssetId: string; coverText: string }[],
      replyPolicy?: string,
    ) =>
      request<Post>(root, '/api/posts', {
        method: 'POST',
        body: JSON.stringify({
          forumId: forumId ?? null,
          watercoolerId: watercoolerId ?? null,
          message,
          parentPostId: parentPostId ?? null,
          title: title ?? null,
          mediaAssetIds: mediaAssetIds ?? null,
          quotedPostId: quotedPostId ?? null,
          nsfwMedia: nsfwMedia ?? null,
          replyPolicy: replyPolicy ?? null,
        }),
      }),
    setMediaNsfw: (postId: string, mediaId: string, payload: { isNsfw: boolean; coverText?: string | null }) =>
      request<Post>(root, `/api/posts/${postId}/media/${mediaId}/nsfw`, {
        method: 'PATCH',
        body: JSON.stringify(payload),
      }),
    fetchMediaBlobUrl: async (mediaId: string) => {
      const headers: Record<string, string> = {};
      const token = getToken();
      if (token) headers.Authorization = `Bearer ${token}`;
      const url = `${rewriteLoopbackForPage(root)}/api/media/${mediaId}`;
      const response = await fetch(url, { headers });
      if (!response.ok) throw new Error(`Could not load image (${response.status})`);
      const blob = await response.blob();
      return URL.createObjectURL(blob);
    },
    uploadMedia: async (file: File) => {
      const form = new FormData();
      form.append('file', file);
      return request<MediaAttachment>(root, '/api/media', {
        method: 'POST',
        body: form,
      });
    },
    getLists: () => request<CuratedList[]>(root, '/api/lists'),
    getList: (listId: string) => request<CuratedList>(root, `/api/lists/${listId}`),
    createList: (data: {
      name: string;
      description: string;
      defaultBehavior: string;
      defaultFlairEmoji?: string;
      defaultIconUrl?: string;
      isPublic: boolean;
    }) =>
      request<CuratedList>(root, '/api/lists', {
        method: 'POST',
        body: JSON.stringify(data),
      }),
    updateList: (
      listId: string,
      data: {
        name: string;
        description: string;
        defaultBehavior: string;
        defaultFlairEmoji?: string;
        defaultIconUrl?: string;
        isPublic: boolean;
      },
    ) =>
      request<CuratedList>(root, `/api/lists/${listId}`, {
        method: 'PUT',
        body: JSON.stringify(data),
      }),
    deleteList: (listId: string) => request<void>(root, `/api/lists/${listId}`, { method: 'DELETE' }),
    transferList: (listId: string, username: string) =>
      request<CuratedList>(root, `/api/lists/${listId}/transfer`, {
        method: 'POST',
        body: JSON.stringify({ username }),
      }),
    subscribeList: (listId: string, appliedBehavior: string) =>
      request<void>(root, `/api/lists/${listId}/subscribe`, {
        method: 'POST',
        body: JSON.stringify({ appliedBehavior }),
      }),
    unsubscribeList: (listId: string) =>
      request<void>(root, `/api/lists/${listId}/subscribe`, { method: 'DELETE' }),
    reorderListSubscriptions: (listIds: string[]) =>
      request<void>(root, '/api/lists/subscriptions/order', {
        method: 'PUT',
        body: JSON.stringify({ listIds }),
      }),
    getListEntries: (listId: string, opts?: { page?: number; pageSize?: number; q?: string }) => {
      const params = new URLSearchParams();
      if (opts?.page) params.set('page', String(opts.page));
      if (opts?.pageSize) params.set('pageSize', String(opts.pageSize));
      if (opts?.q) params.set('q', opts.q);
      const query = params.toString();
      return request<PagedResult<ListEntry>>(
        root,
        `/api/lists/${listId}/entries${query ? `?${query}` : ''}`,
      );
    },
    findListEntriesForUser: (username: string) =>
      request<UserListEntryHit[]>(
        root,
        `/api/lists/entries/by-user/${encodeURIComponent(username.replace(/^@/, ''))}`,
      ),
    addListEntry: (
      listId: string,
      data: {
        targetUserId: string;
        label: string;
        tooltipText: string;
        flairEmoji?: string;
        flairIconUrl?: string;
        usernameColor?: string;
      },
    ) =>
      request<ListEntry>(root, `/api/lists/${listId}/entries`, {
        method: 'POST',
        body: JSON.stringify(data),
      }),
    updateListEntry: (
      listId: string,
      entryId: string,
      data: {
        label: string;
        tooltipText: string;
        flairEmoji?: string;
        flairIconUrl?: string;
      },
    ) =>
      request<ListEntry>(root, `/api/lists/${listId}/entries/${entryId}`, {
        method: 'PUT',
        body: JSON.stringify(data),
      }),
    deleteListEntry: (listId: string, entryId: string) =>
      request<void>(root, `/api/lists/${listId}/entries/${entryId}`, { method: 'DELETE' }),
    ignoreListEntry: (listId: string, entryId: string) =>
      request<void>(root, `/api/lists/${listId}/entries/${entryId}/ignore`, { method: 'POST' }),
    unignoreListEntry: (listId: string, entryId: string) =>
      request<void>(root, `/api/lists/${listId}/entries/${entryId}/ignore`, { method: 'DELETE' }),

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
    updateForumRole: (
      slug: string,
      roleId: string,
      data: { name: string; color?: string; position: number; permissions: string[] },
      parentSlug?: string,
    ) =>
      request<ForumRole>(root, `${forumApiPath(slug, `/roles/${roleId}`)}${forumParentQuery(parentSlug)}`, {
        method: 'PUT',
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
      data: { emoji?: string; iconUrl?: string; label?: string; nameColor?: string; nameStyle?: string; position: number },
      parentSlug?: string,
    ) =>
      request<ForumFlairTemplate>(root, `${forumApiPath(slug, '/flair-templates')}${forumParentQuery(parentSlug)}`, {
        method: 'POST',
        body: JSON.stringify(data),
      }),
    updateFlairTemplate: (
      slug: string,
      templateId: string,
      data: { emoji?: string; iconUrl?: string; label?: string; nameColor?: string; nameStyle?: string; position?: number },
      parentSlug?: string,
    ) =>
      request<ForumFlairTemplate>(
        root,
        `${forumApiPath(slug, `/flair-templates/${templateId}`)}${forumParentQuery(parentSlug)}`,
        {
          method: 'PUT',
          body: JSON.stringify(data),
        },
      ),
    deleteFlairTemplate: (slug: string, templateId: string, parentSlug?: string) =>
      request<void>(root, `${forumApiPath(slug, `/flair-templates/${templateId}`)}${forumParentQuery(parentSlug)}`, {
        method: 'DELETE',
      }),
    assignForumFlair: (slug: string, userId: string, templateId: string | null, parentSlug?: string) =>
      request<void>(root, `${forumApiPath(slug, `/members/${userId}/flair`)}${forumParentQuery(parentSlug)}`, {
        method: 'PUT',
        body: JSON.stringify({ templateId }),
      }),
    setMyAlias: (slug: string, alias: string, parentSlug?: string) =>
      request<void>(root, `${forumApiPath(slug, '/members/me/alias')}${forumParentQuery(parentSlug)}`, {
        method: 'PUT',
        body: JSON.stringify({ alias }),
      }),
    updateForumSettings: (
      slug: string,
      data: {
        name?: string;
        description?: string;
        allowAliases?: boolean;
        nsfwAgeCheckRequired?: boolean;
        nsfwAgeCheckExplanation?: string;
        avatarMediaId?: string;
        bannerMediaId?: string;
        clearAvatar?: boolean;
        clearBanner?: boolean;
      },
      parentSlug?: string,
    ) =>
      request<void>(root, `${forumApiPath(slug, '/settings')}${forumParentQuery(parentSlug)}`, {
        method: 'PUT',
        body: JSON.stringify(data),
      }),
    getDirectMessageEligibility: (username: string) =>
      request<DirectMessageEligibility>(root, `/api/dm/eligibility/${encodeURIComponent(username)}`),
    getDirectMessageInbox: () => request<DirectMessageInbox>(root, '/api/dm'),
    createDirectMessageRequest: (data: {
      targetUsername: string;
      forumId?: string;
      forumSlug?: string;
      parentSlug?: string;
      message?: string;
    }) =>
      request<DirectConversationRequest>(root, '/api/dm/requests', {
        method: 'POST',
        body: JSON.stringify(data),
      }),
    acceptDirectMessageRequest: (requestId: string) =>
      request<DirectConversationRequest>(root, `/api/dm/requests/${requestId}/accept`, { method: 'POST' }),
    declineDirectMessageRequest: (requestId: string) =>
      request<DirectConversationRequest>(root, `/api/dm/requests/${requestId}/decline`, { method: 'POST' }),
    cancelDirectMessageRequest: (requestId: string) =>
      request<void>(root, `/api/dm/requests/${requestId}/cancel`, { method: 'POST' }),
    getDirectConversation: (conversationId: string) =>
      request<DirectConversation>(root, `/api/dm/conversations/${conversationId}`),
    sendDirectMessage: (conversationId: string, body: string) =>
      request<DirectMessage>(root, `/api/dm/conversations/${conversationId}/messages`, {
        method: 'POST',
        body: JSON.stringify({ body }),
      }),
    getDirectMessageForumPreferences: () =>
      request<DirectMessageForumPreferences>(root, '/api/dm/forum-preferences'),
    setDirectMessageForumPreference: (forumId: string, mode: string) =>
      request<DirectMessageForumPreference>(root, `/api/dm/forum-preferences/${forumId}`, {
        method: 'PUT',
        body: JSON.stringify({ mode }),
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

export async function fetchAggregatedSearch(
  memberships: ContentServerMembership[],
  q: string,
  page = 1,
  pageSize = 20,
): Promise<AggregatedSearchHit[]> {
  const query = q.trim();
  if (!query || memberships.length === 0) return [];

  const results = await Promise.allSettled(
    memberships.map(async (server) => {
      const hits = await createContentClient(server.baseUrl).search(query, page, pageSize);
      return hits.map((hit): AggregatedSearchHit => ({
        ...hit,
        serverDid: hit.thread.ownerDid || server.serverDid,
        serverName: server.name,
        serverBaseUrl: hit.thread.ownerBaseUrl || server.baseUrl,
      }));
    }),
  );

  const merged: AggregatedSearchHit[] = [];
  for (const result of results) {
    if (result.status === 'fulfilled') merged.push(...result.value);
  }

  return merged.sort(
    (a, b) => new Date(b.thread.post.createdAt).getTime() - new Date(a.thread.post.createdAt).getTime(),
  );
}

export async function fetchHubSearch(
  hubUrls: string[],
  q: string,
  limit = 40,
): Promise<HubSearchHit[]> {
  const query = q.trim();
  if (!query || hubUrls.length === 0) return [];

  const results = await Promise.allSettled(hubUrls.map((url) => hubApi.search(url, query, { limit })));
  const merged: HubSearchHit[] = [];
  const seen = new Set<string>();
  for (const result of results) {
    if (result.status !== 'fulfilled') continue;
    for (const hit of result.value) {
      const key = `${hit.thread.serverDid}:${hit.thread.postId}`;
      if (!seen.has(key)) {
        seen.add(key);
        merged.push(hit);
      }
    }
  }
  return merged;
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

export function apiFor(baseUrl: string) {
  return createContentClient(baseUrl);
}

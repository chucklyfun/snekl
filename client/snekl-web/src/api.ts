import type {
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

const CONTENT_API = import.meta.env.VITE_API_URL ?? 'http://localhost:5001';
const AUTH_API = import.meta.env.VITE_AUTH_URL ?? 'http://localhost:5100';

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

  devResetPassword: (username: string, newPassword: string) =>
    request<{ message: string }>(AUTH_API, '/api/auth/dev/reset-password', {
      method: 'POST',
      body: JSON.stringify({ username, newPassword }),
    }),

  me: () => request<RingSession['person']>(AUTH_API, '/api/me'),
};

export const api = {
  register: authApi.register,
  login: authApi.login,

  me: () => request<UserPresentation>(CONTENT_API, '/api/auth/me'),

  getUser: (username: string) => request<UserPresentation>(CONTENT_API, `/api/users/${username}`),

  updateFlair: (flair: {
    emoji?: string;
    iconUrl?: string;
    label?: string;
    nameColor?: string;
    nameStyle?: string;
  }) =>
    request<UserPresentation>(CONTENT_API, '/api/users/me/flair', {
      method: 'PUT',
      body: JSON.stringify(flair),
    }),

  getStatusPresets: () => request<StatusPreset[]>(CONTENT_API, '/api/users/status/presets'),

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
    request<UserStatus>(CONTENT_API, '/api/users/me/status', {
      method: 'PUT',
      body: JSON.stringify(status),
    }),

  clearStatus: () => request<void>(CONTENT_API, '/api/users/me/status', { method: 'DELETE' }),

  getNode: () => request<NodeInfo>(CONTENT_API, '/api/node'),

  getForums: () => request<Forum[]>(CONTENT_API, '/api/forums'),

  getForum: (slug: string) => request<Forum>(CONTENT_API, `/api/forums/${slug}`),

  createForum: (name: string, slug: string, description: string) =>
    request<Forum>(CONTENT_API, '/api/forums', {
      method: 'POST',
      body: JSON.stringify({ name, slug, description }),
    }),

  getForumThreads: (forumId: string) =>
    request<Post[]>(CONTENT_API, `/api/posts/forum/${forumId}`),

  getThread: (postId: string) => request<Post>(CONTENT_API, `/api/posts/${postId}`),

  createPost: (forumId: string, message: string, parentPostId?: string) =>
    request<Post>(CONTENT_API, '/api/posts', {
      method: 'POST',
      body: JSON.stringify({ forumId, message, parentPostId: parentPostId ?? null }),
    }),

  getPeers: () => request<Peer[]>(CONTENT_API, '/api/federation/peers'),

  subscribePeer: (baseUrl: string) =>
    request<Peer>(CONTENT_API, '/api/federation/peers', {
      method: 'POST',
      body: JSON.stringify({ baseUrl }),
    }),

  unsubscribePeer: (peerId: string) =>
    request<void>(CONTENT_API, `/api/federation/peers/${peerId}`, { method: 'DELETE' }),

  getLists: () => request<CuratedList[]>(CONTENT_API, '/api/lists'),

  createList: (data: {
    name: string;
    description: string;
    defaultBehavior: string;
    defaultFlairEmoji?: string;
    isPublic: boolean;
  }) =>
    request<CuratedList>(CONTENT_API, '/api/lists', {
      method: 'POST',
      body: JSON.stringify(data),
    }),

  subscribeList: (listId: string, appliedBehavior: string) =>
    request<void>(CONTENT_API, `/api/lists/${listId}/subscribe`, {
      method: 'POST',
      body: JSON.stringify({ appliedBehavior }),
    }),

  unsubscribeList: (listId: string) =>
    request<void>(CONTENT_API, `/api/lists/${listId}/subscribe`, { method: 'DELETE' }),

  addListEntry: (listId: string, data: {
    targetUserId: string;
    severity: number;
    tooltipText: string;
    flairEmoji?: string;
    usernameColor?: string;
  }) =>
    request<ListAnnotation>(CONTENT_API, `/api/lists/${listId}/entries`, {
      method: 'POST',
      body: JSON.stringify(data),
    }),
};

export { AUTH_API, CONTENT_API };

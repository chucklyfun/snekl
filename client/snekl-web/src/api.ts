import type {
  AuthResponse,
  CuratedList,
  Forum,
  ListAnnotation,
  NodeInfo,
  Peer,
  Post,
  StatusPreset,
  UserPresentation,
  UserStatus,
} from './types';

const API_BASE = import.meta.env.VITE_API_URL ?? 'http://localhost:5000';

function getToken(): string | null {
  return localStorage.getItem('snekl_token');
}

async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    ...(options.headers as Record<string, string> | undefined),
  };

  const token = getToken();
  if (token) headers.Authorization = `Bearer ${token}`;

  const response = await fetch(`${API_BASE}${path}`, { ...options, headers });

  if (!response.ok) {
    const body = await response.json().catch(() => ({}));
    throw new Error(body.error ?? `Request failed (${response.status})`);
  }

  if (response.status === 204) return undefined as T;
  return response.json();
}

export const api = {
  register: (username: string, email: string, password: string, displayName: string) =>
    request<AuthResponse>('/api/auth/register', {
      method: 'POST',
      body: JSON.stringify({ username, email, password, displayName }),
    }),

  login: (username: string, password: string) =>
    request<AuthResponse>('/api/auth/login', {
      method: 'POST',
      body: JSON.stringify({ username, password }),
    }),

  me: () => request<UserPresentation>('/api/auth/me'),

  getUser: (username: string) => request<UserPresentation>(`/api/users/${username}`),

  updateFlair: (flair: {
    emoji?: string;
    iconUrl?: string;
    label?: string;
    nameColor?: string;
    nameStyle?: string;
  }) =>
    request<UserPresentation>('/api/users/me/flair', {
      method: 'PUT',
      body: JSON.stringify(flair),
    }),

  getStatusPresets: () => request<StatusPreset[]>('/api/users/status/presets'),

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
    request<UserStatus>('/api/users/me/status', {
      method: 'PUT',
      body: JSON.stringify(status),
    }),

  clearStatus: () => request<void>('/api/users/me/status', { method: 'DELETE' }),

  getNode: () => request<NodeInfo>('/api/node'),

  getForums: () => request<Forum[]>('/api/forums'),

  getForum: (slug: string) => request<Forum>(`/api/forums/${slug}`),

  createForum: (name: string, slug: string, description: string) =>
    request<Forum>('/api/forums', {
      method: 'POST',
      body: JSON.stringify({ name, slug, description }),
    }),

  getForumThreads: (forumId: string) =>
    request<Post[]>(`/api/posts/forum/${forumId}`),

  getThread: (postId: string) => request<Post>(`/api/posts/${postId}`),

  createPost: (forumId: string, message: string, parentPostId?: string) =>
    request<Post>('/api/posts', {
      method: 'POST',
      body: JSON.stringify({ forumId, message, parentPostId: parentPostId ?? null }),
    }),

  getPeers: () => request<Peer[]>('/api/federation/peers'),

  subscribePeer: (baseUrl: string) =>
    request<Peer>('/api/federation/peers', {
      method: 'POST',
      body: JSON.stringify({ baseUrl }),
    }),

  unsubscribePeer: (peerId: string) =>
    request<void>(`/api/federation/peers/${peerId}`, { method: 'DELETE' }),

  getLists: () => request<CuratedList[]>('/api/lists'),

  createList: (data: {
    name: string;
    description: string;
    defaultBehavior: string;
    defaultFlairEmoji?: string;
    isPublic: boolean;
  }) =>
    request<CuratedList>('/api/lists', {
      method: 'POST',
      body: JSON.stringify(data),
    }),

  subscribeList: (listId: string, appliedBehavior: string) =>
    request<void>(`/api/lists/${listId}/subscribe`, {
      method: 'POST',
      body: JSON.stringify({ appliedBehavior }),
    }),

  unsubscribeList: (listId: string) =>
    request<void>(`/api/lists/${listId}/subscribe`, { method: 'DELETE' }),

  addListEntry: (listId: string, data: {
    targetUserId: string;
    severity: number;
    tooltipText: string;
    flairEmoji?: string;
    usernameColor?: string;
  }) =>
    request<ListAnnotation>(`/api/lists/${listId}/entries`, {
      method: 'POST',
      body: JSON.stringify(data),
    }),
};

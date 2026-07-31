import type { AuthResponse, Forum, NodeInfo, Peer, Post, User } from './types';

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

  me: () => request<User>('/api/auth/me'),

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
};

import { CONTENT_API } from './api';
import type { NodeInfo, Peer, SiteAdmin } from './types';

function getAdminToken(): string | null {
  return localStorage.getItem('snekl_admin_token');
}

async function adminRequest<T>(path: string, options: RequestInit = {}): Promise<T> {
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    ...(options.headers as Record<string, string> | undefined),
  };

  const token = getAdminToken();
  if (token) headers.Authorization = `Bearer ${token}`;

  const response = await fetch(`${CONTENT_API}${path}`, { ...options, headers });

  if (!response.ok) {
    const body = await response.json().catch(() => ({}));
    throw new Error(body.error ?? `Request failed (${response.status})`);
  }

  if (response.status === 204) return undefined as T;
  return response.json();
}

export const adminApi = {
  login: (username: string, password: string) =>
    adminRequest<{ token: string; admin: SiteAdmin }>('/api/admin/login', {
      method: 'POST',
      body: JSON.stringify({ username, password }),
    }),

  me: () => adminRequest<SiteAdmin>('/api/admin/me'),

  getNode: () => adminRequest<NodeInfo>('/api/admin/site/node'),

  updateNode: (data: { name: string; baseUrl: string; description: string }) =>
    adminRequest<NodeInfo>('/api/admin/site/node', {
      method: 'PUT',
      body: JSON.stringify(data),
    }),

  getPeers: () => adminRequest<Peer[]>('/api/admin/site/peers'),

  subscribePeer: (baseUrl: string) =>
    adminRequest<Peer>('/api/admin/site/peers', {
      method: 'POST',
      body: JSON.stringify({ baseUrl }),
    }),

  unsubscribePeer: (peerId: string) =>
    adminRequest<void>(`/api/admin/site/peers/${peerId}`, { method: 'DELETE' }),

  getIssuers: () => adminRequest<TrustedAuthIssuer[]>('/api/admin/site/issuers'),

  registerIssuer: (data: { issuerUrl: string; jwksUrl: string; name: string }) =>
    adminRequest<TrustedAuthIssuer>('/api/admin/site/issuers', {
      method: 'POST',
      body: JSON.stringify(data),
    }),
};

export interface TrustedAuthIssuer {
  id: string;
  issuerUrl: string;
  jwksUrl: string;
  name: string;
  isActive: boolean;
}

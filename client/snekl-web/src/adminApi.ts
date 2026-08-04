import { CONTENT_API } from './api';
import type { NodeInfo, Peer, SiteAdmin, SuperUser } from './types';

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

  const url = `${CONTENT_API}${path}`;
  let response: Response;
  try {
    response = await fetch(url, { ...options, headers });
  } catch {
    throw new Error(
      `Can't reach ${CONTENT_API}. Is the content API running? Try: dotnet run --project src/Snekl.Server --no-launch-profile -- status`,
    );
  }

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

  getSuperUsers: () => adminRequest<SuperUser[]>('/api/admin/site/super-users'),

  grantSuperUser: (username: string, note?: string) =>
    adminRequest<SuperUser>('/api/admin/site/super-users', {
      method: 'POST',
      body: JSON.stringify({ username, note }),
    }),

  revokeSuperUser: (userId: string) =>
    adminRequest<void>(`/api/admin/site/super-users/${userId}`, { method: 'DELETE' }),
};

export interface TrustedAuthIssuer {
  id: string;
  issuerUrl: string;
  jwksUrl: string;
  name: string;
  isActive: boolean;
}

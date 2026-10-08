import { ApiRequestError, TwoFactorRequiredError } from '../api';
import { applyMemberProof, createDeviceProof, getDevicePublicJwk, readBoundToken } from '../deviceKey';
import type { AdminBootstrap } from '../types';

const ADMIN_TOKEN = 'snekl_admin_token';
const MEMBER_TOKEN = 'snekl_admin_member_token';
const CONTENT_SERVER_ID = 'snekl_content_server_id';

export function setContentServerId(id: string) {
  localStorage.setItem(CONTENT_SERVER_ID, id);
}

export type AdminKind = 'auth' | 'hub' | 'content';

export const ADMIN_KIND_UI: Record<AdminKind, { title: string; badge: string; mark: string }> = {
  auth: { title: 'Auth admin', badge: 'Auth', mark: 'A' },
  hub: { title: 'Hub admin', badge: 'Hub', mark: 'H' },
  content: { title: 'Content admin', badge: 'Content', mark: 'C' },
};

export function memberToken(): string | null {
  return readBoundToken(MEMBER_TOKEN);
}

export function adminToken(): string | null {
  return localStorage.getItem(ADMIN_TOKEN);
}

export function setMemberToken(token: string) {
  localStorage.setItem(MEMBER_TOKEN, token);
}

export function setAdminToken(token: string) {
  localStorage.setItem(ADMIN_TOKEN, token);
}

export function clearAdminSession() {
  localStorage.removeItem(ADMIN_TOKEN);
  localStorage.removeItem(MEMBER_TOKEN);
}

export async function loadBootstrap(): Promise<AdminBootstrap> {
  const res = await fetch('/server/bootstrap', { cache: 'no-store' });
  if (!res.ok) throw new Error('This host did not return admin bootstrap. Is it a Snekl Auth, Hub, or content server?');
  return res.json();
}

type AccountSession = { accessToken: string; person?: { displayName?: string; canonicalUsername?: string } };

async function readAccountSession(res: Response): Promise<AccountSession> {
  const body = (await res.json().catch(() => ({}))) as {
    error?: string;
    code?: string;
    challengeToken?: string;
    accessToken?: string;
  };
  if (!res.ok) {
    if (body.code === 'two_factor_required' && typeof body.challengeToken === 'string') {
      throw new TwoFactorRequiredError(body.challengeToken);
    }
    throw new ApiRequestError(res.status, body, res.url);
  }
  if (!body.accessToken) throw new Error('Auth server did not return an access token.');
  return body as AccountSession;
}

export async function accountLogin(authBase: string, email: string, password: string) {
  const res = await fetch(`${authBase.replace(/\/$/, '')}/api/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password, deviceKey: await getDevicePublicJwk() }),
  });
  return readAccountSession(res);
}

export async function completeAccountTwoFactor(authBase: string, challengeToken: string, code: string) {
  const res = await fetch(`${authBase.replace(/\/$/, '')}/api/auth/login/two-factor`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ challengeToken, code }),
  });
  return readAccountSession(res);
}

function statusPath(kind: AdminKind) {
  if (kind === 'hub') return '/api/hub/admin/status';
  if (kind === 'auth') return '/api/auth/admin/status';
  return '/api/admin/operator-status';
}

function loginPath(kind: AdminKind) {
  if (kind === 'hub') return '/api/hub/admin/login';
  if (kind === 'auth') return '/api/auth/admin/login';
  return '/api/admin/login';
}

export async function operatorStatus(kind: AdminKind): Promise<{ state: string }> {
  return adminFetch(kind, statusPath(kind), { member: true });
}

export async function operatorLogin(kind: AdminKind, state: string, password: string) {
  const payload = state === 'ready' ? { adminPassword: password } : { newAdminPassword: password };
  return adminFetch<{ token: string }>(kind, loginPath(kind), {
    member: true,
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function adminFetch<T>(
  kind: AdminKind,
  path: string,
  options: RequestInit & { member?: boolean; admin?: boolean; memberProof?: { method: string; url: string } } = {},
): Promise<T> {
  const { member: wantMember = false, admin: wantAdminFlag = false, memberProof, headers: optionHeaders, ...rest } = options;
  const headers: Record<string, string> = { ...(optionHeaders as Record<string, string> | undefined) };
  if (rest.body && !headers['Content-Type']) headers['Content-Type'] = 'application/json';
  const member = memberToken();
  const admin = adminToken();
  const wantAdmin = wantAdminFlag || (kind === 'content' && !wantMember);
  const method = rest.method ?? 'GET';
  const url = new URL(path, window.location.origin).href;

  if (kind === 'content') {
    const serverId = localStorage.getItem(CONTENT_SERVER_ID);
    if (serverId && !headers['X-Snekl-Server']) headers['X-Snekl-Server'] = serverId;
  }

  if (wantMember && member) {
    await applyMemberProof(headers, method, url, member);
  } else if (wantAdmin && admin) {
    headers.Authorization = `Bearer ${admin}`;
    if ((kind === 'hub' || kind === 'auth') && member) {
      const proofMethod = memberProof?.method ?? method;
      const proofUrl = memberProof?.url ?? url;
      headers['X-Snekl-Member-Authorization'] = `DPoP ${member}`;
      headers['X-Snekl-Member-DPoP'] = await createDeviceProof(proofMethod, proofUrl, member);
    }
  }
  const res = await fetch(path, { ...rest, headers, cache: 'no-store' });
  if (res.status === 401) {
    clearAdminSession();
    throw new Error('Session expired. Log in again.');
  }
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw new Error(body.error || `Request failed (${res.status})`);
  }
  if (res.status === 204) return undefined as T;
  return res.json();
}

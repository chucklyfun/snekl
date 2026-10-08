import type {
  BusinessAccountSummary,
  CreateBusinessServerErrorBody,
  CreateBusinessServerResponse,
} from './businessHosting';
import { applyMemberProof, readBoundToken } from './deviceKey';
import { defaultContentFallback, getAuthBaseUrl, rewriteLoopbackForPage } from './hosts';

export class BusinessHostingRequestError extends Error {
  readonly status: number;
  readonly body: CreateBusinessServerErrorBody;

  constructor(status: number, body: CreateBusinessServerErrorBody, url: string) {
    super(body.error ?? `Request failed (${status}) at ${url}`);
    this.name = 'BusinessHostingRequestError';
    this.status = status;
    this.body = body;
  }
}

async function memberFetch<T>(
  url: string,
  init: Omit<RequestInit, 'headers'> & { headers?: Record<string, string> } = {},
): Promise<T> {
  const headers: Record<string, string> = { 'Content-Type': 'application/json', ...(init.headers ?? {}) };
  const token = readBoundToken('snekl_token');
  if (token) await applyMemberProof(headers, init.method ?? 'GET', url, token);
  let response: Response;
  try {
    response = await fetch(url, { ...init, headers });
  } catch {
    throw new BusinessHostingRequestError(0, { error: `Can't reach ${new URL(url).origin}.` }, url);
  }
  if (!response.ok) {
    const body = (await response.json().catch(() => ({}))) as CreateBusinessServerErrorBody;
    throw new BusinessHostingRequestError(response.status, body, url);
  }
  return (await response.json()) as T;
}

/** The signed-in member's business accounts, from their Auth. */
export function fetchMyBusinessAccounts(): Promise<BusinessAccountSummary[]> {
  const url = `${rewriteLoopbackForPage(getAuthBaseUrl())}/api/business/me`;
  return memberFetch<BusinessAccountSummary[]>(url);
}

/** Asks the configured content host to create (or finish linking) the business's hosted server. */
export function createBusinessServer(businessAccountId: string): Promise<CreateBusinessServerResponse> {
  const root = rewriteLoopbackForPage(defaultContentFallback());
  const url = `${root}/api/host/business-server`;
  const headers: Record<string, string> = {};
  try {
    // Same per-origin server selection the rest of the app uses (see api.ts request()).
    const serverId = sessionStorage.getItem(`snekl-server:${root}`);
    if (serverId) headers['X-Snekl-Server'] = serverId;
  } catch {
    // Session storage can be unavailable in private embeddings.
  }
  return memberFetch<CreateBusinessServerResponse>(url, {
    method: 'POST',
    headers,
    body: JSON.stringify({ businessAccountId }),
  });
}

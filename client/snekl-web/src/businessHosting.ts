/**
 * Pure logic for the "Hosted server" card on /servers: which business account to show, what state it
 * is in, and how to word errors from POST /api/host/business-server. No browser APIs here so it can be
 * unit tested; network calls live in businessHostingApi.ts.
 */

/** Shape of one row from Auth GET /api/business/me (only the fields the card needs). */
export type BusinessAccountSummary = {
  id: string;
  businessName: string;
  status: string;
  createdAt: string;
  reviewedAt?: string | null;
  serverId?: string | null;
  serverDid?: string | null;
  serverBaseUrl?: string | null;
};

export type BusinessCardKind = 'pending' | 'approved' | 'rejected' | 'has-server';

export type BusinessCardState =
  | { kind: 'none' }
  | { kind: BusinessCardKind; business: BusinessAccountSummary };

/** Content host response after creating (or finishing) the hosted server. */
export type CreateBusinessServerResponse = {
  serverId: string;
  serverDid: string;
  name: string;
  baseUrl: string;
  businessAccountId?: string | null;
  created: boolean;
};

export type CreateBusinessServerErrorBody = {
  error?: string;
  code?: string;
  status?: string;
  serverId?: string | null;
  serverDid?: string | null;
};

export function businessCardKind(business: BusinessAccountSummary): BusinessCardKind {
  if (business.serverId) return 'has-server';
  if (business.status === 'approved') return 'approved';
  if (business.status === 'rejected') return 'rejected';
  return 'pending';
}

const KIND_ORDER: Record<BusinessCardKind, number> = {
  approved: 0,
  'has-server': 1,
  pending: 2,
  rejected: 3,
};

/**
 * One card per member: an approved business that still needs its server comes first, then one that
 * has a server, then pending, then rejected. Newest wins within a group.
 */
export function pickBusinessCardState(accounts: readonly BusinessAccountSummary[] | null | undefined): BusinessCardState {
  if (!accounts || accounts.length === 0) return { kind: 'none' };
  const sorted = [...accounts].sort((a, b) => {
    const byKind = KIND_ORDER[businessCardKind(a)] - KIND_ORDER[businessCardKind(b)];
    if (byKind !== 0) return byKind;
    return (Date.parse(b.createdAt) || 0) - (Date.parse(a.createdAt) || 0);
  });
  const business = sorted[0];
  return { kind: businessCardKind(business), business };
}

/** Folds a successful create into the list so the card flips to "has a server" without a reload. */
export function applyCreatedServer(
  accounts: readonly BusinessAccountSummary[],
  businessId: string,
  response: CreateBusinessServerResponse,
): BusinessAccountSummary[] {
  return accounts.map((account) =>
    account.id === businessId
      ? {
          ...account,
          serverId: response.serverId,
          serverDid: response.serverDid,
          serverBaseUrl: response.baseUrl,
        }
      : account,
  );
}

export function createBusinessServerErrorMessage(status: number, body: CreateBusinessServerErrorBody): string {
  switch (body.code) {
    case 'not_approved':
      return body.status === 'rejected'
        ? 'This business account was not approved, so a hosted server is not available.'
        : 'This business account is still waiting for approval.';
    case 'already_has_server':
      return 'This business already has a hosted server.';
    case 'link_failed':
      return 'Your server was created, but setup did not finish. Try again to finish setup.';
    case 'auth_unavailable':
      return 'Could not reach your auth server. Try again in a moment.';
    case 'auth_rejected':
      return 'This host is not set up to create business servers yet. Contact the host operator.';
    case 'no_business':
      return 'No business account was found for this Snekl Account.';
    case 'not_signed_in':
      return 'Please log in again to create your hosted server.';
    default:
      return body.error ?? `Request failed (${status}).`;
  }
}

/** Errors after which the card should re-read /api/business/me (state may have changed underneath). */
export function shouldReloadAfterError(body: CreateBusinessServerErrorBody): boolean {
  return body.code === 'already_has_server' || body.code === 'not_approved' || body.code === 'no_business';
}

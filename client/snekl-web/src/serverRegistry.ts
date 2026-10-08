import type { ContentServerMembership } from './types';
import { defaultContentFallback, defaultHubFallback, homeSubscriptionsEnabled } from './hosts';

export function isFeedVisibleStatus(status: string): boolean {
  return status === 'active' || status === 'favorite';
}

/** True when this account has nothing to query yet and we should attach the local home node. */
export function shouldBootstrapHomeSubscriptions(options: {
  hasToken: boolean;
  loading: boolean;
  serverCount: number;
  federationCount: number;
  alreadyAttempted: boolean;
  enabled?: boolean;
}): boolean {
  const enabled = options.enabled ?? homeSubscriptionsEnabled();
  return (
    enabled &&
    options.hasToken &&
    !options.loading &&
    !options.alreadyAttempted &&
    options.serverCount === 0 &&
    options.federationCount === 0
  );
}

export function homeSubscriptionUrls(): { content: string; hub: string } {
  return {
    content: defaultContentFallback(),
    hub: defaultHubFallback(),
  };
}

/** Origins advertised by a subscribed hub, treated as active unless the user muted/blocked them. */
export function federationOriginMembership(origin: {
  did: string;
  baseUrl: string;
  name: string;
}): ContentServerMembership {
  return {
    serverDid: origin.did,
    baseUrl: origin.baseUrl.replace(/\/$/, ''),
    name: origin.name || origin.baseUrl,
    status: 'active',
    subscribedAt: '',
  };
}

export function originForNav(
  origin: ContentServerMembership,
  subscriptions: ContentServerMembership[],
): ContentServerMembership | null {
  const sub = subscriptions.find((s) => s.serverDid === origin.serverDid);
  if (sub && !isFeedVisibleStatus(sub.status)) return null;
  return sub ?? origin;
}

/**
 * Personal subscriptions win on status. Hub map origins are included when the user
 * has not muted or blocked that DID.
 */
export function mergeNetworkServers(
  subscriptions: ContentServerMembership[],
  federationOrigins: ContentServerMembership[],
): ContentServerMembership[] {
  const personal = new Map(subscriptions.map((server) => [server.serverDid, server]));
  const byDid = new Map<string, ContentServerMembership>();

  for (const origin of federationOrigins) {
    const sub = personal.get(origin.serverDid);
    if (sub && !isFeedVisibleStatus(sub.status)) continue;
    byDid.set(origin.serverDid, sub ?? origin);
  }

  for (const sub of subscriptions) {
    if (!isFeedVisibleStatus(sub.status)) continue;
    byDid.set(sub.serverDid, sub);
  }

  return [...byDid.values()];
}

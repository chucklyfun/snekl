import { describe, expect, it } from 'vitest';
import {
  federationOriginMembership,
  isFeedVisibleStatus,
  mergeNetworkServers,
  originForNav,
  shouldBootstrapHomeSubscriptions,
} from './serverRegistry';
import type { ContentServerMembership } from './types';

function sub(
  did: string,
  name: string,
  status: string,
  baseUrl = `http://localhost/${did}`,
): ContentServerMembership {
  return { serverDid: did, baseUrl, name, status, subscribedAt: '2026-01-01' };
}

describe('mergeNetworkServers', () => {
  it('includes hub origins that are not in the personal subscription list', () => {
    const network = mergeNetworkServers(
      [sub('did:alpha', 'Snekl Alpha', 'favorite')],
      [
        federationOriginMembership({ did: 'did:alpha', baseUrl: 'http://localhost:5001', name: 'Snekl Alpha' }),
        federationOriginMembership({ did: 'did:beta', baseUrl: 'http://localhost:5002', name: 'Snekl Beta' }),
      ],
    );

    expect(network.map((s) => s.name).sort()).toEqual(['Snekl Alpha', 'Snekl Beta']);
    expect(network.find((s) => s.serverDid === 'did:alpha')?.status).toBe('favorite');
  });

  it('omits a hub origin the user muted', () => {
    const network = mergeNetworkServers(
      [sub('did:beta', 'Snekl Beta', 'muted')],
      [federationOriginMembership({ did: 'did:beta', baseUrl: 'http://localhost:5002', name: 'Snekl Beta' })],
    );
    expect(network).toEqual([]);
  });
});

describe('originForNav', () => {
  it('omits muted personal subscriptions from a hub origin', () => {
    const origin = federationOriginMembership({
      did: 'did:beta',
      baseUrl: 'http://localhost:5002',
      name: 'Snekl Beta',
    });
    expect(originForNav(origin, [sub('did:beta', 'Snekl Beta', 'muted')])).toBeNull();
    expect(originForNav(origin, [sub('did:beta', 'Snekl Beta', 'favorite')])?.status).toBe('favorite');
  });
});

describe('isFeedVisibleStatus', () => {
  it('treats active and favorite as visible', () => {
    expect(isFeedVisibleStatus('active')).toBe(true);
    expect(isFeedVisibleStatus('favorite')).toBe(true);
    expect(isFeedVisibleStatus('muted')).toBe(false);
  });
});

describe('shouldBootstrapHomeSubscriptions', () => {
  const ready = {
    hasToken: true,
    loading: false,
    serverCount: 0,
    federationCount: 0,
    alreadyAttempted: false,
    enabled: true,
  };

  it('runs once for a signed-in account with no servers', () => {
    expect(shouldBootstrapHomeSubscriptions(ready)).toBe(true);
  });

  it('skips anonymous visitors, in-flight loads, existing lists, and retries', () => {
    expect(shouldBootstrapHomeSubscriptions({ ...ready, hasToken: false })).toBe(false);
    expect(shouldBootstrapHomeSubscriptions({ ...ready, loading: true })).toBe(false);
    expect(shouldBootstrapHomeSubscriptions({ ...ready, serverCount: 1 })).toBe(false);
    expect(shouldBootstrapHomeSubscriptions({ ...ready, federationCount: 1 })).toBe(false);
    expect(shouldBootstrapHomeSubscriptions({ ...ready, alreadyAttempted: true })).toBe(false);
    expect(shouldBootstrapHomeSubscriptions({ ...ready, enabled: false })).toBe(false);
  });
});

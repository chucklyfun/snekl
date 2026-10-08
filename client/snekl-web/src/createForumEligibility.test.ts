import { describe, expect, it } from 'vitest';
import { slugifyForumName, sortEligibleServers, type EligibleServer } from './createForumEligibility';

function server(partial: Partial<EligibleServer> & Pick<EligibleServer, 'serverDid' | 'name' | 'status'>): EligibleServer {
  return {
    baseUrl: 'http://localhost',
    subscribedAt: '',
    canCreateTopLevel: true,
    canRequestTopLevel: false,
    createForumRoles: [],
    ...partial,
  };
}

describe('sortEligibleServers', () => {
  it('puts favorites first, then active, then by name', () => {
    const sorted = sortEligibleServers([
      server({ serverDid: 'c', name: 'Zeta', status: 'active' }),
      server({ serverDid: 'a', name: 'Alpha', status: 'paused' }),
      server({ serverDid: 'b', name: 'Beta', status: 'favorite' }),
      server({ serverDid: 'd', name: 'Gamma', status: 'active' }),
    ]);
    expect(sorted.map((s) => s.name)).toEqual(['Beta', 'Gamma', 'Zeta', 'Alpha']);
  });
});

describe('slugifyForumName', () => {
  it('builds a lowercase hyphenated slug', () => {
    expect(slugifyForumName('Hello World!')).toBe('hello-world');
  });
});

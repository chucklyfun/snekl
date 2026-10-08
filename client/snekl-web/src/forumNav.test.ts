import { describe, expect, it } from 'vitest';
import type { ContentServerMembership, FederationOriginGroup } from './types';
import {
  DIRECT_SERVERS_GROUP_ID,
  discoverableForums,
  favoriteForums,
  filterBrowseForums,
  forumNavKey,
  groupForumsByFederation,
  groupForumsByServer,
  joinedForums,
  managedForums,
  normalizeGroupAxes,
  originGroupingAxes,
  type ForumNavItem,
} from './forumNav';

function item(partial: Partial<ForumNavItem> & Pick<ForumNavItem, 'forumId' | 'name' | 'slug'>): ForumNavItem {
  const serverDid = partial.serverDid ?? 'did:alpha';
  return {
    key: forumNavKey(serverDid, partial.forumId),
    serverDid,
    serverName: partial.serverName ?? 'Alpha',
    serverBaseUrl: partial.serverBaseUrl ?? 'http://alpha',
    forumId: partial.forumId,
    name: partial.name,
    slug: partial.slug,
    parentSlug: partial.parentSlug ?? null,
    parentForumId: partial.parentForumId ?? null,
    viewerIsMember: partial.viewerIsMember ?? false,
    viewerCanManage: partial.viewerCanManage ?? false,
    viewerIsFavorite: partial.viewerIsFavorite ?? false,
    approvalStatus: partial.approvalStatus,
    avatarUrl: partial.avatarUrl,
  };
}

function server(did: string, name: string): ContentServerMembership {
  return { serverDid: did, baseUrl: `http://${name}`, name, status: 'active', subscribedAt: '' };
}

describe('nest nav sections', () => {
  it('keeps lurked nests out of joined and manage', () => {
    const lurk = item({ forumId: '1', name: 'Public', slug: 'public' });
    const joined = item({ forumId: '2', name: 'Club', slug: 'club', viewerIsMember: true });
    const managed = item({
      forumId: '3',
      name: 'Staff',
      slug: 'staff',
      viewerIsMember: true,
      viewerCanManage: true,
    });

    expect(joinedForums([lurk, joined, managed]).map((f) => f.slug)).toEqual(['club', 'staff']);
    expect(managedForums([lurk, joined, managed]).map((f) => f.slug)).toEqual(['staff']);
    expect(discoverableForums([lurk, joined, managed]).map((f) => f.slug)).toEqual(['public']);
  });

  it('filters browse nests by name description and slug', () => {
    const forums = [
      { name: 'Garden', slug: 'garden', description: 'plants and dirt' },
      { name: 'Code', slug: 'dev-talk', description: 'compilers' },
    ];
    expect(filterBrowseForums(forums, 'GARD').map((f) => f.slug)).toEqual(['garden']);
    expect(filterBrowseForums(forums, 'compiler').map((f) => f.slug)).toEqual(['dev-talk']);
    expect(filterBrowseForums(forums, 'dev-talk').map((f) => f.slug)).toEqual(['dev-talk']);
    expect(filterBrowseForums(forums, '').length).toBe(2);
  });

  it('includes pending nests in manage and only subscribed favorites', () => {
    const pending = item({ forumId: 'p', name: 'Pending', slug: 'pending', approvalStatus: 'pending' });
    const starred = item({
      forumId: '2',
      name: 'Club',
      slug: 'club',
      viewerIsMember: true,
      viewerIsFavorite: true,
    });
    const lurkStar = item({
      forumId: '3',
      name: 'Nope',
      slug: 'nope',
      viewerIsFavorite: true,
    });

    expect(managedForums([pending]).map((f) => f.slug)).toEqual(['pending']);
    expect(discoverableForums([pending])).toEqual([]);
    expect(favoriteForums([starred, lurkStar]).map((f) => f.slug)).toEqual(['club']);
  });
});

describe('nest nav grouping', () => {
  it('nests subnests under their parent when grouping by server', () => {
    const parent = item({ forumId: 'parent', name: 'Garden', slug: 'garden', viewerIsMember: true });
    const child = item({
      forumId: 'child',
      name: 'Roses',
      slug: 'roses',
      parentForumId: 'parent',
      parentSlug: 'garden',
      viewerIsMember: true,
    });

    const groups = groupForumsByServer([child, parent]);
    expect(groups).toHaveLength(1);
    expect(groups[0].tree).toHaveLength(1);
    expect(groups[0].tree[0].slug).toBe('garden');
    expect(groups[0].tree[0].children.map((c) => c.slug)).toEqual(['roses']);
  });

  it('duplicates a server across federations and buckets unaffiliated origins as Direct servers', () => {
    const alphaForum = item({
      forumId: 'a',
      name: 'Alpha Nest',
      slug: 'alpha',
      serverDid: 'did:alpha',
      serverName: 'Alpha',
      viewerIsMember: true,
    });
    const betaForum = item({
      forumId: 'b',
      name: 'Beta Nest',
      slug: 'beta',
      serverDid: 'did:beta',
      serverName: 'Beta',
      viewerIsMember: true,
    });
    const loneForum = item({
      forumId: 'c',
      name: 'Lone',
      slug: 'lone',
      serverDid: 'did:lone',
      serverName: 'Lone',
      viewerIsMember: true,
    });

    const federations: FederationOriginGroup[] = [
      { baseUrl: 'http://hub-a', name: 'Fed A', origins: [server('did:alpha', 'Alpha')] },
      {
        baseUrl: 'http://hub-b',
        name: 'Fed B',
        origins: [server('did:alpha', 'Alpha'), server('did:beta', 'Beta')],
      },
    ];

    const groups = groupForumsByFederation(
      [alphaForum, betaForum, loneForum],
      federations,
      [server('did:alpha', 'Alpha'), server('did:beta', 'Beta'), server('did:lone', 'Lone')],
    );

    expect(groups.map((g) => g.name)).toEqual(['Fed A', 'Fed B', 'Direct servers']);
    expect(groups[0].servers.map((s) => s.serverDid)).toEqual(['did:alpha']);
    expect(groups[1].servers.map((s) => s.serverDid).sort()).toEqual(['did:alpha', 'did:beta']);
    expect(groups[2].id).toBe(DIRECT_SERVERS_GROUP_ID);
    expect(groups[2].servers.map((s) => s.serverDid)).toEqual(['did:lone']);
  });

  it('treats an empty axis list as ungrouped subscribed nests', () => {
    expect(normalizeGroupAxes([])).toEqual([]);
    expect(normalizeGroupAxes(['server', 'section', 'server', 'nope'])).toEqual(['server', 'section']);
    expect(originGroupingAxes(['section', 'federation', 'server'])).toEqual(['federation', 'server']);
  });
});

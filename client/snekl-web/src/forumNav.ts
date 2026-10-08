import type { AggregatedForum, ContentServerMembership, FederationOriginGroup, ForumFavorite } from './types';

export const DIRECT_SERVERS_GROUP_ID = 'direct';
export const FORUM_NAV_GROUP_KEY = 'snekl_forum_nav_group';
export const MAX_FORUM_RECENTS = 15;

export type ForumNavAxis = 'section' | 'federation' | 'server';
export const FORUM_NAV_AXES: ForumNavAxis[] = ['section', 'federation', 'server'];
export const FORUM_NAV_AXIS_LABELS: Record<ForumNavAxis, string> = {
  section: 'Section',
  federation: 'Federation',
  server: 'Server',
};

export type ForumNavGroupMode = 'federation' | 'server';

export type ForumNavItem = {
  key: string;
  serverDid: string;
  serverName: string;
  serverBaseUrl: string;
  forumId: string;
  name: string;
  slug: string;
  parentSlug?: string | null;
  parentForumId?: string | null;
  viewerIsMember: boolean;
  viewerCanManage: boolean;
  viewerIsFavorite: boolean;
  approvalStatus?: string;
  avatarUrl?: string | null;
};

export type ForumRecent = {
  serverDid: string;
  forumId: string;
  name: string;
  slug: string;
  parentSlug?: string | null;
  serverName: string;
  serverBaseUrl?: string;
  visitedAt: string;
};

export type ForumTreeNode = ForumNavItem & { children: ForumTreeNode[] };

export type ServerForumGroup = {
  serverDid: string;
  serverName: string;
  serverBaseUrl: string;
  tree: ForumTreeNode[];
};

export type FederationForumGroup = {
  id: string;
  name: string;
  servers: ServerForumGroup[];
};

export type ForumNavAxisGroup = {
  id: string;
  name: string;
  forums: ForumNavItem[];
};

export function forumNavKey(serverDid: string, forumId: string): string {
  return `${serverDid}:${forumId}`;
}

export function toForumNavItem(forum: AggregatedForum): ForumNavItem {
  return {
    key: forumNavKey(forum.serverDid, forum.id),
    serverDid: forum.serverDid,
    serverName: forum.serverName,
    serverBaseUrl: forum.serverBaseUrl,
    forumId: forum.id,
    name: forum.name,
    slug: forum.slug,
    parentSlug: forum.parentSlug,
    parentForumId: forum.parentForumId,
    viewerIsMember: Boolean(forum.viewerIsMember),
    viewerCanManage: Boolean(forum.viewerCanManage),
    viewerIsFavorite: Boolean(forum.viewerIsFavorite),
    approvalStatus: forum.approvalStatus,
    avatarUrl: forum.avatarUrl,
  };
}

export function favoriteToNavItem(favorite: ForumFavorite, serverBaseUrl = ''): ForumNavItem {
  return {
    key: forumNavKey(favorite.serverDid, favorite.forumId),
    serverDid: favorite.serverDid,
    serverName: favorite.serverName,
    serverBaseUrl,
    forumId: favorite.forumId,
    name: favorite.name,
    slug: favorite.slug,
    parentSlug: favorite.parentSlug,
    parentForumId: null,
    viewerIsMember: false,
    viewerCanManage: false,
    viewerIsFavorite: false,
  };
}

export function recentToNavItem(recent: ForumRecent): ForumNavItem {
  return {
    key: forumNavKey(recent.serverDid, recent.forumId),
    serverDid: recent.serverDid,
    serverName: recent.serverName,
    serverBaseUrl: recent.serverBaseUrl ?? '',
    forumId: recent.forumId,
    name: recent.name,
    slug: recent.slug,
    parentSlug: recent.parentSlug,
    parentForumId: null,
    viewerIsMember: false,
    viewerCanManage: false,
    viewerIsFavorite: false,
  };
}

export function joinedForums(forums: ForumNavItem[]): ForumNavItem[] {
  return forums.filter((forum) => forum.viewerIsMember);
}

export function discoverableForums(forums: ForumNavItem[]): ForumNavItem[] {
  return forums.filter(
    (forum) =>
      !forum.viewerIsMember &&
      !forum.viewerCanManage &&
      forum.approvalStatus !== 'pending',
  );
}

export function managedForums(forums: ForumNavItem[]): ForumNavItem[] {
  return forums.filter(
    (forum) => forum.viewerCanManage || forum.approvalStatus === 'pending',
  );
}

export function favoriteForums(forums: ForumNavItem[]): ForumNavItem[] {
  return forums.filter((forum) => forum.viewerIsFavorite && forum.viewerIsMember);
}

export function recentForums(forums: ForumNavItem[], recents: ForumRecent[]): ForumNavItem[] {
  const byKey = new Map(forums.map((forum) => [forum.key, forum]));
  return recents.map((recent) => byKey.get(forumNavKey(recent.serverDid, recent.forumId)) ?? recentToNavItem(recent));
}

export function filterBrowseForums<T extends { name: string; slug: string; description?: string }>(
  forums: T[],
  query: string,
): T[] {
  const needle = query.trim().toLowerCase();
  if (!needle) return forums;
  return forums.filter(
    (forum) =>
      forum.name.toLowerCase().includes(needle) ||
      forum.slug.toLowerCase().includes(needle) ||
      (forum.description ?? '').toLowerCase().includes(needle),
  );
}

export function buildForumTree(forums: ForumNavItem[]): ForumTreeNode[] {
  const nodes = new Map<string, ForumTreeNode>();
  for (const forum of forums) {
    nodes.set(forum.forumId, { ...forum, children: [] });
  }

  const roots: ForumTreeNode[] = [];
  for (const node of [...nodes.values()].sort((a, b) => a.name.localeCompare(b.name))) {
    const parentId = node.parentForumId;
    if (parentId && nodes.has(parentId)) {
      nodes.get(parentId)!.children.push(node);
    } else {
      roots.push(node);
    }
  }
  return roots;
}

export function groupForumsByServer(forums: ForumNavItem[]): ServerForumGroup[] {
  const byServer = new Map<string, ForumNavItem[]>();
  for (const forum of forums) {
    const list = byServer.get(forum.serverDid) ?? [];
    list.push(forum);
    byServer.set(forum.serverDid, list);
  }

  return [...byServer.values()]
    .map((items) => ({
      serverDid: items[0].serverDid,
      serverName: items[0].serverName,
      serverBaseUrl: items[0].serverBaseUrl,
      tree: buildForumTree(items),
    }))
    .filter((group) => group.tree.length > 0)
    .sort((a, b) => a.serverName.localeCompare(b.serverName));
}

export function groupForumsByFederation(
  forums: ForumNavItem[],
  federationGroups: FederationOriginGroup[],
  activeServers: ContentServerMembership[],
): FederationForumGroup[] {
  const federatedDids = new Set<string>();
  const groups: FederationForumGroup[] = [];

  for (const federation of federationGroups) {
    const originDids = new Set(federation.origins.map((origin) => origin.serverDid));
    originDids.forEach((did) => federatedDids.add(did));
    const servers = groupForumsByServer(forums.filter((forum) => originDids.has(forum.serverDid)));
    if (servers.length === 0) continue;
    groups.push({
      id: federation.baseUrl,
      name: federation.name,
      servers,
    });
  }

  const direct = groupForumsByServer(
    forums.filter((forum) => {
      if (federatedDids.has(forum.serverDid)) return false;
      return activeServers.some((server) => server.serverDid === forum.serverDid);
    }),
  );
  if (direct.length > 0) {
    groups.push({
      id: DIRECT_SERVERS_GROUP_ID,
      name: 'Direct servers',
      servers: direct,
    });
  }

  return groups;
}

export function normalizeGroupAxes(axes?: string[] | null): ForumNavAxis[] {
  const seen = new Set<ForumNavAxis>();
  const result: ForumNavAxis[] = [];
  for (const axis of axes ?? []) {
    if (axis !== 'section' && axis !== 'federation' && axis !== 'server') continue;
    if (seen.has(axis)) continue;
    seen.add(axis);
    result.push(axis);
  }
  return result;
}

export function originGroupingAxes(axes: ForumNavAxis[]): Array<'federation' | 'server'> {
  return axes.filter((axis): axis is 'federation' | 'server' => axis !== 'section');
}

export function partitionByServer(forums: ForumNavItem[]): ForumNavAxisGroup[] {
  return groupForumsByServer(forums).map((group) => ({
    id: group.serverDid,
    name: group.serverName,
    forums: flattenForumTree(group.tree),
  }));
}

export function partitionByFederation(
  forums: ForumNavItem[],
  federationGroups: FederationOriginGroup[],
  activeServers: ContentServerMembership[],
): ForumNavAxisGroup[] {
  return groupForumsByFederation(forums, federationGroups, activeServers).map((group) => ({
    id: group.id,
    name: group.name,
    forums: group.servers.flatMap((server) => flattenForumTree(server.tree)),
  }));
}

export function flattenForumTree(nodes: ForumTreeNode[]): ForumNavItem[] {
  const items: ForumNavItem[] = [];
  const walk = (node: ForumTreeNode) => {
    const { children, ...item } = node;
    items.push(item);
    children.forEach(walk);
  };
  nodes.forEach(walk);
  return items;
}

export function federationNamesForServer(
  serverDid: string,
  federationGroups: FederationOriginGroup[],
): string[] {
  const names: string[] = [];
  const seen = new Set<string>();
  for (const federation of federationGroups) {
    if (!federation.origins.some((origin) => origin.serverDid === serverDid)) continue;
    if (seen.has(federation.name)) continue;
    seen.add(federation.name);
    names.push(federation.name);
  }
  return names;
}

export function readForumNavGroupMode(): ForumNavGroupMode {
  try {
    const value = localStorage.getItem(FORUM_NAV_GROUP_KEY);
    return value === 'server' ? 'server' : 'federation';
  } catch {
    return 'federation';
  }
}

export function writeForumNavGroupMode(mode: ForumNavGroupMode): void {
  try {
    localStorage.setItem(FORUM_NAV_GROUP_KEY, mode);
  } catch {
    // Ignore quota / private mode.
  }
}

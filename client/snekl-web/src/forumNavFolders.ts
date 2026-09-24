import { forumNavKey, type ForumNavItem } from './forumNav';

export type ForumNavFolder = {
  id: string;
  name: string;
  parentId: string | null;
  position: number;
};

export type ForumNavFolderItem = {
  folderId: string;
  serverDid: string;
  forumId: string;
  name: string;
  slug: string;
  parentSlug?: string | null;
  serverName: string;
  position: number;
};

export type ForumNavHidden = {
  sectionId: string;
  serverDid: string;
  forumId: string;
};

export type ForumNavPeerSettings = {
  id: string;
  enabled: boolean;
  position: number;
  groupAxes?: Array<'federation' | 'server'> | null;
};

export type ForumNavLayout = {
  folders: ForumNavFolder[];
  items: ForumNavFolderItem[];
  hidden: ForumNavHidden[];
  groupAxes?: string[];
  peers?: ForumNavPeerSettings[];
};

export const FORUM_NAV_BUILTIN_PEERS = [
  { id: 'section:favorites', title: 'Favorites' },
  { id: 'section:recents', title: 'Recent visits' },
  { id: 'section:manage', title: 'Forums you manage' },
  { id: 'section:joined', title: "Forums you've joined" },
] as const;

export const FORUM_NAV_HIDEABLE_SECTIONS = [
  'section:favorites',
  'section:recents',
  'section:manage',
  'section:joined',
] as const;

export type ForumNavHideableSection = (typeof FORUM_NAV_HIDEABLE_SECTIONS)[number];

export type FolderTreeNode = ForumNavFolder & {
  children: FolderTreeNode[];
  forums: ForumNavFolderItem[];
};

export const emptyForumNavLayout = (): ForumNavLayout => ({
  folders: [],
  items: [],
  hidden: [],
  groupAxes: ['section'],
  peers: [],
});

function hiddenList(layout: ForumNavLayout): ForumNavHidden[] {
  return layout.hidden ?? [];
}

function hiddenKey(sectionId: string, serverDid: string, forumId: string): string {
  return `${sectionId}:${serverDid}:${forumId}`;
}

export function newFolderId(): string {
  return crypto.randomUUID();
}

export function buildFolderTree(layout: ForumNavLayout): FolderTreeNode[] {
  const nodes = new Map<string, FolderTreeNode>();
  for (const folder of [...layout.folders].sort((a, b) => a.position - b.position || a.name.localeCompare(b.name))) {
    nodes.set(folder.id, { ...folder, children: [], forums: [] });
  }

  for (const item of [...layout.items].sort((a, b) => a.position - b.position || a.name.localeCompare(b.name))) {
    nodes.get(item.folderId)?.forums.push(item);
  }

  const roots: FolderTreeNode[] = [];
  for (const node of nodes.values()) {
    if (node.parentId && nodes.has(node.parentId)) {
      nodes.get(node.parentId)!.children.push(node);
    } else {
      roots.push(node);
    }
  }
  return roots;
}

export function flattenFolderOptions(layout: ForumNavLayout): Array<{ id: string; label: string }> {
  const ids = new Set(layout.folders.map((folder) => folder.id));
  const byParent = new Map<string | null, ForumNavFolder[]>();
  for (const folder of layout.folders) {
    const key = folder.parentId && ids.has(folder.parentId) ? folder.parentId : null;
    const list = byParent.get(key) ?? [];
    list.push(folder);
    byParent.set(key, list);
  }
  for (const list of byParent.values()) {
    list.sort((a, b) => a.position - b.position || a.name.localeCompare(b.name));
  }

  const result: Array<{ id: string; label: string }> = [];
  const walk = (parentId: string | null, prefix: string) => {
    for (const folder of byParent.get(parentId) ?? []) {
      const label = prefix ? `${prefix} / ${folder.name}` : folder.name;
      result.push({ id: folder.id, label });
      walk(folder.id, label);
    }
  };
  walk(null, '');
  return result;
}

export function createFolder(layout: ForumNavLayout, name: string, parentId: string | null): ForumNavLayout {
  const trimmed = name.trim();
  if (!trimmed) return layout;
  const siblings = layout.folders.filter((folder) => (folder.parentId ?? null) === parentId);
  const position = siblings.reduce((max, folder) => Math.max(max, folder.position), -1) + 1;
  return {
    ...layout,
    folders: [
      ...layout.folders,
      { id: newFolderId(), name: trimmed.slice(0, 80), parentId, position },
    ],
  };
}

export function renameFolder(layout: ForumNavLayout, folderId: string, name: string): ForumNavLayout {
  const trimmed = name.trim().slice(0, 80);
  if (!trimmed) return layout;
  return {
    ...layout,
    folders: layout.folders.map((folder) => (folder.id === folderId ? { ...folder, name: trimmed } : folder)),
  };
}

export function deleteFolder(layout: ForumNavLayout, folderId: string): ForumNavLayout {
  const remove = new Set<string>([folderId]);
  let added = true;
  while (added) {
    added = false;
    for (const folder of layout.folders) {
      if (folder.parentId && remove.has(folder.parentId) && !remove.has(folder.id)) {
        remove.add(folder.id);
        added = true;
      }
    }
  }
  return {
    ...layout,
    folders: layout.folders.filter((folder) => !remove.has(folder.id)),
    items: layout.items.filter((item) => !remove.has(item.folderId)),
  };
}

export function addForumToFolder(
  layout: ForumNavLayout,
  folderId: string,
  forum: Pick<ForumNavItem, 'serverDid' | 'forumId' | 'name' | 'slug' | 'parentSlug' | 'serverName'>,
): ForumNavLayout {
  if (!layout.folders.some((folder) => folder.id === folderId)) return layout;
  const exists = layout.items.some(
    (item) =>
      item.folderId === folderId && item.serverDid === forum.serverDid && item.forumId === forum.forumId,
  );
  if (exists) return layout;
  const siblings = layout.items.filter((item) => item.folderId === folderId);
  const position = siblings.reduce((max, item) => Math.max(max, item.position), -1) + 1;
  return {
    ...layout,
    items: [
      ...layout.items,
      {
        folderId,
        serverDid: forum.serverDid,
        forumId: forum.forumId,
        name: forum.name,
        slug: forum.slug,
        parentSlug: forum.parentSlug,
        serverName: forum.serverName,
        position,
      },
    ],
  };
}

export function removeForumFromFolder(
  layout: ForumNavLayout,
  folderId: string,
  serverDid: string,
  forumId: string,
): ForumNavLayout {
  return {
    ...layout,
    items: layout.items.filter(
      (item) => !(item.folderId === folderId && item.serverDid === serverDid && item.forumId === forumId),
    ),
  };
}

export function folderItemToNavItem(
  item: ForumNavFolderItem,
  forums: ForumNavItem[],
): ForumNavItem {
  const key = forumNavKey(item.serverDid, item.forumId);
  return (
    forums.find((forum) => forum.key === key) ?? {
      key,
      serverDid: item.serverDid,
      serverName: item.serverName,
      serverBaseUrl: '',
      forumId: item.forumId,
      name: item.name,
      slug: item.slug,
      parentSlug: item.parentSlug,
      parentForumId: null,
      viewerIsMember: false,
      viewerCanManage: false,
      viewerIsFavorite: false,
    }
  );
}

export function isForumHiddenInSection(
  layout: ForumNavLayout,
  sectionId: string,
  serverDid: string,
  forumId: string,
): boolean {
  const key = hiddenKey(sectionId, serverDid, forumId);
  return hiddenList(layout).some(
    (entry) => hiddenKey(entry.sectionId, entry.serverDid, entry.forumId) === key,
  );
}

export function hideForumInSection(
  layout: ForumNavLayout,
  sectionId: string,
  forum: Pick<ForumNavItem, 'serverDid' | 'forumId'>,
): ForumNavLayout {
  if (!(FORUM_NAV_HIDEABLE_SECTIONS as readonly string[]).includes(sectionId)) return layout;
  if (isForumHiddenInSection(layout, sectionId, forum.serverDid, forum.forumId)) return layout;
  return {
    ...layout,
    hidden: [
      ...hiddenList(layout),
      { sectionId, serverDid: forum.serverDid, forumId: forum.forumId },
    ],
  };
}

export function unhideForumInSection(
  layout: ForumNavLayout,
  sectionId: string,
  serverDid: string,
  forumId: string,
): ForumNavLayout {
  const key = hiddenKey(sectionId, serverDid, forumId);
  return {
    ...layout,
    hidden: hiddenList(layout).filter(
      (entry) => hiddenKey(entry.sectionId, entry.serverDid, entry.forumId) !== key,
    ),
  };
}

export function visibleForumsInSection(
  forums: ForumNavItem[],
  layout: ForumNavLayout,
  sectionId: string,
): ForumNavItem[] {
  return forums.filter((forum) => !isForumHiddenInSection(layout, sectionId, forum.serverDid, forum.forumId));
}

export function hiddenForumsInSection(
  forums: ForumNavItem[],
  layout: ForumNavLayout,
  sectionId: string,
): ForumNavItem[] {
  return forums.filter((forum) => isForumHiddenInSection(layout, sectionId, forum.serverDid, forum.forumId));
}

export type ResolvedForumNavPeer = {
  id: string;
  title: string;
  enabled: boolean;
  position: number;
  groupAxes: Array<'federation' | 'server'> | null;
  kind: 'builtin' | 'folder';
  folderId?: string;
};

function isRootFolder(layout: ForumNavLayout, folder: ForumNavFolder): boolean {
  const ids = new Set(layout.folders.map((item) => item.id));
  return !folder.parentId || !ids.has(folder.parentId);
}

export function layoutGroupAxes(layout: ForumNavLayout): Array<'section' | 'federation' | 'server'> {
  if (layout.groupAxes == null) return ['section'];
  const seen = new Set<string>();
  const result: Array<'section' | 'federation' | 'server'> = [];
  for (const axis of layout.groupAxes) {
    if (axis !== 'section' && axis !== 'federation' && axis !== 'server') continue;
    if (seen.has(axis)) continue;
    seen.add(axis);
    result.push(axis);
  }
  return result;
}

export function resolvedPeers(layout: ForumNavLayout): ResolvedForumNavPeer[] {
  const saved = new Map((layout.peers ?? []).map((peer) => [peer.id, peer]));
  const peers: ResolvedForumNavPeer[] = [];
  let fallback = 0;
  for (const builtin of FORUM_NAV_BUILTIN_PEERS) {
    const existing = saved.get(builtin.id);
    peers.push({
      id: builtin.id,
      title: builtin.title,
      enabled: existing?.enabled ?? true,
      position: existing?.position ?? fallback,
      groupAxes: existing?.groupAxes ?? null,
      kind: 'builtin',
    });
    fallback += 1;
  }
  for (const folder of layout.folders.filter((item) => isRootFolder(layout, item))) {
    const id = `folder:${folder.id}`;
    const existing = saved.get(id);
    peers.push({
      id,
      title: folder.name,
      enabled: existing?.enabled ?? true,
      position: existing?.position ?? fallback,
      groupAxes: existing?.groupAxes ?? null,
      kind: 'folder',
      folderId: folder.id,
    });
    fallback += 1;
  }
  return peers.sort((a, b) => a.position - b.position || a.title.localeCompare(b.title));
}

function persistPeers(layout: ForumNavLayout, peers: ResolvedForumNavPeer[]): ForumNavLayout {
  return {
    ...layout,
    peers: peers.map((peer, position) => ({
      id: peer.id,
      enabled: peer.enabled,
      position,
      groupAxes: peer.groupAxes,
    })),
  };
}

export function setLayoutGroupAxes(
  layout: ForumNavLayout,
  axes: Array<'section' | 'federation' | 'server'>,
): ForumNavLayout {
  return { ...layout, groupAxes: axes };
}

export function toggleLayoutAxis(
  layout: ForumNavLayout,
  axis: 'section' | 'federation' | 'server',
): ForumNavLayout {
  const current = layoutGroupAxes(layout);
  const next = current.includes(axis) ? current.filter((item) => item !== axis) : [...current, axis];
  return setLayoutGroupAxes(layout, next);
}

export function moveLayoutAxis(
  layout: ForumNavLayout,
  axis: 'section' | 'federation' | 'server',
  direction: -1 | 1,
): ForumNavLayout {
  const current = [...layoutGroupAxes(layout)];
  const index = current.indexOf(axis);
  const nextIndex = index + direction;
  if (index < 0 || nextIndex < 0 || nextIndex >= current.length) return layout;
  const swap = current[index];
  current[index] = current[nextIndex];
  current[nextIndex] = swap;
  return setLayoutGroupAxes(layout, current);
}

export function setPeerEnabled(layout: ForumNavLayout, peerId: string, enabled: boolean): ForumNavLayout {
  return persistPeers(
    layout,
    resolvedPeers(layout).map((peer) => (peer.id === peerId ? { ...peer, enabled } : peer)),
  );
}

export function movePeer(layout: ForumNavLayout, peerId: string, direction: -1 | 1): ForumNavLayout {
  const peers = resolvedPeers(layout);
  const index = peers.findIndex((peer) => peer.id === peerId);
  const nextIndex = index + direction;
  if (index < 0 || nextIndex < 0 || nextIndex >= peers.length) return layout;
  const swap = peers[index];
  peers[index] = peers[nextIndex];
  peers[nextIndex] = swap;
  return persistPeers(layout, peers);
}

export function setPeerGroupAxes(
  layout: ForumNavLayout,
  peerId: string,
  groupAxes: Array<'federation' | 'server'> | null,
): ForumNavLayout {
  return persistPeers(
    layout,
    resolvedPeers(layout).map((peer) => (peer.id === peerId ? { ...peer, groupAxes } : peer)),
  );
}

export function applyGroupingToAllPeers(
  layout: ForumNavLayout,
  groupAxes: Array<'federation' | 'server'>,
): ForumNavLayout {
  return persistPeers(
    layout,
    resolvedPeers(layout).map((peer) => ({ ...peer, groupAxes })),
  );
}

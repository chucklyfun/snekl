import { describe, expect, it, vi } from 'vitest';
import {
  addForumToFolder,
  buildFolderTree,
  createFolder,
  deleteFolder,
  flattenFolderOptions,
  hideForumInSection,
  hiddenForumsInSection,
  layoutGroupAxes,
  movePeer,
  renameFolder,
  removeForumFromFolder,
  resolvedPeers,
  setLayoutGroupAxes,
  setPeerEnabled,
  unhideForumInSection,
  visibleForumsInSection,
  type ForumNavLayout,
} from './forumNavFolders';

function layout(partial?: Partial<ForumNavLayout>): ForumNavLayout {
  return {
    folders: partial?.folders ?? [
      { id: 'art', name: 'Art', parentId: null, position: 0 },
      { id: 'fan', name: 'Fanart', parentId: 'art', position: 0 },
    ],
    items: partial?.items ?? [
      {
        folderId: 'fan',
        serverDid: 'did:a',
        forumId: '1',
        name: 'Roses',
        slug: 'roses',
        serverName: 'Alpha',
        position: 0,
      },
    ],
    hidden: partial?.hidden ?? [],
  };
}

describe('nest nav folders', () => {
  it('nests subgroups and nests in the folder tree', () => {
    const tree = buildFolderTree(layout());
    expect(tree).toHaveLength(1);
    expect(tree[0].name).toBe('Art');
    expect(tree[0].children.map((c) => c.name)).toEqual(['Fanart']);
    expect(tree[0].children[0].forums.map((f) => f.slug)).toEqual(['roses']);
  });

  it('flattens paths for add-to-group menus', () => {
    expect(flattenFolderOptions(layout()).map((o) => o.label)).toEqual(['Art', 'Art / Fanart']);
  });

  it('creates subgroups, renames, and cascade-deletes', () => {
    vi.stubGlobal('crypto', { randomUUID: () => 'new-id' });
    const withChild = createFolder(layout(), 'Commissions', 'art');
    expect(withChild.folders.some((f) => f.id === 'new-id' && f.parentId === 'art')).toBe(true);

    const renamed = renameFolder(withChild, 'art', 'Artwork');
    expect(renamed.folders.find((f) => f.id === 'art')?.name).toBe('Artwork');

    const deleted = deleteFolder(renamed, 'art');
    expect(deleted.folders).toEqual([]);
    expect(deleted.items).toEqual([]);
    expect(deleted.hidden).toEqual([]);
    vi.unstubAllGlobals();
  });

  it('adds and removes a nest from a folder without duplicating', () => {
    const forum = {
      serverDid: 'did:a',
      forumId: '2',
      name: 'Garden',
      slug: 'garden',
      parentSlug: null,
      serverName: 'Alpha',
    };
    const added = addForumToFolder(layout(), 'art', forum);
    expect(added.items.filter((i) => i.forumId === '2')).toHaveLength(1);
    const twice = addForumToFolder(added, 'art', forum);
    expect(twice.items.filter((i) => i.forumId === '2')).toHaveLength(1);
    const removed = removeForumFromFolder(twice, 'art', 'did:a', '2');
    expect(removed.items.filter((i) => i.forumId === '2')).toHaveLength(0);
  });

  it('treats folders with a missing parent as roots in the add-to menu', () => {
    expect(
      flattenFolderOptions(
        layout({
          folders: [
            { id: 'art', name: 'Art', parentId: 'gone', position: 0 },
            { id: 'fan', name: 'Fanart', parentId: 'art', position: 0 },
          ],
        }),
      ).map((o) => o.label),
    ).toEqual(['Art', 'Art / Fanart']);
  });

  it('hides a nest from one section without affecting another', () => {
    const forum = {
      key: 'did:a:1',
      serverDid: 'did:a',
      serverName: 'Alpha',
      serverBaseUrl: '',
      forumId: '1',
      name: 'Roses',
      slug: 'roses',
      viewerIsMember: true,
      viewerCanManage: true,
      viewerIsFavorite: false,
    };
    const hidden = hideForumInSection(layout(), 'section:joined', forum);
    expect(visibleForumsInSection([forum], hidden, 'section:joined')).toEqual([]);
    expect(hiddenForumsInSection([forum], hidden, 'section:joined').map((f) => f.slug)).toEqual(['roses']);
    expect(visibleForumsInSection([forum], hidden, 'section:manage')).toEqual([forum]);

    const shown = unhideForumInSection(hidden, 'section:joined', forum.serverDid, forum.forumId);
    expect(visibleForumsInSection([forum], shown, 'section:joined')).toEqual([forum]);

    expect(deleteFolder(hidden, 'art').hidden).toEqual(hidden.hidden);
  });

  it('defaults to section grouping and can hide or reorder peers', () => {
    const start = layout();
    expect(layoutGroupAxes(start)).toEqual(['section']);
    expect(resolvedPeers(start).map((peer) => peer.id)).toEqual([
      'section:favorites',
      'section:recents',
      'section:manage',
      'section:joined',
      'folder:art',
    ]);

    const hiddenRecents = setPeerEnabled(start, 'section:recents', false);
    expect(resolvedPeers(hiddenRecents).find((peer) => peer.id === 'section:recents')?.enabled).toBe(false);

    const moved = movePeer(start, 'section:joined', -1);
    expect(resolvedPeers(moved).map((peer) => peer.id)).toEqual([
      'section:favorites',
      'section:recents',
      'section:joined',
      'section:manage',
      'folder:art',
    ]);

    const noGroups = setLayoutGroupAxes(start, []);
    expect(layoutGroupAxes(noGroups)).toEqual([]);
  });
});

import { type FormEvent, type ReactNode, useEffect, useRef, useState } from 'react';
import { NavLink } from 'react-router';
import { ForumAvatar } from './ForumAvatar';
import { useForumNav } from '../ForumNavContext';
import { useServerRegistry } from '../ServerRegistryContext';
import {
  favoriteForums,
  federationNamesForServer,
  FORUM_NAV_AXES,
  FORUM_NAV_AXIS_LABELS,
  groupForumsByServer,
  joinedForums,
  managedForums,
  originGroupingAxes,
  partitionByFederation,
  partitionByServer,
  recentForums,
  type ForumNavItem,
  type ForumTreeNode,
} from '../forumNav';
import {
  addForumToFolder,
  applyGroupingToAllPeers,
  buildFolderTree,
  createFolder,
  deleteFolder,
  flattenFolderOptions,
  folderItemToNavItem,
  hiddenForumsInSection,
  hideForumInSection,
  layoutGroupAxes,
  moveLayoutAxis,
  movePeer,
  removeForumFromFolder,
  renameFolder,
  resolvedPeers,
  setPeerEnabled,
  setPeerGroupAxes,
  toggleLayoutAxis,
  unhideForumInSection,
  visibleForumsInSection,
  type FolderTreeNode,
  type ResolvedForumNavPeer,
} from '../forumNavFolders';
import { forumPath } from '../serverDid';

function StarButton({
  item,
  favorited,
  onToggle,
}: {
  item: ForumNavItem;
  favorited: boolean;
  onToggle: (item: ForumNavItem) => void;
}) {
  if (!item.viewerIsMember) return null;
  const onClick = (event: React.MouseEvent) => {
    event.preventDefault();
    event.stopPropagation();
    onToggle(item);
  };

  return (
    <button
      type="button"
      className={`forum-browser-star${favorited ? ' forum-browser-star-on' : ''}`}
      aria-label={favorited ? `Remove ${item.name} from favorites` : `Add ${item.name} to favorites`}
      aria-pressed={favorited}
      onClick={onClick}
    >
      {favorited ? '★' : '☆'}
    </button>
  );
}

function Collapsible({
  id,
  title,
  className,
  extra,
  children,
  defaultOpen = true,
}: {
  id: string;
  title: ReactNode;
  className: string;
  extra?: ReactNode;
  children: ReactNode;
  defaultOpen?: boolean;
}) {
  const { isGroupOpen, setGroupOpen } = useForumNav();
  const open = isGroupOpen(id, defaultOpen);

  return (
    <details
      className={className}
      open={open}
      onToggle={(event) => {
        event.stopPropagation();
        const next = event.currentTarget.open;
        if (next !== open) setGroupOpen(id, next);
      }}
    >
      <summary>
        <span className="forum-browser-caret" aria-hidden="true" />
        <span className="forum-browser-summary-label">{title}</span>
        {extra && (
          <span
            className="forum-browser-summary-extra"
            onClick={(event) => {
              event.preventDefault();
              event.stopPropagation();
            }}
            onMouseDown={(event) => event.stopPropagation()}
          >
            {extra}
          </span>
        )}
      </summary>
      {children}
    </details>
  );
}

function OverflowMenu({
  label,
  children,
}: {
  label: string;
  children: ReactNode;
}) {
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const onPointerDown = (event: globalThis.MouseEvent) => {
      if (rootRef.current && !rootRef.current.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', onPointerDown);
    return () => document.removeEventListener('mousedown', onPointerDown);
  }, [open]);

  return (
    <div className="forum-browser-menu" ref={rootRef}>
      <button
        type="button"
        className="forum-browser-menu-trigger"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-label={label}
        onClick={(event) => {
          event.preventDefault();
          event.stopPropagation();
          setOpen((value) => !value);
        }}
      >
        ⋯
      </button>
      {open && (
        <div className="forum-browser-menu-panel" role="menu">
          {children}
        </div>
      )}
    </div>
  );
}

function OriginLabels({ item, axes }: { item: ForumNavItem; axes: Array<'federation' | 'server'> }) {
  const { federationOriginGroups } = useServerRegistry();
  const labels: string[] = [];
  if (!axes.includes('server') && item.serverName) labels.push(item.serverName);
  if (!axes.includes('federation')) {
    labels.push(...federationNamesForServer(item.serverDid, federationOriginGroups));
  }
  if (labels.length === 0) return null;
  const text = labels.join(' · ');
  return (
    <span className="forum-browser-labels" title={text}>
      {text}
    </span>
  );
}

function ForumRow({
  item,
  depth,
  folderId,
  showRemoveFromGroup,
  sectionId,
  hiddenInSection,
  labelAxes,
}: {
  item: ForumNavItem;
  depth: number;
  folderId?: string;
  showRemoveFromGroup?: boolean;
  sectionId?: string;
  hiddenInSection?: boolean;
  labelAxes: Array<'federation' | 'server'>;
}) {
  const { isFavorite, toggleFavorite, layout, saveLayout } = useForumNav();
  const folderOptions = flattenFolderOptions(layout);

  return (
    <div
      className={`forum-browser-link-row${hiddenInSection ? ' forum-browser-link-row-hidden' : ''}`}
      style={{ paddingLeft: `${0.35 + depth * 0.85}rem` }}
    >
      <NavLink
        to={forumPath(item.serverDid, item.slug, item.parentSlug ?? undefined)}
        className={({ isActive }) =>
          isActive ? 'forum-browser-link forum-browser-link-active' : 'forum-browser-link'
        }
      >
        <span className="forum-browser-link-main">
          <ForumAvatar name={item.name} src={item.avatarUrl} size={22} />
          <span className="forum-browser-link-name" title={item.name}>
            {item.name}
          </span>
          {item.approvalStatus === 'pending' && <span className="badge badge-pending">pending</span>}
        </span>
        <OriginLabels item={item} axes={labelAxes} />
      </NavLink>
      <StarButton
        item={item}
        favorited={isFavorite(item.serverDid, item.forumId)}
        onToggle={(current) => {
          void toggleFavorite(current).catch(() => undefined);
        }}
      />
      <OverflowMenu label={`More actions for ${item.name}`}>
        {folderOptions.map((folder) => (
          <button
            key={folder.id}
            type="button"
            role="menuitem"
            onClick={() => {
              void saveLayout(addForumToFolder(layout, folder.id, item)).catch(() => undefined);
            }}
          >
            Add to {folder.label}
          </button>
        ))}
        <button
          type="button"
          role="menuitem"
          onClick={() => {
            const next = createFolder(layout, 'New group', null);
            const created = next.folders.find(
              (folder) => !layout.folders.some((existing) => existing.id === folder.id),
            );
            void saveLayout(created ? addForumToFolder(next, created.id, item) : next).catch(() => undefined);
          }}
        >
          New group
        </button>
        {showRemoveFromGroup && folderId && (
          <button
            type="button"
            role="menuitem"
            onClick={() => {
              void saveLayout(removeForumFromFolder(layout, folderId, item.serverDid, item.forumId)).catch(
                () => undefined,
              );
            }}
          >
            Remove from group
          </button>
        )}
        {sectionId && !hiddenInSection && (
          <button
            type="button"
            role="menuitem"
            onClick={() => {
              void saveLayout(hideForumInSection(layout, sectionId, item)).catch(() => undefined);
            }}
          >
            Hide from this list
          </button>
        )}
        {sectionId && hiddenInSection && (
          <button
            type="button"
            role="menuitem"
            onClick={() => {
              void saveLayout(unhideForumInSection(layout, sectionId, item.serverDid, item.forumId)).catch(
                () => undefined,
              );
            }}
          >
            Show in this list
          </button>
        )}
      </OverflowMenu>
    </div>
  );
}

function ForumTree({
  nodes,
  depth = 0,
  sectionId,
  labelAxes,
  folderId,
}: {
  nodes: ForumTreeNode[];
  depth?: number;
  sectionId?: string;
  labelAxes: Array<'federation' | 'server'>;
  folderId?: string;
}) {
  const { isGroupOpen, setGroupOpen } = useForumNav();

  return (
    <ul className="forum-browser-tree">
      {nodes.map((node) => {
        const collapseId = `${sectionId ?? 'all'}:forum:${node.key}`;
        const open = isGroupOpen(collapseId);
        return (
          <li key={node.key}>
            <div className="forum-browser-tree-row" style={{ paddingLeft: `${depth * 0.85}rem` }}>
              {node.children.length > 0 ? (
                <button
                  type="button"
                  className={`forum-browser-caret-btn${open ? ' is-open' : ''}`}
                  aria-expanded={open}
                  aria-label={open ? `Collapse ${node.name}` : `Expand ${node.name}`}
                  onClick={() => setGroupOpen(collapseId, !open)}
                >
                  <span className="forum-browser-caret" aria-hidden="true" />
                </button>
              ) : (
                <span className="forum-browser-caret-spacer" aria-hidden="true" />
              )}
              <ForumRow
                item={node}
                depth={0}
                sectionId={sectionId}
                labelAxes={labelAxes}
                folderId={folderId}
                showRemoveFromGroup={Boolean(folderId)}
              />
            </div>
            {node.children.length > 0 && open && (
              <ForumTree
                nodes={node.children}
                depth={depth + 1}
                sectionId={sectionId}
                labelAxes={labelAxes}
                folderId={folderId}
              />
            )}
          </li>
        );
      })}
    </ul>
  );
}

function forumTrees(forums: ForumNavItem[]): ForumTreeNode[] {
  return groupForumsByServer(forums).flatMap((group) => group.tree);
}

function AxisTree({
  forums,
  axes,
  path,
  sectionId,
  folderId,
}: {
  forums: ForumNavItem[];
  axes: Array<'federation' | 'server'>;
  path: string;
  sectionId?: string;
  folderId?: string;
}) {
  const { federationOriginGroups, activeServers } = useServerRegistry();
  if (forums.length === 0) return null;
  if (axes.length === 0) {
    return <ForumTree nodes={forumTrees(forums)} sectionId={sectionId} labelAxes={axes} folderId={folderId} />;
  }

  const [head, ...rest] = axes;
  const groups =
    head === 'federation'
      ? partitionByFederation(forums, federationOriginGroups, activeServers)
      : partitionByServer(forums);

  return (
    <>
      {groups.map((group) => (
        <Collapsible
          key={group.id}
          id={`${path}:${head}:${group.id}`}
          className="forum-browser-subgroup"
          title={group.name}
        >
          {rest.length === 0 ? (
            <ForumTree
              nodes={forumTrees(group.forums)}
              sectionId={sectionId}
              labelAxes={axes}
              folderId={folderId}
            />
          ) : (
            <AxisTree
              forums={group.forums}
              axes={rest}
              path={`${path}:${head}:${group.id}`}
              sectionId={sectionId}
              folderId={folderId}
            />
          )}
        </Collapsible>
      ))}
    </>
  );
}

function NameForm({
  initial,
  placeholder,
  onSubmit,
  onCancel,
}: {
  initial?: string;
  placeholder: string;
  onSubmit: (name: string) => void;
  onCancel: () => void;
}) {
  const [value, setValue] = useState(initial ?? '');
  const submit = (event: FormEvent) => {
    event.preventDefault();
    const name = value.trim();
    if (!name) return;
    onSubmit(name);
  };

  return (
    <form className="forum-browser-name-form" onSubmit={submit}>
      <input
        autoFocus
        value={value}
        placeholder={placeholder}
        onChange={(event) => setValue(event.target.value)}
        onKeyDown={(event) => {
          if (event.key === 'Escape') onCancel();
        }}
      />
      <button type="submit" className="btn-ghost">
        Save
      </button>
    </form>
  );
}

function innerAxesForPeer(
  peer: ResolvedForumNavPeer,
  afterSection: Array<'federation' | 'server'>,
): Array<'federation' | 'server'> {
  return peer.groupAxes ?? afterSection;
}

function PeerMenu({ peer }: { peer: ResolvedForumNavPeer }) {
  const { layout, saveLayout } = useForumNav();
  const [renaming, setRenaming] = useState(false);
  const [addingChild, setAddingChild] = useState(false);
  const save = (next: typeof layout) => {
    void saveLayout(next).catch(() => undefined);
  };

  return (
    <>
      <OverflowMenu label={`Options for ${peer.title}`}>
        <button type="button" role="menuitem" onClick={() => save(setPeerGroupAxes(layout, peer.id, null))}>
          Group: same as sidebar{peer.groupAxes == null ? ' ✓' : ''}
        </button>
        <button type="button" role="menuitem" onClick={() => save(setPeerGroupAxes(layout, peer.id, []))}>
          Group: none{peer.groupAxes?.length === 0 ? ' ✓' : ''}
        </button>
        <button type="button" role="menuitem" onClick={() => save(setPeerGroupAxes(layout, peer.id, ['server']))}>
          Group: server{peer.groupAxes?.length === 1 && peer.groupAxes[0] === 'server' ? ' ✓' : ''}
        </button>
        <button type="button" role="menuitem" onClick={() => save(setPeerGroupAxes(layout, peer.id, ['federation']))}>
          Group: federation{peer.groupAxes?.length === 1 && peer.groupAxes[0] === 'federation' ? ' ✓' : ''}
        </button>
        <button
          type="button"
          role="menuitem"
          onClick={() => save(setPeerGroupAxes(layout, peer.id, ['federation', 'server']))}
        >
          Group: federation then server
          {peer.groupAxes?.join(',') === 'federation,server' ? ' ✓' : ''}
        </button>
        <button type="button" role="menuitem" onClick={() => save(movePeer(layout, peer.id, -1))}>
          Move up
        </button>
        <button type="button" role="menuitem" onClick={() => save(movePeer(layout, peer.id, 1))}>
          Move down
        </button>
        <button type="button" role="menuitem" onClick={() => save(setPeerEnabled(layout, peer.id, false))}>
          Hide this group
        </button>
        {peer.kind === 'folder' && peer.folderId && (
          <>
            <button type="button" role="menuitem" onClick={() => setRenaming(true)}>
              Rename
            </button>
            <button type="button" role="menuitem" onClick={() => setAddingChild(true)}>
              Add subgroup
            </button>
            <button type="button" role="menuitem" onClick={() => save(deleteFolder(layout, peer.folderId!))}>
              Delete group
            </button>
          </>
        )}
      </OverflowMenu>
      {renaming && peer.folderId && (
        <NameForm
          initial={peer.title}
          placeholder="Group name"
          onSubmit={(name) => {
            save(renameFolder(layout, peer.folderId!, name));
            setRenaming(false);
          }}
          onCancel={() => setRenaming(false)}
        />
      )}
      {addingChild && peer.folderId && (
        <NameForm
          placeholder="Subgroup name"
          onSubmit={(name) => {
            save(createFolder(layout, name, peer.folderId!));
            setAddingChild(false);
          }}
          onCancel={() => setAddingChild(false)}
        />
      )}
    </>
  );
}

function NestedFolderNodes({
  nodes,
  labelAxes,
}: {
  nodes: FolderTreeNode[];
  labelAxes: Array<'federation' | 'server'>;
}) {
  const { forums } = useForumNav();
  return (
    <>
      {nodes.map((node) => (
        <Collapsible key={node.id} id={`folder:${node.id}`} className="forum-browser-subgroup" title={node.name}>
          <ForumTree
            nodes={forumTrees(node.forums.map((item) => folderItemToNavItem(item, forums)))}
            sectionId={`folder:${node.id}`}
            labelAxes={labelAxes}
            folderId={node.id}
          />
          <NestedFolderNodes nodes={node.children} labelAxes={labelAxes} />
        </Collapsible>
      ))}
    </>
  );
}

function PeerBody({
  peer,
  forums,
  hidden,
  innerAxes,
}: {
  peer: ResolvedForumNavPeer;
  forums: ForumNavItem[];
  hidden: ForumNavItem[];
  innerAxes: Array<'federation' | 'server'>;
}) {
  const { layout } = useForumNav();
  const tree = peer.folderId ? buildFolderTree(layout).find((node) => node.id === peer.folderId) : null;

  return (
    <>
      <AxisTree
        forums={forums}
        axes={innerAxes}
        path={peer.id}
        sectionId={peer.kind === 'builtin' ? peer.id : undefined}
        folderId={peer.folderId}
      />
      {tree && <NestedFolderNodes nodes={tree.children} labelAxes={innerAxes} />}
      {hidden.length > 0 && (
        <Collapsible
          id={`${peer.id}:hidden`}
          className="forum-browser-subgroup"
          title={`Hidden (${hidden.length})`}
          defaultOpen={false}
        >
          {hidden.map((item) => (
            <ForumRow
              key={`hidden:${item.key}`}
              item={item}
              depth={0}
              sectionId={peer.id}
              hiddenInSection
              labelAxes={innerAxes}
            />
          ))}
        </Collapsible>
      )}
    </>
  );
}

function SidebarGroupMenu() {
  const { layout, saveLayout } = useForumNav();
  const axes = layoutGroupAxes(layout);
  const peers = resolvedPeers(layout);
  const save = (next: typeof layout) => {
    void saveLayout(next).catch(() => undefined);
  };

  return (
    <OverflowMenu label="Group forums">
      <p className="forum-browser-menu-heading">Group by</p>
      {FORUM_NAV_AXES.map((axis) => (
        <button
          key={axis}
          type="button"
          role="menuitemcheckbox"
          aria-checked={axes.includes(axis)}
          onClick={() => save(toggleLayoutAxis(layout, axis))}
        >
          {axes.includes(axis) ? '✓ ' : ''}
          {FORUM_NAV_AXIS_LABELS[axis]}
        </button>
      ))}
      {axes.length > 1 && (
        <>
          <p className="forum-browser-menu-heading">Order</p>
          {axes.map((axis) => (
            <div key={`order:${axis}`} className="forum-browser-menu-order">
              <span>{FORUM_NAV_AXIS_LABELS[axis]}</span>
              <button type="button" onClick={() => save(moveLayoutAxis(layout, axis, -1))}>
                Up
              </button>
              <button type="button" onClick={() => save(moveLayoutAxis(layout, axis, 1))}>
                Down
              </button>
            </div>
          ))}
        </>
      )}
      <p className="forum-browser-menu-heading">Groups</p>
      {peers.map((peer) => (
        <button
          key={peer.id}
          type="button"
          role="menuitemcheckbox"
          aria-checked={peer.enabled}
          onClick={() => save(setPeerEnabled(layout, peer.id, !peer.enabled))}
        >
          {peer.enabled ? '✓ ' : ''}
          {peer.title}
        </button>
      ))}
      <button
        type="button"
        role="menuitem"
        onClick={() => save(applyGroupingToAllPeers(layout, originGroupingAxes(axes)))}
      >
        Apply grouping to all groups
      </button>
      <button
        type="button"
        role="menuitem"
        onClick={() => {
          void saveLayout(createFolder(layout, 'New group', null)).catch(() => undefined);
        }}
      >
        New group
      </button>
    </OverflowMenu>
  );
}

type PeerSources = {
  favorites: ForumNavItem[];
  recents: ForumNavItem[];
  manage: ForumNavItem[];
  joined: ForumNavItem[];
};

function forumsForPeer(
  peer: ResolvedForumNavPeer,
  sources: PeerSources,
  layout: ReturnType<typeof useForumNav>['layout'],
  allForums: ForumNavItem[],
): { visible: ForumNavItem[]; hidden: ForumNavItem[] } {
  if (peer.kind === 'folder' && peer.folderId) {
    const node = buildFolderTree(layout).find((item) => item.id === peer.folderId);
    const items = node?.forums.map((item) => folderItemToNavItem(item, allForums)) ?? [];
    return { visible: items, hidden: [] };
  }
  const source =
    peer.id === 'section:favorites'
      ? sources.favorites
      : peer.id === 'section:recents'
        ? sources.recents
        : peer.id === 'section:manage'
          ? sources.manage
          : sources.joined;
  return {
    visible: visibleForumsInSection(source, layout, peer.id),
    hidden: hiddenForumsInSection(source, layout, peer.id),
  };
}

function unionForums(peers: Array<{ visible: ForumNavItem[]; hidden: ForumNavItem[] }>): ForumNavItem[] {
  const seen = new Set<string>();
  const result: ForumNavItem[] = [];
  for (const peer of peers) {
    for (const item of [...peer.visible, ...peer.hidden]) {
      if (seen.has(item.key)) continue;
      seen.add(item.key);
      result.push(item);
    }
  }
  return result;
}

function PeerSections({
  peers,
  sources,
  afterSection,
  outerPath,
  subset,
}: {
  peers: ResolvedForumNavPeer[];
  sources: PeerSources;
  afterSection: Array<'federation' | 'server'>;
  outerPath: string;
  subset?: ForumNavItem[];
}) {
  const { layout, forums } = useForumNav();
  const subsetKeys = subset ? new Set(subset.map((item) => item.key)) : null;

  return (
    <>
      {peers.map((peer) => {
        if (!peer.enabled) return null;
        const lists = forumsForPeer(peer, sources, layout, forums);
        const visible = subsetKeys
          ? lists.visible.filter((item) => subsetKeys.has(item.key))
          : lists.visible;
        const hidden = subsetKeys ? lists.hidden.filter((item) => subsetKeys.has(item.key)) : lists.hidden;
        if (visible.length === 0 && hidden.length === 0) return null;
        const inner = innerAxesForPeer(peer, afterSection);
        return (
          <Collapsible
            key={`${outerPath}:${peer.id}`}
            id={`${outerPath}:${peer.id}`}
            className="forum-browser-section"
            title={peer.title}
            extra={<PeerMenu peer={peer} />}
          >
            <PeerBody peer={peer} forums={visible} hidden={hidden} innerAxes={inner} />
          </Collapsible>
        );
      })}
    </>
  );
}

function OuterAxisThenPeers({
  forums,
  axes,
  path,
  peers,
  sources,
  afterSection,
}: {
  forums: ForumNavItem[];
  axes: Array<'federation' | 'server'>;
  path: string;
  peers: ResolvedForumNavPeer[];
  sources: PeerSources;
  afterSection: Array<'federation' | 'server'>;
}) {
  const { federationOriginGroups, activeServers } = useServerRegistry();
  if (axes.length === 0) {
    return (
      <PeerSections
        peers={peers}
        sources={sources}
        afterSection={afterSection}
        outerPath={path}
        subset={forums}
      />
    );
  }
  const [head, ...rest] = axes;
  const groups =
    head === 'federation'
      ? partitionByFederation(forums, federationOriginGroups, activeServers)
      : partitionByServer(forums);
  return (
    <>
      {groups.map((group) => (
        <Collapsible
          key={group.id}
          id={`${path}:${head}:${group.id}`}
          className="forum-browser-section"
          title={group.name}
        >
          <OuterAxisThenPeers
            forums={group.forums}
            axes={rest}
            path={`${path}:${head}:${group.id}`}
            peers={peers}
            sources={sources}
            afterSection={afterSection}
          />
        </Collapsible>
      ))}
    </>
  );
}

export function ForumBrowser({ onNavigate }: { onNavigate?: () => void }) {
  const { forums, recents, layout, loading } = useForumNav();
  const sources: PeerSources = {
    favorites: favoriteForums(forums),
    recents: recentForums(forums, recents),
    manage: managedForums(forums),
    joined: joinedForums(forums),
  };
  const axes = layoutGroupAxes(layout);
  const peers = resolvedPeers(layout);
  const enabledPeers = peers.filter((peer) => peer.enabled);
  const sectionIndex = axes.indexOf('section');
  const afterSection = originGroupingAxes(sectionIndex === -1 ? axes : axes.slice(sectionIndex + 1));
  const beforeSection = originGroupingAxes(sectionIndex === -1 ? [] : axes.slice(0, sectionIndex));
  const peerLists = enabledPeers.map((peer) => forumsForPeer(peer, sources, layout, forums));
  const subscribedOnly = sources.joined;
  const sectionPool = unionForums(peerLists);
  const empty = subscribedOnly.length === 0 && sources.recents.length === 0 && sources.manage.length === 0;

  return (
    <div
      className="forum-browser-inner"
      onClick={(event) => {
        const target = event.target as HTMLElement;
        if (target.closest('a')) onNavigate?.();
      }}
    >
      <div className="forum-browser-header">
        <div className="forum-browser-header-row">
          <h2 className="forum-browser-title">Forums</h2>
          <SidebarGroupMenu />
        </div>
        <NavLink to="/forums" className="forum-browser-browse">
          Browse all forums
        </NavLink>
      </div>

      {loading && empty && <p className="forum-browser-empty">Loading forums…</p>}

      {!loading && empty && (
        <p className="forum-browser-empty">
          You haven&apos;t joined any forums yet.{' '}
          <NavLink to="/forums">Browse all forums</NavLink>
          {' or '}
          <NavLink to="/servers">add a server</NavLink>.
        </p>
      )}

      {sectionIndex === -1 ? (
        <AxisTree forums={subscribedOnly} axes={originGroupingAxes(axes)} path="rail" />
      ) : beforeSection.length === 0 ? (
        <PeerSections peers={enabledPeers} sources={sources} afterSection={afterSection} outerPath="rail" />
      ) : (
        <OuterAxisThenPeers
          forums={sectionPool}
          axes={beforeSection}
          path="rail"
          peers={enabledPeers}
          sources={sources}
          afterSection={afterSection}
        />
      )}
    </div>
  );
}

import { Link } from 'react-router';
import { useEffect, useRef, useState, type CSSProperties, type ReactNode } from 'react';
import { ExtensionFrame } from './ExtensionFrame';
import { ForumAvatar } from './ForumAvatar';
import { PostBody } from './PostBody';
import { UserByline } from './UserPresentation';
import { isForumPending } from '../createForumAccess';
import {
  clampedColSpan,
  columnsTemplate,
  containerGapPx,
  getChildSize,
  gridColumnCount,
  stackBelowPx,
} from '../forumLayoutEditor';
import {
  subforumPath,
  watercoolerPath,
} from '../serverDid';
import type { CatalogEmote } from '../emotes';
import type {
  Forum,
  ForumPageRegion,
  Post,
  WatercoolerSummary,
} from '../types';
import './ForumLayout.css';

function useContainerCollapsed(stackBelow: number) {
  const ref = useRef<HTMLDivElement>(null);
  const [collapsed, setCollapsed] = useState(false);

  useEffect(() => {
    const node = ref.current;
    if (!node || stackBelow <= 0) {
      setCollapsed(false);
      return;
    }
    const update = () => setCollapsed(node.clientWidth < stackBelow);
    update();
    const observer = new ResizeObserver(update);
    observer.observe(node);
    return () => observer.disconnect();
  }, [stackBelow]);

  return { ref, collapsed };
}

export function regionWatercoolerId(region: ForumPageRegion): string | null {
  const raw = region.settings?.watercoolerId;
  if (typeof raw === 'string' && raw.length > 0) return raw;
  return null;
}

export function findWatercoolerViewId(region: ForumPageRegion | null | undefined): string | null {
  if (!region) return null;
  if (region.typeKey === 'watercooler-view' || region.typeKey === 'cooler-feed') {
    return regionWatercoolerId(region);
  }
  for (const child of region.children ?? []) {
    const found = findWatercoolerViewId(child);
    if (found) return found;
  }
  return null;
}

/** Client fallback when API has not yet returned homePage. */
export function fallbackHomeLayout(forum: Forum): ForumPageRegion {
  const pinned = forum.contentBlocks?.find((b) => b.typeKey === 'cooler-feed' && b.slot === 'main');
  const children: ForumPageRegion[] = [
    { id: 'fallback-coolers', typeKey: 'cooler-list', settings: {} },
  ];
  if (pinned?.watercoolerId) {
    children.push({
      id: 'fallback-feed',
      typeKey: 'watercooler-view',
      settings: { watercoolerId: pinned.watercoolerId },
    });
  }
  children.push(
    { id: 'fallback-subs', typeKey: 'subforum-grid', settings: {} },
    { id: 'fallback-threads', typeKey: 'forum-view', settings: {} },
  );
  return { id: 'fallback-root', typeKey: 'stack', settings: {}, children };
}

/** Client fallback for locked shared sections (chrome). */
export function fallbackChromeLayout(): ForumPageRegion {
  return {
    id: 'fallback-chrome',
    typeKey: 'stack',
    settings: { gap: 16 },
    children: [
      { id: 'fallback-chrome-header', typeKey: 'forum-header', settings: {} },
      { id: 'fallback-chrome-body', typeKey: 'page-body', settings: {} },
    ],
  };
}

export function ForumLayoutRenderer({
  region,
  forum,
  serverDid,
  parentSlug,
  userLoggedIn,
  ageBlocked,
  canCreateThread,
  membershipBusy,
  isFavorite,
  onToggleFavorite,
  onMembershipClick,
  onCreatePost,
  emotes,
  feedPosts,
  feedComposer,
  sortBar,
  threadList,
  settingsHref,
  emotesSettingsHref,
  youSettingsHref,
  contentBaseUrl,
}: {
  region: ForumPageRegion;
  forum: Forum;
  serverDid: string;
  parentSlug?: string;
  userLoggedIn: boolean;
  ageBlocked: boolean;
  canCreateThread: boolean;
  membershipBusy: boolean;
  isFavorite: boolean;
  onToggleFavorite: () => void;
  onMembershipClick: () => void;
  onCreatePost: () => void;
  emotes: CatalogEmote[];
  feedPosts: Post[];
  feedComposer: ReactNode;
  sortBar: ReactNode;
  threadList: ReactNode;
  settingsHref?: string | null;
  emotesSettingsHref?: string | null;
  youSettingsHref?: string | null;
  contentBaseUrl?: string | null;
}) {
  const type = region.typeKey;
  const renderProps: LayoutRenderProps = {
    region,
    forum,
    serverDid,
    parentSlug,
    userLoggedIn,
    ageBlocked,
    canCreateThread,
    membershipBusy,
    isFavorite,
    onToggleFavorite,
    onMembershipClick,
    onCreatePost,
    emotes,
    feedPosts,
    feedComposer,
    sortBar,
    threadList,
    settingsHref,
    emotesSettingsHref,
    youSettingsHref,
    contentBaseUrl,
  };

  if (type === 'stack') {
    const gap = containerGapPx(region);
    return (
      <div className="forum-layout-stack" style={{ gap: `${gap}px` }}>
        {(region.children ?? []).map((child) => {
          const size = getChildSize(child);
          const style: CSSProperties =
            size.mode === 'px' ? { minHeight: `${size.size}px` } : {};
          return (
            <div key={child.id} className="forum-layout-stack-child" style={style}>
              <ForumLayoutRenderer {...renderProps} region={child} />
            </div>
          );
        })}
      </div>
    );
  }

  if (type === 'columns') {
    return <ColumnsLayout {...renderProps} />;
  }

  if (type === 'grid') {
    return <GridLayout {...renderProps} />;
  }

  if (type === 'forum-header') {
    const showBanner = region.settings?.showBanner !== false;
    const showDescription = region.settings?.showDescription !== false;
    const canJoin =
      userLoggedIn && !isForumPending(forum) && (!ageBlocked || forum.viewerIsMember) && !forum.viewerIsMember;
    const canLeave = userLoggedIn && Boolean(forum.viewerIsMember);
    return (
      <div className={`forum-hero${forum.bannerUrl && showBanner ? ' forum-hero-has-banner' : ''}`}>
        {forum.bannerUrl && showBanner && (
          <div className="forum-banner">
            <img src={forum.bannerUrl} alt="" />
          </div>
        )}
        <div className="forum-hero-body">
          <div className="forum-hero-head">
            <ForumAvatar name={forum.name} src={forum.avatarUrl} size={56} />
            <div className="forum-hero-copy">
              <div className="forum-hero-title-row">
                <h1 className="page-title">
                  {forum.name}
                  {isForumPending(forum) && <span className="badge badge-pending">pending</span>}
                </h1>
                {canLeave && (
                  <button
                    type="button"
                    className={`forum-header-favorite${isFavorite ? ' forum-browser-star-on' : ''}`}
                    aria-pressed={isFavorite}
                    aria-label={isFavorite ? 'Remove from favorites' : 'Add to favorites'}
                    title={isFavorite ? 'Favorited' : 'Favorite'}
                    onClick={onToggleFavorite}
                  >
                    {isFavorite ? '★' : '☆'}
                  </button>
                )}
                <ForumHeaderMenu
                  settingsHref={settingsHref}
                  emotesSettingsHref={emotesSettingsHref}
                  youSettingsHref={youSettingsHref}
                  canLeave={canLeave}
                  leaveBusy={membershipBusy}
                  onLeave={onMembershipClick}
                />
              </div>
              {showDescription && <p className="page-subtitle">{forum.description}</p>}
            </div>
          </div>
          {canJoin && (
            <div className="page-actions">
              <button
                type="button"
                className="btn-primary"
                disabled={membershipBusy}
                onClick={onMembershipClick}
              >
                Join
              </button>
            </div>
          )}
        </div>
      </div>
    );
  }

  if (type === 'cooler-list') {
    const coolers = forum.watercoolers ?? [];
    if (coolers.length === 0) return null;
    return (
      <section className="card" style={{ marginBottom: '1rem' }}>
        <h2 className="card-title">Watercoolers</h2>
        <div className="subforum-grid">
          {coolers.map((cooler) => (
            <CoolerCard
              key={cooler.id}
              cooler={cooler}
              serverDid={serverDid}
              forumSlug={forum.slug}
              parentSlug={parentSlug ?? forum.parentSlug ?? undefined}
            />
          ))}
        </div>
      </section>
    );
  }

  if (type === 'watercooler-view' || type === 'cooler-feed') {
    const coolerId = regionWatercoolerId(region);
    const cooler = forum.watercoolers?.find((c) => c.id === coolerId);
    if (!cooler || !cooler.viewerCanView) return null;
    return (
      <section className="card" style={{ marginBottom: '1rem' }}>
        <h2 className="card-title">{cooler.name}</h2>
        <p className="card-meta">
          Pinned watercooler ·{' '}
          <Link to={watercoolerPath(serverDid, cooler.slug, forum.slug, parentSlug ?? forum.parentSlug ?? undefined)}>
            Open room
          </Link>
        </p>
        {cooler.newestAtBottom === false && feedComposer}
        {feedPosts.slice(-8).map((post) => (
          <div key={post.id} style={{ marginTop: '0.75rem' }}>
            <UserByline presentation={post.author} timestamp={post.createdAt} showStatus={false} />
            <PostBody message={post.message} emotes={emotes} home={{ forumId: forum.id, serverDid }} />
          </div>
        ))}
        {cooler.newestAtBottom !== false && feedComposer}
      </section>
    );
  }

  if (type === 'subforum-grid') {
    const subs = forum.subforums ?? [];
    if (subs.length === 0) return null;
    return (
      <section className="card" style={{ marginBottom: '1rem' }}>
        <h2 className="card-title">Subnests</h2>
        <div className="subforum-grid">
          {subs.map((sub) => (
            <Link
              key={sub.id}
              to={subforumPath(serverDid, forum.slug, sub.slug)}
              className="card card-link subforum-card"
              style={{ padding: '0.75rem 1rem' }}
            >
              <ForumAvatar name={sub.name} src={sub.avatarUrl} size={28} />
              <div>
                <strong>{sub.name}</strong>
                <div className="card-meta">{sub.postCount} threads</div>
              </div>
            </Link>
          ))}
        </div>
      </section>
    );
  }

  if (type === 'page-body') {
    return null;
  }

  if (type === 'extension') {
    return <ExtensionFrame settings={region.settings ?? undefined} contentBaseUrl={contentBaseUrl} />;
  }

  if (type === 'forum-view') {
    return (
      <div style={{ marginBottom: '1rem' }}>
        <div className="forum-view-toolbar">
          {sortBar}
          <div className="forum-view-toolbar-actions">
            {canCreateThread && (
              <button type="button" className="btn-primary" onClick={onCreatePost}>
                Create a post
              </button>
            )}
            {userLoggedIn && !canCreateThread && !isForumPending(forum) && (
              <div className="card card-meta" style={{ margin: 0 }}>
                You do not have permission to create threads here.
              </div>
            )}
            {!userLoggedIn && (
              <div className="card" style={{ margin: 0 }}>
                <Link to="/login">Log in</Link> to create a post.
              </div>
            )}
          </div>
        </div>
        {threadList}
      </div>
    );
  }

  return (
    <div className="card card-meta" style={{ marginBottom: '1rem' }}>
      Unknown layout view: {type}
    </div>
  );
}

type LayoutRenderProps = {
  region: ForumPageRegion;
  forum: Forum;
  serverDid: string;
  parentSlug?: string;
  userLoggedIn: boolean;
  ageBlocked: boolean;
  canCreateThread: boolean;
  membershipBusy: boolean;
  isFavorite: boolean;
  onToggleFavorite: () => void;
  onMembershipClick: () => void;
  onCreatePost: () => void;
  emotes: CatalogEmote[];
  feedPosts: Post[];
  feedComposer: ReactNode;
  sortBar: ReactNode;
  threadList: ReactNode;
  settingsHref?: string | null;
  emotesSettingsHref?: string | null;
  youSettingsHref?: string | null;
  contentBaseUrl?: string | null;
};

function ForumHeaderMenu({
  settingsHref,
  emotesSettingsHref,
  youSettingsHref,
  canLeave,
  leaveBusy,
  onLeave,
}: {
  settingsHref?: string | null;
  emotesSettingsHref?: string | null;
  youSettingsHref?: string | null;
  canLeave?: boolean;
  leaveBusy?: boolean;
  onLeave?: () => void;
}) {
  const [open, setOpen] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);
  const linkItems = [
    settingsHref ? { href: settingsHref, label: 'Settings' } : null,
    emotesSettingsHref ? { href: emotesSettingsHref, label: 'Emotes' } : null,
    youSettingsHref ? { href: youSettingsHref, label: 'Your flair' } : null,
  ].filter((item): item is { href: string; label: string } => item != null);
  const hasLeave = Boolean(canLeave && onLeave);
  const hasItems = linkItems.length > 0 || hasLeave;

  useEffect(() => {
    if (!open) return;
    const onPointerDown = (event: PointerEvent) => {
      if (!menuRef.current?.contains(event.target as Node)) setOpen(false);
    };
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setOpen(false);
    };
    document.addEventListener('pointerdown', onPointerDown);
    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.removeEventListener('pointerdown', onPointerDown);
      document.removeEventListener('keydown', onKeyDown);
    };
  }, [open]);

  if (!hasItems) return null;

  return (
    <div className="forum-header-menu" ref={menuRef}>
      <button
        type="button"
        className="forum-header-menu-trigger"
        aria-expanded={open}
        aria-haspopup="menu"
        aria-label="Nest menu"
        onClick={() => setOpen((value) => !value)}
      >
        ⋯
      </button>
      {open && (
        <div className="forum-header-menu-panel" role="menu">
          {linkItems.map((item) => (
            <Link key={item.href} to={item.href} role="menuitem" onClick={() => setOpen(false)}>
              {item.label}
            </Link>
          ))}
          {hasLeave && (
            <button
              type="button"
              role="menuitem"
              disabled={leaveBusy}
              onClick={() => {
                setOpen(false);
                onLeave?.();
              }}
            >
              {leaveBusy ? 'Leaving…' : 'Leave'}
            </button>
          )}
        </div>
      )}
    </div>
  );
}

function GridLayout(props: LayoutRenderProps) {
  const { region } = props;
  const kids = region.children ?? [];
  const gap = containerGapPx(region);
  const stackBelow = stackBelowPx(region);
  const columns = gridColumnCount(region);
  const { ref, collapsed } = useContainerCollapsed(stackBelow);
  const cols = collapsed ? 1 : columns;

  return (
    <div
      ref={ref}
      className="forum-layout-grid"
      data-collapsed={collapsed ? '1' : '0'}
      style={{
        gap: `${gap}px`,
        gridTemplateColumns: `repeat(${cols}, minmax(0, 1fr))`,
      }}
    >
      {kids.map((child) => (
        <div
          key={child.id}
          className="forum-layout-grid-child"
          style={{ gridColumn: `span ${clampedColSpan(child, cols)}` }}
        >
          <ForumLayoutRenderer {...props} region={child} />
        </div>
      ))}
    </div>
  );
}

function ColumnsLayout(props: LayoutRenderProps) {
  const { region } = props;
  const kids = region.children ?? [];
  const gap = containerGapPx(region);
  const stackBelow = stackBelowPx(region);
  const template = columnsTemplate(kids);
  const { ref, collapsed } = useContainerCollapsed(stackBelow);

  return (
    <div
      ref={ref}
      className="forum-layout-columns"
      data-collapsed={collapsed ? '1' : '0'}
      style={
        {
          gap: `${gap}px`,
          '--forum-cols-template': template,
        } as CSSProperties
      }
    >
      {kids.map((child) => (
        <div key={child.id} className="forum-layout-columns-child">
          <ForumLayoutRenderer {...props} region={child} />
        </div>
      ))}
    </div>
  );
}

function CoolerCard({
  cooler,
  serverDid,
  forumSlug,
  parentSlug,
}: {
  cooler: WatercoolerSummary;
  serverDid: string;
  forumSlug: string;
  parentSlug?: string;
}) {
  return (
    <Link
      to={watercoolerPath(serverDid, cooler.slug, forumSlug, parentSlug)}
      className="card card-link"
      style={{ padding: '0.75rem 1rem' }}
    >
      <strong>{cooler.name}</strong>
      <div className="card-meta">
        {cooler.allowUpvotes ? 'Plaza' : 'Channel'} · {cooler.postCount} posts
        {!cooler.viewerCanView ? ' · locked' : ''}
      </div>
    </Link>
  );
}

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { createContentClient, hasPermission, NestRedirectError, rememberContentServer } from '../api';
import { CreatePostDialog } from '../components/CreatePostDialog';
import { PlaceAlertControl } from '../components/PlaceAlertControl';
import {
  fallbackChromeLayout,
  fallbackHomeLayout,
  findWatercoolerViewId,
  ForumLayoutRenderer,
} from '../components/ForumLayoutRenderer';
import { composeLayout } from '../forumLayoutEditor';
import { EmoteAutocomplete } from '../components/EmoteAutocomplete';
import { NsfwAgeCheckPanel, forumAgeStatus } from '../components/NsfwAgeCheckPanel';
import { PostBody } from '../components/PostBody';
import { ReactionBar } from '../components/ReactionBar';
import { UpvoteButton } from '../components/UpvoteButton';
import { UserByline } from '../components/UserPresentation';
import { useAuth } from '../AuthContext';
import { useForumNav } from '../ForumNavContext';
import { useServerRegistry } from '../ServerRegistryContext';
import { postOwnerBaseUrl } from '../postOwner';
import type { PostSort } from '../ranking';
import {
  decodeServerDid,
  forumPath,
  forumSettingsPath,
  resolveServerBaseUrl,
  threadPath,
} from '../serverDid';
import { isForumPending } from '../createForumAccess';
import { loadContextEmotes } from '../emoteCatalog';
import type { Forum, Post } from '../types';
import type { CatalogEmote } from '../emotes';
import { usePostRealtime } from '../usePostRealtime';

const FORUM_THREAD_PAGE_SIZE = 20;

function snippet(text: string, max: number) {
  if (text.length <= max) return text;
  return `${text.slice(0, max)}…`;
}

function ThreadPreview({
  post,
  serverDid,
  baseUrl,
  emotes,
  home,
  manageHref,
}: {
  post: Post;
  serverDid: string;
  baseUrl: string;
  emotes: CatalogEmote[];
  home?: { forumId?: string | null; serverDid?: string | null };
  manageHref?: string;
}) {
  const title = post.title?.trim() || snippet(post.message, 80);
  const preview = post.topReplyPreview;
  const shortReply = preview ? snippet(preview.message, 80) : null;
  const longReply = preview ? snippet(preview.message, 280) : null;

  return (
    <div className="card thread-card">
      <div className="thread-card-main">
        <UpvoteButton
          postId={post.id}
          baseUrl={postOwnerBaseUrl(post, baseUrl)}
          upvoteCount={post.upvoteCount}
          viewerHasUpvoted={post.viewerHasUpvoted}
          author={post.author}
          canUpvote={post.viewerActions?.canUpvote}
          compact
        />
        <div className="thread-card-body">
          <Link to={threadPath(serverDid, post.id)} className="card-link">
            <UserByline presentation={post.author} timestamp={post.createdAt} showStatus={false} />
            {(post.isPinned || post.isLocked || post.replyPolicy === 'following') && (
              <div className="thread-badges">
                {post.isPinned && <span className="thread-badge thread-badge-pinned">Pinned</span>}
                {post.isLocked && <span className="thread-badge thread-badge-locked">Locked</span>}
                {post.replyPolicy === 'following' && (
                  <span className="thread-badge thread-badge-following">Following only</span>
                )}
              </div>
            )}
            <h3 className="thread-card-title">{title}</h3>
            <PostBody message={snippet(post.message, 200)} emotes={emotes} home={home} />
          </Link>
          <div className="card-meta">{post.replyCount} replies</div>
          <ReactionBar
            postId={post.id}
            baseUrl={postOwnerBaseUrl(post, baseUrl)}
            reactions={post.reactions ?? []}
            emotes={emotes}
            forumId={home?.forumId}
            disabled={post.isFederated}
            manageHref={manageHref}
          />
          {preview && shortReply && (
            <div className="thread-reply-preview" tabIndex={0}>
              <span className="thread-reply-preview-label">Top reply · {preview.author.displayName}</span>
              <p className="thread-reply-preview-short">{shortReply}</p>
              {longReply && longReply !== shortReply && (
                <p className="thread-reply-preview-long" role="tooltip">
                  {longReply}
                </p>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

export function ForumPage() {
  const { serverDid: encodedDid, slug, parentSlug } = useParams<{
    serverDid: string;
    slug: string;
    parentSlug?: string;
  }>();
  const { user } = useAuth();
  const { resolveBaseUrl, activeServers } = useServerRegistry();
  const { recordVisit, refresh, isFavorite, toggleFavorite } = useForumNav();
  const navigate = useNavigate();
  const [forum, setForum] = useState<Forum | null>(null);
  const [threads, setThreads] = useState<Post[]>([]);
  const [sort, setSort] = useState<PostSort>('hot');
  const [page, setPage] = useState(1);
  const [hasMore, setHasMore] = useState(false);
  const [loadingThreads, setLoadingThreads] = useState(false);
  const [dialogOpen, setDialogOpen] = useState(false);
  const [creating, setCreating] = useState(false);
  const [membershipBusy, setMembershipBusy] = useState(false);
  const [error, setError] = useState('');
  const [emotes, setEmotes] = useState<CatalogEmote[]>([]);
  const [feedMessage, setFeedMessage] = useState('');
  const [feedPosts, setFeedPosts] = useState<Post[]>([]);
  const requestIdRef = useRef(0);
  const inFlightRef = useRef(false);
  const sentinelRef = useRef<HTMLDivElement>(null);

  const serverDid = encodedDid ? decodeServerDid(encodedDid) : '';
  const baseUrl = resolveServerBaseUrl(serverDid, resolveBaseUrl);
  const client = useMemo(() => (baseUrl ? createContentClient(baseUrl) : null), [baseUrl]);
  const layout = useMemo(() => {
    if (!forum) return null;
    const page = forum.homePage?.layout ?? fallbackHomeLayout(forum);
    const chrome = forum.sharedChrome?.layout ?? fallbackChromeLayout();
    return composeLayout(chrome, page);
  }, [forum]);
  const pinnedCoolerId = findWatercoolerViewId(layout);
  const pinnedCooler = forum?.watercoolers?.find((c) => c.id === pinnedCoolerId);

  useEffect(() => {
    if (!client || !pinnedCooler?.viewerCanView || forum?.nsfwAgeCheckBlocksEntry) {
      setFeedPosts([]);
      return;
    }
    void client
      .getWatercoolerStream(pinnedCooler.id, pinnedCooler.allowUpvotes ? 'hot' : 'new', 1, 15)
      .then(setFeedPosts)
      .catch(() => setFeedPosts([]));
  }, [client, pinnedCooler?.id, pinnedCooler?.viewerCanView, pinnedCooler?.allowUpvotes, forum?.nsfwAgeCheckBlocksEntry]);
  const serverName = activeServers.find((server) => server.serverDid === serverDid)?.name ?? serverDid;

  const ageBlocked = Boolean(forum?.nsfwAgeCheckBlocksEntry);
  const canCreateThread =
    hasPermission(forum?.viewerPermissions, 'CreateThread') && !isForumPending(forum) && !ageBlocked;
  const canAccessSettings =
    hasPermission(forum?.viewerPermissions, 'ManageForum') ||
    hasPermission(forum?.viewerPermissions, 'ManageRoles') ||
    hasPermission(forum?.viewerPermissions, 'ManageMembers') ||
    hasPermission(forum?.viewerPermissions, 'ManagePosts') ||
    hasPermission(forum?.viewerPermissions, 'ManageContentBlocks') ||
    hasPermission(forum?.viewerPermissions, 'ViewHistory');
  const canManageEmotes = hasPermission(forum?.viewerPermissions, 'ManageForum');
  const settingsBaseHref =
    forum && serverDid
      ? forumSettingsPath(serverDid, forum.slug, parentSlug ?? forum.parentSlug ?? undefined)
      : null;
  const emoteSettingsHref =
    canManageEmotes && settingsBaseHref ? `${settingsBaseHref}#emotes` : undefined;
  const youSettingsHref =
    user && forum?.viewerIsMember && settingsBaseHref ? `${settingsBaseHref}#you` : null;

  const loadThreads = useCallback(
    async (forumId: string, pageNum: number, requestId: number) => {
      if (!client) return;
      const batch = await client.getForumThreads(forumId, sort, pageNum, FORUM_THREAD_PAGE_SIZE);
      if (requestIdRef.current !== requestId) return;
      setThreads((prev) => (pageNum === 1 ? batch : [...prev, ...batch]));
      setPage(pageNum);
      setHasMore(batch.length >= FORUM_THREAD_PAGE_SIZE);
    },
    [client, sort],
  );

  const reloadForum = useCallback(
    (resetThreads = true) => {
      if (!slug || !client) return;
      const requestId = ++requestIdRef.current;
      if (resetThreads) {
        setThreads([]);
        setPage(1);
        setHasMore(false);
      }
      setLoadingThreads(true);
      inFlightRef.current = true;
      void (async () => {
        try {
          const f = await client.getForum(slug, parentSlug);
          if (requestIdRef.current !== requestId) return;
          setForum(f);
          if (f.nsfwAgeCheckBlocksEntry) {
            setThreads([]);
            setHasMore(false);
          } else {
            await loadThreads(f.id, 1, requestId);
          }
        } catch (err) {
          if (err instanceof NestRedirectError) {
            if (baseUrl) rememberContentServer(baseUrl, err.redirect.serverId);
            navigate(
              forumPath(err.redirect.serverDid, err.redirect.slug, err.redirect.parentSlug ?? undefined),
              { replace: true },
            );
            return;
          }
          if (requestIdRef.current === requestId) {
            setError(err instanceof Error ? err.message : 'Failed to load nest');
          }
        } finally {
          if (requestIdRef.current === requestId) {
            inFlightRef.current = false;
            setLoadingThreads(false);
          }
        }
      })();
    },
    [slug, parentSlug, client, loadThreads, baseUrl, navigate],
  );

  useEffect(() => {
    setError('');
    reloadForum(true);
  }, [reloadForum]);

  useEffect(() => {
    if (!client || !forum?.id) return;
    void loadContextEmotes({
      homeClient: client,
      forumId: forum.id,
      destPermissions: forum.viewerPermissions,
      homeServerDid: serverDid,
      activeServers,
    })
      .then(setEmotes)
      .catch(() => setEmotes([]));
  }, [activeServers, client, forum?.id, forum?.viewerPermissions, serverDid]);

  useEffect(() => {
    if (!user || !forum || !serverDid) return;
    recordVisit({
      serverDid,
      forumId: forum.id,
      name: forum.name,
      slug: forum.slug,
      parentSlug: forum.parentSlug ?? parentSlug,
      serverName,
      serverBaseUrl: baseUrl ?? '',
    });
  }, [user, forum, serverDid, parentSlug, serverName, baseUrl, recordVisit]);

  const requestMore = useCallback(() => {
    if (!forum || !client || inFlightRef.current || loadingThreads || !hasMore) return;
    const requestId = requestIdRef.current;
    const nextPage = page + 1;
    inFlightRef.current = true;
    setLoadingThreads(true);
    void (async () => {
      try {
        await loadThreads(forum.id, nextPage, requestId);
      } catch (err) {
        if (requestIdRef.current === requestId) {
          setError(err instanceof Error ? err.message : 'Failed to load more threads');
        }
      } finally {
        if (requestIdRef.current === requestId) {
          inFlightRef.current = false;
          setLoadingThreads(false);
        }
      }
    })();
  }, [forum, client, loadingThreads, hasMore, page, loadThreads]);

  useEffect(() => {
    const node = sentinelRef.current;
    if (!node || !hasMore || loadingThreads) return;
    const observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((entry) => entry.isIntersecting)) requestMore();
      },
      { rootMargin: '240px' },
    );
    observer.observe(node);
    return () => observer.disconnect();
  }, [hasMore, loadingThreads, requestMore, threads.length]);

  usePostRealtime({
    baseUrl,
    forumId: forum?.id,
    watercoolerId: pinnedCooler?.id,
    onEvent: (evt) => {
      if (pinnedCooler && evt.watercoolerId === pinnedCooler.id && client) {
        void client
          .getWatercoolerStream(pinnedCooler.id, pinnedCooler.allowUpvotes ? 'hot' : 'new', 1, 15)
          .then(setFeedPosts)
          .catch(() => undefined);
      }
      if (
        evt.type === 'post.created' ||
        evt.type === 'post.deleted' ||
        evt.type === 'post.hardDeleted' ||
        evt.type === 'thread.locked' ||
        evt.type === 'thread.pinned' ||
        evt.type === 'post.upvoted'
      ) {
        reloadForum(true);
      }
    },
  });

  if (!baseUrl) return <div className="error-banner">Unknown content server.</div>;
  if (!forum && !error) return <p>Loading nest...</p>;

  const feedComposer =
    user && client && pinnedCooler?.viewerCanSpeak ? (
      <form
        style={{ marginTop: pinnedCooler.newestAtBottom === false ? '0.75rem' : '1rem' }}
        onSubmit={(e) => {
          e.preventDefault();
          if (!feedMessage.trim()) return;
          void client
            .createPost(undefined, feedMessage.trim(), undefined, undefined, undefined, pinnedCooler.id)
            .then((created) => {
              setFeedMessage('');
              setFeedPosts((prev) =>
                pinnedCooler.newestAtBottom === false ? [created, ...prev] : [...prev, created],
              );
            })
            .catch((err) => setError(err instanceof Error ? err.message : 'Failed to post'));
        }}
      >
        <div className="form-group">
          <EmoteAutocomplete
            emotes={emotes}
            home={{ forumId: forum?.id, serverDid }}
            value={feedMessage}
            onChange={setFeedMessage}
          >
            {(bind) => (
              <textarea
                ref={bind.ref}
                value={feedMessage}
                onChange={bind.onChange}
                onKeyDown={bind.onKeyDown}
                onSelect={bind.onSelect}
                onClick={bind.onClick}
                onBlur={bind.onBlur}
                placeholder={`Message ${pinnedCooler.name}… Type : for emotes`}
                required
              />
            )}
          </EmoteAutocomplete>
        </div>
        <button type="submit" className="btn-primary">
          Send
        </button>
      </form>
    ) : null;

  const sortBar = (
    <div className="sort-bar">
      <span className="sort-bar-label">Sort</span>
      <button
        type="button"
        className={`filter-chip${sort === 'hot' ? ' filter-chip-active' : ''}`}
        onClick={() => setSort('hot')}
      >
        Hot
      </button>
      <button
        type="button"
        className={`filter-chip${sort === 'new' ? ' filter-chip-active' : ''}`}
        onClick={() => setSort('new')}
      >
        New
      </button>
    </div>
  );

  const threadList = (
    <div style={{ marginTop: '1rem' }}>
      {threads.map((thread) => (
        <ThreadPreview
          key={thread.id}
          post={thread}
          serverDid={serverDid}
          baseUrl={baseUrl}
          emotes={emotes}
          home={{ forumId: forum?.id, serverDid }}
          manageHref={emoteSettingsHref}
        />
      ))}
      {threads.length > 0 && (
        <div ref={sentinelRef} className="feed-more">
          {hasMore ? (
            <button type="button" className="btn-ghost" disabled={loadingThreads} onClick={requestMore}>
              {loadingThreads ? 'Loading…' : 'Load more'}
            </button>
          ) : (
            !loadingThreads && <p className="card-meta">You're caught up.</p>
          )}
        </div>
      )}
      {threads.length === 0 && forum && !error && !loadingThreads && (
        <div className="card">
          <p>No threads yet.</p>
        </div>
      )}
    </div>
  );

  return (
    <div>
      <div className="forum-page-chrome" style={{ display: 'flex', flexWrap: 'wrap', gap: '0.75rem', alignItems: 'center', marginBottom: '0.75rem' }}>
        {forum?.parentSlug ? (
          <Link to={forumPath(serverDid, forum.parentSlug)} style={{ color: 'var(--muted)' }}>
            ← {forum.topName || 'Top nest'}
          </Link>
        ) : (
          <Link to="/" style={{ color: 'var(--muted)' }}>
            ← Home
          </Link>
        )}
        {user && client && forum && <PlaceAlertControl client={client} place={{ kind: 'nest', id: forum.id }} />}
      </div>

      {forum && client && (
        <NsfwAgeCheckPanel
          client={client}
          forumId={forum.id}
          status={forumAgeStatus(forum)}
          loggedIn={Boolean(user)}
          onCompleted={() => reloadForum(true)}
        />
      )}

      {error && <div className="error-banner">{error}</div>}
      {forum?.locked && <div className="card">This nest is locked. Members cannot see it until a server super user unlocks it.</div>}
      {forum?.hidden && <div className="card">This nest is hidden. Restore it from the nest map, or purge it.</div>}

      {!ageBlocked && forum && layout && (
        <>
          {isForumPending(forum) && (
            <div className="card" style={{ marginBottom: '1rem' }}>
              <p>
                This nest is waiting for approval. It is hidden from normal browsing and cannot be posted in until
                someone with CreateForum approves it.
              </p>
              {forum.viewerCanApprove && client && (
                <div className="server-row-actions" style={{ marginTop: '0.75rem' }}>
                  <button
                    type="button"
                    className="btn-primary"
                    onClick={async () => {
                      try {
                        const updated = await client.approveForum(forum.slug, parentSlug);
                        setForum(updated);
                      } catch (err) {
                        setError(err instanceof Error ? err.message : 'Approve failed');
                      }
                    }}
                  >
                    Approve
                  </button>
                  <button
                    type="button"
                    className="btn-ghost"
                    onClick={async () => {
                      try {
                        await client.rejectForum(forum.slug, parentSlug);
                        navigate('/');
                      } catch (err) {
                        setError(err instanceof Error ? err.message : 'Reject failed');
                      }
                    }}
                  >
                    Reject
                  </button>
                </div>
              )}
            </div>
          )}

          <ForumLayoutRenderer
            region={layout}
            forum={forum}
            serverDid={serverDid}
            parentSlug={parentSlug}
            userLoggedIn={Boolean(user)}
            ageBlocked={ageBlocked}
            canCreateThread={canCreateThread}
            membershipBusy={membershipBusy}
            isFavorite={isFavorite(serverDid, forum.id)}
            settingsHref={canAccessSettings ? settingsBaseHref : null}
            emotesSettingsHref={emoteSettingsHref}
            youSettingsHref={youSettingsHref}
            contentBaseUrl={baseUrl}
            onToggleFavorite={() =>
              void toggleFavorite({
                serverDid,
                forumId: forum.id,
                name: forum.name,
                slug: forum.slug,
                parentSlug: forum.parentSlug ?? parentSlug,
                serverName,
                serverBaseUrl: baseUrl ?? '',
                viewerIsMember: Boolean(forum.viewerIsMember),
              }).catch(() => undefined)
            }
            onMembershipClick={() => {
              if (!client) return;
              setMembershipBusy(true);
              setError('');
              void (forum.viewerIsMember
                ? client.leaveForum(forum.slug, parentSlug ?? forum.parentSlug ?? undefined)
                : client.joinForum(forum.slug, parentSlug ?? forum.parentSlug ?? undefined)
              )
                .then(async (updated) => {
                  setForum(updated);
                  await refresh();
                })
                .catch((err) => setError(err instanceof Error ? err.message : 'Membership update failed'))
                .finally(() => setMembershipBusy(false));
            }}
            onCreatePost={() => setDialogOpen(true)}
            emotes={emotes}
            feedPosts={feedPosts}
            feedComposer={feedComposer}
            sortBar={sortBar}
            threadList={threadList}
          />

          {client && (
            <CreatePostDialog
              open={dialogOpen}
              forumName={forum.name}
              busy={creating}
              onClose={() => {
                if (!creating) setDialogOpen(false);
              }}
              uploadMedia={client.uploadMedia}
              emotes={emotes}
              home={{ forumId: forum.id, serverDid }}
              manageHref={emoteSettingsHref}
              onSubmit={async ({ title, message, mediaAssetIds, nsfwMedia, replyPolicy }) => {
                setCreating(true);
                setError('');
                try {
                  const post = await client.createPost(
                    forum.id,
                    message,
                    undefined,
                    title,
                    mediaAssetIds,
                    undefined,
                    undefined,
                    nsfwMedia,
                    replyPolicy,
                  );
                  setDialogOpen(false);
                  reloadForum(true);
                  navigate(threadPath(serverDid, post.id));
                  return post;
                } catch (err) {
                  const msg = err instanceof Error ? err.message : 'Failed to create thread';
                  setError(msg);
                  throw err;
                } finally {
                  setCreating(false);
                }
              }}
            />
          )}
        </>
      )}
    </div>
  );
}

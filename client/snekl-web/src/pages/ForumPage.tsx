import { type FormEvent, useEffect, useMemo, useState } from 'react';
import { Link, useParams } from 'react-router';
import { createContentClient, hasPermission } from '../api';
import { UpvoteButton } from '../components/UpvoteButton';
import { PostBody } from '../components/PostBody';
import { ReactionBar } from '../components/ReactionBar';
import { UserByline } from '../components/UserPresentation';
import { useAuth } from '../AuthContext';
import { useServerRegistry } from '../ServerRegistryContext';
import { postOwnerBaseUrl } from '../postOwner';
import type { PostSort } from '../ranking';
import {
  decodeServerDid,
  forumPath,
  forumSettingsPath,
  resolveServerBaseUrl,
  subforumPath,
  createWatercoolerPath,
  threadPath,
  watercoolerPath,
} from '../serverDid';
import type { Forum, Post } from '../types';
import { loadContextEmotes } from '../emoteCatalog';
import type { CatalogEmote } from '../emotes';
import { usePostRealtime } from '../usePostRealtime';

function ThreadPreview({
  post,
  serverDid,
  baseUrl,
  emotes,
}: {
  post: Post;
  serverDid: string;
  baseUrl: string;
  emotes: CatalogEmote[];
}) {
  return (
    <div className="card thread-card">
      <div className="thread-card-main">
        <UpvoteButton
          postId={post.id}
          baseUrl={postOwnerBaseUrl(post, baseUrl)}
          upvoteCount={post.upvoteCount}
          viewerHasUpvoted={post.viewerHasUpvoted}
          compact
        />
        <Link to={threadPath(serverDid, post.id)} className="thread-card-body card-link">
          <UserByline presentation={post.author} timestamp={post.createdAt} showStatus={false} />
          {(post.isPinned || post.isLocked) && (
            <div className="thread-badges">
              {post.isPinned && <span className="thread-badge thread-badge-pinned">Pinned</span>}
              {post.isLocked && <span className="thread-badge thread-badge-locked">Locked</span>}
            </div>
          )}
          <PostBody
            message={`${post.message.slice(0, 200)}${post.message.length > 200 ? '…' : ''}`}
            emotes={emotes}
          />
          <div className="card-meta">{post.replyCount} replies</div>
        </Link>
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
  const { resolveBaseUrl } = useServerRegistry();
  const [forum, setForum] = useState<Forum | null>(null);
  const [threads, setThreads] = useState<Post[]>([]);
  const [sort, setSort] = useState<PostSort>('hot');
  const [message, setMessage] = useState('');
  const [feedMessage, setFeedMessage] = useState('');
  const [feedPosts, setFeedPosts] = useState<Post[]>([]);
  const [emotes, setEmotes] = useState<CatalogEmote[]>([]);
  const [error, setError] = useState('');

  const serverDid = encodedDid ? decodeServerDid(encodedDid) : '';
  const baseUrl = resolveServerBaseUrl(serverDid, resolveBaseUrl);
  const client = useMemo(() => (baseUrl ? createContentClient(baseUrl) : null), [baseUrl]);

  const pinnedFeed = forum?.contentBlocks?.find((b) => b.typeKey === 'cooler-feed' && b.slot === 'main');
  const pinnedCooler = forum?.watercoolers?.find((c) => c.id === pinnedFeed?.watercoolerId);

  useEffect(() => {
    if (!client || !pinnedCooler?.viewerCanView) {
      setFeedPosts([]);
      return;
    }
    void client
      .getWatercoolerStream(pinnedCooler.id, pinnedCooler.allowUpvotes ? 'hot' : 'new', 1, 15)
      .then(setFeedPosts)
      .catch(() => setFeedPosts([]));
  }, [client, pinnedCooler?.id, pinnedCooler?.viewerCanView, pinnedCooler?.allowUpvotes]);

  const canManageForum = hasPermission(forum?.viewerPermissions, 'ManageForum');
  const canCreateThread = hasPermission(forum?.viewerPermissions, 'CreateThread');
  const canAccessSettings =
    hasPermission(forum?.viewerPermissions, 'ManageForum') ||
    hasPermission(forum?.viewerPermissions, 'ManageRoles') ||
    hasPermission(forum?.viewerPermissions, 'ManageMembers') ||
    hasPermission(forum?.viewerPermissions, 'ManagePosts');

  const load = () => {
    if (!slug || !client) return;
    client
      .getForum(slug, parentSlug)
      .then((f) => {
        setForum(f);
        return client.getForumThreads(f.id, sort);
      })
      .then(setThreads)
      .catch((err) => setError(err.message));
  };

  useEffect(load, [slug, parentSlug, client, sort]);

  useEffect(() => {
    if (!client || !forum) return;
    loadContextEmotes({
      homeClient: client,
      forumId: forum.id,
      destPermissions: forum.viewerPermissions,
    })
      .then(setEmotes)
      .catch(() => setEmotes([]));
  }, [client, forum]);

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
        || evt.type === 'post.reacted'
      ) {
        load();
      }
    },
  });

  const handleNewThread = async (e: FormEvent) => {
    e.preventDefault();
    if (!forum || !message.trim() || !client) return;
    setError('');
    try {
      await client.createPost(forum.id, message.trim());
      setMessage('');
      load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create thread');
    }
  };

  if (!baseUrl) return <div className="error-banner">Unknown content server.</div>;
  if (!forum && !error) return <p>Loading forum...</p>;

  return (
    <div>
      {forum?.parentSlug && (
        <Link to={forumPath(serverDid, forum.parentSlug)} style={{ color: 'var(--muted)' }}>
          ← Parent forum
        </Link>
      )}
      {!forum?.parentSlug && (
        <Link to="/" style={{ color: 'var(--muted)' }}>
          ← All forums
        </Link>
      )}

      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: '1rem' }}>
        <div>
          <h1 className="page-title">{forum?.name}</h1>
          <p className="page-subtitle">{forum?.description}</p>
        </div>
        <div style={{ display: 'flex', gap: '0.5rem' }}>
          {user && canManageForum && forum && (
            <Link to={createWatercoolerPath(serverDid, forum.id)} className="btn-ghost">
              New watercooler
            </Link>
          )}
        {user && canAccessSettings && forum && (
          <Link to={forumSettingsPath(serverDid, forum.slug, parentSlug ?? forum.parentSlug ?? undefined)} className="btn-ghost">
            Settings
          </Link>
        )}
        </div>
      </div>

      {error && <div className="error-banner">{error}</div>}

      {forum && forum.watercoolers && forum.watercoolers.length > 0 && (
        <section className="card" style={{ marginBottom: '1rem' }}>
          <h2 className="card-title">Watercoolers</h2>
          <div className="subforum-grid">
            {forum.watercoolers.map((cooler) => (
              <Link
                key={cooler.id}
                to={watercoolerPath(serverDid, cooler.slug, forum.slug, parentSlug ?? forum.parentSlug ?? undefined)}
                className="card card-link"
                style={{ padding: '0.75rem 1rem' }}
              >
                <strong>{cooler.name}</strong>
                <div className="card-meta">
                  {cooler.allowUpvotes ? 'Plaza' : 'Channel'} · {cooler.postCount} posts
                  {!cooler.viewerCanView ? ' · locked' : ''}
                </div>
              </Link>
            ))}
          </div>
        </section>
      )}

      {pinnedCooler && pinnedCooler.viewerCanView && client && (
        <section className="card" style={{ marginBottom: '1rem' }}>
          <h2 className="card-title">{pinnedCooler.name}</h2>
          <p className="card-meta">
            Pinned watercooler ·{' '}
            <Link to={watercoolerPath(serverDid, pinnedCooler.slug, forum!.slug, parentSlug ?? forum!.parentSlug ?? undefined)}>
              Open room
            </Link>
          </p>
          {feedPosts.slice(-8).map((post) => (
            <div key={post.id} style={{ marginTop: '0.75rem' }}>
              <UserByline presentation={post.author} timestamp={post.createdAt} showStatus={false} />
              <PostBody message={post.message} emotes={emotes} />
              <ReactionBar
                postId={post.id}
                baseUrl={baseUrl}
                reactions={post.reactions ?? []}
                emotes={emotes}
                forumId={forum?.id}
                disabled={post.isFederated}
              />
            </div>
          ))}
          {user && pinnedCooler.viewerCanSpeak && (
            <form
              style={{ marginTop: '1rem' }}
              onSubmit={(e) => {
                e.preventDefault();
                if (!feedMessage.trim()) return;
                void client
                  .createPost(undefined, feedMessage.trim(), undefined, undefined, undefined, pinnedCooler.id)
                  .then(() => {
                    setFeedMessage('');
                    return client.getWatercoolerStream(pinnedCooler.id, pinnedCooler.allowUpvotes ? 'hot' : 'new', 1, 15);
                  })
                  .then(setFeedPosts)
                  .catch((err) => setError(err instanceof Error ? err.message : 'Failed to post'));
              }}
            >
              <div className="form-group">
                <textarea
                  value={feedMessage}
                  onChange={(e) => setFeedMessage(e.target.value)}
                  placeholder={`Message ${pinnedCooler.name}`}
                  required
                />
              </div>
              <button type="submit" className="btn-primary">
                Post
              </button>
            </form>
          )}
        </section>
      )}

      {forum && forum.subforums && forum.subforums.length > 0 && (
        <section className="card" style={{ marginBottom: '1rem' }}>
          <h2 className="card-title">Subforums</h2>
          <div className="subforum-grid">
            {forum.subforums.map((sub) => (
              <Link
                key={sub.id}
                to={subforumPath(serverDid, forum.slug, sub.slug)}
                className="card card-link"
                style={{ padding: '0.75rem 1rem' }}
              >
                <strong>{sub.name}</strong>
                <div className="card-meta">{sub.postCount} threads</div>
              </Link>
            ))}
          </div>
        </section>
      )}

      {user && forum && canCreateThread && (
        <form onSubmit={handleNewThread} className="card">
          <h2 className="card-title">Start a thread</h2>
          <div className="form-group">
            <textarea
              value={message}
              onChange={(e) => setMessage(e.target.value)}
              placeholder="What's on your mind?"
              required
            />
          </div>
          <button type="submit" className="btn-primary">
            Post thread
          </button>
        </form>
      )}

      {user && forum && !canCreateThread && (
        <div className="card card-meta">You do not have permission to create threads here.</div>
      )}

      {!user && (
        <div className="card">
          <Link to="/login">Log in</Link> to start a thread.
        </div>
      )}

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

      <div style={{ marginTop: '1rem' }}>
        {threads.map((thread) => (
          <ThreadPreview key={thread.id} post={thread} serverDid={serverDid} baseUrl={baseUrl} emotes={emotes} />
        ))}
      </div>
    </div>
  );
}

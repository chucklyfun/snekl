import { type FormEvent, useEffect, useMemo, useState } from 'react';
import { Link, useParams } from 'react-router';
import { createContentClient, hasPermission } from '../api';
import { UpvoteButton } from '../components/UpvoteButton';
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
  threadPath,
} from '../serverDid';
import type { Forum, Post } from '../types';
import { usePostRealtime } from '../usePostRealtime';

function ThreadPreview({
  post,
  serverDid,
  baseUrl,
}: {
  post: Post;
  serverDid: string;
  baseUrl: string;
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
          <p className="post-body">
            {post.message.slice(0, 200)}
            {post.message.length > 200 ? '…' : ''}
          </p>
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
  const [error, setError] = useState('');

  const serverDid = encodedDid ? decodeServerDid(encodedDid) : '';
  const baseUrl = resolveServerBaseUrl(serverDid, resolveBaseUrl);
  const client = useMemo(() => (baseUrl ? createContentClient(baseUrl) : null), [baseUrl]);

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

  usePostRealtime({
    baseUrl,
    forumId: forum?.id,
    onEvent: (evt) => {
      if (
        evt.type === 'post.created' ||
        evt.type === 'post.deleted' ||
        evt.type === 'post.hardDeleted' ||
        evt.type === 'thread.locked' ||
        evt.type === 'thread.pinned' ||
        evt.type === 'post.upvoted'
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
        {user && canAccessSettings && forum && (
          <Link to={forumSettingsPath(serverDid, forum.slug, parentSlug ?? forum.parentSlug ?? undefined)} className="btn-ghost">
            Settings
          </Link>
        )}
      </div>

      {error && <div className="error-banner">{error}</div>}

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
          <ThreadPreview key={thread.id} post={thread} serverDid={serverDid} baseUrl={baseUrl} />
        ))}
      </div>
    </div>
  );
}

import { type FormEvent, useEffect, useMemo, useState } from 'react';
import { Link, useParams } from 'react-router';
import { createContentClient } from '../api';
import { PostBody } from '../components/PostBody';
import { NsfwAgeCheckPanel } from '../components/NsfwAgeCheckPanel';
import { PostAttachments } from '../components/PostAttachments';
import { ReactionBar } from '../components/ReactionBar';
import { UpvoteButton } from '../components/UpvoteButton';
import { UserByline } from '../components/UserPresentation';
import { useAuth } from '../AuthContext';
import { loadContextEmotes } from '../emoteCatalog';
import type { CatalogEmote } from '../emotes';
import { useServerRegistry } from '../ServerRegistryContext';
import { postOwnerBaseUrl } from '../postOwner';
import { decodeServerDid, resolveServerBaseUrl } from '../serverDid';
import type { NsfwAgeCheckStatus, Post, PostRevision } from '../types';
import { usePostRealtime } from '../usePostRealtime';

const isDev = import.meta.env.DEV;

function PostNode({
  post,
  forumId,
  baseUrl,
  threadLocked,
  onChanged,
  createPost,
  updatePost,
  deletePost,
  getPostRevisions,
  setThreadLocked,
  setThreadPinned,
  setReplyPolicy,
  reportPost,
  emotes = [],
}: {
  post: Post;
  forumId?: string | null;
  baseUrl: string;
  threadLocked: boolean;
  onChanged: () => void;
  createPost: (
    forumId: string | undefined,
    message: string,
    parentPostId?: string,
    title?: string,
    mediaAssetIds?: string[],
    watercoolerId?: string,
  ) => Promise<Post>;
  updatePost: (postId: string, message: string) => Promise<Post>;
  deletePost: (postId: string, hard?: boolean) => Promise<void>;
  getPostRevisions: (postId: string) => Promise<PostRevision[]>;
  setThreadLocked: (postId: string, locked: boolean) => Promise<void>;
  setThreadPinned: (postId: string, pinned: boolean) => Promise<void>;
  setReplyPolicy: (postId: string, replyPolicy: string) => Promise<void>;
  reportPost: (postId: string, reason: string) => Promise<void>;
  emotes?: CatalogEmote[];
}) {
  const { user } = useAuth();
  const [reply, setReply] = useState('');
  const [editMessage, setEditMessage] = useState(post.message);
  const [showReply, setShowReply] = useState(false);
  const [showEdit, setShowEdit] = useState(false);
  const [showHistory, setShowHistory] = useState(false);
  const [showReport, setShowReport] = useState(false);
  const [reportReason, setReportReason] = useState('');
  const [history, setHistory] = useState<PostRevision[]>([]);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    setEditMessage(post.isDeleted ? '' : post.message);
  }, [post.id, post.message, post.isDeleted]);

  const actions = post.viewerActions;
  const isRoot = !post.parentPostId;
  const followingOnly = post.replyPolicy === 'following';
  const canModerateLocked = Boolean(actions?.canLock || actions?.canUnlock);
  const repliesBlocked = (threadLocked && !canModerateLocked) || actions?.canReply === false;

  const submitReply = async (e: FormEvent) => {
    e.preventDefault();
    if (!reply.trim()) return;
    setError('');
    setBusy(true);
    try {
      await createPost(post.forumId ?? undefined, reply.trim(), post.id, undefined, undefined, post.watercoolerId ?? undefined);
      setReply('');
      setShowReply(false);
      onChanged();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Reply failed');
    } finally {
      setBusy(false);
    }
  };

  const submitEdit = async (e: FormEvent) => {
    e.preventDefault();
    if (!editMessage.trim()) return;
    setError('');
    setBusy(true);
    try {
      await updatePost(post.id, editMessage.trim());
      setShowEdit(false);
      onChanged();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Edit failed');
    } finally {
      setBusy(false);
    }
  };

  const softDelete = async () => {
    if (!window.confirm('Delete this post? The content will be hidden but history may remain for moderators.')) return;
    setError('');
    setBusy(true);
    try {
      await deletePost(post.id, false);
      onChanged();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Delete failed');
    } finally {
      setBusy(false);
    }
  };

  const hardDelete = async () => {
    if (!window.confirm('Permanently remove this post and its history? This cannot be undone.')) return;
    setError('');
    setBusy(true);
    try {
      await deletePost(post.id, true);
      onChanged();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Hard delete failed');
    } finally {
      setBusy(false);
    }
  };

  const loadHistory = async () => {
    if (showHistory) {
      setShowHistory(false);
      return;
    }
    setError('');
    try {
      const rows = await getPostRevisions(post.id);
      setHistory(rows);
      setShowHistory(true);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not load edit history');
    }
  };

  const toggleLock = async (locked: boolean) => {
    setError('');
    setBusy(true);
    try {
      await setThreadLocked(post.id, locked);
      onChanged();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Lock failed');
    } finally {
      setBusy(false);
    }
  };

  const togglePin = async (pinned: boolean) => {
    setError('');
    setBusy(true);
    try {
      await setThreadPinned(post.id, pinned);
      onChanged();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Pin failed');
    } finally {
      setBusy(false);
    }
  };

  const toggleReplyPolicy = async (policy: 'everyone' | 'following') => {
    setError('');
    setBusy(true);
    try {
      await setReplyPolicy(post.id, policy);
      onChanged();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not update who can reply');
    } finally {
      setBusy(false);
    }
  };

  const submitReport = async (e: FormEvent) => {
    e.preventDefault();
    if (!reportReason.trim()) return;
    setError('');
    setBusy(true);
    try {
      await reportPost(post.id, reportReason.trim());
      setReportReason('');
      setShowReport(false);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Report failed');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className={`post-item${post.isDeleted ? ' post-item-deleted' : ''}`}>
      <div className="post-item-header">
        <UpvoteButton
          postId={post.id}
          baseUrl={postOwnerBaseUrl(post, baseUrl)}
          upvoteCount={post.upvoteCount}
          viewerHasUpvoted={post.viewerHasUpvoted}
          compact
        />
        <div className="post-item-content">
          <UserByline presentation={post.author} timestamp={post.createdAt} editedAt={post.editedAt} />
          {isRoot && (post.isPinned || post.isLocked || followingOnly) && (
            <div className="thread-badges">
              {post.isPinned && <span className="thread-badge thread-badge-pinned">Pinned</span>}
              {post.isLocked && <span className="thread-badge thread-badge-locked">Locked</span>}
              {followingOnly && <span className="thread-badge thread-badge-following">Following only</span>}
            </div>
          )}
          {isRoot && !threadLocked && followingOnly && (
            <p className="card-meta">Replies are limited to people the original poster follows.</p>
          )}
          {isRoot && threadLocked && (
            <p className="card-meta">This thread is locked. New replies are disabled.</p>
          )}
          {showEdit ? (
            <form onSubmit={submitEdit}>
              {error && <div className="error-banner">{error}</div>}
              <div className="form-group">
                <textarea value={editMessage} onChange={(e) => setEditMessage(e.target.value)} required />
              </div>
              <div className="post-actions">
                <button type="submit" className="btn-primary" disabled={busy}>
                  Save edit
                </button>
                <button type="button" className="btn-ghost" onClick={() => setShowEdit(false)} disabled={busy}>
                  Cancel
                </button>
              </div>
            </form>
          ) : (
            <>
            {post.quotedPost && (
              <div className="card" style={{ margin: '0.5rem 0' }}>
                {post.quotedPost.viewerCanView ? (
                  <>
                    <UserByline presentation={post.quotedPost.author} timestamp={post.quotedPost.createdAt} showStatus={false} />
                    <PostBody message={post.quotedPost.message} emotes={emotes} />
                  </>
                ) : (
                  <p className="card-meta">Quoted post is locked.</p>
                )}
              </div>
            )}
            <PostBody message={post.message} emotes={emotes} />
            <ReactionBar
              postId={post.id}
              baseUrl={baseUrl}
              reactions={post.reactions ?? []}
              emotes={emotes}
              forumId={forumId ?? undefined}
              disabled={post.isFederated}
            />
            <PostAttachments post={post} baseUrl={baseUrl} onChanged={onChanged} />
            </>
          )}
          {error && !showEdit && !showReport && <div className="error-banner">{error}</div>}
          <div className="post-actions">
            {user && !post.isDeleted && !repliesBlocked && (
              <button type="button" className="btn-ghost" onClick={() => setShowReply(!showReply)} disabled={busy}>
                Reply
              </button>
            )}
            {actions?.canEdit && !showEdit && (
              <button type="button" className="btn-ghost" onClick={() => setShowEdit(true)} disabled={busy}>
                Edit
              </button>
            )}
            {actions?.canSoftDelete && (
              <button type="button" className="btn-ghost" onClick={softDelete} disabled={busy}>
                Delete
              </button>
            )}
            {isDev && actions?.canHardDelete && (
              <button type="button" className="btn-ghost btn-danger" onClick={hardDelete} disabled={busy}>
                Purge (dev)
              </button>
            )}
            {actions?.canViewHistory && (
              <button type="button" className="btn-ghost" onClick={loadHistory} disabled={busy}>
                {showHistory ? 'Hide history' : 'Edit history'}
              </button>
            )}
            {actions?.canLock && (
              <button type="button" className="btn-ghost" onClick={() => toggleLock(true)} disabled={busy}>
                Lock thread
              </button>
            )}
            {actions?.canUnlock && (
              <button type="button" className="btn-ghost" onClick={() => toggleLock(false)} disabled={busy}>
                Unlock thread
              </button>
            )}
            {actions?.canPin && (
              <button type="button" className="btn-ghost" onClick={() => togglePin(true)} disabled={busy}>
                Pin thread
              </button>
            )}
            {actions?.canUnpin && (
              <button type="button" className="btn-ghost" onClick={() => togglePin(false)} disabled={busy}>
                Unpin thread
              </button>
            )}
            {actions?.canSetReplyPolicy && followingOnly && (
              <button type="button" className="btn-ghost" onClick={() => toggleReplyPolicy('everyone')} disabled={busy}>
                Allow anyone to reply
              </button>
            )}
            {actions?.canSetReplyPolicy && !followingOnly && (
              <button type="button" className="btn-ghost" onClick={() => toggleReplyPolicy('following')} disabled={busy}>
                Limit replies to following
              </button>
            )}
            {actions?.canReport && (
              <button type="button" className="btn-ghost" onClick={() => setShowReport(!showReport)} disabled={busy}>
                Report
              </button>
            )}
          </div>
          {showReport && (
            <form onSubmit={submitReport} style={{ marginTop: '0.75rem' }}>
              <div className="form-group">
                <textarea
                  value={reportReason}
                  onChange={(e) => setReportReason(e.target.value)}
                  placeholder="Why are you reporting this post?"
                  required
                />
              </div>
              <div className="post-actions">
                <button type="submit" className="btn-primary" disabled={busy}>
                  Submit report
                </button>
                <button type="button" className="btn-ghost" onClick={() => setShowReport(false)} disabled={busy}>
                  Cancel
                </button>
              </div>
            </form>
          )}
          {showHistory && history.length > 0 && (
            <div className="post-history">
              {history.map((revision) => (
                <div key={revision.id} className="post-history-item">
                  <div className="card-meta">
                    {revision.editedByUsername} · {new Date(revision.createdAt).toLocaleString()}
                  </div>
                  <p className="post-body">{revision.message}</p>
                </div>
              ))}
            </div>
          )}
          {showReply && !post.isDeleted && !repliesBlocked && (
            <form onSubmit={submitReply}>
              <div className="form-group">
                <textarea
                  value={reply}
                  onChange={(e) => setReply(e.target.value)}
                  placeholder="Write a reply..."
                  required
                />
              </div>
              <button type="submit" className="btn-primary" disabled={busy}>
                Submit reply
              </button>
            </form>
          )}
        </div>
      </div>
      <div className="post-tree">
        {post.replies.map((child) => (
          <PostNode
            key={child.id}
            post={child}
            forumId={forumId}
            baseUrl={baseUrl}
            threadLocked={threadLocked}
            onChanged={onChanged}
            createPost={createPost}
            updatePost={updatePost}
            deletePost={deletePost}
            getPostRevisions={getPostRevisions}
            setThreadLocked={setThreadLocked}
            setThreadPinned={setThreadPinned}
            setReplyPolicy={setReplyPolicy}
            reportPost={reportPost}
            emotes={emotes}
          />
        ))}
      </div>
    </div>
  );
}

export function ThreadPage() {
  const { serverDid: encodedDid, postId } = useParams<{ serverDid: string; postId: string }>();
  const { resolveBaseUrl } = useServerRegistry();
  const [thread, setThread] = useState<Post | null>(null);
  const [error, setError] = useState('');
  const [removed, setRemoved] = useState(false);
  const [emotes, setEmotes] = useState<CatalogEmote[]>([]);
  const [ageStatus, setAgeStatus] = useState<NsfwAgeCheckStatus | null>(null);
  const { user } = useAuth();

  const serverDid = encodedDid ? decodeServerDid(encodedDid) : '';
  const baseUrl = resolveServerBaseUrl(serverDid, resolveBaseUrl);
  const client = useMemo(() => (baseUrl ? createContentClient(baseUrl) : null), [baseUrl]);

  const load = () => {
    if (!postId || !client) return;
    setRemoved(false);
    client
      .getThread(postId)
      .then(setThread)
      .catch((err) => setError(err.message));
  };

  useEffect(load, [postId, client]);

  usePostRealtime({
    baseUrl,
    threadId: postId,
    watercoolerId: thread?.watercoolerId,
    onEvent: () => load(),
  });

  useEffect(() => {
    if (!client) return;
    loadContextEmotes({ homeClient: client, forumId: thread?.forumId ?? undefined })
      .then(setEmotes)
      .catch(() => setEmotes([]));
  }, [client, thread?.forumId]);

  useEffect(() => {
    if (!client || !thread?.forumId) return;
    client.getNsfwAgeCheck(thread.forumId).then(setAgeStatus).catch(() => setAgeStatus(null));
  }, [client, thread?.forumId]);

  const handleDelete = async (targetPostId: string, hard?: boolean) => {
    if (!client) return;
    await client.deletePost(targetPostId, hard);
    if (hard && targetPostId === postId) {
      setThread(null);
      setRemoved(true);
      return;
    }
    load();
  };

  if (!baseUrl) return <div className="error-banner">Unknown content server.</div>;
  if (removed) {
    return (
      <div>
        <Link to="/" style={{ color: 'var(--muted)' }}>
          ← Back to forums
        </Link>
        <p className="card-meta">This post was permanently removed.</p>
      </div>
    );
  }
  if (!thread && !error) return <p>Loading thread...</p>;

  return (
    <div>
      <Link to="/" style={{ color: 'var(--muted)' }}>
        ← Back to forums
      </Link>
      <h1 className="page-title">{thread?.title?.trim() || 'Thread'}</h1>
      {error && <div className="error-banner">{error}</div>}
      {ageStatus?.blocksEntry && client && (
        <NsfwAgeCheckPanel
          client={client}
          forumId={thread?.forumId ?? undefined}
          status={ageStatus}
          loggedIn={Boolean(user)}
          onCompleted={(next) => {
            setAgeStatus(next);
            load();
          }}
        />
      )}
      {thread && client && (
        <div className="card">
          {(thread.ancestors ?? []).map((ancestor) => (
            <PostNode
              key={ancestor.id}
              post={ancestor}
              forumId={thread.forumId}
              baseUrl={baseUrl}
              threadLocked={thread.isLocked}
              onChanged={load}
              createPost={client.createPost.bind(client)}
              updatePost={client.updatePost.bind(client)}
              deletePost={handleDelete}
              getPostRevisions={client.getPostRevisions.bind(client)}
              setThreadLocked={async (id, locked) => { await client.setThreadLocked(id, locked); }}
              setThreadPinned={async (id, pinned) => { await client.setThreadPinned(id, pinned); }}
              setReplyPolicy={async (id, policy) => { await client.setReplyPolicy(id, policy); }}
              reportPost={async (id, reason) => { await client.reportPost(id, reason); }}
              emotes={emotes}
            />
          ))}
          <PostNode
            post={thread}
            forumId={thread.forumId}
            baseUrl={baseUrl}
            threadLocked={thread.isLocked}
            onChanged={load}
            createPost={client.createPost.bind(client)}
            updatePost={client.updatePost.bind(client)}
            deletePost={handleDelete}
            getPostRevisions={client.getPostRevisions.bind(client)}
            setThreadLocked={async (id, locked) => {
              await client.setThreadLocked(id, locked);
            }}
            setThreadPinned={async (id, pinned) => {
              await client.setThreadPinned(id, pinned);
            }}
            setReplyPolicy={async (id, policy) => {
              await client.setReplyPolicy(id, policy);
            }}
            reportPost={async (id, reason) => {
              await client.reportPost(id, reason);
            }}
            emotes={emotes}
          />
        </div>
      )}
    </div>
  );
}

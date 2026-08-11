import { type FormEvent, useEffect, useMemo, useState } from 'react';
import { Link, useParams } from 'react-router';
import { createContentClient } from '../api';
import { UpvoteButton } from '../components/UpvoteButton';
import { UserByline } from '../components/UserPresentation';
import { useAuth } from '../AuthContext';
import { useServerRegistry } from '../ServerRegistryContext';
import { postOwnerBaseUrl } from '../postOwner';
import { decodeServerDid, resolveServerBaseUrl } from '../serverDid';
import type { Post, PostRevision } from '../types';
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
  reportPost,
}: {
  post: Post;
  forumId: string;
  baseUrl: string;
  threadLocked: boolean;
  onChanged: () => void;
  createPost: (forumId: string, message: string, parentPostId?: string) => Promise<Post>;
  updatePost: (postId: string, message: string) => Promise<Post>;
  deletePost: (postId: string, hard?: boolean) => Promise<void>;
  getPostRevisions: (postId: string) => Promise<PostRevision[]>;
  setThreadLocked: (postId: string, locked: boolean) => Promise<void>;
  setThreadPinned: (postId: string, pinned: boolean) => Promise<void>;
  reportPost: (postId: string, reason: string) => Promise<void>;
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
  const canModerateLocked = Boolean(actions?.canLock || actions?.canUnlock);
  const repliesBlocked = threadLocked && !canModerateLocked;

  const submitReply = async (e: FormEvent) => {
    e.preventDefault();
    if (!reply.trim()) return;
    setError('');
    setBusy(true);
    try {
      await createPost(forumId, reply.trim(), post.id);
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
          {isRoot && (post.isPinned || post.isLocked) && (
            <div className="thread-badges">
              {post.isPinned && <span className="thread-badge thread-badge-pinned">Pinned</span>}
              {post.isLocked && <span className="thread-badge thread-badge-locked">Locked</span>}
            </div>
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
            <p className="post-body">{post.message}</p>
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
            reportPost={reportPost}
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
    onEvent: () => load(),
  });

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
      <h1 className="page-title">Thread</h1>
      {error && <div className="error-banner">{error}</div>}
      {thread && client && (
        <div className="card">
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
            reportPost={async (id, reason) => {
              await client.reportPost(id, reason);
            }}
          />
        </div>
      )}
    </div>
  );
}

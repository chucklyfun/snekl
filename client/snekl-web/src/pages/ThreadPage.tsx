import { type FormEvent, useEffect, useMemo, useState } from 'react';
import { Link, useParams } from 'react-router';
import { createContentClient } from '../api';
import { UserByline } from '../components/UserPresentation';
import { useAuth } from '../AuthContext';
import { useServerRegistry } from '../ServerRegistryContext';
import { decodeServerDid, resolveServerBaseUrl } from '../serverDid';
import type { Post } from '../types';

function PostNode({
  post,
  forumId,
  onReply,
  createPost,
}: {
  post: Post;
  forumId: string;
  onReply: () => void;
  createPost: (forumId: string, message: string, parentPostId?: string) => Promise<Post>;
}) {
  const { user } = useAuth();
  const [reply, setReply] = useState('');
  const [showReply, setShowReply] = useState(false);
  const [error, setError] = useState('');

  const submitReply = async (e: FormEvent) => {
    e.preventDefault();
    if (!reply.trim()) return;
    setError('');
    try {
      await createPost(forumId, reply.trim(), post.id);
      setReply('');
      setShowReply(false);
      onReply();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Reply failed');
    }
  };

  return (
    <div className="post-item">
      <UserByline presentation={post.author} timestamp={post.createdAt} />
      <p className="post-body">{post.message}</p>
      {user && (
        <button type="button" className="btn-ghost" onClick={() => setShowReply(!showReply)}>
          Reply
        </button>
      )}
      {showReply && (
        <form onSubmit={submitReply}>
          {error && <div className="error-banner">{error}</div>}
          <div className="form-group">
            <textarea
              value={reply}
              onChange={(e) => setReply(e.target.value)}
              placeholder="Write a reply..."
              required
            />
          </div>
          <button type="submit" className="btn-primary">
            Submit reply
          </button>
        </form>
      )}
      <div className="post-tree">
        {post.replies.map((child) => (
          <PostNode
            key={child.id}
            post={child}
            forumId={forumId}
            onReply={onReply}
            createPost={createPost}
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

  const serverDid = encodedDid ? decodeServerDid(encodedDid) : '';
  const baseUrl = resolveServerBaseUrl(serverDid, resolveBaseUrl);
  const client = useMemo(() => (baseUrl ? createContentClient(baseUrl) : null), [baseUrl]);

  const load = () => {
    if (!postId || !client) return;
    client
      .getThread(postId)
      .then(setThread)
      .catch((err) => setError(err.message));
  };

  useEffect(load, [postId, client]);

  if (!baseUrl) return <div className="error-banner">Unknown content server.</div>;
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
            onReply={load}
            createPost={client.createPost.bind(client)}
          />
        </div>
      )}
    </div>
  );
}

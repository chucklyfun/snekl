import { type FormEvent, useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api } from '../api';
import { useAuth } from '../AuthContext';
import type { Post } from '../types';

function PostNode({ post, forumId, onReply }: { post: Post; forumId: string; onReply: () => void }) {
  const { user } = useAuth();
  const [reply, setReply] = useState('');
  const [showReply, setShowReply] = useState(false);
  const [error, setError] = useState('');

  const submitReply = async (e: FormEvent) => {
    e.preventDefault();
    if (!reply.trim()) return;
    setError('');
    try {
      await api.createPost(forumId, reply.trim(), post.id);
      setReply('');
      setShowReply(false);
      onReply();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Reply failed');
    }
  };

  return (
    <div className="post-item">
      <div>
        <span className="post-author">{post.author.displayName}</span>
        {post.isFederated && <span className="badge badge-federated">federated</span>}
        <span className="post-time">{new Date(post.createdAt).toLocaleString()}</span>
      </div>
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
            <textarea value={reply} onChange={(e) => setReply(e.target.value)} placeholder="Write a reply..." required />
          </div>
          <button type="submit" className="btn-primary">Submit reply</button>
        </form>
      )}
      <div className="post-tree">
        {post.replies.map((child) => (
          <PostNode key={child.id} post={child} forumId={forumId} onReply={onReply} />
        ))}
      </div>
    </div>
  );
}

export function ThreadPage() {
  const { postId } = useParams<{ postId: string }>();
  const [thread, setThread] = useState<Post | null>(null);
  const [error, setError] = useState('');

  const load = () => {
    if (!postId) return;
    api.getThread(postId)
      .then(setThread)
      .catch((err) => setError(err.message));
  };

  useEffect(load, [postId]);

  if (!thread && !error) return <p>Loading thread...</p>;

  return (
    <div>
      <Link to="/" style={{ color: 'var(--muted)' }}>← Back to forums</Link>
      <h1 className="page-title">Thread</h1>
      {error && <div className="error-banner">{error}</div>}
      {thread && (
        <div className="card">
          <PostNode post={thread} forumId={thread.forumId} onReply={load} />
        </div>
      )}
    </div>
  );
}

import { type FormEvent, useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api } from '../api';
import { UserByline } from '../components/UserPresentation';
import { useAuth } from '../AuthContext';
import type { Forum, Post } from '../types';

function ThreadPreview({ post }: { post: Post }) {
  return (
    <Link to={`/posts/${post.id}`} className="card card-link">
      <UserByline presentation={post.author} timestamp={post.createdAt} showStatus={false} />
      <p className="post-body">{post.message.slice(0, 200)}{post.message.length > 200 ? '…' : ''}</p>
      <div className="card-meta">{post.replyCount} replies</div>
    </Link>
  );
}

export function ForumPage() {
  const { slug } = useParams<{ slug: string }>();
  const { user } = useAuth();
  const [forum, setForum] = useState<Forum | null>(null);
  const [threads, setThreads] = useState<Post[]>([]);
  const [message, setMessage] = useState('');
  const [error, setError] = useState('');

  const load = () => {
    if (!slug) return;
    api.getForum(slug)
      .then((f) => {
        setForum(f);
        return api.getForumThreads(f.id);
      })
      .then(setThreads)
      .catch((err) => setError(err.message));
  };

  useEffect(load, [slug]);

  const handleNewThread = async (e: FormEvent) => {
    e.preventDefault();
    if (!forum || !message.trim()) return;
    setError('');
    try {
      await api.createPost(forum.id, message.trim());
      setMessage('');
      load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create thread');
    }
  };

  if (!forum && !error) return <p>Loading forum...</p>;

  return (
    <div>
      <Link to="/" style={{ color: 'var(--muted)' }}>← All forums</Link>
      <h1 className="page-title">{forum?.name}</h1>
      <p className="page-subtitle">{forum?.description}</p>

      {error && <div className="error-banner">{error}</div>}

      {user && forum && (
        <form onSubmit={handleNewThread} className="card">
          <h2 className="card-title">Start a thread</h2>
          <div className="form-group">
            <textarea value={message} onChange={(e) => setMessage(e.target.value)} placeholder="What's on your mind?" required />
          </div>
          <button type="submit" className="btn-primary">Post thread</button>
        </form>
      )}

      {!user && (
        <div className="card">
          <Link to="/login">Log in</Link> to start a thread.
        </div>
      )}

      <div style={{ marginTop: '1.5rem' }}>
        {threads.map((thread) => (
          <ThreadPreview key={thread.id} post={thread} />
        ))}
      </div>
    </div>
  );
}

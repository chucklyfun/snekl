import { type FormEvent, useEffect, useMemo, useState } from 'react';
import { Link, useParams } from 'react-router';
import { createContentClient } from '../api';
import { UserByline } from '../components/UserPresentation';
import { useAuth } from '../AuthContext';
import { useServerRegistry } from '../ServerRegistryContext';
import { decodeServerDid, resolveServerBaseUrl, threadPath } from '../serverDid';
import type { Forum, Post } from '../types';

function ThreadPreview({ post, serverDid }: { post: Post; serverDid: string }) {
  return (
    <Link to={threadPath(serverDid, post.id)} className="card card-link">
      <UserByline presentation={post.author} timestamp={post.createdAt} showStatus={false} />
      <p className="post-body">
        {post.message.slice(0, 200)}
        {post.message.length > 200 ? '…' : ''}
      </p>
      <div className="card-meta">{post.replyCount} replies</div>
    </Link>
  );
}

export function ForumPage() {
  const { serverDid: encodedDid, slug } = useParams<{ serverDid: string; slug: string }>();
  const { user } = useAuth();
  const { resolveBaseUrl } = useServerRegistry();
  const [forum, setForum] = useState<Forum | null>(null);
  const [threads, setThreads] = useState<Post[]>([]);
  const [message, setMessage] = useState('');
  const [error, setError] = useState('');

  const serverDid = encodedDid ? decodeServerDid(encodedDid) : '';
  const baseUrl = resolveServerBaseUrl(serverDid, resolveBaseUrl);
  const client = useMemo(() => (baseUrl ? createContentClient(baseUrl) : null), [baseUrl]);

  const load = () => {
    if (!slug || !client) return;
    client
      .getForum(slug)
      .then((f) => {
        setForum(f);
        return client.getForumThreads(f.id);
      })
      .then(setThreads)
      .catch((err) => setError(err.message));
  };

  useEffect(load, [slug, client]);

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
      <Link to="/" style={{ color: 'var(--muted)' }}>
        ← All forums
      </Link>
      <h1 className="page-title">{forum?.name}</h1>
      <p className="page-subtitle">{forum?.description}</p>

      {error && <div className="error-banner">{error}</div>}

      {user && forum && (
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

      {!user && (
        <div className="card">
          <Link to="/login">Log in</Link> to start a thread.
        </div>
      )}

      <div style={{ marginTop: '1.5rem' }}>
        {threads.map((thread) => (
          <ThreadPreview key={thread.id} post={thread} serverDid={serverDid} />
        ))}
      </div>
    </div>
  );
}

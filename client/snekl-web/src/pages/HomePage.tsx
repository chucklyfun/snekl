import { useEffect, useState } from 'react';
import { Link } from 'react-router';
import { aggregateForums, api, CONTENT_API, fetchAggregatedRecentPosts } from '../api';
import { UpvoteButton } from '../components/UpvoteButton';
import { UserByline } from '../components/UserPresentation';
import { useAuth } from '../AuthContext';
import { useServerRegistry } from '../ServerRegistryContext';
import { forumPath, shortServerDid, threadPath } from '../serverDid';
import type { AggregatedForum, AggregatedPost, Forum } from '../types';

function toAggregated(forum: Forum): AggregatedForum {
  return {
    ...forum,
    serverDid: '',
    serverName: 'Local',
    serverBaseUrl: '',
    serverStatus: 'active',
  };
}

export function HomePage() {
  const { user } = useAuth();
  const { activeServers, loading: serversLoading } = useServerRegistry();
  const [forums, setForums] = useState<AggregatedForum[]>([]);
  const [hotPosts, setHotPosts] = useState<AggregatedPost[]>([]);
  const [error, setError] = useState('');
  const [filterDid, setFilterDid] = useState<string | null>(null);

  useEffect(() => {
    setError('');
    if (user && !serversLoading) {
      if (activeServers.length === 0) {
        setForums([]);
        setHotPosts([]);
        return;
      }
      aggregateForums(activeServers)
        .then(setForums)
        .catch((err) => setError(err.message));
      fetchAggregatedRecentPosts(activeServers, 'hot', 15)
        .then(setHotPosts)
        .catch(() => setHotPosts([]));
      return;
    }

    if (!user) {
      api
        .getForums()
        .then((list) => setForums(list.map(toAggregated)))
        .catch((err) => setError(err.message));
      fetchAggregatedRecentPosts(
        [{ serverDid: '', baseUrl: CONTENT_API, name: 'Local', status: 'active', subscribedAt: '' }],
        'hot',
        15,
      )
        .then(setHotPosts)
        .catch(() => setHotPosts([]));
    }
  }, [user, activeServers, serversLoading]);

  const serverFilters = [...new Map(forums.map((f) => [f.serverDid, f.serverName])).entries()].filter(
    ([did]) => did,
  );

  const visibleForums = filterDid
    ? forums.filter((f) => f.serverDid === filterDid)
    : forums;

  return (
    <div>
      <h1 className="page-title">Forums</h1>
      <p className="page-subtitle">
        {user ? (
          <>Mixed feed from {activeServers.length} subscribed server{activeServers.length === 1 ? '' : 's'}.</>
        ) : (
          <>Browse forums on the default node. <Link to="/login">Log in</Link> to subscribe to more servers.</>
        )}
      </p>

      {error && <div className="error-banner">{error}</div>}

      {hotPosts.length > 0 && (
        <section style={{ marginBottom: '2rem' }}>
          <h2 className="section-title">Hot posts</h2>
          <p className="page-subtitle" style={{ marginTop: '-0.5rem' }}>
            Owned posts from your selected servers — one query handles aggregation.
          </p>
          {hotPosts.map((post) => (
            <div key={`${post.serverDid}:${post.id}`} className="card thread-card">
              <div className="thread-card-main">
                <UpvoteButton
                  postId={post.id}
                  baseUrl={post.serverBaseUrl}
                  upvoteCount={post.upvoteCount}
                  viewerHasUpvoted={post.viewerHasUpvoted}
                  compact
                />
                <div className="thread-card-body">
                  <div className="card-meta" style={{ marginBottom: '0.35rem' }}>
                    {post.serverDid ? (
                      <span className="badge badge-server" title={post.serverDid}>
                        {post.serverName} · {shortServerDid(post.serverDid)}
                      </span>
                    ) : null}
                    <Link
                      to={
                        post.serverDid
                          ? forumPath(post.serverDid, post.forumSlug, post.parentForumSlug ?? undefined)
                          : `/forums/${post.forumSlug}`
                      }
                      style={{ marginLeft: post.serverDid ? '0.5rem' : 0 }}
                    >
                      {post.forumName}
                    </Link>
                  </div>
                  <Link
                    to={
                      post.serverDid
                        ? threadPath(post.serverDid, post.id)
                        : `/threads/${post.id}`
                    }
                    className="card-link"
                  >
                    <UserByline presentation={post.author} timestamp={post.createdAt} showStatus={false} />
                    <p className="post-body">
                      {post.message.slice(0, 180)}
                      {post.message.length > 180 ? '…' : ''}
                    </p>
                  </Link>
                  <div className="card-meta">{post.replyCount} replies</div>
                </div>
              </div>
            </div>
          ))}
        </section>
      )}

      {user && serverFilters.length > 1 && (
        <div className="filter-chips">
          <button
            type="button"
            className={`filter-chip${filterDid === null ? ' filter-chip-active' : ''}`}
            onClick={() => setFilterDid(null)}
          >
            All
          </button>
          {serverFilters.map(([did, name]) => (
            <button
              key={did}
              type="button"
              className={`filter-chip${filterDid === did ? ' filter-chip-active' : ''}`}
              onClick={() => setFilterDid(did)}
            >
              {name}
            </button>
          ))}
        </div>
      )}

      {visibleForums.length === 0 && !error && !serversLoading && (
        <div className="card">
          <p>
            No forums yet.{' '}
            {user ? (
              <>
                <Link to="/servers">Subscribe to a server</Link> or create a forum on one of your nodes.
              </>
            ) : (
              'Sign up and create the first one.'
            )}
          </p>
        </div>
      )}

      {visibleForums.map((forum) => (
        <Link
          key={`${forum.serverDid}:${forum.id}`}
          to={forum.serverDid ? forumPath(forum.serverDid, forum.slug) : `/forums/${forum.slug}`}
          className="card card-link"
        >
          <h2 className="card-title">
            {forum.name}
            {forum.isFederated && <span className="badge badge-federated">federated</span>}
            {forum.serverDid && (
              <span className="badge badge-server" title={forum.serverDid}>
                {forum.serverName} · {shortServerDid(forum.serverDid)}
              </span>
            )}
          </h2>
          <p>{forum.description || 'No description'}</p>
          <div className="card-meta">
            {forum.postCount} threads · created {new Date(forum.createdAt).toLocaleDateString()}
            {forum.originNodeUrl && <> · from {forum.originNodeUrl}</>}
          </div>
        </Link>
      ))}
    </div>
  );
}

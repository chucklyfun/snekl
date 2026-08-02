import { useEffect, useState } from 'react';
import { Link } from 'react-router';
import { aggregateForums, api } from '../api';
import { useAuth } from '../AuthContext';
import { useServerRegistry } from '../ServerRegistryContext';
import { forumPath, shortServerDid } from '../serverDid';
import type { AggregatedForum, Forum } from '../types';

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
  const [error, setError] = useState('');
  const [filterDid, setFilterDid] = useState<string | null>(null);

  useEffect(() => {
    setError('');
    if (user && !serversLoading) {
      if (activeServers.length === 0) {
        setForums([]);
        return;
      }
      aggregateForums(activeServers)
        .then(setForums)
        .catch((err) => setError(err.message));
      return;
    }

    if (!user) {
      api
        .getForums()
        .then((list) => setForums(list.map(toAggregated)))
        .catch((err) => setError(err.message));
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

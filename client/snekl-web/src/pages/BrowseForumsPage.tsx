import { useEffect, useState } from 'react';
import { Link } from 'react-router';
import { aggregateForums } from '../api';
import { useAuth } from '../AuthContext';
import { useServerRegistry } from '../ServerRegistryContext';
import { forumPath } from '../serverDid';
import type { AggregatedForum } from '../types';

export function BrowseForumsPage() {
  const { user } = useAuth();
  const { activeServers, loading } = useServerRegistry();
  const [forums, setForums] = useState<AggregatedForum[]>([]);
  const [error, setError] = useState('');

  useEffect(() => {
    if (!user || loading) return;
    let cancelled = false;
    aggregateForums(activeServers)
      .then((rows) => {
        if (!cancelled) setForums(rows);
      })
      .catch((err) => {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Failed to load forums');
      });
    return () => {
      cancelled = true;
    };
  }, [user, loading, activeServers]);

  if (!user) {
    return (
      <div className="card">
        <p>
          Please <Link to="/login">log in</Link> to browse forums on your servers.
        </p>
      </div>
    );
  }

  return (
    <div>
      <h1 className="page-title">Browse forums</h1>
      {error && <div className="error-banner">{error}</div>}
      {forums.length === 0 && !error && <p className="card-meta">No forums on your servers yet.</p>}
      {forums.map((forum) => (
        <article key={`${forum.serverDid}:${forum.id}`} className="card">
          <h2>
            <Link to={forumPath(forum.serverDid, forum.slug, forum.parentSlug ?? undefined)}>{forum.name}</Link>
          </h2>
          <p className="card-meta">{forum.serverName}</p>
        </article>
      ))}
    </div>
  );
}

import { type FormEvent, useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { createContentClient } from '../api';
import { UserByline } from '../components/UserPresentation';
import { useServerRegistry } from '../ServerRegistryContext';
import { forumPath, threadPath } from '../serverDid';
import type { SearchHit } from '../types';

export function SearchPage() {
  const { activeServers } = useServerRegistry();
  const [params, setParams] = useSearchParams();
  const query = params.get('q') ?? '';
  const [draft, setDraft] = useState(query);
  const [hits, setHits] = useState<SearchHit[]>([]);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    setDraft(query);
    const q = query.trim();
    if (!q || activeServers.length === 0) {
      setHits([]);
      setError('');
      setLoading(false);
      return;
    }

    let cancelled = false;
    setLoading(true);
    setError('');
    Promise.allSettled(activeServers.map((server) => createContentClient(server.baseUrl).search(q)))
      .then((groups) => {
        if (cancelled) return;
        const fulfilled = groups.flatMap((group) => (group.status === 'fulfilled' ? group.value : []));
        if (fulfilled.length === 0 && groups.some((group) => group.status === 'rejected')) {
          setHits([]);
          setError('Search failed');
          return;
        }

        const seen = new Set<string>();
        const merged: SearchHit[] = [];
        for (const hit of fulfilled) {
          if (seen.has(hit.thread.post.id)) continue;
          seen.add(hit.thread.post.id);
          merged.push(hit);
        }
        setHits(merged);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [query, activeServers]);

  const onSubmit = (event: FormEvent) => {
    event.preventDefault();
    const next = draft.trim();
    if (next) setParams({ q: next });
    else setParams({});
  };

  return (
    <div>
      <h1 className="page-title">Search</h1>
      <form className="search-page-form" onSubmit={onSubmit} role="search">
        <input
          type="search"
          value={draft}
          onChange={(event) => setDraft(event.target.value)}
          placeholder="Search posts"
          aria-label="Search posts"
        />
        <button type="submit" className="btn-primary">
          Search
        </button>
      </form>
      {error && <div className="error-banner">{error}</div>}
      {activeServers.length === 0 && <p className="card-meta">Subscribe to a server to search its posts.</p>}
      {loading && <p>Searching…</p>}
      {!loading && query.trim() && hits.length === 0 && !error && <p className="card-meta">No matches.</p>}
      {hits.map((hit) => (
        <article key={hit.thread.post.id} className="card">
          <UserByline
            presentation={hit.thread.post.author}
            timestamp={hit.thread.post.createdAt}
            showStatus={false}
          />
          <h2>
            <Link to={threadPath(hit.thread.ownerDid, hit.thread.post.id)}>
              {hit.thread.post.title || hit.snippet}
            </Link>
          </h2>
          <p className="card-meta">
            <Link
              to={forumPath(
                hit.thread.ownerDid,
                hit.thread.forumSlug,
                hit.thread.parentForumSlug ?? undefined,
              )}
            >
              {hit.thread.forumName}
            </Link>
            {hit.matchedInReply ? ' · matched in a reply' : ''}
          </p>
          <p className="post-body">{hit.snippet}</p>
        </article>
      ))}
    </div>
  );
}

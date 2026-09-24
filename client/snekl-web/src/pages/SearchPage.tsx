import { type FormEvent, useEffect, useMemo, useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { fetchAggregatedSearch, fetchHubSearch } from '../api';
import { UpvoteButton } from '../components/UpvoteButton';
import { UserByline } from '../components/UserPresentation';
import { useAuth } from '../AuthContext';
import { useServerRegistry } from '../ServerRegistryContext';
import { defaultContentFallback, rewriteLoopbackForPage } from '../hosts';
import { mergeSearchHits } from '../hubFeed';
import { forumPath, threadPath } from '../serverDid';
import type { AggregatedSearchHit, ContentServerMembership } from '../types';

export function SearchPage() {
  const { user } = useAuth();
  const {
    activeServers,
    activeFederations,
    federationOriginGroups,
    loading: serversLoading,
  } = useServerRegistry();
  const [params, setParams] = useSearchParams();
  const q = params.get('q') ?? '';
  const [draft, setDraft] = useState(q);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const [hits, setHits] = useState<AggregatedSearchHit[]>([]);

  const hubUrls = useMemo(
    () =>
      [
        ...new Set([
          ...activeFederations.map((fed) => rewriteLoopbackForPage(fed.baseUrl)),
          ...federationOriginGroups.map((group) => rewriteLoopbackForPage(group.baseUrl)),
        ]),
      ].filter((url) => url.length > 0),
    [activeFederations, federationOriginGroups],
  );

  const servers = useMemo((): ContentServerMembership[] => {
    if (activeServers.length > 0) return activeServers;
    if (hubUrls.length > 0) return [];
    return [
      {
        serverDid: '',
        baseUrl: defaultContentFallback(),
        name: 'Local',
        status: 'active',
        subscribedAt: new Date(0).toISOString(),
      },
    ];
  }, [activeServers, hubUrls.length]);

  useEffect(() => {
    setDraft(q);
  }, [q]);

  useEffect(() => {
    const query = q.trim();
    if (!query) {
      setHits([]);
      setError('');
      setLoading(false);
      return;
    }
    if (serversLoading && user) return;

    let cancelled = false;
    setLoading(true);
    setError('');
    void Promise.all([
      fetchAggregatedSearch(servers, query).catch(() => [] as AggregatedSearchHit[]),
      fetchHubSearch(hubUrls, query).catch(() => []),
    ])
      .then(([originHits, hubHits]) => {
        if (!cancelled) setHits(mergeSearchHits(originHits, hubHits));
      })
      .catch((err) => {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Search failed');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [q, servers, hubUrls, serversLoading, user]);

  const onSubmit = (event: FormEvent) => {
    event.preventDefault();
    const next = draft.trim();
    setParams(next ? { q: next } : {});
  };

  return (
    <div>
      <h1 className="page-title">Search</h1>
      <p className="page-subtitle">
        Find threads across your federations and servers. Matches in replies still open the thread.
      </p>

      <form className="search-page-form" onSubmit={onSubmit}>
        <input
          type="search"
          value={draft}
          onChange={(event) => setDraft(event.target.value)}
          placeholder="Search titles and posts"
          aria-label="Search posts"
          autoFocus
        />
        <button type="submit" className="btn-primary">
          Search
        </button>
      </form>

      {error && <div className="error-banner">{error}</div>}
      {loading && <p className="card-meta">Searching…</p>}

      {!q.trim() && !loading && (
        <div className="card">
          <p>Type a word from a title, post, or reply.</p>
        </div>
      )}

      {q.trim() && !loading && hits.length === 0 && !error && (
        <div className="card">
          <p>No posts matched "{q.trim()}".</p>
        </div>
      )}

      {hits.map((hit) => {
        const post = hit.thread.post;
        const serverDid = hit.serverDid || hit.thread.ownerDid;
        return (
          <div key={`${serverDid}:${post.id}`} className="card thread-card">
            <div className="thread-card-main">
              <UpvoteButton
                postId={post.id}
                baseUrl={hit.serverBaseUrl || hit.thread.ownerBaseUrl}
                upvoteCount={post.upvoteCount}
                viewerHasUpvoted={post.viewerHasUpvoted}
                author={post.author}
                canUpvote={post.viewerActions?.canUpvote}
                compact
              />
              <div className="thread-card-body">
                <div className="card-meta" style={{ marginBottom: '0.35rem' }}>
                  {hit.serverName ? (
                    <span className="badge badge-server" title={serverDid}>
                      {hit.serverName}
                    </span>
                  ) : null}
                  {serverDid ? (
                    <Link
                      to={forumPath(serverDid, hit.thread.forumSlug, hit.thread.parentForumSlug ?? undefined)}
                      style={{ marginLeft: hit.serverName ? '0.5rem' : 0 }}
                    >
                      {hit.thread.forumName}
                    </Link>
                  ) : (
                    <span style={{ marginLeft: '0.5rem' }}>{hit.thread.forumName}</span>
                  )}
                  {hit.matchedInReply && <span> · matched in a reply</span>}
                </div>
                <Link
                  to={serverDid ? threadPath(serverDid, post.id) : `/threads/${post.id}`}
                  className="card-link"
                >
                  <UserByline presentation={post.author} timestamp={post.createdAt} showStatus={false} />
                  <h3 className="thread-card-title">
                    {post.title?.trim() ||
                      `${post.message.slice(0, 80)}${post.message.length > 80 ? '…' : ''}`}
                  </h3>
                  <p className="post-body">{hit.snippet}</p>
                </Link>
                <div className="card-meta">{post.replyCount} replies</div>
              </div>
            </div>
          </div>
        );
      })}
    </div>
  );
}

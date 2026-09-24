import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Link } from 'react-router';
import { createContentClient, fetchAggregatedRecentPosts, fetchHubFeedWindow } from '../api';
import { PostBody } from '../components/PostBody';
import { ReactionBar } from '../components/ReactionBar';
import { UpvoteButton } from '../components/UpvoteButton';
import { UserByline } from '../components/UserPresentation';
import { useAuth } from '../AuthContext';
import { useServerRegistry } from '../ServerRegistryContext';
import { rewriteLoopbackForPage } from '../hosts';
import { HOME_FEED_PAGE_SIZE, homeFeedHasMore, mergeHomeFeed } from '../hubFeed';
import { forumPath, serverSettingsPath, threadPath } from '../serverDid';
import type { AggregatedPost, HubFeedCard } from '../types';
import type { CatalogEmote } from '../emotes';

export function HomePage() {
  const { user } = useAuth();
  const {
    activeServers,
    activeFederations,
    federationOriginGroups,
    loading: serversLoading,
  } = useServerRegistry();
  const [hotPosts, setHotPosts] = useState<AggregatedPost[]>([]);
  const [emotesByForum, setEmotesByForum] = useState<Record<string, CatalogEmote[]>>({});
  const [error, setError] = useState('');
  const [page, setPage] = useState(1);
  const [hasMore, setHasMore] = useState(false);
  const [loadingFeed, setLoadingFeed] = useState(false);
  const requestIdRef = useRef(0);
  const inFlightRef = useRef(false);
  const sentinelRef = useRef<HTMLDivElement>(null);

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

  const directServers = useMemo(() => {
    const federatedDids = new Set(
      federationOriginGroups.flatMap((group) => group.origins.map((origin) => origin.serverDid)),
    );
    return activeServers.filter((server) => !federatedDids.has(server.serverDid));
  }, [activeServers, federationOriginGroups]);

  const loadPage = useCallback(
    async (pageNum: number, previousCount: number, requestId: number) => {
      const limit = pageNum * HOME_FEED_PAGE_SIZE;
      const [hubLists, directPosts] = await Promise.all([
        Promise.all(
          hubUrls.map((url) =>
            fetchHubFeedWindow(url, limit).catch(() => [] as HubFeedCard[]),
          ),
        ),
        directServers.length > 0
          ? fetchAggregatedRecentPosts(directServers, 'hot', limit)
          : Promise.resolve([] as AggregatedPost[]),
      ]);
      if (requestIdRef.current !== requestId) return;
      const merged = mergeHomeFeed(hubLists.flat(), directPosts);
      setHotPosts(merged);
      setPage(pageNum);
      setHasMore(homeFeedHasMore(merged.length, limit, previousCount));
    },
    [directServers, hubUrls],
  );

  useEffect(() => {
    setError('');
    const requestId = ++requestIdRef.current;
    if (!user) {
      setHotPosts([]);
      setHasMore(false);
      setPage(1);
      setLoadingFeed(false);
      return;
    }
    if (serversLoading) return;

    if (hubUrls.length === 0 && directServers.length === 0) {
      setHotPosts([]);
      setHasMore(false);
      setPage(1);
      return;
    }

    let cancelled = false;
    inFlightRef.current = true;
    setLoadingFeed(true);
    void (async () => {
      try {
        await loadPage(1, 0, requestId);
      } catch (err) {
        if (!cancelled && requestIdRef.current === requestId) {
          setError(err instanceof Error ? err.message : 'Failed to load feed');
        }
      } finally {
        if (!cancelled && requestIdRef.current === requestId) {
          inFlightRef.current = false;
          setLoadingFeed(false);
        }
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [user, serversLoading, loadPage, hubUrls.length, directServers.length]);

  const requestMore = useCallback(() => {
    if (!user || inFlightRef.current || loadingFeed || !hasMore) return;
    const requestId = requestIdRef.current;
    const nextPage = page + 1;
    inFlightRef.current = true;
    setLoadingFeed(true);
    void (async () => {
      try {
        await loadPage(nextPage, hotPosts.length, requestId);
      } catch (err) {
        if (requestIdRef.current === requestId) {
          setError(err instanceof Error ? err.message : 'Failed to load more posts');
        }
      } finally {
        if (requestIdRef.current === requestId) {
          inFlightRef.current = false;
          setLoadingFeed(false);
        }
      }
    })();
  }, [user, loadingFeed, hasMore, page, hotPosts.length, loadPage]);

  useEffect(() => {
    const node = sentinelRef.current;
    if (!node || !hasMore || loadingFeed) return;
    const observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((entry) => entry.isIntersecting)) requestMore();
      },
      { rootMargin: '240px' },
    );
    observer.observe(node);
    return () => observer.disconnect();
  }, [hasMore, loadingFeed, requestMore, hotPosts.length]);

  useEffect(() => {
    const missing = new Map<string, { url: string; forumId: string }>();
    for (const post of hotPosts) {
      const key = `${post.serverBaseUrl}|${post.forumId}`;
      if (!emotesByForum[key] && post.serverBaseUrl && post.forumId) {
        missing.set(key, { url: post.serverBaseUrl, forumId: post.forumId });
      }
    }
    if (missing.size === 0) return;
    let cancelled = false;
    void Promise.all(
      [...missing.entries()].map(async ([key, item]) => {
        const list = await createContentClient(item.url).getEmotes(item.forumId).catch(() => [] as CatalogEmote[]);
        return [key, list] as const;
      }),
    ).then((entries) => {
      if (cancelled) return;
      setEmotesByForum((prev) => ({ ...prev, ...Object.fromEntries(entries) }));
    });
    return () => {
      cancelled = true;
    };
  }, [hotPosts, emotesByForum]);

  return (
    <div>
      <h1 className="page-title">Home</h1>
      <p className="page-subtitle">
        {user ? (
          <>Hot posts across forums you follow. Open a forum from the left rail.</>
        ) : (
          <>
            Browse public forums after you add a federation or content server.{' '}
            <Link to="/login">Log in</Link> to load your lists.
          </>
        )}
      </p>

      {error && <div className="error-banner">{error}</div>}

      {hotPosts.length > 0 && (
        <section style={{ marginBottom: '2rem' }}>
          <h2 className="section-title">Hot posts</h2>
          {hotPosts.map((post) => (
            <div key={`${post.serverDid}:${post.id}`} className="card thread-card">
              <div className="thread-card-main">
                <UpvoteButton
                  postId={post.id}
                  baseUrl={post.serverBaseUrl}
                  upvoteCount={post.upvoteCount}
                  viewerHasUpvoted={post.viewerHasUpvoted}
                  author={post.author}
                  canUpvote={post.viewerActions?.canUpvote}
                  compact
                />
                <div className="thread-card-body">
                  <div className="card-meta" style={{ marginBottom: '0.35rem' }}>
                    {post.serverDid ? (
                      <span className="badge badge-server" title={post.serverDid}>
                        {post.serverName}
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
                    to={post.serverDid ? threadPath(post.serverDid, post.id) : `/threads/${post.id}`}
                    className="card-link"
                  >
                    <UserByline presentation={post.author} timestamp={post.createdAt} showStatus={false} />
                    <h3 className="thread-card-title">
                      {post.title?.trim() ||
                        `${post.message.slice(0, 80)}${post.message.length > 80 ? '…' : ''}`}
                    </h3>
                    <PostBody
                      message={`${post.message.slice(0, 180)}${post.message.length > 180 ? '…' : ''}`}
                      emotes={emotesByForum[`${post.serverBaseUrl}|${post.forumId}`] ?? []}
                      home={{ forumId: post.forumId, serverDid: post.serverDid }}
                    />
                    <FeedThumb post={post} />
                  </Link>
                  <div className="card-meta">{post.replyCount} replies</div>
                  <ReactionBar
                    postId={post.id}
                    baseUrl={post.serverBaseUrl}
                    reactions={post.reactions ?? []}
                    emotes={emotesByForum[`${post.serverBaseUrl}|${post.forumId}`] ?? []}
                    forumId={post.forumId}
                    manageHref={
                      post.serverDid ? `${serverSettingsPath(post.serverDid)}#emotes` : undefined
                    }
                    disabled={Boolean(
                      post.isFederated && post.originNodeUrl && post.originNodeUrl !== post.serverBaseUrl,
                    )}
                    onChange={(next) => {
                      setHotPosts((prev) =>
                        prev.map((item) =>
                          item.id === post.id && item.serverDid === post.serverDid
                            ? { ...item, reactions: next }
                            : item,
                        ),
                      );
                    }}
                  />
                </div>
              </div>
            </div>
          ))}
          <div ref={sentinelRef} className="feed-more">
            {hasMore ? (
              <button
                type="button"
                className="btn-ghost"
                disabled={loadingFeed}
                onClick={requestMore}
              >
                {loadingFeed ? 'Loading…' : 'Load more'}
              </button>
            ) : (
              !loadingFeed && <p className="card-meta">You're caught up.</p>
            )}
          </div>
        </section>
      )}

      {hotPosts.length === 0 && !error && !serversLoading && !loadingFeed && (
        <div className="card">
          <p>
            {user ? (
              <>
                No hot posts yet. <Link to="/forums">Browse forums</Link> to join one, or{' '}
                <Link to="/servers">manage your subscriptions</Link>.
              </>
            ) : (
              'Sign up and create the first one.'
            )}
          </p>
        </div>
      )}
    </div>
  );
}

function FeedThumb({ post }: { post: AggregatedPost }) {
  const first = post.attachments?.[0];
  const cover = post.nsfwCover ?? (first?.isNsfw ? first.coverText : null);
  const preview =
    cover
      ? first?.censorUrl ?? post.imagePreviewUrl
      : first?.url ?? post.imagePreviewUrl;
  if (!cover && !preview) return null;
  return (
    <div className={`feed-thumb${cover ? ' feed-thumb-nsfw' : ''}`}>
      {preview ? <img src={preview} alt="" /> : <div className="feed-thumb-empty" />}
      {cover ? <p>{cover}</p> : null}
    </div>
  );
}

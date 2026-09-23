import { rankPosts } from './ranking';
import type {
  AggregatedPost,
  AggregatedSearchHit,
  HubFeedCard,
  HubSearchHit,
  UserPresentation,
} from './types';

export const HOME_FEED_PAGE_SIZE = 15;

export function homeFeedHasMore(
  mergedCount: number,
  requestedCount: number,
  previousCount: number,
): boolean {
  return mergedCount > previousCount && mergedCount >= requestedCount;
}

export function hubCardAuthor(card: HubFeedCard): UserPresentation {
  const name = card.authorDisplayName || card.authorDid || 'unknown';
  return {
    id: card.authorDid || card.postId,
    username: name,
    displayName: name,
    bio: '',
    originNodeUrl: card.originBaseUrl,
    isFederated: true,
    effectiveDisplayName: name,
    annotations: [],
  };
}

export function hubCardToAggregatedPost(card: HubFeedCard): AggregatedPost {
  let serverName = card.forumName;
  try {
    serverName = new URL(card.originBaseUrl).host || card.forumName;
  } catch {
    // keep forum name
  }

  return {
    id: card.postId,
    forumId: card.forumId,
    parentPostId: null,
    author: hubCardAuthor(card),
    title: card.title,
    message: card.excerpt,
    replyCount: card.commentCount,
    upvoteCount: card.score,
    viewerHasUpvoted: false,
    originNodeUrl: card.originBaseUrl,
    isFederated: true,
    isDeleted: false,
    isLocked: false,
    isPinned: false,
    editedAt: card.editedAt,
    createdAt: card.createdAt,
    replies: [],
    serverDid: card.serverDid,
    serverName,
    serverBaseUrl: card.originBaseUrl,
    forumSlug: card.forumSlug,
    forumName: card.forumName,
    nsfwCover: card.nsfwCover ?? null,
    imagePreviewUrl: card.imagePreviewUrl ?? null,
  };
}

export function mergeHomeFeed(
  hubCards: HubFeedCard[],
  directPosts: AggregatedPost[],
  now = Date.now(),
): AggregatedPost[] {
  const byKey = new Map<string, AggregatedPost>();
  for (const post of directPosts) {
    byKey.set(`${post.serverDid}:${post.id}`, post);
  }
  for (const card of hubCards) {
    byKey.set(`${card.serverDid}:${card.postId}`, hubCardToAggregatedPost(card));
  }
  return rankPosts([...byKey.values()], 'hot', now);
}

export function hubCardToSearchHit(hit: HubSearchHit, serverName?: string): AggregatedSearchHit {
  const post = hubCardToAggregatedPost(hit.thread);
  return {
    thread: {
      post,
      forumSlug: hit.thread.forumSlug,
      forumName: hit.thread.forumName,
      ownerBaseUrl: hit.thread.originBaseUrl,
      ownerDid: hit.thread.serverDid,
    },
    snippet: hit.snippet || hit.thread.excerpt,
    matchedInReply: false,
    serverDid: hit.thread.serverDid,
    serverName: serverName || post.serverName,
    serverBaseUrl: hit.thread.originBaseUrl,
  };
}

/** Origin FTS wins on the same thread so reply snippets stay. Hub fills federation gaps. */
export function mergeSearchHits(
  originHits: AggregatedSearchHit[],
  hubHits: HubSearchHit[],
  hubNameByUrl?: Map<string, string>,
): AggregatedSearchHit[] {
  const byKey = new Map<string, AggregatedSearchHit>();
  for (const hit of hubHits) {
    let name = hubNameByUrl?.get(hit.thread.originBaseUrl.replace(/\/$/, ''));
    if (!name) {
      try {
        name = new URL(hit.thread.originBaseUrl).host;
      } catch {
        name = hit.thread.forumName;
      }
    }
    const mapped = hubCardToSearchHit(hit, name);
    byKey.set(`${mapped.serverDid}:${mapped.thread.post.id}`, mapped);
  }
  for (const hit of originHits) {
    byKey.set(`${hit.serverDid}:${hit.thread.post.id}`, hit);
  }
  return [...byKey.values()].sort(
    (a, b) =>
      new Date(b.thread.post.createdAt).getTime() - new Date(a.thread.post.createdAt).getTime(),
  );
}

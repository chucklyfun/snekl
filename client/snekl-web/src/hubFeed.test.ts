import { describe, expect, it } from 'vitest';
import { homeFeedHasMore, hubCardToAggregatedPost, mergeHomeFeed, mergeSearchHits } from './hubFeed';
import type { AggregatedPost, AggregatedSearchHit, HubFeedCard, HubSearchHit } from './types';

function card(partial: Partial<HubFeedCard> & Pick<HubFeedCard, 'postId' | 'serverDid'>): HubFeedCard {
  return {
    schemaVersion: 1,
    forumId: 'forum-1',
    forumSlug: 'garden',
    forumName: 'Garden',
    originBaseUrl: 'http://alpha',
    originPostUrl: 'http://alpha/f/garden/1',
    title: 'Roses',
    excerpt: 'Hello roses',
    imagePreviewUrl: null,
    nsfwCover: null,
    authorDid: 'did:key:alice',
    authorDisplayName: 'Alice',
    createdAt: '2026-01-01T00:00:00Z',
    editedAt: null,
    visibility: 'public',
    score: 10,
    commentCount: 2,
    mapVersion: 1,
    event: 'upsert',
    eventAt: '2026-01-01T00:00:00Z',
    ...partial,
  };
}

function direct(partial: Partial<AggregatedPost> & Pick<AggregatedPost, 'id' | 'serverDid'>): AggregatedPost {
  return {
    forumId: 'forum-2',
    author: {
      id: 'u',
      username: 'bob',
      displayName: 'Bob',
      bio: '',
      isFederated: false,
      effectiveDisplayName: 'Bob',
      annotations: [],
    },
    title: 'Direct',
    message: 'From origin',
    replyCount: 0,
    upvoteCount: 1,
    viewerHasUpvoted: false,
    isFederated: false,
    isDeleted: false,
    isLocked: false,
    isPinned: false,
    createdAt: '2026-01-02T00:00:00Z',
    replies: [],
    serverName: 'Lone',
    serverBaseUrl: 'http://lone',
    forumSlug: 'lone',
    forumName: 'Lone',
    ...partial,
  };
}

describe('hub feed merge', () => {
  it('maps a hub card onto an aggregated post', () => {
    const post = hubCardToAggregatedPost(card({ postId: 'p1', serverDid: 'did:alpha', score: 4 }));
    expect(post.id).toBe('p1');
    expect(post.upvoteCount).toBe(4);
    expect(post.forumSlug).toBe('garden');
    expect(post.author.effectiveDisplayName).toBe('Alice');
  });

  it('prefers hub cards over direct posts with the same id and ranks by hot score', () => {
    const hub = card({
      postId: 'p1',
      serverDid: 'did:alpha',
      score: 50,
      excerpt: 'From hub',
      createdAt: '2026-01-01T00:00:00Z',
    });
    const olderDirect = direct({
      id: 'p1',
      serverDid: 'did:alpha',
      upvoteCount: 1,
      message: 'From origin',
      createdAt: '2026-01-01T00:00:00Z',
    });
    const lone = direct({
      id: 'p2',
      serverDid: 'did:lone',
      upvoteCount: 1,
      createdAt: '2026-01-01T00:00:00Z',
    });

    const merged = mergeHomeFeed([hub], [olderDirect, lone], Date.parse('2026-01-01T02:00:00Z'));
    expect(merged.map((p) => p.id)).toEqual(['p1', 'p2']);
    expect(merged[0].message).toBe('From hub');
  });

  it('asks for another home page when the window filled and grew', () => {
    expect(homeFeedHasMore(15, 15, 0)).toBe(true);
    expect(homeFeedHasMore(8, 15, 0)).toBe(false);
    expect(homeFeedHasMore(15, 30, 15)).toBe(false);
    expect(homeFeedHasMore(30, 30, 15)).toBe(true);
  });

  it('keeps origin reply snippets when merging hub cards for the same thread', () => {
    const hub: HubSearchHit = {
      snippet: 'From hub excerpt',
      thread: card({ postId: 'p1', serverDid: 'did:alpha', excerpt: 'From hub excerpt' }),
    };
    const origin: AggregatedSearchHit = {
      snippet: 'anchovy pineapple is a crime',
      matchedInReply: true,
      serverDid: 'did:alpha',
      serverName: 'Alpha',
      serverBaseUrl: 'http://alpha',
      thread: {
        forumSlug: 'garden',
        forumName: 'Garden',
        ownerBaseUrl: 'http://alpha',
        ownerDid: 'did:alpha',
        post: direct({ id: 'p1', serverDid: 'did:alpha' }),
      },
    };
    const extra: HubSearchHit = {
      snippet: 'Only on the hub',
      thread: card({ postId: 'p3', serverDid: 'did:beta', title: 'Beta only', excerpt: 'Only on the hub' }),
    };

    const merged = mergeSearchHits([origin], [hub, extra]);
    expect(merged.map((h) => h.thread.post.id).sort()).toEqual(['p1', 'p3']);
    const alpha = merged.find((h) => h.thread.post.id === 'p1')!;
    expect(alpha.matchedInReply).toBe(true);
    expect(alpha.snippet).toContain('anchovy');
    expect(merged.find((h) => h.thread.post.id === 'p3')?.serverDid).toBe('did:beta');
  });
});

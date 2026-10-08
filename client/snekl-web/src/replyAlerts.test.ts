import { describe, expect, it } from 'vitest';
import { mergeReplyAlerts, replyAlertModeLabel, replyAlertPath, replyAlertSummary } from './replyAlerts';
import { forumPath } from './serverDid';
import type { ContentServerMembership, ReplyAlert } from './types';

function server(name: string, did: string): ContentServerMembership {
  return {
    serverDid: did,
    baseUrl: `http://${name}.example`,
    name,
    status: 'active',
    subscribedAt: '2026-01-01T00:00:00Z',
  };
}

function alert(id: string, at: string): ReplyAlert {
  return {
    id,
    rootPostId: `post-${id}`,
    title: id,
    replyCount: 1,
    latestReplyId: `reply-${id}`,
    latestReplyAt: at,
    createdAt: at,
    readAt: null,
  };
}

describe('mergeReplyAlerts', () => {
  it('merges servers newest first and keeps the server that produced each row', () => {
    const alpha = server('Alpha', 'did:web:alpha');
    const beta = server('Beta', 'did:web:beta');
    const merged = mergeReplyAlerts([
      { server: alpha, alerts: [alert('old', '2026-09-01T00:00:00Z')] },
      { server: beta, alerts: [alert('new', '2026-09-02T00:00:00Z')] },
    ]);

    expect(merged.map((row) => row.id)).toEqual(['new', 'old']);
    expect(merged[0].serverName).toBe('Beta');
    expect(merged[1].serverDid).toBe('did:web:alpha');
  });
});

describe('reply alert copy', () => {
  it('summarizes a burst and labels modes', () => {
    expect(replyAlertSummary(1, 'Hello')).toBe('New reply on Hello');
    expect(replyAlertSummary(3, 'Hello')).toBe('3 new replies on Hello');
    expect(replyAlertSummary(1, 'Sandbox', 'nest')).toBe('New post in Sandbox');
    expect(replyAlertSummary(4, 'Plaza', 'watercooler')).toBe('4 new posts in Plaza');
    expect(replyAlertSummary(10, 'Hello', 'vote')).toBe('10 votes on Hello');
    expect(replyAlertSummary(2, 'Alice', 'follow')).toBe('2 new posts from Alice');
    expect(
      replyAlertPath('did:web:alpha', {
        id: 'a',
        title: 'Sandbox',
        replyCount: 3,
        latestReplyId: 'r',
        latestReplyAt: '2026-09-01T00:00:00Z',
        createdAt: '2026-09-01T00:00:00Z',
        readAt: null,
        kind: 'nest',
        slug: 'sandbox',
        parentSlug: 'top',
      }),
    ).toBe(forumPath('did:web:alpha', 'sandbox', 'top'));
    expect(replyAlertModeLabel('replies')).toBe('Replies');
    expect(replyAlertModeLabel('all')).toBe('All activity');
    expect(replyAlertModeLabel('off')).toBe('Off');
  });
});

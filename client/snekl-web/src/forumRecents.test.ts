import { describe, expect, it, beforeEach } from 'vitest';
import { MAX_FORUM_RECENTS } from './forumNav';
import { patchForumRecentIdentity, readForumRecents, recordForumVisit } from './forumRecents';

const memory = new Map<string, string>();

const localStorageStub = {
  getItem: (key: string) => memory.get(key) ?? null,
  setItem: (key: string, value: string) => {
    memory.set(key, value);
  },
  removeItem: (key: string) => {
    memory.delete(key);
  },
};

Object.defineProperty(globalThis, 'localStorage', {
  value: localStorageStub,
  configurable: true,
});

describe('forum recents', () => {
  const did = 'did:key:zTest';

  beforeEach(() => {
    memory.clear();
  });

  it('dedupes and caps visits with the newest first', () => {
    recordForumVisit(did, {
      serverDid: 'did:a',
      forumId: '1',
      name: 'One',
      slug: 'one',
      serverName: 'A',
    });
    recordForumVisit(did, {
      serverDid: 'did:a',
      forumId: '2',
      name: 'Two',
      slug: 'two',
      serverName: 'A',
    });
    const recents = recordForumVisit(did, {
      serverDid: 'did:a',
      forumId: '1',
      name: 'One',
      slug: 'one',
      serverName: 'A',
    });

    expect(recents.map((item) => item.forumId)).toEqual(['1', '2']);
    expect(readForumRecents(did).map((item) => item.forumId)).toEqual(['1', '2']);
  });

  it('keeps at most MAX_FORUM_RECENTS entries', () => {
    for (let i = 0; i < MAX_FORUM_RECENTS + 4; i += 1) {
      recordForumVisit(did, {
        serverDid: 'did:a',
        forumId: String(i),
        name: `F${i}`,
        slug: `f${i}`,
        serverName: 'A',
      });
    }
    expect(readForumRecents(did)).toHaveLength(MAX_FORUM_RECENTS);
    expect(readForumRecents(did)[0].forumId).toBe(String(MAX_FORUM_RECENTS + 3));
  });

  it('updates a renamed forum and child addresses that used its old slug', () => {
    recordForumVisit(did, {
      serverDid: 'did:a',
      forumId: 'parent',
      name: 'The',
      slug: 'the',
      serverName: 'A',
    });
    recordForumVisit(did, {
      serverDid: 'did:a',
      forumId: 'child',
      name: 'Nest',
      slug: 'nest',
      parentSlug: 'the',
      serverName: 'A',
    });

    const recents = patchForumRecentIdentity(
      did,
      { serverDid: 'did:a', forumId: 'parent', previousSlug: 'the', updateChildAddresses: true },
      { name: 'The Snackleverse', slug: 'snackleverse' },
    );

    expect(recents.find((item) => item.forumId === 'parent')).toMatchObject({
      name: 'The Snackleverse',
      slug: 'snackleverse',
    });
    expect(recents.find((item) => item.forumId === 'child')?.parentSlug).toBe('snackleverse');
  });
});

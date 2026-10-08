import { describe, expect, it, beforeEach } from 'vitest';
import {
  forumNavOpenStorageKey,
  isForumNavGroupOpen,
  readForumNavOpenState,
  writeForumNavOpenState,
} from './forumNavCollapse';

const memory = new Map<string, string>();

Object.defineProperty(globalThis, 'localStorage', {
  value: {
    getItem: (key: string) => memory.get(key) ?? null,
    setItem: (key: string, value: string) => memory.set(key, value),
    removeItem: (key: string) => memory.delete(key),
  },
  configurable: true,
});

describe('nest nav collapse', () => {
  const did = 'did:key:zTest';

  beforeEach(() => memory.clear());

  it('defaults to open and remembers a collapsed group', () => {
    expect(isForumNavGroupOpen({}, 'section:joined')).toBe(true);
    writeForumNavOpenState(did, { 'section:joined': false });
    const state = readForumNavOpenState(did);
    expect(isForumNavGroupOpen(state, 'section:joined')).toBe(false);
    expect(isForumNavGroupOpen(state, 'section:favorites')).toBe(true);
    expect(localStorage.getItem(forumNavOpenStorageKey(did))).toContain('section:joined');
  });
});

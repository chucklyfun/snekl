import { describe, expect, it } from 'vitest';
import { resolveForumSettingsSection } from './forumSettingsNav';

describe('resolveForumSettingsSection', () => {
  const available = ['general', 'emotes', 'roles'] as const;

  it('uses the hash when that section is available', () => {
    expect(resolveForumSettingsSection('#emotes', [...available])).toBe('emotes');
  });

  it('falls back to the first available section', () => {
    expect(resolveForumSettingsSection('', [...available])).toBe('general');
    expect(resolveForumSettingsSection('#members', [...available])).toBe('general');
  });
});

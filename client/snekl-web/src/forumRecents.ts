import type { ForumRecent } from './forumNav';
import { MAX_FORUM_RECENTS } from './forumNav';

export function forumRecentsStorageKey(personDid: string): string {
  return `snekl_forum_recents:${personDid}`;
}

export function readForumRecents(personDid: string | null | undefined): ForumRecent[] {
  if (!personDid) return [];
  try {
    const raw = localStorage.getItem(forumRecentsStorageKey(personDid));
    if (!raw) return [];
    const parsed = JSON.parse(raw) as ForumRecent[];
    return Array.isArray(parsed) ? parsed.slice(0, MAX_FORUM_RECENTS) : [];
  } catch {
    return [];
  }
}

export function recordForumVisit(
  personDid: string | null | undefined,
  visit: Omit<ForumRecent, 'visitedAt'>,
): ForumRecent[] {
  if (!personDid) return [];
  const next: ForumRecent[] = [
    { ...visit, visitedAt: new Date().toISOString() },
    ...readForumRecents(personDid).filter(
      (item) => !(item.serverDid === visit.serverDid && item.forumId === visit.forumId),
    ),
  ].slice(0, MAX_FORUM_RECENTS);

  try {
    localStorage.setItem(forumRecentsStorageKey(personDid), JSON.stringify(next));
  } catch {
    // Ignore quota / private mode.
  }
  return next;
}

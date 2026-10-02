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

export function patchForumRecentIdentity(
  personDid: string | null | undefined,
  match: { serverDid: string; forumId: string; previousSlug: string; updateChildAddresses?: boolean },
  next: { name: string; slug: string },
): ForumRecent[] {
  if (!personDid) return [];
  const recents = readForumRecents(personDid).map((item) => {
    if (item.serverDid !== match.serverDid) return item;
    if (item.forumId === match.forumId) {
      return { ...item, name: next.name, slug: next.slug };
    }
    if (match.updateChildAddresses && item.parentSlug === match.previousSlug) {
      return { ...item, parentSlug: next.slug };
    }
    return item;
  });

  try {
    localStorage.setItem(forumRecentsStorageKey(personDid), JSON.stringify(recents));
  } catch {
    // Ignore quota / private mode.
  }
  return recents;
}

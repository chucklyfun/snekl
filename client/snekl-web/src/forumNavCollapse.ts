export const FORUM_NAV_OPEN_KEY = 'snekl_forum_nav_open';

export function forumNavOpenStorageKey(personDid: string): string {
  return `${FORUM_NAV_OPEN_KEY}:${personDid}`;
}

export function readForumNavOpenState(personDid: string | null | undefined): Record<string, boolean> {
  if (!personDid) return {};
  try {
    const raw = localStorage.getItem(forumNavOpenStorageKey(personDid));
    if (!raw) return {};
    const parsed = JSON.parse(raw) as Record<string, boolean>;
    return parsed && typeof parsed === 'object' ? parsed : {};
  } catch {
    return {};
  }
}

export function writeForumNavOpenState(
  personDid: string | null | undefined,
  state: Record<string, boolean>,
): void {
  if (!personDid) return;
  try {
    localStorage.setItem(forumNavOpenStorageKey(personDid), JSON.stringify(state));
  } catch {
    // Ignore quota / private mode.
  }
}

export function isForumNavGroupOpen(
  state: Record<string, boolean>,
  id: string,
  defaultOpen = true,
): boolean {
  return state[id] ?? defaultOpen;
}

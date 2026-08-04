/** Mirrors Snekl.Domain.Ranking.PostRanking — safe to use when merging posts from multiple content servers. */
export const HOT_GRAVITY = 1.5;
export const HOT_AGE_OFFSET_HOURS = 2;

export function hotScore(upvoteCount: number, createdAt: string | Date, now = Date.now()): number {
  const createdMs = typeof createdAt === 'string' ? Date.parse(createdAt) : createdAt.getTime();
  const ageHours = Math.max((now - createdMs) / 3_600_000, 0);
  return upvoteCount / (ageHours + HOT_AGE_OFFSET_HOURS) ** HOT_GRAVITY;
}

export type PostSort = 'hot' | 'new';

export function rankPosts<T extends { upvoteCount: number; createdAt: string }>(
  posts: T[],
  sort: PostSort = 'hot',
  now = Date.now(),
): T[] {
  if (sort === 'new') {
    return [...posts].sort(
      (a, b) => Date.parse(b.createdAt) - Date.parse(a.createdAt),
    );
  }

  return [...posts].sort(
    (a, b) => hotScore(b.upvoteCount, b.createdAt, now) - hotScore(a.upvoteCount, a.createdAt, now),
  );
}

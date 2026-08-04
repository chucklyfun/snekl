import type { Post } from './types';

/** Base URL of the server that owns this post (votes and writes go here). */
export function postOwnerBaseUrl(post: Post, browseBaseUrl: string): string {
  return post.originNodeUrl?.replace(/\/$/, '') ?? browseBaseUrl;
}

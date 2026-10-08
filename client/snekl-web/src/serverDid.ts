import { rewriteLoopbackForPage } from './hosts';

export function encodeServerDid(did: string): string {
  const bytes = new TextEncoder().encode(did);
  let binary = '';
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

export function decodeServerDid(encoded: string): string {
  const base64 = encoded.replace(/-/g, '+').replace(/_/g, '/');
  const pad = base64.length % 4 === 0 ? '' : '='.repeat(4 - (base64.length % 4));
  const binary = atob(base64 + pad);
  const bytes = Uint8Array.from(binary, (c) => c.charCodeAt(0));
  return new TextDecoder().decode(bytes);
}

export function forumPath(serverDid: string, slug: string, parentSlug?: string): string {
  if (parentSlug) {
    return `/s/${encodeServerDid(serverDid)}/nests/${parentSlug}/${slug}`;
  }
  return `/s/${encodeServerDid(serverDid)}/nests/${slug}`;
}

export function subforumPath(serverDid: string, parentSlug: string, slug: string): string {
  return forumPath(serverDid, slug, parentSlug);
}

export function forumSettingsPath(serverDid: string, slug: string, parentSlug?: string): string {
  const base = parentSlug
    ? `/s/${encodeServerDid(serverDid)}/nests/${parentSlug}/${slug}/settings`
    : `/s/${encodeServerDid(serverDid)}/nests/${slug}/settings`;
  return base;
}

export function serverSettingsPath(serverDid: string): string {
  return `/s/${encodeServerDid(serverDid)}/settings`;
}

export function serverOwnerSettingsPath(serverDid: string): string {
  return `/s/${encodeServerDid(serverDid)}/owner`;
}

export function threadPath(serverDid: string, postId: string): string {
  return `/s/${encodeServerDid(serverDid)}/posts/${postId}`;
}

export function createForumPath(serverDid: string): string {
  return `/s/${encodeServerDid(serverDid)}/nests/new`;
}

export function watercoolerPath(
  serverDid: string,
  slug: string,
  parentForumSlug?: string,
  forumParentSlug?: string,
): string {
  if (parentForumSlug) {
    const forum = forumPath(serverDid, parentForumSlug, forumParentSlug);
    return `${forum}/coolers/${slug}`;
  }
  return `/s/${encodeServerDid(serverDid)}/coolers/${slug}`;
}

export function createWatercoolerPath(serverDid: string, parentForumId?: string): string {
  const base = `/s/${encodeServerDid(serverDid)}/coolers/new`;
  return parentForumId ? `${base}?parent=${parentForumId}` : base;
}

export function watercoolerSettingsPath(
  serverDid: string,
  slug: string,
  parentForumSlug?: string,
  forumParentSlug?: string,
): string {
  return `${watercoolerPath(serverDid, slug, parentForumSlug, forumParentSlug)}/settings`;
}

/** Create-forum page when no content server is in the URL context. */
export function createForumChooserPath(): string {
  return '/nests/new';
}

export function shortServerDid(did: string): string {
  if (did.length <= 24) return did;
  return `${did.slice(0, 16)}…`;
}

/** Recover content server base URL from did:web when not in the user's subscription list. */
export function baseUrlFromServerDid(did: string): string | undefined {
  const prefix = 'did:web:';
  if (!did.startsWith(prefix)) return undefined;
  const hostPart = did.slice(prefix.length);
  if (!hostPart) return undefined;
  if (hostPart.includes('%3A') || hostPart.includes(':')) {
      const decoded = decodeURIComponent(hostPart);
      const colon = decoded.lastIndexOf(':');
      if (colon > 0) {
        const host = decoded.slice(0, colon);
        const port = decoded.slice(colon + 1);
        const scheme = host === 'localhost' || host === '127.0.0.1' ? 'http' : 'https';
        return `${scheme}://${host}:${port}`;
      }
    }
    const scheme = hostPart.startsWith('localhost') || hostPart.startsWith('127.0.0.1') ? 'http' : 'https';
    return `${scheme}://${hostPart}`;
}

export function resolveServerBaseUrl(
  serverDid: string,
  registryLookup: (did: string) => string | undefined,
): string | undefined {
  const raw = registryLookup(serverDid) ?? baseUrlFromServerDid(serverDid);
  return raw ? rewriteLoopbackForPage(raw) : undefined;
}

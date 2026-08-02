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

export function forumPath(serverDid: string, slug: string): string {
  return `/s/${encodeServerDid(serverDid)}/forums/${slug}`;
}

export function threadPath(serverDid: string, postId: string): string {
  return `/s/${encodeServerDid(serverDid)}/posts/${postId}`;
}

export function createForumPath(serverDid: string): string {
  return `/s/${encodeServerDid(serverDid)}/forums/new`;
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
      return `http://${host}:${port}`;
    }
  }
  return `http://${hostPart}`;
}

export function resolveServerBaseUrl(
  serverDid: string,
  registryLookup: (did: string) => string | undefined,
): string | undefined {
  return registryLookup(serverDid) ?? baseUrlFromServerDid(serverDid);
}

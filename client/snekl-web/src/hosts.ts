const AUTH_STORAGE_KEY = 'snekl_auth_url';

export type RuntimeHostConfig = {
  authUrl?: string;
  hubUrl?: string;
  contentUrl?: string;
};

let runtime: RuntimeHostConfig = {};

export function resetRuntimeConfig(): void {
  runtime = {};
}

/** Load Caddy-served /config.json so one SPA image works on any public hostnames. */
export async function loadRuntimeConfig(): Promise<void> {
  if (typeof fetch === 'undefined') return;
  try {
    const response = await fetch('/config.json', { cache: 'no-store' });
    if (!response.ok) return;
    const data = (await response.json()) as RuntimeHostConfig;
    runtime = {
      authUrl: data.authUrl ? normalizeBaseUrl(data.authUrl) : undefined,
      hubUrl: data.hubUrl ? normalizeBaseUrl(data.hubUrl) : undefined,
      contentUrl: data.contentUrl ? normalizeBaseUrl(data.contentUrl) : undefined,
    };
  } catch {
    // Local Vite has no config.json; fall back to VITE_* / localhost.
  }
}

export function normalizeBaseUrl(url: string): string {
  return url.trim().replace(/\/$/, '');
}

/**
 * When the SPA is opened via a LAN host (e.g. http://192.168.0.250:5173) but
 * services were registered as localhost, rewrite loopback hosts to the page host
 * so the browser can reach APIs bound on 0.0.0.0.
 */
export function rewriteLoopbackForPage(url: string, pageHostname?: string): string {
  const normalized = normalizeBaseUrl(url);
  const host =
    pageHostname ??
    (typeof window !== 'undefined' ? window.location.hostname : undefined);
  if (!host || host === 'localhost' || host === '127.0.0.1') return normalized;

  try {
    const parsed = new URL(normalized);
    if (parsed.hostname === 'localhost' || parsed.hostname === '127.0.0.1') {
      parsed.hostname = host;
      return normalizeBaseUrl(parsed.toString());
    }
  } catch {
    // leave malformed URLs alone
  }
  return normalized;
}

/** Rewrite loopback hosts inside JSON `*Url` / `url` fields for the page hostname. */
export function rewriteLoopbackUrlFields<T>(data: T, fieldName?: string, pageHostname?: string): T {
  if (typeof data === 'string') {
    if (fieldName && /(?:^url|url)$/i.test(fieldName)) {
      return rewriteLoopbackForPage(data, pageHostname) as T;
    }
    return data;
  }
  if (Array.isArray(data)) {
    return data.map((item) => rewriteLoopbackUrlFields(item, fieldName, pageHostname)) as T;
  }
  if (data && typeof data === 'object') {
    const next: Record<string, unknown> = {};
    for (const [key, value] of Object.entries(data as Record<string, unknown>)) {
      next[key] = rewriteLoopbackUrlFields(value, key, pageHostname);
    }
    return next as T;
  }
  return data;
}

export function adminUrl(baseUrl: string): string {
  return `${normalizeBaseUrl(baseUrl)}/admin/`;
}

/** First-run convenience only. Production clients remember the Auth URL after login. */
export function defaultAuthFallback(): string {
  return runtime.authUrl
    || normalizeBaseUrl(import.meta.env.VITE_AUTH_URL || 'http://localhost:5100');
}

export function defaultContentFallback(): string {
  return runtime.contentUrl
    || normalizeBaseUrl(import.meta.env.VITE_CONTENT_URL || 'http://localhost:5001');
}

export function defaultHubFallback(): string {
  return runtime.hubUrl
    || normalizeBaseUrl(import.meta.env.VITE_HUB_URL || 'http://localhost:5200');
}

/** Local/dev and explicitly configured clients may attach the site's home origin and hub. */
export function homeSubscriptionsEnabled(): boolean {
  return Boolean(
    import.meta.env.DEV
      || import.meta.env.VITE_CONTENT_URL
      || import.meta.env.VITE_HUB_URL
      || runtime.contentUrl
      || runtime.hubUrl,
  );
}

export function getAuthBaseUrl(): string {
  try {
    const stored = localStorage.getItem(AUTH_STORAGE_KEY);
    if (stored) return rewriteLoopbackForPage(stored);
  } catch {
    // ignore
  }
  return rewriteLoopbackForPage(defaultAuthFallback());
}

export function setAuthBaseUrl(url: string): void {
  localStorage.setItem(AUTH_STORAGE_KEY, normalizeBaseUrl(url));
}

export function federationConfigUrls(baseUrl: string): string[] {
  const root = normalizeBaseUrl(baseUrl);
  return [`${root}/.well-known/snekl-federation.json`, `${root}/api/hub/config`, `${root}/api/hub`];
}

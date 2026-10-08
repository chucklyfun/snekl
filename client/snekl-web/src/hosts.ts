import { Capacitor } from '@capacitor/core';

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

/**
 * Point a loopback iframe at the page host so the frame is same-site with the SPA.
 * Also rewrites localhost to 127.0.0.1 (and the reverse) when the page itself is loopback.
 * API URL rewriting stays in rewriteLoopbackForPage and still leaves those two hosts alone.
 */
export function rewriteFrameUrl(url: string, pageHostname?: string): string {
  const normalized = normalizeBaseUrl(url);
  const host =
    pageHostname ??
    (typeof window !== 'undefined' ? window.location.hostname : undefined);
  if (!host) return normalized;

  try {
    const parsed = new URL(normalized);
    const frameHost = parsed.hostname;
    if (frameHost === host) return normalized;
    if (frameHost !== 'localhost' && frameHost !== '127.0.0.1' && frameHost !== '[::1]') return normalized;
    parsed.hostname = host;
    return normalizeBaseUrl(parsed.toString());
  } catch {
    return normalized;
  }
}

function loopbackHostname(host: string): boolean {
  return host === 'localhost' || host === '127.0.0.1' || host === '[::1]';
}

/**
 * The frame may answer from localhost, 127.0.0.1, or the page's LAN host
 * while the install URL still says localhost. Same scheme and port is the same dev frame.
 */
export function frameOriginMatches(actualOrigin: string, frameUrl: string, pageHostname?: string): boolean {
  const pageHost =
    pageHostname ?? (typeof window !== 'undefined' ? window.location.hostname : '');
  try {
    const actual = new URL(actualOrigin);
    const expected = new URL(frameUrl);
    if (actual.protocol !== expected.protocol || actual.port !== expected.port) return false;
    if (actual.hostname === expected.hostname) return true;
    if (loopbackHostname(expected.hostname) && loopbackHostname(actual.hostname)) return true;
    return Boolean(pageHost) && loopbackHostname(expected.hostname) && actual.hostname === pageHost;
  } catch {
    return false;
  }
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
  return `${normalizeBaseUrl(baseUrl)}/server/`;
}

function isLoopbackBaseUrl(url: string): boolean {
  try {
    const host = new URL(url).hostname;
    return host === 'localhost' || host === '127.0.0.1' || host === '[::1]';
  } catch {
    return false;
  }
}

/** First-run convenience only. Production clients remember the Auth URL after login. */
export function defaultAuthFallback(native = Capacitor.isNativePlatform()): string {
  if (runtime.authUrl) return runtime.authUrl;
  const configured = normalizeBaseUrl(import.meta.env.VITE_AUTH_URL || 'http://localhost:5100');
  // On a phone, localhost is the device. Leave the login field empty until a real Auth URL is entered.
  if (native && isLoopbackBaseUrl(configured)) return '';
  return configured;
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
export function homeSubscriptionsEnabled(native = Capacitor.isNativePlatform()): boolean {
  const enabled = Boolean(
    import.meta.env.DEV
      || import.meta.env.VITE_CONTENT_URL
      || import.meta.env.VITE_HUB_URL
      || runtime.contentUrl
      || runtime.hubUrl,
  );
  if (!enabled || !native) return enabled;
  return [defaultContentFallback(), defaultHubFallback()].some((url) => !isLoopbackBaseUrl(url));
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

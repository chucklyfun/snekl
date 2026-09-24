import { describe, expect, it } from 'vitest';
import {
  defaultAuthFallback,
  defaultContentFallback,
  defaultHubFallback,
  loadRuntimeConfig,
  resetRuntimeConfig,
  rewriteLoopbackForPage,
  rewriteLoopbackUrlFields,
} from './hosts';

describe('rewriteLoopbackForPage', () => {
  it('leaves URLs alone when the page is already on localhost', () => {
    expect(rewriteLoopbackForPage('http://localhost:5002', 'localhost')).toBe('http://localhost:5002');
    expect(rewriteLoopbackForPage('http://localhost:5002', '127.0.0.1')).toBe('http://localhost:5002');
  });

  it('rewrites localhost service URLs to the LAN page host', () => {
    expect(rewriteLoopbackForPage('http://localhost:5002', '192.168.0.250')).toBe(
      'http://192.168.0.250:5002',
    );
    expect(rewriteLoopbackForPage('http://127.0.0.1:5200/', '192.168.0.250')).toBe(
      'http://192.168.0.250:5200',
    );
  });

  it('does not rewrite non-loopback hosts', () => {
    expect(rewriteLoopbackForPage('http://alpha.example:5001', '192.168.0.250')).toBe(
      'http://alpha.example:5001',
    );
  });
});

describe('rewriteLoopbackUrlFields', () => {
  it('rewrites nested imageUrl fields for a LAN page host', () => {
    expect(
      rewriteLoopbackUrlFields(
        {
          imageUrl: 'http://localhost:5001/api/media/abc',
          message: 'see http://localhost:5001 in the post',
          reactions: [{ imageUrl: 'http://127.0.0.1:5001/api/media/def' }],
        },
        undefined,
        '192.168.0.250',
      ),
    ).toEqual({
      imageUrl: 'http://192.168.0.250:5001/api/media/abc',
      message: 'see http://localhost:5001 in the post',
      reactions: [{ imageUrl: 'http://192.168.0.250:5001/api/media/def' }],
    });
  });
});

describe('default service URLs', () => {
  it('falls back to local Auth, content, and hub ports', () => {
    resetRuntimeConfig();
    expect(defaultAuthFallback()).toBe('http://localhost:5100');
    expect(defaultContentFallback()).toBe('http://localhost:5001');
    expect(defaultHubFallback()).toBe('http://localhost:5200');
  });

  it('prefers /config.json when the edge proxy serves it', async () => {
    const original = globalThis.fetch;
    globalThis.fetch = (async () =>
      new Response(
        JSON.stringify({
          authUrl: 'https://auth.example.com/',
          hubUrl: 'https://hub.example.com',
          contentUrl: 'https://origin.example.com',
        }),
        { status: 200 },
      )) as typeof fetch;
    try {
      await loadRuntimeConfig();
      expect(defaultAuthFallback()).toBe('https://auth.example.com');
      expect(defaultHubFallback()).toBe('https://hub.example.com');
      expect(defaultContentFallback()).toBe('https://origin.example.com');
    } finally {
      globalThis.fetch = original;
      resetRuntimeConfig();
    }
  });
});

import { describe, expect, it } from 'vitest';
import {
  defaultAuthFallback,
  defaultContentFallback,
  defaultHubFallback,
  homeSubscriptionsEnabled,
  loadRuntimeConfig,
  resetRuntimeConfig,
  frameOriginMatches,
  rewriteFrameUrl,
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

describe('rewriteFrameUrl', () => {
  it('keeps a loopback frame when the page host already matches', () => {
    expect(rewriteFrameUrl('http://localhost:5301', 'localhost')).toBe('http://localhost:5301');
  });

  it('aligns localhost and 127.0.0.1 so the frame matches the page', () => {
    expect(rewriteFrameUrl('http://localhost:5301/', '127.0.0.1')).toBe('http://127.0.0.1:5301');
    expect(rewriteFrameUrl('http://127.0.0.1:5301', 'localhost')).toBe('http://localhost:5301');
  });

  it('rewrites a loopback frame to the LAN page host', () => {
    expect(rewriteFrameUrl('http://localhost:5301', '192.168.0.250')).toBe('http://192.168.0.250:5301');
  });

  it('leaves a public frame host alone', () => {
    expect(rewriteFrameUrl('https://commissions.example', '192.168.0.250')).toBe(
      'https://commissions.example',
    );
  });
});

describe('frameOriginMatches', () => {
  it('accepts the install host, a loopback alias, or the LAN page host on the same port', () => {
    expect(frameOriginMatches('http://localhost:5301', 'http://localhost:5301', 'localhost')).toBe(true);
    expect(frameOriginMatches('http://127.0.0.1:5301', 'http://localhost:5301', 'localhost')).toBe(true);
    expect(frameOriginMatches('http://192.168.0.250:5301', 'http://localhost:5301', '192.168.0.250')).toBe(
      true,
    );
  });

  it('rejects a different port or an unrelated host', () => {
    expect(frameOriginMatches('http://localhost:5300', 'http://localhost:5301', 'localhost')).toBe(false);
    expect(frameOriginMatches('http://evil.example:5301', 'http://localhost:5301', '192.168.0.250')).toBe(
      false,
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

  it('ignores loopback defaults in the Android shell', () => {
    resetRuntimeConfig();
    expect(defaultAuthFallback(true)).toBe('');
    expect(homeSubscriptionsEnabled(true)).toBe(false);
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
      expect(defaultAuthFallback(true)).toBe('https://auth.example.com');
      expect(defaultHubFallback()).toBe('https://hub.example.com');
      expect(defaultContentFallback()).toBe('https://origin.example.com');
      expect(homeSubscriptionsEnabled(true)).toBe(true);
    } finally {
      globalThis.fetch = original;
      resetRuntimeConfig();
    }
  });
});

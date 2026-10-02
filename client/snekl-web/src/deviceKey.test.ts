import { describe, expect, it } from 'vitest';
import { normalizeCoordinate, thumbprintPublicJwk, tokenIsDeviceBound } from './deviceKey';

describe('device key binding', () => {
  it('normalizes base64url coordinates', () => {
    expect(normalizeCoordinate('AAAA')).toBe('AAAA');
  });

  it('thumbprints the canonical P-256 fields', async () => {
    const x = 'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA';
    const y = 'BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB';
    const first = await thumbprintPublicJwk({ kty: 'EC', crv: 'P-256', x, y });
    const second = await thumbprintPublicJwk({ kty: 'EC', crv: 'P-256', x: `${x}=`, y });
    expect(first).toBe(second);
    expect(first.length).toBeGreaterThan(20);
  });

  it('treats a token without a thumbprint as unbound', () => {
    const payload = btoa(JSON.stringify({ sub: '1' })).replaceAll('+', '-').replaceAll('/', '_').replace(/=+$/g, '');
    expect(tokenIsDeviceBound(`aaa.${payload}.bbb`)).toBe(false);
    const bound = btoa(JSON.stringify({ cnf_jkt: 'thumb' })).replaceAll('+', '-').replaceAll('/', '_').replace(/=+$/g, '');
    expect(tokenIsDeviceBound(`aaa.${bound}.bbb`)).toBe(true);
  });
});

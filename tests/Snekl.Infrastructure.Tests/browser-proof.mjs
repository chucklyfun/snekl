import { webcrypto } from 'node:crypto';

const crypto = webcrypto;

function b64(bytes) {
  const view = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes);
  let binary = '';
  for (const byte of view) binary += String.fromCharCode(byte);
  return btoa(binary).replaceAll('+', '-').replaceAll('/', '_').replace(/=+$/g, '');
}

const pair = await crypto.subtle.generateKey({ name: 'ECDSA', namedCurve: 'P-256' }, false, ['sign']);
const exported = await crypto.subtle.exportKey('jwk', pair.publicKey);
const publicJwk = { kty: 'EC', crv: 'P-256', x: exported.x, y: exported.y };
const token = 'access-token';
const url = 'http://localhost/api/me';
const header = b64(new TextEncoder().encode(JSON.stringify({ typ: 'dpop+jwt', alg: 'ES256', jwk: publicJwk })));
const ath = b64(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(token)));
const payload = b64(new TextEncoder().encode(JSON.stringify({
  htm: 'GET',
  htu: url,
  iat: Math.floor(Date.now() / 1000),
  jti: '0123456789abcdef0123456789abcdef',
  ath,
})));
const signingInput = `${header}.${payload}`;
const signature = await crypto.subtle.sign(
  { name: 'ECDSA', hash: 'SHA-256' },
  pair.privateKey,
  new TextEncoder().encode(signingInput),
);
process.stdout.write(JSON.stringify({
  proof: `${signingInput}.${b64(signature)}`,
  x: publicJwk.x,
  y: publicJwk.y,
  token,
  url,
}));

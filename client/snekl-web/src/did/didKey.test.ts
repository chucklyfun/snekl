import { describe, expect, it } from 'vitest';
import {
  buildRegisterChallenge,
  createDidFromPublicKey,
  decodePrivateKey,
  encodePrivateKey,
  generateDidKey,
  signMultibaseProof,
} from './didKey';

describe('didKey', () => {
  it('generates a did:key and round-trips the private key encoding', async () => {
    const material = await generateDidKey();
    expect(material.did.startsWith('did:key:z')).toBe(true);
    expect(material.publicKey).toHaveLength(32);
    expect(material.privateKey).toHaveLength(32);
    expect(createDidFromPublicKey(material.publicKey)).toBe(material.did);

    const encoded = encodePrivateKey(material.privateKey);
    expect(decodePrivateKey(encoded)).toEqual(material.privateKey);
  });

  it('builds the register challenge used by the auth API', () => {
    expect(buildRegisterChallenge('Alice', 'did:key:zabc')).toBe(
      'snekl-register:alice:did:key:zabc',
    );
  });

  it('signs a multibase ownership proof', async () => {
    const material = await generateDidKey();
    const challenge = buildRegisterChallenge('bob', material.did);
    const proof = await signMultibaseProof(material.privateKey, challenge);
    expect(proof.startsWith('z')).toBe(true);
    expect(proof.length).toBeGreaterThan(10);
  });
});

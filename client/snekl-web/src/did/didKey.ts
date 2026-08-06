import bs58 from 'bs58';
import * as ed from '@noble/ed25519';

const ED25519_MULTICODEC_PREFIX = 0xed;

export interface DidKeyMaterial {
  did: string;
  publicKey: Uint8Array;
  privateKey: Uint8Array;
}

export async function generateDidKey(): Promise<DidKeyMaterial> {
  const privateKey = ed.utils.randomSecretKey();
  const publicKey = await ed.getPublicKeyAsync(privateKey);
  return {
    did: createDidFromPublicKey(publicKey),
    publicKey,
    privateKey,
  };
}

export function createDidFromPublicKey(publicKey: Uint8Array): string {
  const multicodec = new Uint8Array(1 + publicKey.length);
  multicodec[0] = ED25519_MULTICODEC_PREFIX;
  multicodec.set(publicKey, 1);
  return `did:key:z${bs58.encode(multicodec)}`;
}

export function buildRegisterChallenge(username: string, did: string): string {
  return `snekl-register:${username.trim().toLowerCase()}:${did}`;
}

export async function signMultibaseProof(privateKey: Uint8Array, payload: string): Promise<string> {
  const signature = await ed.signAsync(new TextEncoder().encode(payload), privateKey);
  return `z${bs58.encode(signature)}`;
}

export function encodePrivateKey(privateKey: Uint8Array): string {
  return btoa(String.fromCharCode(...privateKey));
}

export function decodePrivateKey(encoded: string): Uint8Array {
  const binary = atob(encoded);
  return Uint8Array.from(binary, (c) => c.charCodeAt(0));
}

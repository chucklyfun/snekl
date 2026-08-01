import * as ed from '@noble/ed25519';
import { createDidFromPublicKey, decodePrivateKey, encodePrivateKey, type DidKeyMaterial } from './didKey';

const DID_STORAGE_KEY = 'snekl_did';
const PRIVATE_KEY_STORAGE_KEY = 'snekl_did_private_key';

export function getStoredDid(): string | null {
  return localStorage.getItem(DID_STORAGE_KEY);
}

export async function getStoredKeyMaterial(): Promise<DidKeyMaterial | null> {
  const did = getStoredDid();
  const encodedPrivateKey = localStorage.getItem(PRIVATE_KEY_STORAGE_KEY);
  if (!did || !encodedPrivateKey) return null;

  const privateKey = decodePrivateKey(encodedPrivateKey);
  const publicKey = await ed.getPublicKeyAsync(privateKey);
  if (createDidFromPublicKey(publicKey) !== did) return null;
  return { did, privateKey, publicKey };
}

export async function getOrCreateDidKey(): Promise<DidKeyMaterial> {
  const existing = await getStoredKeyMaterial();
  if (existing) return existing;

  const privateKey = ed.utils.randomSecretKey();
  const publicKey = await ed.getPublicKeyAsync(privateKey);
  const did = createDidFromPublicKey(publicKey);
  localStorage.setItem(DID_STORAGE_KEY, did);
  localStorage.setItem(PRIVATE_KEY_STORAGE_KEY, encodePrivateKey(privateKey));
  return { did, privateKey, publicKey };
}

export function clearDidKey(): void {
  localStorage.removeItem(DID_STORAGE_KEY);
  localStorage.removeItem(PRIVATE_KEY_STORAGE_KEY);
}

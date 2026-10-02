const DB_NAME = 'snekl-device';
const STORE = 'keys';
const KEY_ID = 'member';

export type DevicePublicJwk = {
  kty: 'EC';
  crv: 'P-256';
  x: string;
  y: string;
};

type StoredKey = {
  privateKey: CryptoKey;
  publicJwk: DevicePublicJwk;
};

function base64Url(bytes: ArrayBuffer | Uint8Array): string {
  const view = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes);
  let binary = '';
  for (const byte of view) binary += String.fromCharCode(byte);
  return btoa(binary).replaceAll('+', '-').replaceAll('/', '_').replace(/=+$/g, '');
}

function base64UrlDecode(value: string): Uint8Array {
  const padded = value.replaceAll('-', '+').replaceAll('_', '/') + '='.repeat((4 - (value.length % 4)) % 4);
  const binary = atob(padded);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i += 1) bytes[i] = binary.charCodeAt(i);
  return bytes;
}

export function normalizeCoordinate(value: string): string {
  return base64Url(base64UrlDecode(value));
}

export async function thumbprintPublicJwk(jwk: DevicePublicJwk): Promise<string> {
  const x = normalizeCoordinate(jwk.x);
  const y = normalizeCoordinate(jwk.y);
  const canonical = `{"crv":"P-256","kty":"EC","x":"${x}","y":"${y}"}`;
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(canonical));
  return base64Url(digest);
}

export async function accessTokenHash(accessToken: string): Promise<string> {
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(accessToken));
  return base64Url(digest);
}

function openDb(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(DB_NAME, 1);
    request.onupgradeneeded = () => {
      const db = request.result;
      if (!db.objectStoreNames.contains(STORE)) db.createObjectStore(STORE);
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error ?? new Error('Could not open the device key database.'));
  });
}

async function readStored(db: IDBDatabase): Promise<StoredKey | null> {
  return new Promise((resolve, reject) => {
    const tx = db.transaction(STORE, 'readonly');
    const request = tx.objectStore(STORE).get(KEY_ID);
    request.onsuccess = () => resolve((request.result as StoredKey | undefined) ?? null);
    request.onerror = () => reject(request.error ?? new Error('Could not read the device key.'));
  });
}

async function writeStored(db: IDBDatabase, value: StoredKey): Promise<void> {
  return new Promise((resolve, reject) => {
    const tx = db.transaction(STORE, 'readwrite');
    tx.objectStore(STORE).put(value, KEY_ID);
    tx.oncomplete = () => resolve();
    tx.onerror = () => reject(tx.error ?? new Error('Could not store the device key.'));
  });
}

async function getOrCreateDeviceKey(): Promise<StoredKey> {
  const db = await openDb();
  try {
    const existing = await readStored(db);
    if (existing?.privateKey && existing.publicJwk) return existing;

    const pair = await crypto.subtle.generateKey({ name: 'ECDSA', namedCurve: 'P-256' }, false, ['sign']);
    const exported = (await crypto.subtle.exportKey('jwk', pair.publicKey)) as JsonWebKey;
    if (!exported.x || !exported.y) throw new Error('This browser did not return a device public key.');
    const publicJwk: DevicePublicJwk = {
      kty: 'EC',
      crv: 'P-256',
      x: normalizeCoordinate(exported.x),
      y: normalizeCoordinate(exported.y),
    };
    const stored = { privateKey: pair.privateKey, publicJwk };
    await writeStored(db, stored);
    return stored;
  } finally {
    db.close();
  }
}

export async function getDevicePublicJwk(): Promise<DevicePublicJwk> {
  const stored = await getOrCreateDeviceKey();
  return stored.publicJwk;
}

export function tokenIsDeviceBound(token: string): boolean {
  const payload = token.split('.')[1];
  if (!payload) return false;
  try {
    const json = JSON.parse(new TextDecoder().decode(base64UrlDecode(payload))) as { cnf_jkt?: unknown };
    return typeof json.cnf_jkt === 'string' && json.cnf_jkt.length > 0;
  } catch {
    return false;
  }
}

export function readBoundToken(storageKey: string): string | null {
  const token = localStorage.getItem(storageKey);
  if (!token) return null;
  if (tokenIsDeviceBound(token)) return token;
  localStorage.removeItem(storageKey);
  return null;
}

export async function createDeviceProof(method: string, url: string, accessToken: string): Promise<string> {
  const { privateKey, publicJwk } = await getOrCreateDeviceKey();
  const header = base64Url(new TextEncoder().encode(JSON.stringify({
    typ: 'dpop+jwt',
    alg: 'ES256',
    jwk: publicJwk,
  })));
  const payload = base64Url(new TextEncoder().encode(JSON.stringify({
    htm: method.toUpperCase(),
    htu: url,
    iat: Math.floor(Date.now() / 1000),
    jti: crypto.randomUUID().replaceAll('-', ''),
    ath: await accessTokenHash(accessToken),
  })));
  const signingInput = `${header}.${payload}`;
  const signature = await crypto.subtle.sign({ name: 'ECDSA', hash: 'SHA-256' }, privateKey, new TextEncoder().encode(signingInput));
  return `${signingInput}.${base64Url(signature)}`;
}

export async function applyMemberProof(
  headers: Record<string, string>,
  method: string,
  url: string,
  accessToken: string,
): Promise<void> {
  headers.Authorization = `DPoP ${accessToken}`;
  headers.DPoP = await createDeviceProof(method, url, accessToken);
}

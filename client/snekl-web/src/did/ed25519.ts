import * as ed from '@noble/ed25519';
import { sha512 } from '@noble/hashes/sha2.js';

// WebCrypto's subtle API exists only in secure contexts (https, localhost).
// SHA-512 from @noble/hashes keeps did:key generation working on LAN HTTP origins.
ed.hashes.sha512 = sha512;
ed.hashes.sha512Async = (message) => Promise.resolve(sha512(message));

export { ed };

import { createDeviceProof, readBoundToken } from './deviceKey';

function readServerId(canonical: string): string | null {
  const keys = [canonical];
  try {
    const url = new URL(canonical);
    if (url.hostname === 'localhost' || url.hostname === '127.0.0.1') {
      url.hostname = url.hostname === 'localhost' ? '127.0.0.1' : 'localhost';
      keys.push(url.toString().replace(/\/$/, ''));
    }
  } catch {
    // The content URL is still sent; the server id is only a hint.
  }
  try {
    for (const key of keys) {
      const value = sessionStorage.getItem(`snekl-server:${key}`);
      if (value) return value;
    }
  } catch {
    return null;
  }
  return null;
}

export async function postCommissionsSession(
  target: Window,
  iframeOrigin: string,
  contentBaseUrl: string,
  serverId?: string | null,
) {
  const token = readBoundToken('snekl_token');
  if (!token) return;
  const canonical = contentBaseUrl.replace(/\/$/, '');
  const meUrl = `${canonical}/api/auth/me`;
  const rolesUrl = `${canonical}/api/auth/me/forum-roles`;
  const dpop = await createDeviceProof('GET', meUrl, token);
  const rolesDpop = await createDeviceProof('GET', rolesUrl, token);
  const resolvedServerId = serverId || readServerId(canonical);
  target.postMessage(
    {
      type: 'snekl-commissions-session',
      contentBaseUrl: canonical,
      meUrl,
      accessToken: token,
      dpop,
      serverId: resolvedServerId,
      rolesUrl,
      rolesDpop,
    },
    iframeOrigin,
  );
}

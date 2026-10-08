import { normalizeBaseUrl } from './hosts';

export type AuthServerOption = {
  name: string;
  issuerUrl: string;
};

export function uniqueAuthServers(issuers: AuthServerOption[]): AuthServerOption[] {
  const seen = new Set<string>();
  const options: AuthServerOption[] = [];
  for (const issuer of issuers) {
    const issuerUrl = normalizeBaseUrl(issuer.issuerUrl);
    if (!issuerUrl || seen.has(issuerUrl)) continue;
    seen.add(issuerUrl);
    options.push({ name: issuer.name.trim() || issuerUrl, issuerUrl });
  }
  return options;
}

/** Keep a remembered URL only when it is one of the published auth servers. */
export function pickAuthServer(issuers: AuthServerOption[], stored: string): string {
  const current = normalizeBaseUrl(stored);
  const match = issuers.find((issuer) => issuer.issuerUrl === current);
  return match?.issuerUrl ?? issuers[0]?.issuerUrl ?? '';
}

export function authServerLabel(issuer: AuthServerOption): string {
  try {
    const host = new URL(issuer.issuerUrl).host;
    if (!issuer.name || issuer.name === host) return issuer.name || host;
    return `${issuer.name} (${host})`;
  } catch {
    return issuer.name || issuer.issuerUrl;
  }
}

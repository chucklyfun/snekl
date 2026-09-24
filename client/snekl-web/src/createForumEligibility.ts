import { createContentClient } from './api';
import {
  canCreateForumFromServerRoles,
  canRequestCreateForumFromServerRoles,
  rolesThatGrantCreateForum,
  rolesThatGrantRequestCreateForum,
} from './createForumAccess';
import { resolveServerBaseUrl } from './serverDid';
import type { ContentServerMembership } from './types';

export type EligibleServer = ContentServerMembership & {
  canCreateTopLevel: boolean;
  canRequestTopLevel: boolean;
  createForumRoles: string[];
  checkError?: string;
};

export function sortEligibleServers(servers: EligibleServer[]): EligibleServer[] {
  return [...servers].sort((a, b) => {
    const rank = (status: string) => (status === 'favorite' ? 0 : status === 'active' ? 1 : 2);
    const byStatus = rank(a.status) - rank(b.status);
    if (byStatus !== 0) return byStatus;
    return a.name.localeCompare(b.name);
  });
}

export async function loadServerEligibility(server: ContentServerMembership): Promise<EligibleServer> {
  try {
    const info = await createContentClient(server.baseUrl).getServerPermissions();
    const createRoles = rolesThatGrantCreateForum(info.roles);
    const requestRoles = rolesThatGrantRequestCreateForum(info.roles);
    const canCreate = canCreateForumFromServerRoles(info);
    const labels = canCreate
      ? createRoles.length > 0
        ? createRoles.map((role) => role.name)
        : info.isSuperUser
          ? ['Super User']
          : []
      : requestRoles.map((role) => role.name);
    return {
      ...server,
      canCreateTopLevel: canCreate,
      canRequestTopLevel: canRequestCreateForumFromServerRoles(info),
      createForumRoles: labels,
    };
  } catch (err) {
    return {
      ...server,
      canCreateTopLevel: false,
      canRequestTopLevel: false,
      createForumRoles: [],
      checkError: err instanceof Error ? err.message : 'Could not check permissions',
    };
  }
}

export function membershipForDid(
  serverDid: string,
  servers: ContentServerMembership[],
  activeServers: ContentServerMembership[],
  resolveBaseUrl: (did: string) => string | undefined,
): ContentServerMembership | null {
  const known = servers.find((s) => s.serverDid === serverDid) ?? activeServers.find((s) => s.serverDid === serverDid);
  const resolvedBase = resolveServerBaseUrl(serverDid, resolveBaseUrl);
  if (!resolvedBase) return null;
  return (
    known ?? {
      serverDid,
      baseUrl: resolvedBase,
      name: 'Content server',
      status: 'active',
      subscribedAt: '',
    }
  );
}

export function slugifyForumName(value: string) {
  return value
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-|-$/g, '');
}

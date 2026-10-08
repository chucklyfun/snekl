import { hasPermission } from './api';
import type { ServerPermissionsInfo, ServerRole } from './types';

export function rolesThatGrantCreateForum(roles: ServerRole[] | undefined): ServerRole[] {
  return (roles ?? []).filter((role) => hasPermission(role.permissions, 'CreateForum'));
}

export function rolesThatGrantRequestCreateForum(roles: ServerRole[] | undefined): ServerRole[] {
  return (roles ?? []).filter((role) => hasPermission(role.permissions, 'RequestCreateForum'));
}

/** Super User is origin god-mode for publishing. Otherwise CreateForum comes from assigned server roles. */
export function canCreateForumFromServerRoles(info: ServerPermissionsInfo): boolean {
  if (info.isSuperUser) return true;
  if (info.roles && info.roles.length > 0) {
    return rolesThatGrantCreateForum(info.roles).length > 0;
  }
  return hasPermission(info.permissions, 'CreateForum');
}

export function canRequestCreateForumFromServerRoles(info: ServerPermissionsInfo): boolean {
  if (canCreateForumFromServerRoles(info)) return false;
  if (info.roles && info.roles.length > 0) {
    return rolesThatGrantRequestCreateForum(info.roles).length > 0;
  }
  return hasPermission(info.permissions, 'RequestCreateForum');
}

export function canOpenCreateForumPage(info: ServerPermissionsInfo): boolean {
  return canCreateForumFromServerRoles(info) || canRequestCreateForumFromServerRoles(info);
}

export function isForumPending(forum: { approvalStatus?: string } | null | undefined): boolean {
  return forum?.approvalStatus === 'pending';
}

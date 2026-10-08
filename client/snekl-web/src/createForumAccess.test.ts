import { describe, expect, it } from 'vitest';
import {
  canCreateForumFromServerRoles,
  canOpenCreateForumPage,
  canRequestCreateForumFromServerRoles,
  rolesThatGrantCreateForum,
} from './createForumAccess';
import type { ServerRole } from './types';

function role(name: string, permissions: string[], isEveryone = false): ServerRole {
  return { id: name, name, permissions, position: 0, isEveryone };
}

describe('canCreateForumFromServerRoles', () => {
  it('allows Super User on that origin even without a CreateForum role', () => {
    expect(
      canCreateForumFromServerRoles({
        isSuperUser: true,
        permissions: ['ViewServer'],
        roles: [role('@everyone', ['ViewServer'], true)],
      }),
    ).toBe(true);
  });

  it('uses assigned server roles when the user is not a Super User', () => {
    expect(
      canCreateForumFromServerRoles({
        isSuperUser: false,
        permissions: ['ViewServer'],
        roles: [role('@everyone', ['ViewServer'], true)],
      }),
    ).toBe(false);

    expect(
      canCreateForumFromServerRoles({
        isSuperUser: false,
        permissions: ['ViewServer', 'CreateForum'],
        roles: [role('@everyone', ['ViewServer'], true), role('Member', ['ViewServer', 'CreateForum'])],
      }),
    ).toBe(true);
  });

  it('falls back to merged permissions when roles are omitted', () => {
    expect(canCreateForumFromServerRoles({ isSuperUser: false, permissions: ['CreateForum'] })).toBe(true);
    expect(canCreateForumFromServerRoles({ isSuperUser: false, permissions: ['ViewServer'] })).toBe(false);
    expect(canCreateForumFromServerRoles({ isSuperUser: true, permissions: ['ViewServer'] })).toBe(true);
  });
});

describe('canRequestCreateForumFromServerRoles', () => {
  it('is true only when the user can request but not publish', () => {
    expect(
      canRequestCreateForumFromServerRoles({
        isSuperUser: false,
        permissions: ['ViewServer', 'RequestCreateForum'],
        roles: [
          role('@everyone', ['ViewServer'], true),
          role('Nest requester', ['ViewServer', 'RequestCreateForum']),
        ],
      }),
    ).toBe(true);

    expect(
      canRequestCreateForumFromServerRoles({
        isSuperUser: false,
        permissions: ['ViewServer', 'CreateForum', 'RequestCreateForum'],
        roles: [role('Member', ['ViewServer', 'CreateForum', 'RequestCreateForum'])],
      }),
    ).toBe(false);
  });
});

describe('canOpenCreateForumPage', () => {
  it('allows either publish or request roles', () => {
    expect(
      canOpenCreateForumPage({
        isSuperUser: false,
        permissions: ['RequestCreateForum'],
        roles: [role('Nest requester', ['RequestCreateForum'])],
      }),
    ).toBe(true);
  });
});

describe('rolesThatGrantCreateForum', () => {
  it('returns the role names that include CreateForum', () => {
    const granted = rolesThatGrantCreateForum([
      role('@everyone', ['ViewServer'], true),
      role('Member', ['ViewServer', 'CreateForum']),
    ]);
    expect(granted.map((r) => r.name)).toEqual(['Member']);
  });
});

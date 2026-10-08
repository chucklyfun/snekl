import { describe, expect, it } from 'vitest';
import {
  FORUM_ROLE_STARTER_PACKS,
  nextForumRolePosition,
  starterPackPermissions,
} from './forumRoles';

describe('nest role starter packs', () => {
  it('gives new roles a member pack without management permissions', () => {
    const perms = starterPackPermissions('member');
        expect(perms).toEqual(['SeeForum', 'ViewForum', 'SeeContentBlocks', 'CreateThread', 'Reply']);
    expect(perms).not.toContain('ManagePosts');
    expect(perms).not.toContain('ForumAdministrator');
    expect(perms).not.toContain('RequestDirectMessage');
  });

  it('matches the seeded Moderator default for the moderator pack', () => {
    expect(starterPackPermissions('moderator')).toEqual(
      FORUM_ROLE_STARTER_PACKS.find((pack) => pack.id === 'moderator')?.permissions,
    );
    expect(starterPackPermissions('moderator')).toContain('ViewHistory');
    expect(starterPackPermissions('moderator')).toContain('ManageMembers');
    expect(starterPackPermissions('moderator')).toContain('RequestDirectMessage');
    expect(starterPackPermissions('moderator')).not.toContain('ManageRoles');
    expect(starterPackPermissions('moderator')).not.toContain('ViewNsfwMedia');
    expect(starterPackPermissions('moderator')).not.toContain('MarkNsfwMedia');
  });
});

describe('nextForumRolePosition', () => {
  it('places a new role after seeded Everyone, Moderator, and Owner', () => {
    expect(
      nextForumRolePosition([
        { position: 0 },
        { position: 50 },
        { position: 100 },
      ]),
    ).toBe(101);
  });
});

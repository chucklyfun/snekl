export const FORUM_PERMISSION_OPTIONS = [
  'SeeForum',
  'ViewForum',
  'SeeContentBlocks',
  'CreateThread',
  'Reply',
  'RequestDirectMessage',
  'EditOwnPost',
  'DeleteOwnPost',
  'ManagePosts',
  'ViewHistory',
  'ManageMembers',
  'ManageRoles',
  'ManageForum',
  'ManageContentBlocks',
  'ViewNsfwMedia',
  'MarkNsfwMedia',
  'UseOtherForumEmotes',
  'UseOtherServerEmotes',
  'ForumAdministrator',
] as const;

export const DEFAULT_FORUM_ROLE_COLOR = '#43b581';

export type ForumRoleStarterPackId = 'member' | 'viewer' | 'moderator';

export type ForumRoleStarterPack = {
  id: ForumRoleStarterPackId;
  label: string;
  description: string;
  permissions: readonly string[];
};

export const FORUM_ROLE_STARTER_PACKS: readonly ForumRoleStarterPack[] = [
  {
    id: 'member',
    label: 'Member',
    description: 'See, view, start threads, and reply',
    permissions: ['SeeForum', 'ViewForum', 'SeeContentBlocks', 'CreateThread', 'Reply'],
  },
  {
    id: 'viewer',
    label: 'Viewer',
    description: 'See and read the nest',
    permissions: ['SeeForum', 'ViewForum', 'SeeContentBlocks'],
  },
  {
    id: 'moderator',
    label: 'Moderator',
    description: 'Member plus manage posts and members',
    permissions: [
      'SeeForum',
      'ViewForum',
      'SeeContentBlocks',
      'CreateThread',
      'Reply',
      'RequestDirectMessage',
      'EditOwnPost',
      'DeleteOwnPost',
      'ManagePosts',
      'ViewHistory',
      'ManageMembers',
      'ManageContentBlocks',
      'UseOtherForumEmotes',
    ],
  },
];

export function starterPackPermissions(id: ForumRoleStarterPackId): string[] {
  const pack = FORUM_ROLE_STARTER_PACKS.find((item) => item.id === id) ?? FORUM_ROLE_STARTER_PACKS[0];
  return [...pack.permissions];
}

export function nextForumRolePosition(roles: { position: number }[]): number {
  return roles.reduce((max, role) => Math.max(max, role.position), 0) + 1;
}

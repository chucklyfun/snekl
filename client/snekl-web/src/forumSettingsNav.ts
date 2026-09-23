export type ForumSettingsSectionId =
  | 'general'
  | 'emotes'
  | 'structure'
  | 'roles'
  | 'members'
  | 'moderation'
  | 'you'
  | 'ownership'
  | 'history';

export const FORUM_SETTINGS_SECTIONS: { id: ForumSettingsSectionId; label: string }[] = [
  { id: 'general', label: 'General' },
  { id: 'emotes', label: 'Emotes' },
  { id: 'structure', label: 'Forums & rooms' },
  { id: 'roles', label: 'Roles' },
  { id: 'members', label: 'Members' },
  { id: 'moderation', label: 'Reports' },
  { id: 'you', label: 'Your identity' },
  { id: 'ownership', label: 'Ownership' },
  { id: 'history', label: 'History' },
];

function isForumSettingsSectionId(value: string): value is ForumSettingsSectionId {
  return FORUM_SETTINGS_SECTIONS.some((section) => section.id === value);
}

export function resolveForumSettingsSection(
  hash: string,
  available: ForumSettingsSectionId[],
): ForumSettingsSectionId | null {
  if (available.length === 0) return null;
  const raw = hash.replace(/^#/, '');
  if (isForumSettingsSectionId(raw) && available.includes(raw)) return raw;
  return available[0] ?? null;
}

import { useEffect, useState } from 'react';
import type { CatalogEmote } from '../emotes';
import type { ForumFlairTemplate } from '../types';
import { forumFlairLabel, forumFlairSelectLabel } from './ForumFlairRow';
import { InlineEditRow } from './InlineEditRow';

export type MemberRoleDraft = {
  username: string;
  roleIds: string[];
  flairId: string | null;
};

type AssignableRole = {
  id: string;
  name: string;
  color?: string | null;
  isEveryone?: boolean;
};

type MemberLike = {
  userId: string;
  username: string;
  displayName: string;
  alias?: string | null;
  roles: { id: string; name: string }[];
  flair?: ForumFlairTemplate | null;
};

export function MemberRoleRow({
  member,
  creating = false,
  roles,
  flairs = [],
  emotes = [],
  onSave,
  onCancel,
}: {
  member?: MemberLike;
  creating?: boolean;
  roles: AssignableRole[];
  flairs?: ForumFlairTemplate[];
  emotes?: CatalogEmote[];
  onSave: (draft: MemberRoleDraft, existing?: MemberLike) => Promise<void>;
  onCancel?: () => void;
}) {
  const assignable = roles.filter((role) => !role.isEveryone);
  const [username, setUsername] = useState(member?.username ?? '');
  const [roleIds, setRoleIds] = useState(member?.roles.map((role) => role.id) ?? []);
  const [flairId, setFlairId] = useState(member?.flair?.id ?? '');
  const fieldId = member?.userId ?? 'new';

  useEffect(() => {
    if (creating || !member) return;
    setUsername(member.username);
    setRoleIds(member.roles.map((role) => role.id));
    setFlairId(member.flair?.id ?? '');
  }, [creating, member]);

  const label = member ? (member.alias ?? member.displayName) : '';

  return (
    <InlineEditRow
      as="div"
      className="server-row member-role-row"
      creating={creating}
      canEdit
      includeRemove={false}
      preface={
        member ? (
          <div>
            <strong>{label}</strong>
            <div className="card-meta">@{member.username}</div>
          </div>
        ) : null
      }
      view={
        member ? (
          <div>
            <strong>{label}</strong>
            <div className="card-meta">
              @{member.username} · {member.roles.map((role) => role.name).join(', ') || 'no roles'}
              {member.flair ? ` · ${forumFlairLabel(member.flair)}` : ''}
            </div>
          </div>
        ) : null
      }
      fieldsClassName="member-role-bar"
      afterFields={
        <div className="form-group">
          <label>Roles</label>
          {assignable.length === 0 ? (
            <p className="card-meta">No assignable roles yet.</p>
          ) : (
            <div className="member-role-picks">
              {assignable.map((role) => (
                <label key={role.id} className="permission-check">
                  <input
                    type="checkbox"
                    checked={roleIds.includes(role.id)}
                    onChange={(e) =>
                      setRoleIds((prev) =>
                        e.target.checked ? [...prev, role.id] : prev.filter((id) => id !== role.id),
                      )
                    }
                  />
                  <span style={role.color ? { color: role.color } : undefined}>{role.name}</span>
                </label>
              ))}
            </div>
          )}
        </div>
      }
      saveDisabled={creating && !username.trim()}
      onSave={() =>
        onSave(
          {
            username: username.trim().toLowerCase(),
            roleIds: roleIds.filter((id) => assignable.some((role) => role.id === id)),
            flairId: flairId || null,
          },
          member,
        )
      }
      onCancel={() => {
        if (member) {
          setUsername(member.username);
          setRoleIds(member.roles.map((role) => role.id));
          setFlairId(member.flair?.id ?? '');
        } else {
          setUsername('');
          setRoleIds([]);
          setFlairId('');
        }
        onCancel?.();
      }}
    >
      {creating && (
        <div className="form-group member-role-user">
          <label htmlFor={`member-user-${fieldId}`}>Username</label>
          <input
            id={`member-user-${fieldId}`}
            value={username}
            onChange={(e) => setUsername(e.target.value)}
            placeholder="alice"
            required
            autoFocus
            size={16}
          />
        </div>
      )}
      {flairs.length > 0 && (
        <div className="form-group member-role-flair">
          <label htmlFor={`member-flair-${fieldId}`}>Flair</label>
          <select
            id={`member-flair-${fieldId}`}
            className="flair-select"
            value={flairId}
            onChange={(e) => setFlairId(e.target.value)}
          >
            <option value="">None</option>
            {flairs.map((flair) => (
              <option key={flair.id} value={flair.id}>
                {forumFlairSelectLabel(flair, emotes)}
              </option>
            ))}
          </select>
        </div>
      )}
    </InlineEditRow>
  );
}

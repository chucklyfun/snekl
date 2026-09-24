import { useEffect, useState } from 'react';
import {
  DEFAULT_FORUM_ROLE_COLOR,
  FORUM_PERMISSION_OPTIONS,
  FORUM_ROLE_STARTER_PACKS,
  starterPackPermissions,
  type ForumRoleStarterPackId,
} from '../forumRoles';
import type { ContentBlockType, ForumRole } from '../types';
import { HoverExpandHint, PermissionHint } from './HoverExpandHint';
import { InlineEditRow } from './InlineEditRow';

export type ForumRoleDraft = {
  name: string;
  color: string;
  permissions: string[];
};

export function ForumRoleRow({
  role,
  creating = false,
  blockTypes,
  onSave,
  onCancel,
}: {
  role?: ForumRole;
  creating?: boolean;
  blockTypes: ContentBlockType[];
  onSave: (draft: ForumRoleDraft, existing?: ForumRole) => Promise<void>;
  onCancel?: () => void;
}) {
  const [name, setName] = useState(role?.name ?? '');
  const [color, setColor] = useState(role?.color || DEFAULT_FORUM_ROLE_COLOR);
  const [permissions, setPermissions] = useState(role?.permissions ?? starterPackPermissions('member'));
  const [starterPack, setStarterPack] = useState<ForumRoleStarterPackId>('member');
  const fieldId = role?.id ?? 'new';
  const isEveryone = Boolean(role?.isEveryone);

  useEffect(() => {
    if (creating || !role) return;
    setName(role.name);
    setColor(role.color || DEFAULT_FORUM_ROLE_COLOR);
    setPermissions([...role.permissions]);
  }, [creating, role]);

  return (
    <InlineEditRow
      as="div"
      className="server-row role-row"
      creating={creating}
      canEdit
      includeRemove={false}
      view={
        role ? (
          <div>
            <strong style={{ color: role.color ?? undefined }}>{role.name}</strong>
            <div className="card-meta">{role.permissions.join(', ') || 'no permissions'}</div>
          </div>
        ) : null
      }
      fieldsClassName="role-edit-bar"
      afterFields={
        <>
          {isEveryone && (
            <p className="card-meta">
              @everyone cannot be renamed. Color and permissions can be changed like any other role.
            </p>
          )}
          {creating && (
            <p className="card-meta">This fills the checklist below. You can still tick or untick individual permissions.</p>
          )}
          <div className="form-group">
            <label>Permissions</label>
            <ForumPermissionChecklist selected={permissions} onChange={setPermissions} blockTypes={blockTypes} />
          </div>
        </>
      }
      onSave={() => onSave({ name, color, permissions }, role)}
      onCancel={() => {
        if (role) {
          setName(role.name);
          setColor(role.color || DEFAULT_FORUM_ROLE_COLOR);
          setPermissions([...role.permissions]);
        } else {
          setName('');
          setColor(DEFAULT_FORUM_ROLE_COLOR);
          setStarterPack('member');
          setPermissions(starterPackPermissions('member'));
        }
        onCancel?.();
      }}
    >
      <div className="form-group role-edit-name">
        <label htmlFor={`role-name-${fieldId}`}>Role name</label>
        <input
          id={`role-name-${fieldId}`}
          value={name}
          onChange={(e) => setName(e.target.value)}
          disabled={isEveryone}
          required={!isEveryone}
          autoFocus={creating}
          size={16}
          maxLength={64}
        />
      </div>
      <div className="form-group role-edit-color">
        <label htmlFor={`role-color-${fieldId}`}>Color</label>
        <input
          id={`role-color-${fieldId}`}
          type="color"
          value={color}
          onChange={(e) => setColor(e.target.value)}
        />
      </div>
      {creating && (
        <div className="form-group role-edit-starter">
          <label htmlFor={`role-starter-${fieldId}`}>Starter permissions</label>
          <select
            id={`role-starter-${fieldId}`}
            value={starterPack}
            onChange={(e) => {
              const pack = e.target.value as ForumRoleStarterPackId;
              setStarterPack(pack);
              setPermissions(starterPackPermissions(pack));
            }}
          >
            {FORUM_ROLE_STARTER_PACKS.map((pack) => (
              <option key={pack.id} value={pack.id}>
                {pack.label} — {pack.description}
              </option>
            ))}
          </select>
        </div>
      )}
    </InlineEditRow>
  );
}

function ForumPermissionChecklist({
  selected,
  onChange,
  blockTypes,
}: {
  selected: string[];
  onChange: (next: string[]) => void;
  blockTypes: ContentBlockType[];
}) {
  const extras = blockTypes.flatMap((type) =>
    type.permissions
      .filter((permission) => permission.scope === 'forum')
      .map((permission) => ({ ...permission, group: type.name })),
  );

  return (
    <>
      <div className="permission-grid">
        {FORUM_PERMISSION_OPTIONS.map((permission) => (
          <label key={permission} className="permission-check">
            <input
              type="checkbox"
              checked={selected.includes(permission)}
              onChange={(e) =>
                onChange(
                  e.target.checked
                    ? [...selected, permission]
                    : selected.filter((item) => item !== permission),
                )
              }
            />
            {permission}
            <PermissionHint name={permission} />
          </label>
        ))}
      </div>
      {extras.length > 0 && (
        <div style={{ marginTop: '0.75rem' }}>
          <p className="card-meta">Content block permissions</p>
          <div className="permission-grid">
            {extras.map((permission) => (
              <label key={`${permission.group}:${permission.name}`} className="permission-check">
                <input
                  type="checkbox"
                  checked={selected.includes(permission.name)}
                  onChange={(e) =>
                    onChange(
                      e.target.checked
                        ? [...selected, permission.name]
                        : selected.filter((item) => item !== permission.name),
                    )
                  }
                />
                {permission.name}
                {permission.group ? <HoverExpandHint text={permission.group} /> : null}
              </label>
            ))}
          </div>
        </div>
      )}
    </>
  );
}

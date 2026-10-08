import { useEffect, useState } from 'react';
import type { ContentBlockType, ServerRole } from '../types';
import { HoverExpandHint, PermissionHint } from './HoverExpandHint';
import { InlineEditRow } from './InlineEditRow';

export const DEFAULT_SERVER_ROLE_COLOR = '#4f8cff';

const SERVER_PERMISSION_OPTIONS = [
  'ViewServer',
  'CreateForum',
  'RequestCreateForum',
  'CreateWatercooler',
  'RequestCreateWatercooler',
  'ManageServerMembers',
  'ManageServerRoles',
  'ManageServerSettings',
  'ViewHistory',
  'UseOtherForumEmotes',
  'UseOtherServerEmotes',
  'ServerAdministrator',
  'RequestFederation',
  'AcceptFederation',
  'ManageContentBlocks',
];

export type ServerRoleDraft = {
  name: string;
  color: string;
  permissions: string[];
};

export function ServerRoleRow({
  role,
  creating = false,
  blockTypes,
  onSave,
  onCancel,
}: {
  role?: ServerRole;
  creating?: boolean;
  blockTypes: ContentBlockType[];
  onSave: (draft: ServerRoleDraft, existing?: ServerRole) => Promise<void>;
  onCancel?: () => void;
}) {
  const [name, setName] = useState(role?.name ?? '');
  const [color, setColor] = useState(role?.color || DEFAULT_SERVER_ROLE_COLOR);
  const [permissions, setPermissions] = useState(role?.permissions ?? ['ViewServer']);
  const fieldId = role?.id ?? 'new';
  const isEveryone = Boolean(role?.isEveryone);

  useEffect(() => {
    if (creating || !role) return;
    setName(role.name);
    setColor(role.color || DEFAULT_SERVER_ROLE_COLOR);
    setPermissions([...role.permissions]);
  }, [creating, role]);

  const extras = blockTypes.flatMap((type) =>
    type.permissions
      .filter((permission) => permission.scope === 'server')
      .map((permission) => ({ ...permission, group: type.name })),
  );

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
            <div className="card-meta">{role.permissions.join(', ') || 'none'}</div>
          </div>
        ) : null
      }
      fieldsClassName="role-edit-bar"
      afterFields={
        <>
          {isEveryone && (
            <p className="card-meta">@everyone cannot be renamed. Permissions can be changed like any other role.</p>
          )}
          <div className="form-group">
            <label>Permissions</label>
            <div className="permission-grid">
              {SERVER_PERMISSION_OPTIONS.map((permission) => (
                <label key={permission} className="permission-check">
                  <input
                    type="checkbox"
                    checked={permissions.includes(permission)}
                    onChange={(e) =>
                      setPermissions((prev) =>
                        e.target.checked ? [...prev, permission] : prev.filter((item) => item !== permission),
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
                        checked={permissions.includes(permission.name)}
                        onChange={(e) =>
                          setPermissions((prev) =>
                            e.target.checked
                              ? [...prev, permission.name]
                              : prev.filter((item) => item !== permission.name),
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
          </div>
        </>
      }
      onSave={() => onSave({ name, color, permissions }, role)}
      onCancel={() => {
        if (role) {
          setName(role.name);
          setColor(role.color || DEFAULT_SERVER_ROLE_COLOR);
          setPermissions([...role.permissions]);
        } else {
          setName('');
          setColor(DEFAULT_SERVER_ROLE_COLOR);
          setPermissions(['ViewServer']);
        }
        onCancel?.();
      }}
    >
      <div className="form-group role-edit-name">
        <label htmlFor={`server-role-name-${fieldId}`}>Role name</label>
        <input
          id={`server-role-name-${fieldId}`}
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
        <label htmlFor={`server-role-color-${fieldId}`}>Color</label>
        <input
          id={`server-role-color-${fieldId}`}
          type="color"
          value={color}
          onChange={(e) => setColor(e.target.value)}
          disabled={isEveryone}
        />
      </div>
    </InlineEditRow>
  );
}

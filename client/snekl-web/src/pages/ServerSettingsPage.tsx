import { type FormEvent, useEffect, useMemo, useState } from 'react';
import { Link, useParams } from 'react-router';
import { createContentClient, hasPermission } from '../api';
import { useAuth } from '../AuthContext';
import { useServerRegistry } from '../ServerRegistryContext';
import { decodeServerDid, resolveServerBaseUrl } from '../serverDid';
import type { ServerMember, ServerRole } from '../types';

const SERVER_PERMISSION_OPTIONS = [
  'ViewServer',
  'CreateForum',
  'ManageServerMembers',
  'ManageServerRoles',
  'ManageServerSettings',
  'ServerAdministrator',
];

export function ServerSettingsPage() {
  const { serverDid: encodedDid } = useParams<{ serverDid: string }>();
  const { user, isSuperUser, serverPermissions } = useAuth();
  const { resolveBaseUrl } = useServerRegistry();
  const [roles, setRoles] = useState<ServerRole[]>([]);
  const [members, setMembers] = useState<ServerMember[]>([]);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');

  const [roleName, setRoleName] = useState('');
  const [roleColor, setRoleColor] = useState('#4f8cff');
  const [rolePerms, setRolePerms] = useState<string[]>(['ViewServer']);
  const [assignUsername, setAssignUsername] = useState('');
  const [assignRoleIds, setAssignRoleIds] = useState<string[]>([]);

  const serverDid = encodedDid ? decodeServerDid(encodedDid) : '';
  const baseUrl = resolveServerBaseUrl(serverDid, resolveBaseUrl);
  const client = useMemo(() => (baseUrl ? createContentClient(baseUrl) : null), [baseUrl]);

  const canManage =
    isSuperUser ||
    hasPermission(serverPermissions, 'ManageServerRoles') ||
    hasPermission(serverPermissions, 'ManageServerMembers');

  const load = () => {
    if (!client) return;
    Promise.all([client.getServerRoles(), client.getServerMembers()])
      .then(([r, m]) => {
        setRoles(r);
        setMembers(m);
      })
      .catch((err) => setError(err.message));
  };

  useEffect(load, [client]);

  if (!user) {
    return (
      <div className="card">
        <Link to="/login">Log in</Link> to manage server settings.
      </div>
    );
  }

  if (!canManage) {
    return <div className="error-banner">You do not have permission to manage this server.</div>;
  }

  const createRole = async (e: FormEvent) => {
    e.preventDefault();
    if (!client) return;
    setError('');
    try {
      await client.createServerRole({
        name: roleName.trim(),
        color: roleColor,
        position: roles.length + 1,
        permissions: rolePerms,
      });
      setRoleName('');
      setSuccess('Role created.');
      load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create role');
    }
  };

  const assignRoles = async (e: FormEvent) => {
    e.preventDefault();
    if (!client) return;
    setError('');
    try {
      await client.assignServerRolesByUsername(assignUsername.trim().toLowerCase(), assignRoleIds);
      setSuccess('Roles assigned.');
      load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to assign roles');
    }
  };

  return (
    <div>
      <Link to="/" style={{ color: 'var(--muted)' }}>
        ← Forums
      </Link>
      <h1 className="page-title">Server settings</h1>
      {isSuperUser && (
        <p className="page-subtitle">
          You are a Super User on this node — server configuration only. Forum access and moderation follow your assigned roles.
        </p>
      )}

      {error && <div className="error-banner">{error}</div>}
      {success && (
        <div className="card" style={{ borderColor: 'var(--accent)', marginBottom: '1rem' }}>
          {success}
        </div>
      )}

      <section className="card">
        <h2 className="card-title">Roles</h2>
        {roles.map((role) => (
          <div key={role.id} className="server-row">
            <div>
              <strong style={{ color: role.color ?? undefined }}>{role.name}</strong>
              <div className="card-meta">{role.permissions.join(', ') || 'none'}</div>
            </div>
          </div>
        ))}

        <form onSubmit={createRole} style={{ marginTop: '1rem' }}>
          <div className="form-group">
            <label htmlFor="role-name">New role name</label>
            <input id="role-name" value={roleName} onChange={(e) => setRoleName(e.target.value)} required />
          </div>
          <div className="form-group">
            <label htmlFor="role-color">Color</label>
            <input id="role-color" type="color" value={roleColor} onChange={(e) => setRoleColor(e.target.value)} />
          </div>
          <div className="form-group">
            <label>Permissions</label>
            <div className="permission-grid">
              {SERVER_PERMISSION_OPTIONS.map((p) => (
                <label key={p} className="permission-check">
                  <input
                    type="checkbox"
                    checked={rolePerms.includes(p)}
                    onChange={(e) =>
                      setRolePerms((prev) =>
                        e.target.checked ? [...prev, p] : prev.filter((x) => x !== p),
                      )
                    }
                  />
                  {p}
                </label>
              ))}
            </div>
          </div>
          <button type="submit" className="btn-primary">
            Add role
          </button>
        </form>
      </section>

      <section className="card">
        <h2 className="card-title">Members</h2>
        {members.map((m) => (
          <div key={m.userId} className="server-row">
            <div>
              <strong>{m.displayName}</strong>
              <div className="card-meta">
                @{m.username} · {m.roles.map((r) => r.name).join(', ') || 'no roles'}
              </div>
            </div>
          </div>
        ))}

        <form onSubmit={assignRoles} style={{ marginTop: '1rem' }}>
          <div className="form-group">
            <label htmlFor="assign-user">Username</label>
            <input
              id="assign-user"
              value={assignUsername}
              onChange={(e) => setAssignUsername(e.target.value)}
              placeholder="alice"
              required
            />
          </div>
          <div className="form-group">
            <label>Roles to assign</label>
            <div className="permission-grid">
              {roles
                .filter((r) => !r.isEveryone)
                .map((r) => (
                  <label key={r.id} className="permission-check">
                    <input
                      type="checkbox"
                      checked={assignRoleIds.includes(r.id)}
                      onChange={(e) =>
                        setAssignRoleIds((prev) =>
                          e.target.checked ? [...prev, r.id] : prev.filter((x) => x !== r.id),
                        )
                      }
                    />
                    {r.name}
                  </label>
                ))}
            </div>
          </div>
          <button type="submit" className="btn-primary">
            Assign roles
          </button>
        </form>
      </section>
    </div>
  );
}

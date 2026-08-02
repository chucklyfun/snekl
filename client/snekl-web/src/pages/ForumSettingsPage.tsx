import { type FormEvent, useEffect, useMemo, useState } from 'react';
import { Link, useParams } from 'react-router';
import { createContentClient, hasPermission } from '../api';
import { useAuth } from '../AuthContext';
import { useServerRegistry } from '../ServerRegistryContext';
import {
  createForumPath,
  decodeServerDid,
  forumPath,
  resolveServerBaseUrl,
} from '../serverDid';
import type { Forum, ForumFlairTemplate, ForumMember, ForumRole } from '../types';

const FORUM_PERMISSION_OPTIONS = [
  'ViewForum',
  'CreateThread',
  'Reply',
  'EditOwnPost',
  'DeleteOwnPost',
  'ManagePosts',
  'ManageMembers',
  'ManageRoles',
  'ManageForum',
  'ForumAdministrator',
];

export function ForumSettingsPage() {
  const { serverDid: encodedDid, slug, parentSlug } = useParams<{
    serverDid: string;
    slug: string;
    parentSlug?: string;
  }>();
  const { user } = useAuth();
  const { resolveBaseUrl } = useServerRegistry();
  const [forum, setForum] = useState<Forum | null>(null);
  const [roles, setRoles] = useState<ForumRole[]>([]);
  const [members, setMembers] = useState<ForumMember[]>([]);
  const [flairs, setFlairs] = useState<ForumFlairTemplate[]>([]);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');

  const [roleName, setRoleName] = useState('');
  const [roleColor] = useState('#43b581');
  const [rolePerms, setRolePerms] = useState<string[]>(['ViewForum']);
  const [flairEmoji, setFlairEmoji] = useState('');
  const [flairLabel, setFlairLabel] = useState('');
  const [myAlias, setMyAlias] = useState('');

  const serverDid = encodedDid ? decodeServerDid(encodedDid) : '';
  const baseUrl = resolveServerBaseUrl(serverDid, resolveBaseUrl);
  const client = useMemo(() => (baseUrl ? createContentClient(baseUrl) : null), [baseUrl]);
  const effectiveParentSlug = parentSlug ?? forum?.parentSlug ?? undefined;

  const canManageRoles = hasPermission(forum?.viewerPermissions, 'ManageRoles');
  const canManageMembers = hasPermission(forum?.viewerPermissions, 'ManageMembers');
  const canManageForum = hasPermission(forum?.viewerPermissions, 'ManageForum');

  const load = () => {
    if (!slug || !client) return;
    client
      .getForum(slug, effectiveParentSlug)
      .then((f) => {
        setForum(f);
        return Promise.all([
          client.getForumRoles(slug, effectiveParentSlug),
          client.getForumMembers(slug, effectiveParentSlug),
          client.getFlairTemplates(slug, effectiveParentSlug),
        ]);
      })
      .then(([r, m, fl]) => {
        setRoles(r);
        setMembers(m);
        setFlairs(fl);
      })
      .catch((err) => setError(err.message));
  };

  useEffect(load, [slug, client, effectiveParentSlug]);

  if (!user) {
    return (
      <div className="card">
        <Link to="/login">Log in</Link> to manage forum settings.
      </div>
    );
  }

  if (!forum && !error) return <p>Loading...</p>;

  if (forum && !canManageRoles && !canManageMembers && !canManageForum) {
    return <div className="error-banner">You do not have permission to manage this forum.</div>;
  }

  const createRole = async (e: FormEvent) => {
    e.preventDefault();
    if (!client || !slug) return;
    setError('');
    try {
      await client.createForumRole(
        slug,
        { name: roleName.trim(), color: roleColor, position: roles.length + 1, permissions: rolePerms },
        effectiveParentSlug,
      );
      setRoleName('');
      setSuccess('Forum role created.');
      load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed');
    }
  };

  const createFlair = async (e: FormEvent) => {
    e.preventDefault();
    if (!client || !slug) return;
    setError('');
    try {
      await client.createFlairTemplate(
        slug,
        { emoji: flairEmoji || undefined, label: flairLabel || undefined, position: flairs.length },
        effectiveParentSlug,
      );
      setFlairEmoji('');
      setFlairLabel('');
      setSuccess('Flair template added.');
      load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed');
    }
  };

  const saveAlias = async (e: FormEvent) => {
    e.preventDefault();
    if (!client || !slug || !forum?.allowAliases) return;
    setError('');
    try {
      await client.setMyAlias(slug, myAlias.trim(), effectiveParentSlug);
      setSuccess('Alias saved.');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed');
    }
  };

  return (
    <div>
      {forum && (
        <Link to={forumPath(serverDid, forum.slug, effectiveParentSlug)} style={{ color: 'var(--muted)' }}>
          ← {forum.name}
        </Link>
      )}
      <h1 className="page-title">Forum settings</h1>

      {error && <div className="error-banner">{error}</div>}
      {success && (
        <div className="card" style={{ borderColor: 'var(--accent)', marginBottom: '1rem' }}>
          {success}
        </div>
      )}

      {canManageForum && forum && !forum.parentForumId && (
        <section className="card">
          <h2 className="card-title">Subforums</h2>
          <p className="card-meta">Create focused areas under this forum.</p>
          <Link
            to={`${createForumPath(serverDid)}?parent=${forum.id}&parentSlug=${forum.slug}`}
            className="btn-primary-link"
          >
            Create subforum
          </Link>
          {forum.subforums && forum.subforums.length > 0 && (
            <ul style={{ marginTop: '1rem' }}>
              {forum.subforums.map((s) => (
                <li key={s.id}>
                  <Link to={forumPath(serverDid, s.slug, forum.slug)}>{s.name}</Link>
                </li>
              ))}
            </ul>
          )}
        </section>
      )}

      {canManageRoles && (
        <section className="card">
          <h2 className="card-title">Roles</h2>
          {roles.map((role) => (
            <div key={role.id} className="server-row">
              <strong style={{ color: role.color ?? undefined }}>{role.name}</strong>
              <div className="card-meta">{role.permissions.join(', ')}</div>
            </div>
          ))}
          <form onSubmit={createRole} style={{ marginTop: '1rem' }}>
            <div className="form-group">
              <label htmlFor="forum-role-name">Role name</label>
              <input id="forum-role-name" value={roleName} onChange={(e) => setRoleName(e.target.value)} required />
            </div>
            <div className="permission-grid">
              {FORUM_PERMISSION_OPTIONS.map((p) => (
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
            <button type="submit" className="btn-primary" style={{ marginTop: '0.5rem' }}>
              Add role
            </button>
          </form>
        </section>
      )}

      {canManageMembers && (
        <section className="card">
          <h2 className="card-title">Members</h2>
          {members.map((m) => (
            <div key={m.userId} className="server-row">
              <strong>{m.alias ?? m.displayName}</strong>
              <div className="card-meta">
                @{m.username} · {m.roles.map((r) => r.name).join(', ')}
              </div>
            </div>
          ))}
        </section>
      )}

      {(canManageMembers || canManageRoles) && (
        <section className="card">
          <h2 className="card-title">Flair templates</h2>
          {flairs.map((f) => (
            <div key={f.id} className="server-row">
              {f.emoji} {f.label}
            </div>
          ))}
          <form onSubmit={createFlair} style={{ marginTop: '1rem' }}>
            <div className="inline-form">
              <input value={flairEmoji} onChange={(e) => setFlairEmoji(e.target.value)} placeholder="Emoji" />
              <input value={flairLabel} onChange={(e) => setFlairLabel(e.target.value)} placeholder="Label" />
              <button type="submit" className="btn-primary">
                Add flair
              </button>
            </div>
          </form>
        </section>
      )}

      {forum?.allowAliases && (
        <section className="card">
          <h2 className="card-title">Your alias</h2>
          <form onSubmit={saveAlias}>
            <div className="inline-form">
              <input
                value={myAlias}
                onChange={(e) => setMyAlias(e.target.value)}
                placeholder="Display name in this forum"
              />
              <button type="submit" className="btn-primary">
                Save alias
              </button>
            </div>
          </form>
        </section>
      )}
    </div>
  );
}

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
  threadPath,
} from '../serverDid';
import type { Forum, ForumBan, ForumFlairTemplate, ForumMember, ForumRole, PostReport } from '../types';

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
  const [bans, setBans] = useState<ForumBan[]>([]);
  const [reports, setReports] = useState<PostReport[]>([]);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');

  const [roleName, setRoleName] = useState('');
  const [roleColor, setRoleColor] = useState('#43b581');
  const [rolePerms, setRolePerms] = useState<string[]>(['ViewForum']);
  const [flairEmoji, setFlairEmoji] = useState('');
  const [flairLabel, setFlairLabel] = useState('');
  const [myAlias, setMyAlias] = useState('');
  const [assignUsername, setAssignUsername] = useState('');
  const [assignRoleIds, setAssignRoleIds] = useState<string[]>([]);
  const [banUsername, setBanUsername] = useState('');
  const [banReason, setBanReason] = useState('');

  const serverDid = encodedDid ? decodeServerDid(encodedDid) : '';
  const baseUrl = resolveServerBaseUrl(serverDid, resolveBaseUrl);
  const client = useMemo(() => (baseUrl ? createContentClient(baseUrl) : null), [baseUrl]);
  const effectiveParentSlug = parentSlug ?? forum?.parentSlug ?? undefined;

  const canManageRoles = hasPermission(forum?.viewerPermissions, 'ManageRoles');
  const canManageMembers = hasPermission(forum?.viewerPermissions, 'ManageMembers');
  const canManageForum = hasPermission(forum?.viewerPermissions, 'ManageForum');
  const canManagePosts = hasPermission(forum?.viewerPermissions, 'ManagePosts');
  const canListRoles = canManageRoles || canManageMembers;
  const canAccess = canManageRoles || canManageMembers || canManageForum || canManagePosts;

  const loadForum = () => {
    if (!slug || !client) return;
    client
      .getForum(slug, effectiveParentSlug)
      .then(setForum)
      .catch((err) => setError(err.message));
  };

  const loadRoles = () => {
    if (!slug || !client || !canListRoles) return;
    client.getForumRoles(slug, effectiveParentSlug).then(setRoles).catch((err) => setError(err.message));
  };

  const loadMembers = () => {
    if (!slug || !client || !canManageMembers) return;
    client.getForumMembers(slug, effectiveParentSlug).then(setMembers).catch((err) => setError(err.message));
  };

  const loadFlairs = () => {
    if (!slug || !client || !(canManageMembers || canManageRoles)) return;
    client.getFlairTemplates(slug, effectiveParentSlug).then(setFlairs).catch((err) => setError(err.message));
  };

  const loadBans = () => {
    if (!slug || !client || !canManageMembers) return;
    client.getForumBans(slug, effectiveParentSlug).then(setBans).catch((err) => setError(err.message));
  };

  const loadReports = () => {
    if (!slug || !client || !canManagePosts) return;
    client.getForumReports(slug, effectiveParentSlug).then(setReports).catch((err) => setError(err.message));
  };

  useEffect(() => {
    loadForum();
  }, [slug, client, effectiveParentSlug]);

  useEffect(() => {
    if (!forum) return;
    loadRoles();
    loadMembers();
    loadFlairs();
    loadBans();
    loadReports();
  }, [forum?.id, forum?.viewerPermissions?.join(',')]);

  if (!user) {
    return (
      <div className="card">
        <Link to="/login">Log in</Link> to manage forum settings.
      </div>
    );
  }

  if (!forum && !error) return <p>Loading...</p>;

  if (forum && !canAccess) {
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
      loadRoles();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed');
    }
  };

  const assignRoles = async (e: FormEvent) => {
    e.preventDefault();
    if (!client || !slug) return;
    setError('');
    try {
      await client.assignForumRolesByUsername(slug, assignUsername.trim().toLowerCase(), assignRoleIds, effectiveParentSlug);
      setAssignUsername('');
      setAssignRoleIds([]);
      setSuccess('Roles assigned.');
      loadMembers();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to assign roles');
    }
  };

  const banMember = async (e: FormEvent) => {
    e.preventDefault();
    if (!client || !slug) return;
    setError('');
    try {
      await client.banForumMember(
        slug,
        { username: banUsername.trim().toLowerCase(), reason: banReason.trim() || undefined },
        effectiveParentSlug,
      );
      setBanUsername('');
      setBanReason('');
      setSuccess('Member banned.');
      loadBans();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to ban member');
    }
  };

  const unbanMember = async (userId: string) => {
    if (!client || !slug) return;
    setError('');
    try {
      await client.unbanForumMember(slug, userId, effectiveParentSlug);
      setSuccess('Ban removed.');
      loadBans();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to unban');
    }
  };

  const dismissReport = async (reportId: string) => {
    if (!client || !slug) return;
    setError('');
    try {
      await client.dismissForumReport(slug, reportId, effectiveParentSlug);
      setSuccess('Report dismissed.');
      loadReports();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed');
    }
  };

  const resolveReport = async (reportId: string) => {
    if (!client || !slug) return;
    setError('');
    try {
      await client.resolveForumReport(slug, reportId, effectiveParentSlug);
      setSuccess('Report resolved.');
      loadReports();
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
      loadFlairs();
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
            <div className="form-group">
              <label htmlFor="forum-role-color">Color</label>
              <input
                id="forum-role-color"
                type="color"
                value={roleColor}
                onChange={(e) => setRoleColor(e.target.value)}
              />
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
          {members.length === 0 && <p className="card-meta">No members with assigned roles yet.</p>}
          {members.map((m) => (
            <div key={m.userId} className="server-row">
              <strong>{m.alias ?? m.displayName}</strong>
              <div className="card-meta">
                @{m.username} · {m.roles.map((r) => r.name).join(', ') || 'no roles'}
              </div>
            </div>
          ))}

          {canListRoles && roles.length > 0 && (
            <form onSubmit={assignRoles} style={{ marginTop: '1rem' }}>
              <div className="form-group">
                <label htmlFor="assign-forum-user">Username</label>
                <input
                  id="assign-forum-user"
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
          )}
        </section>
      )}

      {canManageMembers && (
        <section className="card">
          <h2 className="card-title">Banned members</h2>
          {bans.length === 0 && <p className="card-meta">No active bans.</p>}
          {bans.map((ban) => (
            <div key={ban.id} className="server-row">
              <div>
                <strong>{ban.displayName}</strong>
                <div className="card-meta">
                  @{ban.username}
                  {ban.reason ? ` · ${ban.reason}` : ''}
                  {ban.expiresAt ? ` · until ${new Date(ban.expiresAt).toLocaleString()}` : ' · permanent'}
                </div>
              </div>
              <button type="button" className="btn-ghost" onClick={() => unbanMember(ban.userId)}>
                Unban
              </button>
            </div>
          ))}
          <form onSubmit={banMember} style={{ marginTop: '1rem' }}>
            <div className="inline-form">
              <input
                value={banUsername}
                onChange={(e) => setBanUsername(e.target.value)}
                placeholder="Username"
                required
              />
              <input
                value={banReason}
                onChange={(e) => setBanReason(e.target.value)}
                placeholder="Reason (optional)"
              />
              <button type="submit" className="btn-primary">
                Ban
              </button>
            </div>
          </form>
        </section>
      )}

      {canManagePosts && (
        <section className="card">
          <h2 className="card-title">Report queue</h2>
          {reports.length === 0 && <p className="card-meta">No open reports.</p>}
          {reports.map((report) => (
            <div key={report.id} className="server-row" style={{ flexDirection: 'column', alignItems: 'stretch' }}>
              <div className="card-meta">
                Reported by @{report.reporterUsername} · post by @{report.postAuthorUsername} ·{' '}
                {new Date(report.createdAt).toLocaleString()}
              </div>
              <p className="post-body">{report.postPreview}</p>
              <p className="card-meta">Reason: {report.reason}</p>
              <div className="post-actions">
                <Link to={threadPath(serverDid, report.postId)} className="btn-ghost">
                  View thread
                </Link>
                <button type="button" className="btn-ghost" onClick={() => dismissReport(report.id)}>
                  Dismiss
                </button>
                <button type="button" className="btn-primary" onClick={() => resolveReport(report.id)}>
                  Resolve
                </button>
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

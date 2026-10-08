import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useParams } from 'react-router';
import { createContentClient, hasPermission } from '../api';
import { ChangeHistoryList } from '../components/ChangeHistoryList';
import { EmoteCatalogEditor } from '../components/EmoteCatalogEditor';
import { ExtensionInstallsPanel } from '../components/ExtensionInstallsPanel';
import { NsfwAgeCheckPanel } from '../components/NsfwAgeCheckPanel';
import { NestMapSection } from '../components/NestMapSection';
import { MemberRoleRow, type MemberRoleDraft } from '../components/MemberRoleRow';
import { ServerRoleRow, type ServerRoleDraft } from '../components/ServerRoleRow';
import { useAuth } from '../AuthContext';
import { useServerRegistry } from '../ServerRegistryContext';
import {
  decodeServerDid,
  resolveServerBaseUrl,
  createWatercoolerPath,
  serverOwnerSettingsPath,
  watercoolerPath,
  watercoolerSettingsPath,
} from '../serverDid';
import type { ContentBlockType, Forum, NsfwAgeCheckStatus, ServerMember, ServerRole, Watercooler } from '../types';
import { applyEmoteSkinUpdate, type CatalogEmote } from '../emotes';

export function ServerSettingsPage() {
  const { serverDid: encodedDid } = useParams<{ serverDid: string }>();
  const { user } = useAuth();
  const { resolveBaseUrl } = useServerRegistry();
  const [roles, setRoles] = useState<ServerRole[]>([]);
  const [members, setMembers] = useState<ServerMember[]>([]);
  const [forums, setForums] = useState<Forum[]>([]);
  const [plazas, setPlazas] = useState<Watercooler[]>([]);
  const [originIsSuperUser, setOriginIsSuperUser] = useState(false);
  const [originIsServerOwner, setOriginIsServerOwner] = useState(false);
  const [originPermissions, setOriginPermissions] = useState<string[]>([]);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  const [emotes, setEmotes] = useState<CatalogEmote[]>([]);
  const [blockTypes, setBlockTypes] = useState<ContentBlockType[]>([]);
  const [nsfwAgeRequired, setNsfwAgeRequired] = useState(false);
  const [nsfwMinimumAge, setNsfwMinimumAge] = useState(18);
  const [voteAlertBase, setVoteAlertBase] = useState(10);
  const [ageStatus, setAgeStatus] = useState<NsfwAgeCheckStatus | null>(null);

  const [composingRole, setComposingRole] = useState(false);
  const [composingMember, setComposingMember] = useState(false);

  const serverDid = encodedDid ? decodeServerDid(encodedDid) : '';
  const baseUrl = resolveServerBaseUrl(serverDid, resolveBaseUrl);
  const client = useMemo(() => (baseUrl ? createContentClient(baseUrl) : null), [baseUrl]);

  const canManage =
    originIsSuperUser ||
    originIsServerOwner ||
    hasPermission(originPermissions, 'ManageServerRoles') ||
    hasPermission(originPermissions, 'ManageServerMembers');
  const canDeleteForums =
    originIsSuperUser || originIsServerOwner || hasPermission(originPermissions, 'CreateForum');
  const canManageSettings =
    originIsSuperUser || originIsServerOwner || hasPermission(originPermissions, 'ManageServerSettings');
  const canViewHistory =
    originIsSuperUser || originIsServerOwner || hasPermission(originPermissions, 'ViewHistory');

  const pendingForums = forums.filter((f) => f.approvalStatus === 'pending');
  const pendingPlazas = plazas.filter((c) => c.approvalStatus === 'pending');
  const livePlazas = plazas.filter((c) => c.approvalStatus !== 'pending');

  const load = () => {
    if (!client) return;
    Promise.all([
      client.getServerPermissions(),
      client.getServerRoles().catch(() => [] as ServerRole[]),
      client.getServerMembers().catch(() => [] as ServerMember[]),
      client.getForums(),
      client.listServerWatercoolers().catch(() => [] as Watercooler[]),
      client.getEmotes().catch(() => [] as CatalogEmote[]),
      client.getContentBlockTypes().catch(() => [] as ContentBlockType[]),
    ])
      .then(([perms, r, m, forumList, plazaList, serverEmotes, types]) => {
        setOriginIsSuperUser(perms.isSuperUser);
        setOriginIsServerOwner(Boolean(perms.isServerOwner));
        setOriginPermissions(perms.permissions);
        setRoles(r);
        setMembers(m);
        setForums(forumList);
        setPlazas(plazaList);
        setEmotes(serverEmotes);
        setBlockTypes(types);
        setNsfwAgeRequired(Boolean(perms.nsfwAgeCheckRequired));
        setNsfwMinimumAge(perms.nsfwMinimumAge ?? 18);
        setVoteAlertBase(perms.voteAlertBase && perms.voteAlertBase > 0 ? perms.voteAlertBase : 10);
        setAgeStatus({
          enforced: Boolean(perms.nsfwAgeCheckRequired),
          requiredByServer: Boolean(perms.nsfwAgeCheckRequired),
          requiredByForum: false,
          minimumAge: perms.nsfwMinimumAge ?? 18,
          viewerAttested: Boolean(perms.viewerNsfwAgeAttested),
        });
      })
      .catch((err) => setError(err.message));
  };

  useEffect(load, [client]);

  const loadHistory = useCallback(() => {
    if (!client) return Promise.resolve([]);
    return client.getServerHistory();
  }, [client]);

  if (!user) {
    return (
      <div className="card">
        <Link to="/login">Log in</Link> to manage server settings.
      </div>
    );
  }

  if (!canManage && !canDeleteForums && !canManageSettings && !canViewHistory) {
    return <div className="error-banner">You do not have permission to manage this server.</div>;
  }

  const saveRole = async (draft: ServerRoleDraft, existing?: ServerRole) => {
    if (!client) return;
    setError('');
    if (existing) {
      await client.updateServerRole(existing.id, {
        name: existing.isEveryone ? existing.name : draft.name.trim(),
        color: draft.color,
        position: existing.position,
        permissions: draft.permissions,
      });
      setSuccess(`Updated ${existing.isEveryone ? '@everyone' : draft.name.trim() || existing.name}.`);
      load();
      return;
    }
    await client.createServerRole({
      name: draft.name.trim(),
      color: draft.color,
      position: roles.length + 1,
      permissions: draft.permissions,
    });
    setComposingRole(false);
    setSuccess('Role created.');
    load();
  };

  const saveMemberRoles = async (draft: MemberRoleDraft, existing?: { userId: string; username: string; displayName: string }) => {
    if (!client) return;
    setError('');
    if (existing) {
      await client.assignServerRoles(existing.userId, draft.roleIds);
      setSuccess(`Updated roles for ${existing.displayName}.`);
      load();
      return;
    }
    await client.assignServerRolesByUsername(draft.username, draft.roleIds);
    setComposingMember(false);
    setSuccess('Roles assigned.');
    load();
  };

  const deleteForum = async (forum: Forum) => {
    if (!client) return;
    if (!window.confirm(`Delete nest “${forum.name}” (/${forum.slug})? This cannot be undone.`)) return;
    setError('');
    try {
      await client.deleteForum(forum.slug);
      setSuccess(`Deleted ${forum.name} (/${forum.slug}).`);
      load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Delete failed');
    }
  };

  return (
    <div>
      <Link to="/" style={{ color: 'var(--muted)' }}>
        ← Nests
      </Link>
      <h1 className="page-title">Server settings</h1>
      {(originIsSuperUser || originIsServerOwner) && (
        <p className="page-subtitle">
          {originIsServerOwner
            ? 'You are the Server Owner on this node — your powers cannot be revoked by other users.'
            : 'You are a Super User on this node — server configuration only. Nest access and moderation follow your assigned roles.'}
          {originIsServerOwner && serverDid && (
            <>
              {' '}
              <Link to={serverOwnerSettingsPath(serverDid)}>Manage ownership</Link>
            </>
          )}
        </p>
      )}

      {error && <div className="error-banner">{error}</div>}
      {success && (
        <div className="card" style={{ borderColor: 'var(--accent)', marginBottom: '1rem' }}>
          {success}
        </div>
      )}

      {canViewHistory && (
        <section className="card">
          <h2 className="card-title">Change history</h2>
          <ChangeHistoryList load={loadHistory} />
        </section>
      )}

      {canManageSettings && client && (
        <section className="card">
          <h2 className="card-title">NSFW age check</h2>
          <p className="card-meta">
            When enabled, members are asked to confirm their age on every nest. NSFW images stay covered
            until that check is passed, even when this is off. Date of birth is not stored.
          </p>
          <form
            onSubmit={(e) => {
              e.preventDefault();
              setError('');
              void client
                .updateServerNsfwAgeCheck({ required: nsfwAgeRequired, minimumAge: nsfwMinimumAge })
                .then((status) => {
                  setAgeStatus(status);
                  setNsfwAgeRequired(status.requiredByServer);
                  setNsfwMinimumAge(status.minimumAge);
                  setSuccess(
                    status.requiredByServer
                      ? `Server age check on (${status.minimumAge}+).`
                      : 'Server age check off.',
                  );
                })
                .catch((err) => setError(err instanceof Error ? err.message : 'Failed to update age check'));
            }}
          >
            <div className="form-group">
              <label>
                <input
                  type="checkbox"
                  checked={nsfwAgeRequired}
                  onChange={(e) => setNsfwAgeRequired(e.target.checked)}
                />{' '}
                Require age check for NSFW on this server
              </label>
            </div>
            <div className="form-group">
              <label htmlFor="server-nsfw-min-age">Minimum age</label>
              <input
                id="server-nsfw-min-age"
                type="number"
                min={18}
                max={130}
                value={nsfwMinimumAge}
                onChange={(e) => setNsfwMinimumAge(Number(e.target.value))}
              />
            </div>
            <button type="submit" className="btn-primary">
              Save age check
            </button>
          </form>
        </section>
      )}

      {canManageSettings && client && (
        <section className="card">
          <h2 className="card-title">Vote alerts</h2>
          <p className="card-meta">
            The author is notified when a post reaches this many votes, then ten times that, and so on.
            Someone you follow is included when their follow alerts are set to vote milestones or both.
          </p>
          <form
            onSubmit={(e) => {
              e.preventDefault();
              setError('');
              void client
                .updateVoteAlertBase(voteAlertBase)
                .then((saved) => {
                  setVoteAlertBase(saved.base);
                  setSuccess(`Vote alerts start at ${saved.base}.`);
                })
                .catch((err) => setError(err instanceof Error ? err.message : 'Failed to update vote alerts'));
            }}
          >
            <div className="form-group">
              <label htmlFor="vote-alert-base">First notification at</label>
              <input
                id="vote-alert-base"
                type="number"
                min={1}
                max={1000000000}
                value={voteAlertBase}
                onChange={(e) => setVoteAlertBase(Number(e.target.value))}
              />
            </div>
            <button type="submit" className="btn-primary">
              Save vote alerts
            </button>
          </form>
        </section>
      )}

      {client && ageStatus && (
        <NsfwAgeCheckPanel
          client={client}
          status={ageStatus}
          loggedIn={Boolean(user)}
          showIfComplete
          onCompleted={setAgeStatus}
        />
      )}

      {client && (
        <EmoteCatalogEditor
          emotes={emotes}
          canManage={canManageSettings}
          onUpload={(file) => client.uploadMedia(file)}
          onCreate={async ({ code, name, mediaAssetId, skinsUnicode, highlighted }) => {
            await client.createEmote({ code, name, mediaAssetId, skinsUnicode, highlighted });
            setSuccess('Server emote added.');
            load();
          }}
          onUpdate={async (emoteId, input) => {
            const updated = await client.updateEmote(emoteId, input);
            setEmotes((prev) => applyEmoteSkinUpdate(prev, updated, input));
            load();
            setSuccess('Server emote updated.');
          }}
          onReorder={async (ids) => {
            await client.reorderEmotes(ids);
            load();
          }}
          onDelete={async (emoteId) => {
            await client.deleteEmote(emoteId);
            setSuccess('Server emote removed.');
            load();
          }}
        />
      )}

      {(canManageSettings || canManage) && client && (
        <ExtensionInstallsPanel client={client} scope="server" />
      )}

      {(canDeleteForums || livePlazas.length > 0) && (
        <section className="card">
          <h2 className="card-title">Server plazas</h2>
          <p className="card-meta">Untitled rooms that sit beside nests.</p>
          <Link to={createWatercoolerPath(serverDid)} className="btn-primary">
            New server plaza
          </Link>
          {livePlazas.map((plaza) => (
            <div key={plaza.id} className="server-row">
              <div>
                <Link to={watercoolerPath(serverDid, plaza.slug)}>
                  <strong>{plaza.name}</strong>
                </Link>
                <div className="card-meta">/{plaza.slug}</div>
              </div>
              {(plaza.viewerCanManage || plaza.viewerCanApprove) && (
                <div className="server-row-actions">
                  <Link to={watercoolerSettingsPath(serverDid, plaza.slug)} className="btn-ghost">
                    Settings
                  </Link>
                  {plaza.viewerCanManage && (
                    <button
                      type="button"
                      className="btn-ghost"
                      onClick={async () => {
                        if (!window.confirm(`Delete plaza “${plaza.name}”?`)) return;
                        try {
                          await client?.deleteWatercooler(plaza.slug);
                          setSuccess(`Deleted ${plaza.name}.`);
                          load();
                        } catch (err) {
                          setError(err instanceof Error ? err.message : 'Delete failed');
                        }
                      }}
                    >
                      Delete
                    </button>
                  )}
                </div>
              )}
            </div>
          ))}
        </section>
      )}

      {(originIsSuperUser || originIsServerOwner) && client && (
        <NestMapSection client={client} serverDid={serverDid} onChanged={load} />
      )}

      {canDeleteForums && !(originIsSuperUser || originIsServerOwner) && forums.length > 0 && (
        <section className="card">
          <h2 className="card-title">Nests</h2>
          <p className="card-meta">Slugs must be unique. Delete a nest to free its slug.</p>
          {forums.map((forum) => (
            <div key={forum.id} className="server-row">
              <div>
                <strong>{forum.name}</strong>
                {forum.approvalStatus === 'pending' && (
                  <span className="badge badge-pending" style={{ marginLeft: '0.5rem' }}>
                    pending
                  </span>
                )}
                <div className="card-meta">/{forum.slug}</div>
              </div>
              <div className="server-row-actions">
                <button type="button" className="btn-ghost" onClick={() => void deleteForum(forum)}>
                  Delete
                </button>
              </div>
            </div>
          ))}
        </section>
      )}

      {pendingPlazas.length > 0 && (
        <section className="card">
          <h2 className="card-title">Plaza requests</h2>
          <p className="card-meta">These plazas stay hidden until approved.</p>
          {pendingPlazas.map((plaza) => (
            <div key={plaza.id} className="server-row">
              <div>
                <strong>{plaza.name}</strong>
                <div className="card-meta">/{plaza.slug}</div>
              </div>
              {plaza.viewerCanApprove && (
                <div className="server-row-actions">
                  <Link to={watercoolerSettingsPath(serverDid, plaza.slug)} className="btn-ghost">
                    Settings
                  </Link>
                  <button
                    type="button"
                    className="btn-primary"
                    onClick={async () => {
                      try {
                        await client?.approveWatercooler(plaza.slug);
                        setSuccess(`Approved ${plaza.name}.`);
                        load();
                      } catch (err) {
                        setError(err instanceof Error ? err.message : 'Approve failed');
                      }
                    }}
                  >
                    Approve
                  </button>
                  <button
                    type="button"
                    className="btn-ghost"
                    onClick={async () => {
                      try {
                        await client?.rejectWatercooler(plaza.slug);
                        setSuccess(`Rejected ${plaza.name}.`);
                        load();
                      } catch (err) {
                        setError(err instanceof Error ? err.message : 'Reject failed');
                      }
                    }}
                  >
                    Reject
                  </button>
                </div>
              )}
            </div>
          ))}
        </section>
      )}

      {pendingForums.length > 0 && (
        <section className="card">
          <h2 className="card-title">Nest requests</h2>
          <p className="card-meta">These nests stay hidden until approved.</p>
          {pendingForums.map((forum) => (
            <div key={forum.id} className="server-row">
              <div>
                <strong>{forum.name}</strong>
                <div className="card-meta">/{forum.slug}</div>
              </div>
              {forum.viewerCanApprove && (
                <div className="server-row-actions">
                  <button
                    type="button"
                    className="btn-primary"
                    onClick={async () => {
                      try {
                        await client?.approveForum(forum.slug);
                        setSuccess(`Approved ${forum.name}.`);
                        load();
                      } catch (err) {
                        setError(err instanceof Error ? err.message : 'Approve failed');
                      }
                    }}
                  >
                    Approve
                  </button>
                  <button
                    type="button"
                    className="btn-ghost"
                    onClick={async () => {
                      try {
                        await client?.rejectForum(forum.slug);
                        setSuccess(`Rejected ${forum.name}.`);
                        load();
                      } catch (err) {
                        setError(err instanceof Error ? err.message : 'Reject failed');
                      }
                    }}
                  >
                    Reject
                  </button>
                </div>
              )}
            </div>
          ))}
        </section>
      )}

      {canManage && (
      <section className="card list-browser">
        <div className="list-browser-header">
          <div>
            <h2 className="card-title">Roles</h2>
            <p className="card-meta">Add a role or edit an existing one to change server permissions.</p>
          </div>
          <div className="list-browser-entry-actions">
            <button
              type="button"
              className="btn-primary"
              disabled={composingRole}
              onClick={() => {
                setComposingRole(true);
                setSuccess('');
              }}
            >
              Add role
            </button>
          </div>
        </div>
        {composingRole && (
          <ServerRoleRow
            creating
            blockTypes={blockTypes}
            onSave={saveRole}
            onCancel={() => setComposingRole(false)}
          />
        )}
        {roles.map((role) => (
          <ServerRoleRow key={role.id} role={role} blockTypes={blockTypes} onSave={saveRole} />
        ))}
      </section>
      )}

      {canManage && (
      <section className="card list-browser">
        <div className="list-browser-header">
          <div>
            <h2 className="card-title">Members</h2>
            <p className="card-meta">Edit a row to change roles.</p>
          </div>
          <div className="list-browser-entry-actions">
            <button
              type="button"
              className="btn-primary"
              disabled={composingMember}
              onClick={() => {
                setComposingMember(true);
                setSuccess('');
              }}
            >
              Add member
            </button>
          </div>
        </div>
        {members.length === 0 && !composingMember && (
          <p className="card-meta">No members with assigned roles yet.</p>
        )}
        {composingMember && (
          <MemberRoleRow
            creating
            roles={roles}
            onSave={saveMemberRoles}
            onCancel={() => setComposingMember(false)}
          />
        )}
        {members.map((m) => (
          <MemberRoleRow key={m.userId} member={m} roles={roles} onSave={saveMemberRoles} />
        ))}
      </section>
      )}
    </div>
  );
}

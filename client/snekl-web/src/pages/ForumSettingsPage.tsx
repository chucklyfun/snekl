import { type FormEvent, useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useLocation, useNavigate, useParams } from 'react-router';
import { ForumAppearanceFields } from '../components/ForumAppearanceFields';
import { EmoteCatalogEditor } from '../components/EmoteCatalogEditor';
import { NsfwAgeCheckPanel, forumAgeStatus } from '../components/NsfwAgeCheckPanel';
import { createContentClient, hasPermission } from '../api';
import { ChangeHistoryList } from '../components/ChangeHistoryList';
import { ForumFlairRow, forumFlairSelectLabel, type FlairDraft } from '../components/ForumFlairRow';
import { ForumRoleRow, type ForumRoleDraft } from '../components/ForumRoleRow';
import { MemberRoleRow, type MemberRoleDraft } from '../components/MemberRoleRow';
import { TransferOwnershipForm } from '../components/TransferOwnershipForm';
import { iconForBadge } from '../components/ListEntryRow';
import { useAuth } from '../AuthContext';
import { useForumNav } from '../ForumNavContext';
import { nextForumRolePosition } from '../forumRoles';
import { useServerRegistry } from '../ServerRegistryContext';
import {
  createForumPath,
  createWatercoolerPath,
  decodeServerDid,
  forumPath,
  forumSettingsPath,
  resolveServerBaseUrl,
  threadPath,
  watercoolerPath,
  watercoolerSettingsPath,
} from '../serverDid';
import type { ContentBlockType, Forum, ForumBan, ForumFlairTemplate, ForumMember, ForumRole, PostReport } from '../types';
import { applyEmoteSkinUpdate, type CatalogEmote } from '../emotes';
import { patchForumRecentIdentity } from '../forumRecents';
import {
  FORUM_SETTINGS_SECTIONS,
  resolveForumSettingsSection,
  type ForumSettingsSectionId,
} from '../forumSettingsNav';

export function ForumSettingsPage() {
  const { serverDid: encodedDid, slug, parentSlug } = useParams<{
    serverDid: string;
    slug: string;
    parentSlug?: string;
  }>();
  const { hash, pathname, search } = useLocation();
  const { user, did, isSuperUser, isServerOwner, serverPermissions } = useAuth();
  const navigate = useNavigate();
  const { refresh: refreshForumNav } = useForumNav();
  const { resolveBaseUrl } = useServerRegistry();
  const [forum, setForum] = useState<Forum | null>(null);
  const [roles, setRoles] = useState<ForumRole[]>([]);
  const [members, setMembers] = useState<ForumMember[]>([]);
  const [flairs, setFlairs] = useState<ForumFlairTemplate[]>([]);
  const [bans, setBans] = useState<ForumBan[]>([]);
  const [reports, setReports] = useState<PostReport[]>([]);
  const [emotes, setEmotes] = useState<CatalogEmote[]>([]);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  const [originIsServerOwner, setOriginIsServerOwner] = useState(false);
  const [originIsSuperUser, setOriginIsSuperUser] = useState(false);
  const [originPermissions, setOriginPermissions] = useState<string[]>([]);

  const [composingRole, setComposingRole] = useState(false);
  const [composingMember, setComposingMember] = useState(false);
  const [composingFlair, setComposingFlair] = useState(false);
  const [myFlairId, setMyFlairId] = useState('');
  const [myAlias, setMyAlias] = useState('');
  const [banUsername, setBanUsername] = useState('');
  const [banReason, setBanReason] = useState('');
  const [blockTypes, setBlockTypes] = useState<ContentBlockType[]>([]);
  const [name, setName] = useState('');
  const [slugDraft, setSlugDraft] = useState('');

  const serverDid = encodedDid ? decodeServerDid(encodedDid) : '';
  const baseUrl = resolveServerBaseUrl(serverDid, resolveBaseUrl);
  const client = useMemo(() => (baseUrl ? createContentClient(baseUrl) : null), [baseUrl]);
  const effectiveParentSlug = parentSlug ?? forum?.parentSlug ?? undefined;

  const canManageRoles = hasPermission(forum?.viewerPermissions, 'ManageRoles');
  const canManageMembers = hasPermission(forum?.viewerPermissions, 'ManageMembers');
  const canManageForum = hasPermission(forum?.viewerPermissions, 'ManageForum');
  const canManagePosts = hasPermission(forum?.viewerPermissions, 'ManagePosts');
  const canViewHistory = hasPermission(forum?.viewerPermissions, 'ViewHistory');
  const canListRoles = canManageRoles || canManageMembers;
  const canOverrideForumOwner =
    originIsServerOwner ||
    originIsSuperUser ||
    isServerOwner ||
    isSuperUser ||
    hasPermission(originPermissions, 'ServerAdministrator') ||
    hasPermission(serverPermissions, 'ServerAdministrator');
  const canTransferForum = Boolean(forum?.isViewerOwner || canOverrideForumOwner);
  const canAccess =
    canManageRoles ||
    canManageMembers ||
    canManageForum ||
    canManagePosts ||
    canViewHistory ||
    canTransferForum;

  const availableSections = useMemo(() => {
    const visible: ForumSettingsSectionId[] = [];
    if (forum) visible.push('general');
    if (forum && client) visible.push('emotes');
    if (canManageForum && forum) visible.push('structure');
    if (canManageRoles) visible.push('roles');
    if (canManageMembers) visible.push('members');
    if (canManagePosts) visible.push('moderation');
    visible.push('you');
    if (canTransferForum) visible.push('ownership');
    if (canViewHistory) visible.push('history');
    return visible;
  }, [
    forum,
    client,
    canManageForum,
    canManageRoles,
    canManageMembers,
    canManagePosts,
    canTransferForum,
    canViewHistory,
  ]);

  const activeSection = resolveForumSettingsSection(hash, availableSections);

  const loadForum = () => {
    if (!slug || !client) return;
    client
      .getForum(slug, effectiveParentSlug)
      .then((loaded) => {
        setForum(loaded);
        setName(loaded.name);
        setSlugDraft(loaded.slug);
      })
      .catch((err) => setError(err.message));
    client
      .getServerPermissions()
      .then((perms) => {
        setOriginIsSuperUser(perms.isSuperUser);
        setOriginIsServerOwner(Boolean(perms.isServerOwner));
        setOriginPermissions(perms.permissions);
      })
      .catch(() => {
        setOriginIsSuperUser(false);
        setOriginIsServerOwner(false);
        setOriginPermissions([]);
      });
  };

  const loadRoles = () => {
    if (!slug || !client || !canListRoles) return;
    client.getForumRoles(slug, effectiveParentSlug).then(setRoles).catch((err) => setError(err.message));
  };

  const loadMembers = () => {
    if (!slug || !client || !canManageMembers) return;
    client
      .getForumMembers(slug, effectiveParentSlug)
      .then((next) => {
        setMembers(next);
        const mine = user ? next.find((m) => m.userId === user.id) : undefined;
        if (mine) setMyFlairId(mine.flair?.id ?? '');
      })
      .catch((err) => setError(err.message));
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

  const loadEmotes = () => {
    if (!forum || !client) return;
    client.getEmotes(forum.id).then(setEmotes).catch((err) => setError(err.message));
  };

  const loadBlockTypes = () => {
    if (!client) return;
    client.getContentBlockTypes().then(setBlockTypes).catch(() => setBlockTypes([]));
  };

  useEffect(() => {
    loadForum();
    loadBlockTypes();
  }, [slug, client, effectiveParentSlug]);

  useEffect(() => {
    if (!forum) return;
    loadRoles();
    loadMembers();
    loadFlairs();
    loadBans();
    loadReports();
    loadEmotes();
  }, [forum?.id, forum?.viewerPermissions?.join(',')]);

  const loadHistory = useCallback(() => {
    if (!client || !slug) return Promise.resolve([]);
    return client.getForumHistory(slug, effectiveParentSlug);
  }, [client, slug, effectiveParentSlug]);

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

  const saveRole = async (draft: ForumRoleDraft, existing?: ForumRole) => {
    if (!client || !slug) return;
    setError('');
    if (existing) {
      await client.updateForumRole(
        slug,
        existing.id,
        {
          name: existing.isEveryone ? existing.name : draft.name.trim(),
          color: draft.color,
          position: existing.position,
          permissions: draft.permissions,
        },
        effectiveParentSlug,
      );
      setSuccess(`Updated ${existing.isEveryone ? '@everyone' : draft.name.trim() || existing.name}.`);
      loadRoles();
      return;
    }
    await client.createForumRole(
      slug,
      {
        name: draft.name.trim(),
        color: draft.color,
        position: nextForumRolePosition(roles),
        permissions: draft.permissions,
      },
      effectiveParentSlug,
    );
    setComposingRole(false);
    setSuccess('Forum role created.');
    loadRoles();
  };

  const saveMemberRoles = async (draft: MemberRoleDraft, existing?: { userId: string; username: string; displayName: string; alias?: string | null }) => {
    if (!client || !slug) return;
    setError('');
    if (existing) {
      await client.assignForumRoles(slug, existing.userId, draft.roleIds, effectiveParentSlug);
      if (flairs.length > 0) {
        await client.assignForumFlair(slug, existing.userId, draft.flairId, effectiveParentSlug);
      }
      setSuccess(`Updated roles for ${existing.alias ?? existing.displayName}.`);
      loadMembers();
      return;
    }
    await client.assignForumRolesByUsername(slug, draft.username, draft.roleIds, effectiveParentSlug);
    if (draft.flairId) {
      const next = await client.getForumMembers(slug, effectiveParentSlug);
      const created = next.find((member) => member.username.toLowerCase() === draft.username);
      if (created) {
        await client.assignForumFlair(slug, created.userId, draft.flairId, effectiveParentSlug);
      }
    }
    setComposingMember(false);
    setSuccess('Roles assigned.');
    loadMembers();
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

  const saveFlairTemplate = async (draft: FlairDraft, existing?: ForumFlairTemplate) => {
    if (!client || !slug) return;
    setError('');
    const payload = {
      emoji: draft.emoji.trim() || undefined,
      iconUrl: iconForBadge(draft.emoji, emotes),
      label: draft.label.trim() || undefined,
      nameColor: existing?.nameColor ?? undefined,
      nameStyle: existing?.nameStyle ?? undefined,
    };
    if (existing) {
      await client.updateFlairTemplate(
        slug,
        existing.id,
        { ...payload, position: existing.position },
        effectiveParentSlug,
      );
      setSuccess('Flair template updated.');
    } else {
      await client.createFlairTemplate(
        slug,
        { ...payload, position: flairs.length },
        effectiveParentSlug,
      );
      setComposingFlair(false);
      setSuccess('Flair template added.');
    }
    loadFlairs();
    loadMembers();
  };

  const removeFlairTemplate = async (template: ForumFlairTemplate) => {
    if (!client || !slug) return;
    setError('');
    try {
      await client.deleteFlairTemplate(slug, template.id, effectiveParentSlug);
      setSuccess('Flair template removed.');
      loadFlairs();
      loadMembers();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to remove flair');
    }
  };

  const saveMyFlair = async (e: FormEvent) => {
    e.preventDefault();
    if (!client || !slug || !user) return;
    setError('');
    try {
      await client.assignForumFlair(slug, user.id, myFlairId || null, effectiveParentSlug);
      setSuccess(myFlairId ? 'Your forum flair was saved.' : 'Your forum flair was cleared.');
      loadMembers();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save flair');
    }
  };

  const saveIdentity = async (e: FormEvent) => {
    e.preventDefault();
    if (!client || !forum) return;
    const nextName = name.trim();
    const nextSlug = slugDraft.trim().toLowerCase();
    if (!nextName || !nextSlug) return;
    setError('');
    setSuccess('');
    try {
      await client.updateForumSettings(forum.slug, { name: nextName, slug: nextSlug }, effectiveParentSlug);
      patchForumRecentIdentity(
        did,
        {
          serverDid,
          forumId: forum.id,
          previousSlug: forum.slug,
          updateChildAddresses: !forum.parentForumId,
        },
        { name: nextName, slug: nextSlug },
      );
      setName(nextName);
      setSlugDraft(nextSlug);
      setForum({ ...forum, name: nextName, slug: nextSlug });
      void refreshForumNav();
      if (nextSlug !== forum.slug) {
        navigate(forumSettingsPath(serverDid, nextSlug, effectiveParentSlug), { replace: true });
      }
      setSuccess('Name and address updated.');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not update the forum.');
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
      {forum && (
        <p className="page-subtitle" style={{ marginTop: '-0.5rem' }}>
          /{effectiveParentSlug ? `${effectiveParentSlug}/` : ''}
          {forum.slug}
          {forum.ownerUsername ? ` · owner @${forum.ownerUsername}` : ''}
        </p>
      )}

      <nav className="settings-nav" aria-label="Forum settings sections">
        {FORUM_SETTINGS_SECTIONS.filter((section) => availableSections.includes(section.id)).map((section) => (
          <a
            key={section.id}
            href={`${pathname}${search}#${section.id}`}
            className={`btn-ghost${activeSection === section.id ? ' is-active' : ''}`}
            aria-current={activeSection === section.id ? 'page' : undefined}
          >
            {section.label}
          </a>
        ))}
      </nav>

      {error && <div className="error-banner">{error}</div>}
      {success && (
        <div className="card" style={{ borderColor: 'var(--accent)', marginBottom: '1rem' }}>
          {success}
        </div>
      )}

      <div className="settings-section">
      {activeSection === 'ownership' && forum && canTransferForum && client && slug && (
        <section className="card">
          <h2 className="card-title">Ownership</h2>
          <p className="card-meta">
            {forum.ownerUsername ? `Forum owner · @${forum.ownerUsername}` : 'No forum owner is set.'}
          </p>
          <TransferOwnershipForm
            id={forum.id}
            resourceName={forum.name}
            confirmDetail="Forum admins will no longer be able to demote the new owner."
            onTransfer={async (username) => {
              setError('');
              const updated = await client.transferForumOwnership(slug, username, effectiveParentSlug);
              setForum(updated);
              setSuccess(`Transferred forum ownership to @${updated.ownerUsername ?? username}.`);
            }}
          />
        </section>
      )}

      {activeSection === 'history' && canViewHistory && (
        <section className="card">
          <h2 className="card-title">Change history</h2>
          <ChangeHistoryList load={loadHistory} />
        </section>
      )}

      {activeSection === 'emotes' && forum && client && (
        <EmoteCatalogEditor
          emotes={emotes.filter((emote) => emote.forumId === forum.id)}
          forumId={forum.id}
          canManage={canManageForum}
          onUpload={(file) => client.uploadMedia(file)}
          onCreate={async ({ code, name, mediaAssetId, skinsUnicode, highlighted }) => {
            await client.createEmote({ code, name, mediaAssetId, forumId: forum.id, skinsUnicode, highlighted });
            loadEmotes();
            setSuccess('Emote added.');
          }}
          onUpdate={async (emoteId, input) => {
            const updated = await client.updateEmote(emoteId, input);
            setEmotes((prev) => applyEmoteSkinUpdate(prev, updated, input));
            loadEmotes();
            setSuccess('Emote updated.');
          }}
          onReorder={async (ids) => {
            await client.reorderEmotes(ids, forum.id);
            loadEmotes();
          }}
          onDelete={async (emoteId) => {
            await client.deleteEmote(emoteId);
            loadEmotes();
            setSuccess('Emote removed.');
          }}
        />
      )}

      {activeSection === 'general' && canManageForum && forum && client && (
        <form className="card" onSubmit={(e) => void saveIdentity(e)}>
          <h2 className="card-title">Name and address</h2>
          <p className="card-meta">
            The name is the title on the forum page. The slug is the address. Changing the slug makes
            existing links to this forum stop working.
            {!forum.parentForumId ? ' Subforum addresses include this slug too.' : ''}
          </p>
          <div className="form-group">
            <label htmlFor="forum-settings-name">Name</label>
            <input
              id="forum-settings-name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              required
            />
          </div>
          <div className="form-group">
            <label htmlFor="forum-settings-slug">URL slug</label>
            <input
              id="forum-settings-slug"
              value={slugDraft}
              onChange={(e) => setSlugDraft(e.target.value.toLowerCase())}
              required
              pattern="[a-z0-9]+(-[a-z0-9]+)*"
              maxLength={64}
              title="Lowercase letters, numbers, and single hyphens"
            />
            <p className="card-meta">
              /{effectiveParentSlug ? `${effectiveParentSlug}/` : ''}
              {slugDraft.trim().toLowerCase()}
            </p>
          </div>
          <button type="submit" className="btn-primary">
            Save name and address
          </button>
        </form>
      )}

      {activeSection === 'general' && canManageForum && forum && client && (
        <ForumAppearanceFields
          name={forum.name}
          avatarUrl={forum.avatarUrl}
          bannerUrl={forum.bannerUrl}
          onUpload={(file) => client.uploadMedia(file)}
          onSetAvatar={async (mediaId) => {
            await client.updateForumSettings(forum.slug, { avatarMediaId: mediaId }, effectiveParentSlug);
            loadForum();
            void refreshForumNav();
            setSuccess('Profile image updated.');
          }}
          onSetBanner={async (mediaId) => {
            await client.updateForumSettings(forum.slug, { bannerMediaId: mediaId }, effectiveParentSlug);
            loadForum();
            void refreshForumNav();
            setSuccess('Banner updated.');
          }}
          onClearAvatar={async () => {
            await client.updateForumSettings(forum.slug, { clearAvatar: true }, effectiveParentSlug);
            loadForum();
            void refreshForumNav();
            setSuccess('Profile image removed.');
          }}
          onClearBanner={async () => {
            await client.updateForumSettings(forum.slug, { clearBanner: true }, effectiveParentSlug);
            loadForum();
            void refreshForumNav();
            setSuccess('Banner removed.');
          }}
        />
      )}

      {activeSection === 'general' && canManageForum && forum && client && (
        <section className="card">
          <h2 className="card-title">NSFW age check</h2>
          <p className="card-meta">
            NSFW images already stay covered until someone confirms they are {forum.nsfwMinimumAge ?? 18}+.
            Turn this on to also require that check before people can enter or join this forum.
            {forum.nsfwAgeCheckRequiredByServer ? ' The server already requires the NSFW media check.' : ''}
          </p>
          <form
            onSubmit={(e) => {
              e.preventDefault();
              setError('');
              const required = Boolean(forum.nsfwAgeCheckRequired);
              const explanation = forum.nsfwAgeCheckExplanation ?? '';
              client
                .updateForumSettings(
                  forum.slug,
                  { nsfwAgeCheckRequired: required, nsfwAgeCheckExplanation: explanation },
                  effectiveParentSlug,
                )
                .then(() => {
                  loadForum();
                  setSuccess(
                    required
                      ? 'Age check required before entering or joining this forum.'
                      : 'Forum entry age check turned off.',
                  );
                })
                .catch((err) => setError(err instanceof Error ? err.message : 'Failed to update age check'));
            }}
          >
            <label>
              <input
                type="checkbox"
                checked={Boolean(forum.nsfwAgeCheckRequired)}
                onChange={(e) =>
                  setForum({ ...forum, nsfwAgeCheckRequired: e.target.checked })
                }
              />{' '}
              Require age check before entering or joining this forum
            </label>
            <div className="form-group" style={{ marginTop: '1rem' }}>
              <label htmlFor="forum-nsfw-age-explanation">Explanation shown on the age check</label>
              <textarea
                id="forum-nsfw-age-explanation"
                rows={4}
                maxLength={2000}
                value={forum.nsfwAgeCheckExplanation ?? ''}
                onChange={(e) => setForum({ ...forum, nsfwAgeCheckExplanation: e.target.value })}
                placeholder="Why this forum needs an age check, and what people will find here."
              />
            </div>
            <button type="submit" className="btn-primary">
              Save age check
            </button>
          </form>
        </section>
      )}

      {activeSection === 'general' && forum && client && (
        <NsfwAgeCheckPanel
          client={client}
          forumId={forum.id}
          status={forumAgeStatus(forum)}
          loggedIn={Boolean(user)}
          showIfComplete
          onCompleted={() => loadForum()}
        />
      )}

      {activeSection === 'structure' && canManageForum && forum && !forum.parentForumId && (
        <section className="card">
          <h2 className="card-title">Subforums</h2>
          <p className="card-meta">Create focused areas under this forum.</p>
          <Link
            to={`${createForumPath(serverDid)}?parent=${forum.id}&parentSlug=${forum.slug}`}
            className="btn-primary"
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

      {activeSection === 'structure' && canManageForum && forum && (
        <section className="card">
          <h2 className="card-title">Watercoolers</h2>
          <p className="card-meta">Untitled rooms beside this forum. Pin a feed to put the composer on the forum page.</p>
          <Link to={createWatercoolerPath(serverDid, forum.id)} className="btn-primary">
            Create watercooler
          </Link>
          {forum.watercoolers && forum.watercoolers.length > 0 && (
            <ul style={{ marginTop: '1rem' }}>
              {forum.watercoolers.map((cooler) => (
                <li key={cooler.id} style={{ marginBottom: '0.5rem' }}>
                  <Link
                    to={watercoolerPath(
                      serverDid,
                      cooler.slug,
                      forum.slug,
                      parentSlug ?? forum.parentSlug ?? undefined,
                    )}
                  >
                    {cooler.name}
                  </Link>
                  <Link
                    to={watercoolerSettingsPath(
                      serverDid,
                      cooler.slug,
                      forum.slug,
                      parentSlug ?? forum.parentSlug ?? undefined,
                    )}
                    className="btn-ghost"
                    style={{ marginLeft: '0.5rem' }}
                  >
                    Settings
                  </Link>
                  {hasPermission(forum.viewerPermissions, 'ManageContentBlocks') && client && (
                    <button
                      type="button"
                      className="btn-ghost"
                      style={{ marginLeft: '0.5rem' }}
                      onClick={() => {
                        void client
                          .replaceForumContentBlocks(
                            forum.slug,
                            [
                              ...(forum.contentBlocks ?? [])
                                .filter((b) => !(b.typeKey === 'cooler-feed' && b.slot === 'main'))
                                .map((b) => ({
                                  typeKey: b.typeKey,
                                  watercoolerId: b.watercoolerId,
                                  slot: b.slot,
                                  position: b.position,
                                })),
                              {
                                typeKey: 'cooler-feed',
                                watercoolerId: cooler.id,
                                slot: 'main',
                                position: 0,
                              },
                            ],
                            parentSlug,
                          )
                          .then((blocks) => setForum({ ...forum, contentBlocks: blocks }))
                          .catch((err) => setError(err instanceof Error ? err.message : 'Could not pin feed'));
                      }}
                    >
                      Pin feed
                    </button>
                  )}
                </li>
              ))}
            </ul>
          )}
        </section>
      )}

      {activeSection === 'roles' && canManageRoles && (
        <section className="card list-browser">
          <div className="list-browser-header">
            <div>
              <h2 className="card-title">Roles</h2>
              <p className="card-meta">
                @everyone and Moderator are created with every forum. Add a role or edit an existing one to
                change what it can do here.
              </p>
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
            <ForumRoleRow
              creating
              blockTypes={blockTypes}
              onSave={saveRole}
              onCancel={() => setComposingRole(false)}
            />
          )}
          {roles.map((role) => (
            <ForumRoleRow key={role.id} role={role} blockTypes={blockTypes} onSave={saveRole} />
          ))}
        </section>
      )}

      {activeSection === 'members' && canManageMembers && (
        <section className="card list-browser">
          <div className="list-browser-header">
            <div>
              <h2 className="card-title">Members</h2>
              <p className="card-meta">
                {flairs.length > 0
                  ? 'Edit a row to change roles or flair.'
                  : 'Edit a row to change roles.'}
              </p>
            </div>
            {canListRoles && roles.length > 0 && (
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
            )}
          </div>
          {members.length === 0 && !composingMember && (
            <p className="card-meta">No members with assigned roles yet.</p>
          )}
          {composingMember && (
            <MemberRoleRow
              creating
              roles={roles}
              flairs={flairs}
              emotes={emotes}
              onSave={saveMemberRoles}
              onCancel={() => setComposingMember(false)}
            />
          )}
          {members.map((m) => (
            <MemberRoleRow
              key={m.userId}
              member={m}
              roles={roles}
              flairs={flairs}
              emotes={emotes}
              onSave={saveMemberRoles}
            />
          ))}
        </section>
      )}

      {activeSection === 'members' && canManageMembers && (
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

      {activeSection === 'moderation' && canManagePosts && (
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

      {activeSection === 'you' && (canManageMembers || canManageRoles) && (
        <section className="card list-browser">
          <div className="list-browser-header">
            <div>
              <h2 className="card-title">Flair templates</h2>
              <p className="card-meta">
                These badges are available in this forum. Pick one for yourself below, or assign one on a member row.
              </p>
            </div>
            {canManageMembers && (
              <div className="list-browser-entry-actions">
                <button
                  type="button"
                  className="btn-primary"
                  disabled={composingFlair}
                  onClick={() => setComposingFlair(true)}
                >
                  Add flair
                </button>
              </div>
            )}
          </div>
          {flairs.length === 0 && !composingFlair && <p className="card-meta">No flair templates yet.</p>}
          <ul className="list-browser-entries">
            {composingFlair && (
              <ForumFlairRow
                creating
                emotes={emotes}
                canEdit
                onSave={saveFlairTemplate}
                onCancel={() => setComposingFlair(false)}
              />
            )}
            {flairs.map((f) => (
              <ForumFlairRow
                key={f.id}
                template={f}
                emotes={emotes}
                canEdit={canManageMembers}
                onSave={saveFlairTemplate}
                onRemove={removeFlairTemplate}
              />
            ))}
          </ul>
        </section>
      )}

      {activeSection === 'you' && (
      <section className="card">
        <h2 className="card-title">Your flair</h2>
        {flairs.length === 0 ? (
          <p className="card-meta">No flair templates in this forum yet.</p>
        ) : (
          <form onSubmit={saveMyFlair}>
            <div className="inline-form">
              <select
                id="my-forum-flair"
                className="flair-select"
                value={myFlairId}
                onChange={(e) => setMyFlairId(e.target.value)}
                aria-label="Your forum flair"
              >
                <option value="">None</option>
                {flairs.map((f) => (
                  <option key={f.id} value={f.id}>
                    {forumFlairSelectLabel(f, emotes)}
                  </option>
                ))}
              </select>
              <button type="submit" className="btn-primary">
                Save flair
              </button>
            </div>
          </form>
        )}
      </section>
      )}

      {activeSection === 'you' && forum?.allowAliases && (
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
    </div>
  );
}

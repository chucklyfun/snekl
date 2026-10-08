import { type FormEvent, useEffect, useMemo, useState } from 'react';
import { Link, useParams } from 'react-router';
import { apiFor } from '../api';
import { useAuth } from '../AuthContext';
import { EmoteAutocomplete, EmoteGlyph } from '../components/EmoteAutocomplete';
import { IconButton, IconLink } from '../components/IconButton';
import { InlineEditRow } from '../components/InlineEditRow';
import { iconForBadge, ListEntryRow, type EntryDraft } from '../components/ListEntryRow';
import { TransferOwnershipForm } from '../components/TransferOwnershipForm';
import { type CatalogEmote } from '../emotes';
import { useServerRegistry } from '../ServerRegistryContext';
import type { CuratedList, ListEntry, UserListEntryHit } from '../types';

const LIST_BEHAVIORS = ['Highlight', 'Verify', 'Warn', 'Mute', 'Hide'] as const;

type ListFilter = 'all' | 'mine' | 'subscribed';
type ListDraft = {
  name: string;
  description: string;
  behavior: string;
  emoji: string;
  isPublic: boolean;
};

const PREVIEW_PAGE_SIZE = 8;
const LIST_ID_RE = /[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}/i;

function parseListId(raw: string): string | undefined {
  const trimmed = raw.trim();
  const match = LIST_ID_RE.exec(trimmed);
  if (!match) return undefined;
  if (trimmed.toLowerCase() === match[0].toLowerCase()) return match[0];
  try {
    if (new URL(trimmed).pathname.includes('/lists/')) return match[0];
  } catch {
    if (trimmed.includes('/lists/')) return match[0];
  }
  return undefined;
}

function parseOwnerQuery(raw: string): string | undefined {
  const trimmed = raw.trim();
  if (trimmed.startsWith('@') && trimmed.length > 1) return trimmed.slice(1).toLowerCase();
  return undefined;
}

function listShareUrl(listId: string): string {
  return `${window.location.origin}/lists/${listId}`;
}

function listDraftFrom(list?: CuratedList): ListDraft {
  return {
    name: list?.name ?? '',
    description: list?.description ?? '',
    behavior: list?.defaultBehavior ?? 'Highlight',
    emoji: list?.defaultFlairEmoji ?? '★',
    isPublic: list?.isPublic ?? true,
  };
}

export function ListsPage() {
  const { listId: routeListId } = useParams();
  const { user } = useAuth();
  const { activeServers } = useServerRegistry();
  const contentBaseUrl = activeServers[0]?.baseUrl;
  const api = useMemo(() => (contentBaseUrl ? apiFor(contentBaseUrl) : null), [contentBaseUrl]);
  const [lists, setLists] = useState<CuratedList[]>([]);
  const [emotes, setEmotes] = useState<CatalogEmote[]>([]);
  const [entriesByList, setEntriesByList] = useState<Record<string, ListEntry[]>>({});
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  const [query, setQuery] = useState('');
  const [filter, setFilter] = useState<ListFilter>('all');
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [composingList, setComposingList] = useState(false);
  const [composingMemberFor, setComposingMemberFor] = useState<string | null>(null);
  const [personQuery, setPersonQuery] = useState('');
  const [personHits, setPersonHits] = useState<UserListEntryHit[] | null>(null);
  const [personBusy, setPersonBusy] = useState(false);

  const load = () => {
    if (!api) {
      setLists([]);
      setEmotes([]);
      return;
    }
    api.getLists().then(setLists).catch((e) => setError(e.message));
    api.getEmotes().then(setEmotes).catch(() => setEmotes([]));
  };

  useEffect(load, [api]);

  const lookupId = parseListId(query) ?? (routeListId && LIST_ID_RE.test(routeListId) ? routeListId : undefined);

  useEffect(() => {
    if (!api || !lookupId) return;
    let cancelled = false;
    api
      .getList(lookupId)
      .then((list) => {
        if (cancelled) return;
        setLists((prev) => {
          if (prev.some((item) => item.id === list.id)) {
            return prev.map((item) => (item.id === list.id ? list : item));
          }
          return [list, ...prev];
        });
        setExpandedId(list.id);
      })
      .catch((e) => setError(e instanceof Error ? e.message : 'List not found'));
    return () => {
      cancelled = true;
    };
  }, [api, lookupId]);

  useEffect(() => {
    if (!api || !expandedId) return;
    api
      .getListEntries(expandedId, { page: 1, pageSize: PREVIEW_PAGE_SIZE })
      .then((result) => setEntriesByList((prev) => ({ ...prev, [expandedId]: result.items })))
      .catch((e) => setError(e.message));
  }, [api, expandedId]);

  const visible = useMemo(() => {
    const needle = query.trim().toLowerCase();
    const owner = parseOwnerQuery(query);
    const byId = parseListId(query);
    const filtered = lists.filter((list) => {
      if (filter === 'mine' && !list.isOwner) return false;
      if (filter === 'subscribed' && !list.isViewerSubscribed) return false;
      if (!needle) return true;
      if (byId) return list.id.toLowerCase() === byId.toLowerCase();
      if (owner) return (list.ownerUsername ?? '').toLowerCase() === owner;
      const haystack = `${list.name} ${list.description} ${list.ownerUsername ?? ''}`.toLowerCase();
      return haystack.includes(needle);
    });
    if (filter !== 'subscribed') return filtered;
    return [...filtered].sort((a, b) => (a.viewerPriority ?? 0) - (b.viewerPriority ?? 0));
  }, [filter, lists, query]);

  const winningPersonHitId = personHits?.find((hit) => hit.appliesOnPosts)?.entryId;

  const fail = (err: unknown) => setError(err instanceof Error ? err.message : 'Failed');

  const saveList = async (draft: ListDraft, existing?: CuratedList) => {
    if (!api) return;
    setError('');
    const payload = {
      name: draft.name.trim(),
      description: draft.description,
      defaultBehavior: draft.behavior,
      defaultFlairEmoji: draft.emoji,
      defaultIconUrl: iconForBadge(draft.emoji, emotes),
      isPublic: draft.isPublic,
    };
    if (existing) {
      const updated = await api.updateList(existing.id, payload);
      setLists((prev) => prev.map((list) => (list.id === updated.id ? updated : list)));
      setSuccess('List updated.');
      return;
    }
    const created = await api.createList(payload);
    setLists((prev) => [created, ...prev]);
    setComposingList(false);
    setExpandedId(created.id);
    setSuccess('List created and subscribed.');
  };

  const transferList = async (list: CuratedList, username: string) => {
    if (!api || !list.isOwner) return;
    setError('');
    try {
      const updated = await api.transferList(list.id, username);
      if (!updated.isPublic) {
        setLists((prev) => prev.filter((item) => item.id !== list.id));
        if (expandedId === list.id) setExpandedId(null);
      } else {
        setLists((prev) => prev.map((item) => (item.id === updated.id ? updated : item)));
      }
      setSuccess(`Transferred ${list.name} to @${updated.ownerUsername ?? username}.`);
    } catch (err) {
      fail(err);
      throw err;
    }
  };

  const removeList = async (list: CuratedList) => {
    if (!api || !list.isOwner) return;
    if (!window.confirm(`Delete “${list.name}”? This removes every entry.`)) return;
    setError('');
    try {
      await api.deleteList(list.id);
      setLists((prev) => prev.filter((item) => item.id !== list.id));
      if (expandedId === list.id) setExpandedId(null);
      setSuccess('List deleted.');
    } catch (err) {
      fail(err);
    }
  };

  const toggleSubscribe = async (list: CuratedList) => {
    if (!api) return;
    setError('');
    try {
      if (list.isViewerSubscribed) {
        await api.unsubscribeList(list.id);
        setSuccess(`Unsubscribed from ${list.name}.`);
      } else {
        await api.subscribeList(list.id, list.defaultBehavior);
        setSuccess(`Subscribed to ${list.name}.`);
      }
      load();
    } catch (err) {
      fail(err);
    }
  };

  const copyLink = async (list: CuratedList) => {
    try {
      await navigator.clipboard.writeText(listShareUrl(list.id));
      setSuccess(`Copied link for ${list.name}.`);
    } catch (err) {
      fail(err);
    }
  };

  const bumpPriority = async (list: CuratedList, direction: -1 | 1) => {
    if (!api || !list.isViewerSubscribed) return;
    const ranked = lists
      .filter((item) => item.isViewerSubscribed)
      .sort((a, b) => (a.viewerPriority ?? 0) - (b.viewerPriority ?? 0));
    const index = ranked.findIndex((item) => item.id === list.id);
    const swap = index + direction;
    if (index < 0 || swap < 0 || swap >= ranked.length) return;
    const next = [...ranked];
    [next[index], next[swap]] = [next[swap], next[index]];
    setError('');
    try {
      await api.reorderListSubscriptions(next.map((item) => item.id));
      load();
    } catch (err) {
      fail(err);
    }
  };

  const toggleIgnore = async (list: CuratedList, entry: ListEntry) => {
    if (!api) return;
    setError('');
    try {
      if (entry.isIgnoredByViewer) await api.unignoreListEntry(list.id, entry.id);
      else await api.ignoreListEntry(list.id, entry.id);
      setEntriesByList((prev) => ({
        ...prev,
        [list.id]: (prev[list.id] ?? []).map((item) =>
          item.id === entry.id ? { ...item, isIgnoredByViewer: !entry.isIgnoredByViewer } : item,
        ),
      }));
      setPersonHits((prev) =>
        prev?.map((hit) =>
          hit.entryId === entry.id
            ? {
                ...hit,
                isIgnoredByViewer: !entry.isIgnoredByViewer,
                appliesOnPosts: hit.isViewerSubscribed && Boolean(entry.isIgnoredByViewer),
              }
            : hit,
        ) ?? null,
      );
    } catch (err) {
      fail(err);
    }
  };

  const toggleIgnoreHit = async (hit: UserListEntryHit) => {
    if (!api) return;
    setError('');
    try {
      if (hit.isIgnoredByViewer) await api.unignoreListEntry(hit.listId, hit.entryId);
      else await api.ignoreListEntry(hit.listId, hit.entryId);
      const ignored = !hit.isIgnoredByViewer;
      setPersonHits((prev) =>
        prev?.map((item) =>
          item.entryId === hit.entryId
            ? { ...item, isIgnoredByViewer: ignored, appliesOnPosts: item.isViewerSubscribed && !ignored }
            : item,
        ) ?? null,
      );
      setEntriesByList((prev) => ({
        ...prev,
        [hit.listId]: (prev[hit.listId] ?? []).map((item) =>
          item.id === hit.entryId ? { ...item, isIgnoredByViewer: ignored } : item,
        ),
      }));
    } catch (err) {
      fail(err);
    }
  };

  const toggleSubscribeHit = async (hit: UserListEntryHit) => {
    if (!api || !user) return;
    setError('');
    try {
      if (hit.isViewerSubscribed) {
        await api.unsubscribeList(hit.listId);
        setSuccess(`Unsubscribed from ${hit.listName}.`);
      } else {
        await api.subscribeList(hit.listId, hit.listDefaultBehavior);
        setSuccess(`Subscribed to ${hit.listName}.`);
      }
      load();
      const handle = personQuery.trim() || hit.targetUsername;
      setPersonHits(await api.findListEntriesForUser(handle));
    } catch (err) {
      fail(err);
    }
  };

  const searchPerson = async (e: FormEvent) => {
    e.preventDefault();
    if (!api) return;
    const handle = personQuery.trim();
    if (!handle) return;
    setError('');
    setPersonBusy(true);
    try {
      setPersonHits(await api.findListEntriesForUser(handle));
    } catch (err) {
      fail(err);
    } finally {
      setPersonBusy(false);
    }
  };

  const saveEntry = async (list: CuratedList, draft: EntryDraft, existing?: ListEntry) => {
    if (!api || !list.isOwner) return;
    setError('');
    if (existing) {
      const updated = await api.updateListEntry(list.id, existing.id, {
        label: draft.label.trim(),
        tooltipText: draft.tooltip,
        flairEmoji: draft.emoji,
        flairIconUrl: iconForBadge(draft.emoji, emotes),
      });
      setEntriesByList((prev) => ({
        ...prev,
        [list.id]: (prev[list.id] ?? []).map((item) => (item.id === updated.id ? updated : item)),
      }));
      setSuccess(`Updated @${updated.targetUsername}.`);
      return;
    }
    const target = await api.getUser(draft.username);
    const created = await api.addListEntry(list.id, {
      targetUserId: target.id,
      label: draft.label.trim(),
      tooltipText: draft.tooltip,
      flairEmoji: draft.emoji,
      flairIconUrl: iconForBadge(draft.emoji, emotes),
    });
    setEntriesByList((prev) => ({
      ...prev,
      [list.id]: [created, ...(prev[list.id] ?? []).filter((item) => item.id !== created.id)],
    }));
    setComposingMemberFor(null);
    setSuccess(`Added @${created.targetUsername || target.username}.`);
    load();
  };

  const removeEntry = async (list: CuratedList, entry: ListEntry) => {
    if (!api || !list.isOwner) return;
    setError('');
    try {
      await api.deleteListEntry(list.id, entry.id);
      setEntriesByList((prev) => ({
        ...prev,
        [list.id]: (prev[list.id] ?? []).filter((item) => item.id !== entry.id),
      }));
      load();
    } catch (err) {
      fail(err);
    }
  };

  const toggleExpanded = (listId: string) => {
    setExpandedId((current) => {
      if (current === listId) {
        setComposingMemberFor(null);
        return null;
      }
      return listId;
    });
  };

  return (
    <div>
      <div className="list-browser-header">
        <div>
          <h1 className="page-title">Curated lists</h1>
          <p className="page-subtitle">
            Lists live on this content server. Find by name, @owner, or paste a list link. Subscribe to apply
            badges on posts. Open Subscribed to rank which list wins when a person is on more than one.
          </p>
        </div>
        {user && api && (
          <button
            type="button"
            className="btn-primary"
            disabled={composingList}
            onClick={() => {
              setComposingList(true);
              setExpandedId(null);
            }}
          >
            New list
          </button>
        )}
      </div>

      {error && <div className="error-banner">{error}</div>}
      {!api && <p className="card-meta">Subscribe to a content server on Servers to browse lists.</p>}
      {success && (
        <div className="card" style={{ borderColor: 'var(--accent)', marginBottom: '1rem' }}>
          {success}
        </div>
      )}

      {api && (
        <section className="card list-browser">
          <div className="form-group">
            <label htmlFor="listSearch">Find a list</label>
            <input
              id="listSearch"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="Name, @owner, or list link"
            />
          </div>
          <form className="list-members-search" onSubmit={(e) => void searchPerson(e)}>
            <div className="form-group">
              <label htmlFor="personSearch">Find a person</label>
              <input
                id="personSearch"
                value={personQuery}
                onChange={(e) => setPersonQuery(e.target.value)}
                placeholder="@username"
              />
            </div>
            <button type="submit" className="btn-ghost" disabled={personBusy}>
              Search
            </button>
          </form>
          {personHits && (
            <div className="list-person-hits">
              <h3 className="card-title">
                {personHits.length === 0
                  ? 'No list entries for that user'
                  : `List entries for @${personHits[0].targetUsername}`}
              </h3>
              {personHits.length > 0 && (
                <p className="card-meta">
                  Subscribed lists first, in your priority order. Hide this person on a list, or subscribe to the
                  whole list. The first row that still applies is what shows on their posts.
                </p>
              )}
              <ul className="list-browser-entries">
                {personHits.map((hit) => (
                  <li
                    key={hit.entryId}
                    className={`list-browser-entry${hit.isIgnoredByViewer ? ' is-ignored' : ''}`}
                  >
                    <span>
                      <span className="flair-box list-flair-box">
                        <EmoteGlyph value={hit.flairEmoji} emotes={emotes} />
                      </span>{' '}
                      <strong>{hit.listName}</strong> · {hit.label}
                      {hit.appliesOnPosts && hit.entryId === winningPersonHitId ? (
                        <span className="card-meta"> · shows on posts</span>
                      ) : null}
                      <div className="card-meta">
                        {hit.isViewerSubscribed
                          ? `priority ${(hit.viewerPriority ?? 0) + 1}`
                          : 'not subscribed'}
                        {hit.isIgnoredByViewer ? ' · hidden for you' : ''}
                        {hit.listOwnerUsername ? ` · @${hit.listOwnerUsername}` : ''}
                        {hit.tooltipText ? ` · ${hit.tooltipText}` : ''}
                      </div>
                    </span>
                    <span className="list-browser-entry-actions">
                      <IconLink to={`/lists/${hit.listId}`} icon="rows" label="Open list" />
                      {user && (
                        <IconButton
                          icon={hit.isViewerSubscribed ? 'bookmarkOff' : 'bookmark'}
                          label={hit.isViewerSubscribed ? 'Unsubscribe' : 'Subscribe'}
                          onClick={() => void toggleSubscribeHit(hit)}
                        />
                      )}
                      {user && (
                        <IconButton
                          icon={hit.isIgnoredByViewer ? 'eye' : 'eyeOff'}
                          label={hit.isIgnoredByViewer ? 'Show' : 'Hide'}
                          onClick={() => void toggleIgnoreHit(hit)}
                        />
                      )}
                    </span>
                  </li>
                ))}
              </ul>
            </div>
          )}
          <div className="list-browser-filters">
            {(['all', 'mine', 'subscribed'] as const).map((value) => (
              <button
                key={value}
                type="button"
                className={`btn-ghost${filter === value ? ' is-active' : ''}`}
                onClick={() => setFilter(value)}
              >
                {value === 'all' ? 'All' : value === 'mine' ? 'Mine' : 'Subscribed'}
              </button>
            ))}
          </div>
          {visible.length === 0 && !composingList && <p className="card-meta">No lists match.</p>}
          <datalist id="label-suggestions">
            <option value="Verified" />
            <option value="Celebrity" />
            <option value="Public figure" />
            <option value="Trusted contributor" />
            <option value="Warning" />
            <option value="Known scammer" />
          </datalist>
          <ul className="list-browser-rows">
            {composingList && (
              <li className="list-browser-item">
                <ListDetailRow
                  creating
                  emotes={emotes}
                  onSave={saveList}
                  onCancel={() => setComposingList(false)}
                />
              </li>
            )}
            {visible.map((list) => {
              const expanded = expandedId === list.id;
              const members = entriesByList[list.id] ?? [];
              return (
                <li key={list.id} className={`list-browser-item${expanded ? ' is-expanded' : ''}`}>
                  <ListDetailRow
                    list={list}
                    emotes={emotes}
                    expanded={expanded}
                    canEdit={list.isOwner}
                    onToggle={() => toggleExpanded(list.id)}
                    onSave={saveList}
                    onDelete={() => void removeList(list)}
                    onCopyLink={() => void copyLink(list)}
                    onPriorityUp={list.isViewerSubscribed ? () => void bumpPriority(list, -1) : undefined}
                    onPriorityDown={list.isViewerSubscribed ? () => void bumpPriority(list, 1) : undefined}
                    onSubscribe={user ? () => void toggleSubscribe(list) : undefined}
                  />
                  {expanded && (
                    <div className="list-browser-members">
                      <div className="list-browser-members-head">
                        <h3 className="card-title">Members</h3>
                        <span className="list-browser-entry-actions">
                          <Link className="btn-ghost" to={`/lists/${list.id}/members`}>
                            Manage Users
                          </Link>
                          {list.isOwner && (
                            <button
                              type="button"
                              className="btn-primary"
                              disabled={composingMemberFor === list.id}
                              onClick={() => setComposingMemberFor(list.id)}
                            >
                              Add user
                            </button>
                          )}
                        </span>
                      </div>
                      {list.isOwner && (
                        <TransferOwnershipForm
                          id={list.id}
                          resourceName={list.name}
                          confirmDetail={
                            list.isPublic
                              ? 'You will no longer be able to edit it.'
                              : 'This list is private, so you will lose access to it.'
                          }
                          onTransfer={(username) => transferList(list, username)}
                        />
                      )}
                      {members.length === 0 && composingMemberFor !== list.id && (
                        <p className="card-meta">No one is on this list yet.</p>
                      )}
                      {list.entryCount > members.length && (
                        <p className="card-meta">
                          Showing the first {members.length} of {list.entryCount}. Open Manage Users for the full
                          paged editor.
                        </p>
                      )}
                      <ul className="list-browser-entries">
                        {composingMemberFor === list.id && (
                          <ListEntryRow
                            creating
                            defaultEmoji={list.defaultFlairEmoji}
                            emotes={emotes}
                            onSave={(draft) => saveEntry(list, draft)}
                            onCancel={() => setComposingMemberFor(null)}
                          />
                        )}
                        {members.map((entry) => (
                          <ListEntryRow
                            key={entry.id}
                            entry={entry}
                            defaultEmoji={list.defaultFlairEmoji}
                            emotes={emotes}
                            canEdit={list.isOwner}
                            canIgnore={!!user}
                            onSave={(draft, existing) => saveEntry(list, draft, existing)}
                            onRemove={(item) => void removeEntry(list, item)}
                            onIgnore={(item) => void toggleIgnore(list, item)}
                          />
                        ))}
                      </ul>
                    </div>
                  )}
                </li>
              );
            })}
          </ul>
        </section>
      )}
    </div>
  );
}

function ListDetailRow({
  list,
  creating = false,
  emotes,
  expanded = false,
  canEdit = false,
  onToggle,
  onSave,
  onCancel,
  onDelete,
  onCopyLink,
  onPriorityUp,
  onPriorityDown,
  onSubscribe,
}: {
  list?: CuratedList;
  creating?: boolean;
  emotes: CatalogEmote[];
  expanded?: boolean;
  canEdit?: boolean;
  onToggle?: () => void;
  onSave?: (draft: ListDraft, existing?: CuratedList) => Promise<void>;
  onCancel?: () => void;
  onDelete?: () => void;
  onCopyLink?: () => void;
  onPriorityUp?: () => void;
  onPriorityDown?: () => void;
  onSubscribe?: () => void;
}) {
  const [draft, setDraft] = useState(listDraftFrom(list));
  const fieldId = list?.id ?? 'new';

  useEffect(() => {
    setDraft(listDraftFrom(list));
  }, [list]);

  return (
    <InlineEditRow
      as="div"
      className="list-browser-item-head"
      creating={creating}
      canEdit={canEdit}
      includeEdit={false}
      includeRemove={false}
      view={
        list ? (
          <button type="button" className="list-browser-row" onClick={onToggle}>
            <span className="list-browser-row-title">
              <span className="flair-box list-flair-box">
                <EmoteGlyph value={list.defaultFlairEmoji} emotes={emotes} />
              </span>
              <strong>{list.name}</strong>
            </span>
            <span className="card-meta">
              {list.entryCount} members · {list.subscriberCount} subscribers · {list.defaultBehavior}
              {list.isOwner ? ' · owner' : ''}
              {list.isViewerSubscribed ? ' · subscribed' : ''}
              {list.viewerPriority != null ? ` · priority ${list.viewerPriority + 1}` : ''}
              {list.ownerUsername ? ` · @${list.ownerUsername}` : ''}
              {list.isPublic ? ' · public' : ' · private'}
            </span>
            {list.description && <span className="card-meta">{list.description}</span>}
          </button>
        ) : null
      }
      viewActions={(startEdit) =>
        list ? (
          <>
            <IconLink to={`/lists/${list.id}/members`} icon="users" label="Manage users" />
            {onCopyLink && <IconButton icon="link" label="Copy link" onClick={onCopyLink} />}
            {onPriorityUp && <IconButton icon="chevronUp" label="Priority up" onClick={onPriorityUp} />}
            {onPriorityDown && (
              <IconButton icon="chevronDown" label="Priority down" onClick={onPriorityDown} />
            )}
            <IconButton icon="rows" label={expanded ? 'Hide members' : 'Members'} onClick={onToggle} />
            {canEdit && <IconButton icon="pencil" label="Edit" onClick={startEdit} />}
            {onSubscribe && (
              <IconButton
                icon={list.isViewerSubscribed ? 'bookmarkOff' : 'bookmark'}
                label={list.isViewerSubscribed ? 'Unsubscribe' : 'Subscribe'}
                onClick={onSubscribe}
              />
            )}
          </>
        ) : null
      }
      fieldsClassName="list-detail-fields"
      extraEditActions={
        list && onDelete ? <IconButton icon="trash" label="Delete" variant="danger" onClick={onDelete} /> : null
      }
      onSave={async () => {
        if (!onSave) return;
        await onSave(draft, list);
      }}
      onCancel={() => {
        setDraft(listDraftFrom(list));
        onCancel?.();
      }}
    >
      <div className="form-group">
        <label htmlFor={`list-name-${fieldId}`}>Name</label>
        <input
          id={`list-name-${fieldId}`}
          value={draft.name}
          onChange={(e) => setDraft({ ...draft, name: e.target.value })}
          required
          autoFocus={creating}
        />
      </div>
      <div className="form-group">
        <label htmlFor={`list-desc-${fieldId}`}>Description</label>
        <input
          id={`list-desc-${fieldId}`}
          value={draft.description}
          onChange={(e) => setDraft({ ...draft, description: e.target.value })}
        />
      </div>
      <div className="form-group">
        <label htmlFor={`list-behavior-${fieldId}`}>Behavior</label>
        <select
          id={`list-behavior-${fieldId}`}
          value={draft.behavior}
          onChange={(e) => setDraft({ ...draft, behavior: e.target.value })}
        >
          {LIST_BEHAVIORS.map((behavior) => (
            <option key={behavior} value={behavior}>
              {behavior}
            </option>
          ))}
        </select>
      </div>
      <div className="form-group list-detail-badge">
        <label htmlFor={`list-emoji-${fieldId}`}>Badge</label>
        <EmoteAutocomplete
          emotes={emotes}
          value={draft.emoji}
          onChange={(emoji) => setDraft({ ...draft, emoji })}
          mode="field"
        >
          {(bind) => (
            <input
              id={`list-emoji-${fieldId}`}
              ref={bind.ref}
              value={draft.emoji}
              onChange={bind.onChange}
              onKeyDown={bind.onKeyDown}
              onSelect={bind.onSelect}
              onClick={bind.onClick}
              onBlur={bind.onBlur}
              maxLength={40}
            />
          )}
        </EmoteAutocomplete>
      </div>
      <div className="form-group list-detail-public">
        <label htmlFor={`list-public-${fieldId}`}>Visibility</label>
        <label className="list-detail-public-toggle" htmlFor={`list-public-${fieldId}`}>
          <input
            id={`list-public-${fieldId}`}
            type="checkbox"
            checked={draft.isPublic}
            onChange={(e) => setDraft({ ...draft, isPublic: e.target.checked })}
          />
          {draft.isPublic ? 'Public' : 'Private'}
        </label>
      </div>
    </InlineEditRow>
  );
}

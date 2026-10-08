import { type FormEvent, useEffect, useMemo, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { apiFor } from '../api';
import { useAuth } from '../AuthContext';
import { type CatalogEmote } from '../emotes';
import { iconForBadge, ListEntryRow, type EntryDraft } from '../components/ListEntryRow';
import { TransferOwnershipForm } from '../components/TransferOwnershipForm';
import { useServerRegistry } from '../ServerRegistryContext';
import type { CuratedList, ListEntry } from '../types';

const PAGE_SIZE = 20;

export function ListMembersPage() {
  const { listId } = useParams();
  const navigate = useNavigate();
  const { user } = useAuth();
  const { activeServers } = useServerRegistry();
  const contentBaseUrl = activeServers[0]?.baseUrl;
  const api = useMemo(() => (contentBaseUrl ? apiFor(contentBaseUrl) : null), [contentBaseUrl]);
  const [list, setList] = useState<CuratedList | null>(null);
  const [emotes, setEmotes] = useState<CatalogEmote[]>([]);
  const [entries, setEntries] = useState<ListEntry[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [query, setQuery] = useState('');
  const [appliedQuery, setAppliedQuery] = useState('');
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  const [composing, setComposing] = useState(false);
  const [reloadNonce, setReloadNonce] = useState(0);

  const pageCount = Math.max(1, Math.ceil(total / PAGE_SIZE));

  const loadList = () => {
    if (!api || !listId) {
      setList(null);
      return;
    }
    api.getList(listId).then(setList).catch((e) => setError(e.message));
    api.getEmotes().then(setEmotes).catch(() => setEmotes([]));
  };

  const loadEntries = () => {
    if (!api || !listId) {
      setEntries([]);
      setTotal(0);
      return;
    }
    api
      .getListEntries(listId, { page, pageSize: PAGE_SIZE, q: appliedQuery || undefined })
      .then((result) => {
        setEntries(result.items);
        setTotal(result.totalCount);
        if (result.page !== page) setPage(result.page);
      })
      .catch((e) => setError(e.message));
  };

  useEffect(loadList, [api, listId]);
  useEffect(loadEntries, [api, appliedQuery, listId, page, reloadNonce]);

  const fail = (err: unknown) => setError(err instanceof Error ? err.message : 'Failed');

  const toggleSubscribe = async () => {
    if (!api || !list) return;
    setError('');
    try {
      if (list.isViewerSubscribed) {
        await api.unsubscribeList(list.id);
        setSuccess(`Unsubscribed from ${list.name}.`);
      } else {
        await api.subscribeList(list.id, list.defaultBehavior);
        setSuccess(`Subscribed to ${list.name}.`);
      }
      loadList();
    } catch (err) {
      fail(err);
    }
  };

  const copyLink = async () => {
    if (!listId) return;
    try {
      await navigator.clipboard.writeText(`${window.location.origin}/lists/${listId}`);
      setSuccess('Copied list link.');
    } catch (err) {
      fail(err);
    }
  };

  const transferList = async (username: string) => {
    if (!api || !list?.isOwner || !listId) return;
    setError('');
    try {
      const updated = await api.transferList(listId, username);
      if (!updated.isPublic) {
        navigate('/lists');
        return;
      }
      setList(updated);
      setSuccess(`Transferred ${list.name} to @${updated.ownerUsername ?? username}.`);
    } catch (err) {
      fail(err);
      throw err;
    }
  };

  const search = (e: FormEvent) => {
    e.preventDefault();
    setPage(1);
    setAppliedQuery(query.trim());
  };

  const saveEntry = async (draft: EntryDraft, existing?: ListEntry) => {
    if (!api || !list?.isOwner || !listId) return;
    setError('');
    if (existing) {
      const updated = await api.updateListEntry(listId, existing.id, {
        label: draft.label.trim(),
        tooltipText: draft.tooltip,
        flairEmoji: draft.emoji,
        flairIconUrl: iconForBadge(draft.emoji, emotes),
      });
      setEntries((prev) => prev.map((item) => (item.id === updated.id ? updated : item)));
      setSuccess(`Updated @${updated.targetUsername}.`);
      return;
    }
    const target = await api.getUser(draft.username);
    const created = await api.addListEntry(listId, {
      targetUserId: target.id,
      label: draft.label.trim(),
      tooltipText: draft.tooltip,
      flairEmoji: draft.emoji,
      flairIconUrl: iconForBadge(draft.emoji, emotes),
    });
    setComposing(false);
    setSuccess(`Added @${created.targetUsername || target.username}.`);
    setPage(1);
    setAppliedQuery('');
    setQuery('');
    setReloadNonce((n) => n + 1);
    loadList();
  };

  const removeEntry = async (entry: ListEntry) => {
    if (!api || !list?.isOwner || !listId) return;
    setError('');
    try {
      await api.deleteListEntry(listId, entry.id);
      setSuccess(`Removed @${entry.targetUsername}.`);
      loadList();
      setReloadNonce((n) => n + 1);
    } catch (err) {
      fail(err);
    }
  };

  const toggleIgnore = async (entry: ListEntry) => {
    if (!api || !listId) return;
    setError('');
    try {
      if (entry.isIgnoredByViewer) await api.unignoreListEntry(listId, entry.id);
      else await api.ignoreListEntry(listId, entry.id);
      setEntries((prev) =>
        prev.map((item) =>
          item.id === entry.id ? { ...item, isIgnoredByViewer: !entry.isIgnoredByViewer } : item,
        ),
      );
    } catch (err) {
      fail(err);
    }
  };

  if (!listId) return <p className="card-meta">Missing list.</p>;

  return (
    <div>
      <div className="list-browser-header">
        <div>
          <p className="card-meta">
            <Link to="/lists">Curated lists</Link>
          </p>
          <h1 className="page-title">{list ? `Manage users · ${list.name}` : 'Manage users'}</h1>
          <p className="page-subtitle">
            {list
              ? `${list.entryCount} members · ${list.defaultBehavior}${list.isOwner ? ' · owner' : ''}`
              : 'Paged member editor for this list.'}
          </p>
        </div>
        <div className="list-browser-entry-actions">
          <button type="button" className="btn-ghost" onClick={() => void copyLink()}>
            Copy link
          </button>
          {user && list && (
            <button type="button" className="btn-ghost" onClick={() => void toggleSubscribe()}>
              {list.isViewerSubscribed ? 'Unsubscribe' : 'Subscribe'}
            </button>
          )}
          {list?.isOwner && (
            <button
              type="button"
              className="btn-primary"
              disabled={composing}
              onClick={() => setComposing(true)}
            >
              Add user
            </button>
          )}
        </div>
      </div>

      {error && <div className="error-banner">{error}</div>}
      {!api && <p className="card-meta">Subscribe to a content server on Servers to manage list members.</p>}
      {success && (
        <div className="card" style={{ borderColor: 'var(--accent)', marginBottom: '1rem' }}>
          {success}
        </div>
      )}

      {api && (
        <section className="card list-browser">
          {list?.isOwner && (
            <TransferOwnershipForm
              id={list.id}
              resourceName={list.name}
              confirmDetail={
                list.isPublic
                  ? 'You will no longer be able to edit it.'
                  : 'This list is private, so you will lose access to it.'
              }
              onTransfer={transferList}
            />
          )}
          <form className="list-members-search" onSubmit={search}>
            <div className="form-group">
              <label htmlFor="memberSearch">Search members</label>
              <input
                id="memberSearch"
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                placeholder="Username or label"
              />
            </div>
            <button type="submit" className="btn-ghost">
              Search
            </button>
          </form>
          <datalist id="label-suggestions">
            <option value="Verified" />
            <option value="Celebrity" />
            <option value="Public figure" />
            <option value="Trusted contributor" />
            <option value="Warning" />
            <option value="Known scammer" />
          </datalist>
          {entries.length === 0 && !composing && <p className="card-meta">No members on this page.</p>}
          <ul className="list-browser-entries">
            {composing && list && (
              <ListEntryRow
                creating
                defaultEmoji={list.defaultFlairEmoji}
                emotes={emotes}
                onSave={saveEntry}
                onCancel={() => setComposing(false)}
              />
            )}
            {entries.map((entry) => (
              <ListEntryRow
                key={entry.id}
                entry={entry}
                defaultEmoji={list?.defaultFlairEmoji}
                emotes={emotes}
                canEdit={!!list?.isOwner}
                canIgnore={!!user}
                onSave={saveEntry}
                onRemove={(item) => void removeEntry(item)}
                onIgnore={(item) => void toggleIgnore(item)}
              />
            ))}
          </ul>
          {total > PAGE_SIZE && (
            <div className="list-members-pager">
              <button
                type="button"
                className="btn-ghost"
                disabled={page <= 1}
                onClick={() => setPage((current) => Math.max(1, current - 1))}
              >
                Previous
              </button>
              <span className="card-meta">
                Page {page} of {pageCount} · {total} members
              </span>
              <button
                type="button"
                className="btn-ghost"
                disabled={page >= pageCount}
                onClick={() => setPage((current) => current + 1)}
              >
                Next
              </button>
            </div>
          )}
        </section>
      )}
    </div>
  );
}

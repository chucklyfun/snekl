import { type FormEvent, useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { createContentClient, hasPermission } from '../api';
import { ChangeHistoryList } from '../components/ChangeHistoryList';
import { useAuth } from '../AuthContext';
import { useServerRegistry } from '../ServerRegistryContext';
import {
  decodeServerDid,
  forumPath,
  resolveServerBaseUrl,
  watercoolerPath,
} from '../serverDid';
import { WatercoolerModeFields } from '../components/WatercoolerModeFields';
import type { Watercooler } from '../types';
import { isForumPending } from '../createForumAccess';

export function WatercoolerSettingsPage() {
  const { serverDid: encodedDid, slug, parentSlug, coolerSlug } = useParams<{
    serverDid: string;
    slug?: string;
    parentSlug?: string;
    coolerSlug?: string;
  }>();
  const { user } = useAuth();
  const { resolveBaseUrl } = useServerRegistry();
  const navigate = useNavigate();
  const serverDid = encodedDid ? decodeServerDid(encodedDid) : '';
  const baseUrl = resolveServerBaseUrl(serverDid, resolveBaseUrl);
  const client = useMemo(() => (baseUrl ? createContentClient(baseUrl) : null), [baseUrl]);

  const isNestedForum = Boolean(parentSlug && slug && coolerSlug);
  const coolerName = coolerSlug ?? slug ?? '';
  const forumSlug = coolerSlug ? slug : undefined;
  const forumParentSlug = isNestedForum ? parentSlug : undefined;

  const [cooler, setCooler] = useState<Watercooler | null>(null);
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [allowUpvotes, setAllowUpvotes] = useState(false);
  const [newestAtBottom, setNewestAtBottom] = useState(true);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  const [loading, setLoading] = useState(true);

  const load = useCallback(async () => {
    if (!client || !coolerName) return;
    const parentForum = forumSlug ? await client.getForum(forumSlug, forumParentSlug) : null;
    const row = await client.getWatercooler(coolerName, parentForum?.id);
    setCooler(row);
    setName(row.name);
    setDescription(row.description);
    setAllowUpvotes(row.allowUpvotes);
    setNewestAtBottom(row.newestAtBottom);
  }, [client, coolerName, forumSlug, forumParentSlug]);

  const loadHistory = useCallback(() => {
    if (!client || !coolerName) return Promise.resolve([]);
    return client.getWatercoolerHistory(coolerName, cooler?.parentForumId ?? undefined);
  }, [client, coolerName, cooler?.parentForumId]);

  useEffect(() => {
    setError('');
    setLoading(true);
    void load()
      .catch((err) => setError(err instanceof Error ? err.message : 'Failed to load'))
      .finally(() => setLoading(false));
  }, [load]);

  const canManage =
    Boolean(cooler?.viewerCanManage) || hasPermission(cooler?.viewerPermissions, 'ManageWatercooler');
  const canViewHistory = hasPermission(cooler?.viewerPermissions, 'ViewHistory');
  const canApprove = Boolean(cooler?.viewerCanApprove);
  const canAccess = canManage || canApprove || canViewHistory;
  const roomPath = cooler
    ? watercoolerPath(serverDid, cooler.slug, cooler.parentForumSlug ?? undefined, forumParentSlug)
    : '';

  if (!user) {
    return (
      <div className="card">
        <Link to="/login">Log in</Link> to manage this watercooler.
      </div>
    );
  }

  if (!baseUrl) return <div className="error-banner">Unknown content server.</div>;
  if (loading && !cooler) return <p>Loading settings…</p>;
  if (error && !cooler) return <div className="error-banner">{error}</div>;
  if (!canAccess) {
    return <div className="error-banner">You do not have permission to manage this watercooler.</div>;
  }

  const save = async (e: FormEvent) => {
    e.preventDefault();
    if (!client || !cooler || !canManage) return;
    setError('');
    setSuccess('');
    try {
      const updated = await client.updateWatercooler(
        cooler.slug,
        { name: name.trim(), description, allowUpvotes, newestAtBottom },
        cooler.parentForumId ?? undefined,
      );
      setCooler(updated);
      setName(updated.name);
      setDescription(updated.description);
      setAllowUpvotes(updated.allowUpvotes);
      setNewestAtBottom(updated.newestAtBottom);
      setSuccess('Saved.');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save');
    }
  };

  return (
    <div>
      <Link to={roomPath} style={{ color: 'var(--muted)' }}>
        ← {cooler?.name ?? 'Watercooler'}
      </Link>
      <h1 className="page-title">Watercooler settings</h1>
      {cooler?.parentForumSlug && (
        <p className="page-subtitle">
          Under{' '}
          <Link to={forumPath(serverDid, cooler.parentForumSlug, forumParentSlug)}>
            {cooler.parentForumName ?? cooler.parentForumSlug}
          </Link>
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

      {canManage && (
        <form className="card" onSubmit={save}>
          <h2 className="card-title">Room</h2>
          <div className="form-group">
            <label htmlFor="wc-settings-name">Name</label>
            <input id="wc-settings-name" value={name} onChange={(e) => setName(e.target.value)} required />
          </div>
          <div className="form-group">
            <label htmlFor="wc-settings-desc">Description</label>
            <textarea id="wc-settings-desc" value={description} onChange={(e) => setDescription(e.target.value)} />
          </div>
          <WatercoolerModeFields
            idPrefix="wc-settings"
            allowUpvotes={allowUpvotes}
            newestAtBottom={newestAtBottom}
            onAllowUpvotesChange={setAllowUpvotes}
            onNewestAtBottomChange={setNewestAtBottom}
          />
          <button type="submit" className="btn-primary">
            Save
          </button>
        </form>
      )}

      {isForumPending(cooler) && canApprove && client && cooler && (
        <section className="card">
          <h2 className="card-title">Approval</h2>
          <p className="card-meta">This plaza is hidden until someone with CreateWatercooler approves it.</p>
          <div className="server-row-actions">
            <button
              type="button"
              className="btn-primary"
              onClick={() =>
                void client
                  .approveWatercooler(cooler.slug, cooler.parentForumId ?? undefined)
                  .then((updated) => {
                    setCooler(updated);
                    setSuccess(`Approved ${updated.name}.`);
                  })
                  .catch((err) => setError(err instanceof Error ? err.message : 'Approve failed'))
              }
            >
              Approve
            </button>
            <button
              type="button"
              className="btn-ghost"
              onClick={() =>
                void client
                  .rejectWatercooler(cooler.slug, cooler.parentForumId ?? undefined)
                  .then(() => navigate('/'))
                  .catch((err) => setError(err instanceof Error ? err.message : 'Reject failed'))
              }
            >
              Reject
            </button>
          </div>
        </section>
      )}

      {canManage && client && cooler && (
        <section className="card">
          <h2 className="card-title">Danger zone</h2>
          <button
            type="button"
            className="btn-ghost"
            onClick={() => {
              if (!window.confirm(`Delete “${cooler.name}”? This cannot be undone.`)) return;
              void client
                .deleteWatercooler(cooler.slug, cooler.parentForumId ?? undefined)
                .then(() =>
                  navigate(
                    cooler.parentForumSlug
                      ? forumPath(serverDid, cooler.parentForumSlug, forumParentSlug)
                      : '/',
                  ),
                )
                .catch((err) => setError(err instanceof Error ? err.message : 'Delete failed'));
            }}
          >
            Delete watercooler
          </button>
        </section>
      )}
    </div>
  );
}

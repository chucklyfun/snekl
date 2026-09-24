import { type FormEvent, useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router';
import { createContentClient } from '../api';
import { slugifyForumName, type EligibleServer } from '../createForumEligibility';
import { resolveServerBaseUrl } from '../serverDid';
import { useServerRegistry } from '../ServerRegistryContext';
import type { Forum } from '../types';

type CreateForumComposerProps = {
  eligibleServers: EligibleServer[];
  checkErrors: string[];
  permissionsLoading: boolean;
  urlServerDid?: string;
  urlServerCanCreate: boolean | null;
  urlServerCanRequest: boolean | null;
  initialParentForumId?: string;
  showHeading?: boolean;
  onCreated: (forum: Forum, serverDid: string) => void;
  onCancel?: () => void;
};

export function CreateForumComposer({
  eligibleServers,
  checkErrors,
  permissionsLoading,
  urlServerDid,
  urlServerCanCreate,
  urlServerCanRequest,
  initialParentForumId = '',
  showHeading = true,
  onCreated,
  onCancel,
}: CreateForumComposerProps) {
  const { resolveBaseUrl } = useServerRegistry();
  const needsServerPicker = !urlServerDid;
  const [selectedServerDid, setSelectedServerDid] = useState(urlServerDid ?? '');
  const [name, setName] = useState('');
  const [slug, setSlug] = useState('');
  const [description, setDescription] = useState('');
  const [error, setError] = useState('');
  const [parentForums, setParentForums] = useState<Forum[]>([]);
  const [parentForumId, setParentForumId] = useState(initialParentForumId);
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    if (urlServerDid) setSelectedServerDid(urlServerDid);
  }, [urlServerDid]);

  useEffect(() => {
    setSelectedServerDid((current) => {
      if (urlServerDid) return urlServerDid;
      if (current && eligibleServers.some((s) => s.serverDid === current)) return current;
      return eligibleServers[0]?.serverDid ?? '';
    });
  }, [eligibleServers, urlServerDid]);

  const serverDid = urlServerDid || selectedServerDid;
  const baseUrl = serverDid ? resolveServerBaseUrl(serverDid, resolveBaseUrl) : undefined;
  const client = useMemo(() => (baseUrl ? createContentClient(baseUrl) : null), [baseUrl]);
  const isSubforum = Boolean(parentForumId);

  const selectedEligible = eligibleServers.find((s) => s.serverDid === serverDid);
  const canCreateTopLevel = urlServerDid
    ? urlServerCanCreate === true
    : (selectedEligible?.canCreateTopLevel ?? false);
  const canRequestTopLevel = urlServerDid
    ? urlServerCanRequest === true
    : (selectedEligible?.canRequestTopLevel ?? false);
  const canSubmitTopLevel = canCreateTopLevel || canRequestTopLevel;

  useEffect(() => {
    if (!client) {
      setParentForums([]);
      return;
    }
    client
      .getForums()
      .then((forums) => setParentForums(forums.filter((f) => f.approvalStatus !== 'pending')))
      .catch(() => setParentForums([]));
  }, [client]);

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    if (!client || !serverDid) return;
    setError('');
    setSubmitting(true);
    try {
      const forum = await client.createForum(
        name.trim(),
        slug.trim().toLowerCase(),
        description.trim(),
        isSubforum ? parentForumId : undefined,
      );
      onCreated(forum, serverDid);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create forum');
    } finally {
      setSubmitting(false);
    }
  };

  const handleNameChange = (value: string) => {
    setName(value);
    if (!slug || slug === slugifyForumName(name)) {
      setSlug(slugifyForumName(value));
    }
  };

  if (permissionsLoading) {
    return <p>Checking server permissions…</p>;
  }

  if (needsServerPicker && eligibleServers.length === 0) {
    return (
      <div className="error-banner">
        {checkErrors.length > 0 ? (
          <>
            Could not confirm create-forum permission on connected servers.
            <ul>
              {checkErrors.map((msg) => (
                <li key={msg}>{msg}</li>
              ))}
            </ul>
          </>
        ) : (
          <>
            You do not have Super User, CreateForum, or RequestCreateForum on any connected origin.
            Ask a Super User to assign you a role, or <Link to="/servers">subscribe to a server</Link>{' '}
            (and add your hub under Federations so sibling origins like Beta appear).
          </>
        )}
      </div>
    );
  }

  if (!serverDid || !baseUrl || !client) {
    return <div className="error-banner">Unknown content server.</div>;
  }

  if (!isSubforum && !canSubmitTopLevel) {
    return (
      <div className="error-banner">
        You do not have a server role with CreateForum or RequestCreateForum on this origin.
      </div>
    );
  }

  const title = isSubforum ? 'Create subforum' : canCreateTopLevel ? 'Create forum' : 'Request a forum';

  return (
    <>
      {showHeading && <h1 className="page-title">{title}</h1>}

      {needsServerPicker && eligibleServers.length > 0 && (
        <div className="form-group">
          <label htmlFor="server">Server</label>
          <select
            id="server"
            value={serverDid}
            onChange={(e) => {
              setSelectedServerDid(e.target.value);
              setParentForumId('');
              setError('');
            }}
            required
          >
            {eligibleServers.map((server) => (
              <option key={server.serverDid} value={server.serverDid}>
                {server.name}
                {server.createForumRoles.length > 0 ? ` · ${server.createForumRoles.join(', ')}` : ''}
                {server.status === 'favorite' ? ' (favorite)' : ''}
              </option>
            ))}
          </select>
        </div>
      )}

      {checkErrors.length > 0 && (
        <div className="error-banner" style={{ marginBottom: '1rem' }}>
          Some servers could not be checked:
          <ul>
            {checkErrors.map((msg) => (
              <li key={msg}>{msg}</li>
            ))}
          </ul>
        </div>
      )}

      {error && <div className="error-banner">{error}</div>}
      <form onSubmit={(e) => void handleSubmit(e)}>
        {!isSubforum && canRequestTopLevel && (
          <p className="card-meta">
            Your request will be hidden until someone with CreateForum approves it.
          </p>
        )}
        {!isSubforum && canCreateTopLevel && parentForums.length > 0 && (
          <div className="form-group">
            <label htmlFor="parent">Parent forum (optional — creates a subforum)</label>
            <select id="parent" value={parentForumId} onChange={(e) => setParentForumId(e.target.value)}>
              <option value="">None (top-level forum)</option>
              {parentForums.map((f) => (
                <option key={f.id} value={f.id}>
                  {f.name}
                </option>
              ))}
            </select>
          </div>
        )}
        {isSubforum && <p className="card-meta">Creating subforum under parent forum.</p>}
        <div className="form-group">
          <label htmlFor="forum-name">Name</label>
          <input id="forum-name" value={name} onChange={(e) => handleNameChange(e.target.value)} required />
        </div>
        <div className="form-group">
          <label htmlFor="forum-slug">URL slug</label>
          <input
            id="forum-slug"
            value={slug}
            onChange={(e) => setSlug(e.target.value)}
            required
            pattern="[a-z0-9-]+"
          />
        </div>
        <div className="form-group">
          <label htmlFor="forum-description">Description</label>
          <textarea
            id="forum-description"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
          />
        </div>
        <div className="modal-actions">
          {onCancel && (
            <button type="button" className="btn-ghost" onClick={onCancel} disabled={submitting}>
              Cancel
            </button>
          )}
          <button type="submit" className="btn-primary" disabled={submitting}>
            {isSubforum ? 'Create subforum' : canCreateTopLevel ? 'Create forum' : 'Submit request'}
          </button>
        </div>
      </form>
    </>
  );
}

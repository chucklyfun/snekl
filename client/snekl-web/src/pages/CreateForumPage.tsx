import { type FormEvent, useEffect, useMemo, useState } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router';
import { createContentClient, hasPermission } from '../api';
import { useAuth } from '../AuthContext';
import { useServerRegistry } from '../ServerRegistryContext';
import { decodeServerDid, forumPath, resolveServerBaseUrl } from '../serverDid';
import type { Forum } from '../types';

export function CreateForumPage() {
  const { serverDid: encodedDid } = useParams<{ serverDid: string }>();
  const [searchParams] = useSearchParams();
  const { user, isSuperUser, serverPermissions } = useAuth();
  const { resolveBaseUrl } = useServerRegistry();
  const navigate = useNavigate();
  const [name, setName] = useState('');
  const [slug, setSlug] = useState('');
  const [description, setDescription] = useState('');
  const [error, setError] = useState('');
  const [parentForums, setParentForums] = useState<Forum[]>([]);
  const [parentForumId, setParentForumId] = useState(searchParams.get('parent') ?? '');

  const serverDid = encodedDid ? decodeServerDid(encodedDid) : '';
  const baseUrl = resolveServerBaseUrl(serverDid, resolveBaseUrl);
  const client = useMemo(() => (baseUrl ? createContentClient(baseUrl) : null), [baseUrl]);
  const parentSlugParam = searchParams.get('parentSlug') ?? undefined;
  const isSubforum = Boolean(parentForumId);

  const canCreateTopLevel =
    isSuperUser || hasPermission(serverPermissions, 'CreateForum');

  useEffect(() => {
    if (!client) return;
    client.getForums().then(setParentForums).catch(() => {});
  }, [client]);

  if (!user) {
    return (
      <div className="card">
        <p>
          Please <a href="/login">log in</a> to create a forum.
        </p>
      </div>
    );
  }

  if (!baseUrl || !client) {
    return <div className="error-banner">Unknown content server.</div>;
  }

  if (!isSubforum && !canCreateTopLevel) {
    return (
      <div className="error-banner">
        You do not have permission to create top-level forums. Ask a Super User to grant you the CreateForum role.
      </div>
    );
  }

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    try {
      const forum = await client.createForum(
        name.trim(),
        slug.trim().toLowerCase(),
        description.trim(),
        isSubforum ? parentForumId : undefined,
      );
      navigate(forumPath(serverDid, forum.slug, parentSlugParam ?? forum.parentSlug ?? undefined));
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create forum');
    }
  };

  const handleNameChange = (value: string) => {
    setName(value);
    if (!slug || slug === slugify(name)) {
      setSlug(slugify(value));
    }
  };

  return (
    <div className="card" style={{ maxWidth: 560 }}>
      <h1 className="page-title">{isSubforum ? 'Create subforum' : 'Create forum'}</h1>
      {error && <div className="error-banner">{error}</div>}
      <form onSubmit={handleSubmit}>
        {!isSubforum && parentForums.length > 0 && (
          <div className="form-group">
            <label htmlFor="parent">Parent forum (optional — creates a subforum)</label>
            <select
              id="parent"
              value={parentForumId}
              onChange={(e) => setParentForumId(e.target.value)}
            >
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
          <label htmlFor="name">Name</label>
          <input id="name" value={name} onChange={(e) => handleNameChange(e.target.value)} required />
        </div>
        <div className="form-group">
          <label htmlFor="slug">URL slug</label>
          <input
            id="slug"
            value={slug}
            onChange={(e) => setSlug(e.target.value)}
            required
            pattern="[a-z0-9-]+"
          />
        </div>
        <div className="form-group">
          <label htmlFor="description">Description</label>
          <textarea id="description" value={description} onChange={(e) => setDescription(e.target.value)} />
        </div>
        <button type="submit" className="btn-primary">
          {isSubforum ? 'Create subforum' : 'Create forum'}
        </button>
      </form>
    </div>
  );
}

function slugify(value: string) {
  return value
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-|-$/g, '');
}

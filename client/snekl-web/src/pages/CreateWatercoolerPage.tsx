import { type FormEvent, useEffect, useMemo, useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';
import { createContentClient, hasPermission } from '../api';
import { useAuth } from '../AuthContext';
import {
  canCreateForumFromServerRoles,
} from '../createForumAccess';
import { useServerRegistry } from '../ServerRegistryContext';
import { decodeServerDid, forumPath, resolveServerBaseUrl, watercoolerPath } from '../serverDid';
import { WatercoolerModeFields } from '../components/WatercoolerModeFields';
import type { Forum } from '../types';

function slugify(value: string) {
  return value
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-|-$/g, '');
}

export function CreateWatercoolerPage() {
  const { serverDid: encodedDid } = useParams<{ serverDid: string }>();
  const [searchParams] = useSearchParams();
  const { user } = useAuth();
  const { resolveBaseUrl } = useServerRegistry();
  const navigate = useNavigate();
  const parentForumId = searchParams.get('parent') ?? '';
  const [name, setName] = useState('');
  const [slug, setSlug] = useState('');
  const [description, setDescription] = useState('');
  const [allowUpvotes, setAllowUpvotes] = useState(!parentForumId);
  const [newestAtBottom, setNewestAtBottom] = useState(true);
  const [error, setError] = useState('');
  const [parentForum, setParentForum] = useState<Forum | null>(null);
  const [canServerCreate, setCanServerCreate] = useState(false);
  const [canServerRequest, setCanServerRequest] = useState(false);

  const serverDid = encodedDid ? decodeServerDid(encodedDid) : '';
  const baseUrl = resolveServerBaseUrl(serverDid, resolveBaseUrl);
  const client = useMemo(() => (baseUrl ? createContentClient(baseUrl) : null), [baseUrl]);

  useEffect(() => {
    if (!client) return;
    void client.getServerPermissions().then((info) => {
      setCanServerCreate(canCreateForumFromServerRoles(info) || hasPermission(info.permissions, 'CreateWatercooler'));
      setCanServerRequest(hasPermission(info.permissions, 'RequestCreateWatercooler'));
    });
    if (parentForumId) {
      void client.getForums().then((forums) => {
        setParentForum(forums.find((f) => f.id === parentForumId) ?? null);
      });
    }
  }, [client, parentForumId]);

  useEffect(() => {
    setAllowUpvotes(!parentForumId);
    setNewestAtBottom(true);
  }, [parentForumId]);

  if (!user) {
    return (
      <div className="card">
        <p>
          Please <Link to="/login">log in</Link> to create a watercooler.
        </p>
      </div>
    );
  }

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    if (!client) return;
    setError('');
    try {
      const cooler = await client.createWatercooler(
        name.trim(),
        slug.trim().toLowerCase(),
        description.trim(),
        parentForumId || undefined,
        { allowUpvotes, newestAtBottom },
      );
      navigate(
        watercoolerPath(
          serverDid,
          cooler.slug,
          cooler.parentForumSlug ?? undefined,
          parentForum?.parentSlug ?? undefined,
        ),
      );
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create watercooler');
    }
  };

  return (
    <div className="card" style={{ maxWidth: 560 }}>
      <h1 className="page-title">{parentForumId ? 'Create watercooler' : canServerCreate ? 'Create plaza' : 'Request a plaza'}</h1>
      {parentForum && (
        <p className="card-meta">
          Under <Link to={forumPath(serverDid, parentForum.slug, parentForum.parentSlug ?? undefined)}>{parentForum.name}</Link>
        </p>
      )}
      {!parentForumId && canServerRequest && !canServerCreate && (
        <p className="card-meta">Your request will be hidden until someone with CreateWatercooler approves it.</p>
      )}
      {error && <div className="error-banner">{error}</div>}
      <form onSubmit={handleSubmit}>
        <div className="form-group">
          <label htmlFor="wc-name">Name</label>
          <input
            id="wc-name"
            value={name}
            onChange={(e) => {
              setName(e.target.value);
              if (!slug || slug === slugify(name)) setSlug(slugify(e.target.value));
            }}
            required
          />
        </div>
        <div className="form-group">
          <label htmlFor="wc-slug">URL slug</label>
          <input id="wc-slug" value={slug} onChange={(e) => setSlug(e.target.value)} required pattern="[a-z0-9-]+" />
        </div>
        <div className="form-group">
          <label htmlFor="wc-desc">Description</label>
          <textarea id="wc-desc" value={description} onChange={(e) => setDescription(e.target.value)} />
        </div>
        <WatercoolerModeFields
          idPrefix="wc"
          allowUpvotes={allowUpvotes}
          newestAtBottom={newestAtBottom}
          onAllowUpvotesChange={setAllowUpvotes}
          onNewestAtBottomChange={setNewestAtBottom}
        />
        <button type="submit" className="btn-primary">
          {parentForumId || canServerCreate ? 'Create' : 'Submit request'}
        </button>
      </form>
    </div>
  );
}

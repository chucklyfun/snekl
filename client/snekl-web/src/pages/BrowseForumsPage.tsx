import { useEffect, useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router';
import { aggregateForums, createContentClient } from '../api';
import { useAuth } from '../AuthContext';
import { isForumPending } from '../createForumAccess';
import { CreateForumDialog } from '../components/CreateForumDialog';
import { ForumAvatar } from '../components/ForumAvatar';
import { useForumNav } from '../ForumNavContext';
import { filterBrowseForums } from '../forumNav';
import { useServerRegistry } from '../ServerRegistryContext';
import { forumPath, watercoolerPath } from '../serverDid';
import { useCreateForumEligibility } from '../useCreateForumEligibility';
import type { AggregatedForum, Forum, Watercooler } from '../types';

function patchAggregated(forum: AggregatedForum, updated: Forum): AggregatedForum {
  return {
    ...forum,
    ...updated,
    serverDid: forum.serverDid,
    serverName: forum.serverName,
    serverBaseUrl: forum.serverBaseUrl,
    serverStatus: forum.serverStatus,
  };
}

export function BrowseForumsPage() {
  const { user } = useAuth();
  const { activeServers, loading: serversLoading } = useServerRegistry();
  const { refresh } = useForumNav();
  const navigate = useNavigate();
  const {
    eligibleServers,
    checkErrors,
    permissionsLoading,
    urlServerCanCreate,
    urlServerCanRequest,
    canShowCreateButton,
  } = useCreateForumEligibility();
  const [dialogOpen, setDialogOpen] = useState(false);
  const [forums, setForums] = useState<AggregatedForum[]>([]);
  const [plazas, setPlazas] = useState<{ serverDid: string; serverName: string; cooler: Watercooler }[]>([]);
  const [query, setQuery] = useState('');
  const [error, setError] = useState('');
  const [busyKey, setBusyKey] = useState('');
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    if (!user) {
      setForums([]);
      setPlazas([]);
      return;
    }
    if (serversLoading) return;
    if (activeServers.length === 0) {
      setForums([]);
      setPlazas([]);
      return;
    }

    let cancelled = false;
    setLoading(true);
    void Promise.all([
      aggregateForums(activeServers),
      Promise.all(
        activeServers.map(async (server) => {
          try {
            const list = await createContentClient(server.baseUrl).listServerWatercoolers();
            return list.map((cooler) => ({
              serverDid: server.serverDid,
              serverName: server.name,
              cooler,
            }));
          } catch {
            return [];
          }
        }),
      ),
    ])
      .then(([list, plazaLists]) => {
        if (!cancelled) {
          setForums(list);
          setPlazas(plazaLists.flat());
        }
      })
      .catch((err) => {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Failed to load nests');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [user, activeServers, serversLoading]);

  const visiblePlazas = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return plazas;
    return plazas.filter(
      (row) =>
        row.cooler.name.toLowerCase().includes(q) ||
        row.cooler.slug.toLowerCase().includes(q) ||
        row.serverName.toLowerCase().includes(q),
    );
  }, [plazas, query]);
  const visible = useMemo(() => filterBrowseForums(forums, query), [forums, query]);
  const groups = useMemo(() => {
    const byServer = new Map<string, AggregatedForum[]>();
    for (const forum of visible) {
      const list = byServer.get(forum.serverDid) ?? [];
      list.push(forum);
      byServer.set(forum.serverDid, list);
    }
    return [...byServer.values()]
      .map((items) => ({
        serverDid: items[0].serverDid,
        serverName: items[0].serverName,
        forums: [...items].sort((a, b) => a.name.localeCompare(b.name)),
      }))
      .sort((a, b) => a.serverName.localeCompare(b.serverName));
  }, [visible]);

  const toggleMembership = async (forum: AggregatedForum) => {
    setError('');
    setBusyKey(`${forum.serverDid}:${forum.id}`);
    try {
      const client = createContentClient(forum.serverBaseUrl);
      const parentSlug = forum.parentSlug ?? undefined;
      const updated = forum.viewerIsMember
        ? await client.leaveForum(forum.slug, parentSlug)
        : await client.joinForum(forum.slug, parentSlug);
      setForums((prev) =>
        prev.map((item) =>
          item.serverDid === forum.serverDid && item.id === forum.id ? patchAggregated(item, updated) : item,
        ),
      );
      await refresh();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Membership update failed');
    } finally {
      setBusyKey('');
    }
  };

  if (!user) {
    return (
      <div className="card">
        <p>
          Please <Link to="/login">log in</Link> to browse nests on your servers.
        </p>
      </div>
    );
  }

  const noActiveServers = !serversLoading && activeServers.length === 0;

  return (
    <div>
      <div className="browse-forums-heading">
        <div>
          <h1 className="page-title">Browse nests</h1>
          <p className="page-subtitle">
            {noActiveServers
              ? 'Subscribe to a content server first, then you can join its nests.'
              : 'Find nests on servers you subscribe to, then join or leave them.'}
          </p>
        </div>
        {canShowCreateButton && (
          <button type="button" className="btn-primary" onClick={() => setDialogOpen(true)}>
            New nest
          </button>
        )}
      </div>

      {error && <div className="error-banner">{error}</div>}

      <CreateForumDialog
        open={dialogOpen}
        eligibleServers={eligibleServers}
        checkErrors={checkErrors}
        permissionsLoading={permissionsLoading}
        urlServerCanCreate={urlServerCanCreate}
        urlServerCanRequest={urlServerCanRequest}
        onClose={() => setDialogOpen(false)}
        onCreated={(forum, serverDid) => {
          setDialogOpen(false);
          void refresh();
          navigate(forumPath(serverDid, forum.slug, forum.parentSlug ?? undefined));
        }}
      />

      {noActiveServers ? (
        <div className="card">
          <p>
            <Link to="/servers">Add a server</Link> to see nests you can join.
          </p>
        </div>
      ) : (
        <>
      <div className="browse-forums-toolbar">
        <input
          type="search"
          className="browse-forums-search"
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          placeholder="Search by name or description"
          aria-label="Search nests"
        />
      </div>

      {loading && <p>Loading nests…</p>}

      {!loading && visiblePlazas.length > 0 && (
        <section className="browse-forum-group">
          <h2 className="browse-forum-group-title">Server plazas</h2>
          {visiblePlazas.map((row) => (
            <div key={`${row.serverDid}:${row.cooler.id}`} className="server-row browse-forum-row">
              <div>
                <Link to={watercoolerPath(row.serverDid, row.cooler.slug)} className="card-link">
                  <h3 className="card-title" style={{ marginBottom: '0.25rem' }}>
                    {row.cooler.name}
                  </h3>
                </Link>
                <p className="page-subtitle" style={{ marginBottom: '0.35rem' }}>
                  {row.serverName} · /{row.cooler.slug} · {row.cooler.postCount} posts
                </p>
              </div>
            </div>
          ))}
        </section>
      )}

      {!loading && groups.length === 0 && visiblePlazas.length === 0 && (
        <div className="card">
          <p>No nests match that search.</p>
        </div>
      )}

      {groups.map((group) => (
        <section key={group.serverDid} className="browse-forum-group">
          <h2 className="browse-forum-group-title">{group.serverName}</h2>
          {group.forums.map((forum) => {
            const key = `${forum.serverDid}:${forum.id}`;
            const pending = isForumPending(forum);
            const parentLabel = forum.parentSlug ? ` / ${forum.parentSlug}` : '';
            return (
              <div key={key} className="server-row browse-forum-row">
                <ForumAvatar name={forum.name} src={forum.avatarUrl} size={36} />
                <div>
                  <Link to={forumPath(forum.serverDid, forum.slug, forum.parentSlug ?? undefined)} className="card-link">
                    <h3 className="card-title" style={{ marginBottom: '0.25rem' }}>
                      {forum.name}
                      {pending && <span className="badge badge-pending">pending</span>}
                    </h3>
                  </Link>
                  <p className="page-subtitle" style={{ marginBottom: '0.35rem' }}>
                    {forum.slug}
                    {parentLabel} · {forum.postCount} threads
                  </p>
                  {forum.description && <p style={{ margin: 0 }}>{forum.description}</p>}
                </div>
                <div className="server-row-actions">
                  {!pending && forum.nsfwAgeCheckBlocksEntry && !forum.viewerIsMember ? (
                    <Link
                      to={forumPath(forum.serverDid, forum.slug, forum.parentSlug ?? undefined)}
                      className="btn-primary"
                    >
                      Age check
                    </Link>
                  ) : !pending ? (
                    <button
                      type="button"
                      className={forum.viewerIsMember ? 'btn-ghost' : 'btn-primary'}
                      disabled={busyKey === key}
                      onClick={() => void toggleMembership(forum)}
                    >
                      {forum.viewerIsMember ? 'Leave' : 'Join'}
                    </button>
                  ) : null}
                </div>
              </div>
            );
          })}
        </section>
      ))}
        </>
      )}
    </div>
  );
}

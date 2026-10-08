import { useEffect, useState } from 'react';
import { Link } from 'react-router';
import { createContentClient } from '../api';
import { useAuth } from '../AuthContext';
import {
  mergeReplyAlerts,
  notifyReplyAlertsChanged,
  replyAlertPath,
  replyAlertSummary,
  type MergedReplyAlert,
  type ReplyAlertServerFailure,
} from '../replyAlerts';
import { useServerRegistry } from '../ServerRegistryContext';
import type { ContentServerMembership, ReplyAlert } from '../types';

export function AlertsPage() {
  const { user } = useAuth();
  const { servers, loading } = useServerRegistry();
  const [rows, setRows] = useState<MergedReplyAlert[]>([]);
  const [failures, setFailures] = useState<ReplyAlertServerFailure[]>([]);
  const [error, setError] = useState('');
  const [loaded, setLoaded] = useState(false);

  useEffect(() => {
    if (!user || loading) return;
    let cancelled = false;
    setLoaded(false);

    void (async () => {
      const groups: Array<{ server: ContentServerMembership; alerts: ReplyAlert[] }> = [];
      const missed: ReplyAlertServerFailure[] = [];
      await Promise.all(
        servers.map(async (server) => {
          try {
            const alerts = await createContentClient(server.baseUrl).getReplyAlerts();
            groups.push({ server, alerts });
          } catch {
            missed.push({ serverDid: server.serverDid, serverName: server.name });
          }
        }),
      );
      if (cancelled) return;
      setRows(mergeReplyAlerts(groups));
      setFailures(missed);
      setLoaded(true);
    })().catch((err: unknown) => {
      if (!cancelled) setError(err instanceof Error ? err.message : 'Could not load alerts');
    });

    return () => {
      cancelled = true;
    };
  }, [user, loading, servers]);

  if (!user) {
    return (
      <div className="card">
        <p>
          Please <Link to="/login">log in</Link> to see replies to your posts.
        </p>
      </div>
    );
  }

  return (
    <div>
      <h1 className="page-title">Alerts</h1>
      <p className="page-subtitle">
        Replies, posts from people you follow, vote milestones, and nests or watercoolers you watch.
      </p>
      {error && <div className="error-banner">{error}</div>}
      {failures.map((failure) => (
        <div key={failure.serverDid} className="error-banner">
          Couldn't reach {failure.serverName}.
        </div>
      ))}
      {!loaded && !error && <p className="card-meta">Loading...</p>}
      {loaded && servers.length === 0 && (
        <div className="card">
          <p className="card-meta">Add a content server to your account to collect reply alerts.</p>
        </div>
      )}
      {loaded && servers.length > 0 && rows.length === 0 && failures.length === 0 && (
        <div className="card">
          <p className="card-meta">
            No alerts yet. Replies to your posts show up here. You can also watch a nest, a watercooler, or someone you follow.
          </p>
        </div>
      )}
      {rows.map((row) => (
        <Link
          key={`${row.serverDid}:${row.id}`}
          to={replyAlertPath(row.serverDid, row)}
          className={row.readAt ? 'card alert-row' : 'card alert-row alert-row-unread'}
          onClick={() => {
            if (row.readAt) return;
            void createContentClient(row.baseUrl)
              .markReplyAlertRead(row.id)
              .then(() => notifyReplyAlertsChanged())
              .catch(() => {});
          }}
        >
          <strong>{replyAlertSummary(row.replyCount, row.title, row.kind ?? 'thread')}</strong>
          <div className="card-meta">
            {row.serverName} · {new Date(row.latestReplyAt).toLocaleString()}
          </div>
        </Link>
      ))}
    </div>
  );
}

import { type FormEvent, useEffect, useMemo, useState } from 'react';
import { adminFetch } from './api';
import { Checklist, type CheckItem } from './Checklist';

type Config = {
  schema: string;
  name: string;
  baseUrl: string;
  auth: { issuer: string; jwks: string };
  mapVersion: number;
  servers: Array<{ did: string; baseUrl: string; name: string }>;
  feed?: string;
};
type Operator = { id: string; did: string; username: string; hasPassword: boolean };
type Pending = { serverDid: string; baseUrl: string; name: string; status: string };
type FeedSyncResult = {
  originCount: number;
  ingested: number;
  origins: Array<{ serverDid: string; baseUrl: string; name: string; ingested: number; error?: string | null }>;
};

export function HubAdmin() {
  const [error, setError] = useState('');
  const [flash, setFlash] = useState('');
  const [config, setConfig] = useState<Config | null>(null);
  const [operators, setOperators] = useState<Operator[]>([]);
  const [pending, setPending] = useState<Pending[]>([]);
  const [originUrl, setOriginUrl] = useState('');
  const [opUser, setOpUser] = useState('');
  const [syncing, setSyncing] = useState(false);

  const load = () => {
    setError('');
    Promise.all([
      adminFetch<Config>('hub', '/api/hub/config'),
      adminFetch<Operator[]>('hub', '/api/hub/admin/operators', { admin: true }),
      adminFetch<Pending[]>('hub', '/api/hub/join-requests', { admin: true }),
    ])
      .then(([c, o, p]) => {
        setConfig(c);
        setOperators(o);
        setPending(p);
      })
      .catch((err) => setError(err.message));
  };

  useEffect(load, []);

  const checks: CheckItem[] = useMemo(
    () => [
      {
        id: 'ops',
        title: 'Operators',
        detail: operators.length ? `${operators.length} hub operator(s).` : 'Claim this hub.',
        status: operators.length ? 'done' : 'todo',
      },
      {
        id: 'auth',
        title: 'Auth URL (config)',
        detail: config?.auth.issuer || 'Set Hub:AuthBaseUrl on this host.',
        status: config?.auth.issuer ? 'done' : 'todo',
      },
      {
        id: 'members',
        title: 'Content members',
        detail: config?.servers.length
          ? `${config.servers.length} origin(s) in the map.`
          : 'Paste an origin URL to send an invite.',
        status: config?.servers.length ? 'done' : 'todo',
      },
      {
        id: 'pending',
        title: 'Waiting on origin',
        detail: pending.length
          ? `${pending.length} invite(s) waiting for the content site admin to accept.`
          : 'No pending invites.',
        status: pending.length ? 'attention' : 'done',
      },
    ],
    [config, operators, pending],
  );

  const inviteOrigin = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    try {
      const root = originUrl.replace(/\/$/, '');
      const node = await fetch(`${root}/api/node`).then(async (res) => {
        const body = await res.json().catch(() => ({}));
        if (!res.ok) throw new Error(body.error || `Could not load ${root}/api/node`);
        return body as { did: string; name: string; baseUrl: string };
      });
      const result = await adminFetch<{ status: string }>('hub', '/api/hub/servers', {
        admin: true,
        method: 'POST',
        memberProof: { method: 'POST', url: `${root}/api/federation/invites` },
        body: JSON.stringify({
          serverDid: node.did,
          baseUrl: node.baseUrl || root,
          name: node.name || root,
        }),
      });
      setOriginUrl('');
      setFlash(
        result.status === 'active'
          ? 'Already a member.'
          : `Invite sent. Have their site admin open ${root}/server/ and accept.`,
      );
      load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Invite failed');
    }
  };

  const refreshFeed = async () => {
    setError('');
    setFlash('');
    setSyncing(true);
    try {
      const result = await adminFetch<FeedSyncResult>('hub', '/api/hub/admin/feed/sync', {
        admin: true,
        method: 'POST',
      });
      const failed = result.origins.filter((o) => o.error);
      const threadLabel = result.ingested === 1 ? 'thread' : 'threads';
      const originLabel = result.originCount === 1 ? 'origin' : 'origins';
      setFlash(`Pulled ${result.ingested} public ${threadLabel} from ${result.originCount} ${originLabel}.`);
      if (failed.length) {
        setError(failed.map((o) => `${o.name}: ${o.error}`).join(' '));
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Feed refresh failed');
    } finally {
      setSyncing(false);
    }
  };

  return (
    <>
      <Checklist items={checks} />
      {flash && <p className="card-meta">{flash}</p>}
      {error && <div className="error-banner">{error}</div>}

      <div className="card">
        <h2 className="card-title">This federation</h2>
        <p className="card-meta">
          Clients fetch <code>/.well-known/snekl-federation.json</code> with ordinary HTTPS (Account JWT only if you
          require it).
        </p>
        {config && (
          <>
            <p className="mono card-meta">
              {config.name} · {config.baseUrl} · auth {config.auth.issuer} · map v{config.mapVersion}
            </p>
            <p className="card-meta">
              Public Home cards come from member origins. Startup and this button pull the most recently updated public threads.
            </p>
            <button type="button" className="btn-primary" disabled={syncing} onClick={() => void refreshFeed()}>
              {syncing ? 'Pulling origins…' : 'Refresh public feed'}
            </button>
          </>
        )}
      </div>

      <div className="card">
        <h2 className="card-title">Members</h2>
        {(config?.servers ?? []).map((s) => (
          <div key={s.did} className="server-row">
            <div>
              <strong>{s.name}</strong>
              <div className="card-meta">
                {s.baseUrl} · {s.did}
              </div>
            </div>
            <a className="btn-ghost" href={`${s.baseUrl.replace(/\/$/, '')}/server/`}>
              Origin admin
            </a>
          </div>
        ))}
        {!config?.servers.length && <p className="card-meta">No members yet.</p>}
      </div>

      <form className="card" onSubmit={inviteOrigin}>
        <h2 className="card-title">Invite a content server</h2>
        <p className="card-meta">Paste the origin URL. This host loads DID and name from GET /api/node.</p>
        <div className="inline-form">
          <input
            value={originUrl}
            onChange={(e) => setOriginUrl(e.target.value)}
            placeholder="http://localhost:5001"
            required
          />
          <button type="submit" className="btn-primary">
            Send invite
          </button>
        </div>
        {pending.map((p) => (
          <div key={p.serverDid} className="server-row">
            <div>
              <strong>{p.name}</strong>
              <div className="card-meta">
                Waiting on origin · {p.baseUrl}
              </div>
            </div>
            <button
              type="button"
              className="btn-ghost"
              onClick={async () => {
                try {
                  await adminFetch('hub', '/api/hub/join-requests/decline', {
                    method: 'POST',
                    body: JSON.stringify({ serverDid: p.serverDid }),
                  });
                  setFlash(`Cancelled invite to ${p.name}.`);
                  load();
                } catch (err) {
                  setError(err instanceof Error ? err.message : 'Cancel failed');
                }
              }}
            >
              Cancel
            </button>
          </div>
        ))}
      </form>

      <form
        className="card"
        onSubmit={async (e) => {
          e.preventDefault();
          await adminFetch('hub', '/api/hub/admin/operators', {
            admin: true,
            method: 'POST',
            body: JSON.stringify({ username: opUser }),
          });
          setOpUser('');
          load();
        }}
      >
        <h2 className="card-title">Operators</h2>
        <p className="card-meta">Invite by Account username. They set a hub-only password on first login.</p>
        <div className="inline-form">
          <input value={opUser} onChange={(e) => setOpUser(e.target.value)} placeholder="Username" required />
          <button type="submit" className="btn-primary">
            Invite
          </button>
        </div>
        {operators.map((o) => (
          <div key={o.id} className="server-row">
            <div>
              <strong>{o.username}</strong>
              <div className="card-meta">{o.hasPassword ? 'Password set' : 'Waiting'} · {o.did}</div>
            </div>
            <button
              type="button"
              className="btn-ghost"
              onClick={async () => {
                await adminFetch('hub', `/api/hub/admin/operators/${o.id}`, { admin: true, method: 'DELETE' });
                load();
              }}
            >
              Remove
            </button>
          </div>
        ))}
      </form>
    </>
  );
}

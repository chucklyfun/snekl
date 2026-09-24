import { type FormEvent, useEffect, useMemo, useState } from 'react';
import { adminFetch } from './api';
import { Checklist, type CheckItem } from './Checklist';

type NodeInfo = { id: string; did: string; name: string; baseUrl: string; description: string };
type Operator = { id: string; username: string; did?: string; hasPassword: boolean };
type SuperUser = { userId: string; username: string; displayName: string; grantedAt: string; note?: string };
type Issuer = { name: string; issuerUrl: string; jwksUrl: string };
type HubInvite = { id: string; hubName: string; hubBaseUrl: string; requestedByUsername?: string };
type HubMap = {
  mapVersion: number;
  servers: Array<{ serverDid: string; ackedMapVersion: number; status: string }>;
};
type HubMembership = {
  baseUrl: string;
  name: string;
  mapVersion: number;
  member: boolean;
  ackedMapVersion?: number;
};

const HUBS_KEY = 'snekl_content_admin_hubs';

function loadRememberedHubs(): string[] {
  try {
    const parsed = JSON.parse(localStorage.getItem(HUBS_KEY) || '[]');
    return Array.isArray(parsed) ? parsed.filter((item): item is string => typeof item === 'string') : [];
  } catch {
    return [];
  }
}

function rememberHub(url: string): string[] {
  const root = url.replace(/\/$/, '');
  const next = Array.from(new Set([...loadRememberedHubs(), root]));
  localStorage.setItem(HUBS_KEY, JSON.stringify(next));
  return next;
}

function forgetHub(url: string): string[] {
  const root = url.replace(/\/$/, '');
  const next = loadRememberedHubs().filter((hub) => hub !== root);
  localStorage.setItem(HUBS_KEY, JSON.stringify(next));
  return next;
}

export function ContentAdmin() {
  const [error, setError] = useState('');
  const [flash, setFlash] = useState('');
  const [node, setNode] = useState<NodeInfo | null>(null);
  const [operators, setOperators] = useState<Operator[]>([]);
  const [supers, setSupers] = useState<SuperUser[]>([]);
  const [issuers, setIssuers] = useState<Issuer[]>([]);
  const [invites, setInvites] = useState<HubInvite[]>([]);
  const [hubs, setHubs] = useState<HubMembership[]>([]);
  const [hubUrl, setHubUrl] = useState('');
  const [name, setName] = useState('');
  const [baseUrl, setBaseUrl] = useState('');
  const [description, setDescription] = useState('');
  const [opUser, setOpUser] = useState('');
  const [superUser, setSuperUser] = useState('');
  const [issuerName, setIssuerName] = useState('');
  const [issuerUrl, setIssuerUrl] = useState('');
  const [jwksUrl, setJwksUrl] = useState('');

  const load = () => {
    setError('');
    Promise.all([
      adminFetch<NodeInfo>('content', '/api/admin/site/node'),
      adminFetch<Operator[]>('content', '/api/admin/site/operators'),
      adminFetch<SuperUser[]>('content', '/api/admin/site/super-users'),
      adminFetch<Issuer[]>('content', '/api/admin/site/issuers'),
      adminFetch<HubInvite[]>('content', '/api/admin/site/hub-invites'),
    ])
      .then(([n, o, s, i, h]) => {
        setNode(n);
        setName(n.name);
        setBaseUrl(n.baseUrl);
        setDescription(n.description || '');
        setOperators(o);
        setSupers(s);
        setIssuers(i);
        setInvites(h);
        return loadHubMemberships(n.did, h.map((invite) => invite.hubBaseUrl));
      })
      .then(setHubs)
      .catch((err) => setError(err.message));
  };

  useEffect(load, []);

  const checks: CheckItem[] = useMemo(() => {
    const pending = invites.length;
    const memberHint = node?.baseUrl ? 'Join by accepting a hub invite.' : 'Set the public base URL first.';
    return [
      {
        id: 'claim',
        title: 'Operators',
        detail: operators.length ? `${operators.length} operator(s) on this node.` : 'Claim or invite an operator.',
        status: operators.length ? 'done' : 'todo',
      },
      {
        id: 'identity',
        title: 'Public name and URL',
        detail: node?.name && node.baseUrl ? `${node.name} · ${node.baseUrl}` : 'Other hosts find this node by URL.',
        status: node?.name && node.baseUrl ? 'done' : 'todo',
      },
      {
        id: 'issuer',
        title: 'Trusted auth issuer',
        detail: issuers.length ? issuers.map((i) => i.name).join(', ') : 'Register the Account JWKS this node should trust.',
        status: issuers.length ? 'done' : 'todo',
      },
      {
        id: 'invite',
        title: 'Federation invite',
        detail: pending
          ? `${pending} pending hub invite(s).`
          : hubs.some((hub) => hub.member)
            ? 'Member of at least one hub.'
            : memberHint,
        status: pending ? 'attention' : hubs.some((hub) => hub.member) ? 'done' : 'todo',
      },
    ];
  }, [invites, node, operators, issuers, hubs]);

  const saveNode = async (e: FormEvent) => {
    e.preventDefault();
    try {
      await adminFetch('content', '/api/admin/site/node', {
        method: 'PUT',
        body: JSON.stringify({ name, baseUrl, description }),
      });
      setFlash('Node saved.');
      load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Save failed');
    }
  };

  return (
    <>
      <Checklist items={checks} />
      {flash && <p className="card-meta">{flash}</p>}
      {error && <div className="error-banner">{error}</div>}

      {invites.length > 0 && (
        <div className="card">
          <h2 className="card-title">Incoming hub requests</h2>
          <p className="card-meta">Accept on this origin. The hub already sent the invite.</p>
          {invites.map((inv) => (
            <div key={inv.id} className="server-row">
              <div>
                <strong>{inv.hubName || inv.hubBaseUrl}</strong>
                <div className="card-meta">
                  {inv.hubBaseUrl} · from @{inv.requestedByUsername || 'unknown'}
                </div>
              </div>
              <div className="server-row-actions">
                <button
                  type="button"
                  className="btn-primary"
                  onClick={async () => {
                    try {
                      await adminFetch('content', `/api/admin/site/hub-invites/${inv.id}/accept`, { method: 'POST' });
                      rememberHub(inv.hubBaseUrl);
                      setFlash(`Accepted. Hub admin is ${inv.hubBaseUrl}/admin/`);
                      load();
                    } catch (err) {
                      setError(err instanceof Error ? err.message : 'Accept failed');
                    }
                  }}
                >
                  Accept
                </button>
                <button
                  type="button"
                  className="btn-ghost"
                  onClick={async () => {
                    await adminFetch('content', `/api/admin/site/hub-invites/${inv.id}/decline`, { method: 'POST' });
                    load();
                  }}
                >
                  Decline
                </button>
              </div>
            </div>
          ))}
        </div>
      )}

      <form className="card" onSubmit={saveNode}>
        <h2 className="card-title">This content server</h2>
        <p className="mono card-meta">{node ? `DID ${node.did}` : ''}</p>
        <div className="form-group">
          <label>Name</label>
          <input value={name} onChange={(e) => setName(e.target.value)} required />
        </div>
        <div className="form-group">
          <label>Public base URL</label>
          <input value={baseUrl} onChange={(e) => setBaseUrl(e.target.value)} required />
        </div>
        <div className="form-group">
          <label>Description</label>
          <textarea value={description} onChange={(e) => setDescription(e.target.value)} />
        </div>
        <button type="submit" className="btn-primary">
          Save
        </button>
      </form>

      <form
        className="card"
        onSubmit={async (e) => {
          e.preventDefault();
          rememberHub(hubUrl);
          setHubUrl('');
          if (node) setHubs(await loadHubMemberships(node.did, []));
        }}
      >
        <h2 className="card-title">Federation membership</h2>
        <p className="card-meta">
          After you accept a hub invite, ack the map here. Leave removes this origin from that hub’s registry.
        </p>
        <div className="inline-form">
          <input
            value={hubUrl}
            onChange={(e) => setHubUrl(e.target.value)}
            placeholder="http://localhost:5200"
          />
          <button type="submit" className="btn-ghost">
            Track hub URL
          </button>
        </div>
        {hubs.map((hub) => (
          <div key={hub.baseUrl} className="server-row">
            <div>
              <strong>{hub.name}</strong>
              <div className="card-meta">
                {hub.baseUrl} · map v{hub.mapVersion}
                {hub.member
                  ? ` · member (acked v${hub.ackedMapVersion ?? 0})`
                  : ' · not a member'}
              </div>
            </div>
            <div className="server-row-actions">
              {hub.member && node && (
                <>
                  <button
                    type="button"
                    className="btn-primary"
                    onClick={async () => {
                      try {
                        await fetch(`${hub.baseUrl}/api/hub/map/ack`, {
                          method: 'POST',
                          headers: { 'Content-Type': 'application/json' },
                          body: JSON.stringify({ serverDid: node.did, mapVersion: hub.mapVersion }),
                        }).then(async (res) => {
                          if (!res.ok) {
                            const body = await res.json().catch(() => ({}));
                            throw new Error(body.error || `Ack failed (${res.status})`);
                          }
                        });
                        setFlash(`Acknowledged map v${hub.mapVersion}.`);
                        load();
                      } catch (err) {
                        setError(err instanceof Error ? err.message : 'Ack failed');
                      }
                    }}
                  >
                    Ack map
                  </button>
                  <button
                    type="button"
                    className="btn-ghost"
                    onClick={async () => {
                      if (!window.confirm(`Leave ${hub.name}?`)) return;
                      try {
                        const res = await fetch(
                          `${hub.baseUrl}/api/hub/servers/${encodeURIComponent(node.did)}`,
                          { method: 'DELETE' },
                        );
                        if (!res.ok && res.status !== 204) {
                          const body = await res.json().catch(() => ({}));
                          throw new Error(body.error || `Leave failed (${res.status})`);
                        }
                        forgetHub(hub.baseUrl);
                        setFlash('Left federation.');
                        load();
                      } catch (err) {
                        setError(err instanceof Error ? err.message : 'Leave failed');
                      }
                    }}
                  >
                    Leave
                  </button>
                </>
              )}
            </div>
          </div>
        ))}
        {hubs.length === 0 && <p className="card-meta">No hub URLs tracked yet.</p>}
      </form>

      <form
        className="card"
        onSubmit={async (e) => {
          e.preventDefault();
          await adminFetch('content', '/api/admin/site/operators', {
            method: 'POST',
            body: JSON.stringify({ username: opUser }),
          });
          setOpUser('');
          load();
        }}
      >
        <h2 className="card-title">Operators</h2>
        <p className="card-meta">Invite by username. They set a password on this node only.</p>
        <div className="inline-form">
          <input value={opUser} onChange={(e) => setOpUser(e.target.value)} placeholder="Username" required />
          <button type="submit" className="btn-primary">
            Invite
          </button>
        </div>
        {operators.map((o) => (
          <div key={o.id} className="server-row">
            <div>
              <strong>@{o.username}</strong>
              <div className="card-meta">{o.hasPassword ? 'Password set' : 'Waiting to set password'}</div>
            </div>
            <button
              type="button"
              className="btn-ghost"
              onClick={async () => {
                await adminFetch('content', `/api/admin/site/operators/${o.id}`, { method: 'DELETE' });
                load();
              }}
            >
              Remove
            </button>
          </div>
        ))}
      </form>

      <form
        className="card"
        onSubmit={async (e) => {
          e.preventDefault();
          await adminFetch('content', '/api/admin/site/issuers', {
            method: 'POST',
            body: JSON.stringify({ name: issuerName, issuerUrl, jwksUrl }),
          });
          setIssuerName('');
          setIssuerUrl('');
          setJwksUrl('');
          load();
        }}
      >
        <h2 className="card-title">Trusted auth issuers</h2>
        <div className="form-group">
          <label>Name</label>
          <input value={issuerName} onChange={(e) => setIssuerName(e.target.value)} required />
        </div>
        <div className="form-group">
          <label>Issuer URL</label>
          <input value={issuerUrl} onChange={(e) => setIssuerUrl(e.target.value)} required />
        </div>
        <div className="form-group">
          <label>JWKS URL</label>
          <input value={jwksUrl} onChange={(e) => setJwksUrl(e.target.value)} required />
        </div>
        <button type="submit" className="btn-primary">
          Register issuer
        </button>
        {issuers.map((i) => (
          <p key={i.issuerUrl} className="card-meta">
            {i.name} · {i.issuerUrl}
          </p>
        ))}
      </form>

      <form
        className="card"
        onSubmit={async (e) => {
          e.preventDefault();
          await adminFetch('content', '/api/admin/site/super-users', {
            method: 'POST',
            body: JSON.stringify({ username: superUser }),
          });
          setSuperUser('');
          load();
        }}
      >
        <h2 className="card-title">Super Users</h2>
        <p className="card-meta">Product god-mode on this origin. Optional automatic grant with operator access is controlled by Admin:GrantOperatorsSuperUser.</p>
        <div className="inline-form">
          <input value={superUser} onChange={(e) => setSuperUser(e.target.value)} placeholder="Username" required />
          <button type="submit" className="btn-primary">
            Grant
          </button>
        </div>
        {supers.map((s) => (
          <div key={s.userId} className="server-row">
            <strong>@{s.username}</strong>
            <button
              type="button"
              className="btn-ghost"
              onClick={async () => {
                await adminFetch('content', `/api/admin/site/super-users/${s.userId}`, { method: 'DELETE' });
                load();
              }}
            >
              Revoke
            </button>
          </div>
        ))}
      </form>
    </>
  );
}

async function loadHubMemberships(serverDid: string, extraUrls: string[]): Promise<HubMembership[]> {
  const urls = Array.from(
    new Set(
      [...loadRememberedHubs(), ...extraUrls]
        .map((url) => url.replace(/\/$/, ''))
        .filter(Boolean),
    ),
  );

  const results: HubMembership[] = [];
  for (const baseUrl of urls) {
    try {
      const [infoRes, mapRes] = await Promise.all([
        fetch(`${baseUrl}/api/hub`),
        fetch(`${baseUrl}/api/hub/map`),
      ]);
      const info = (await infoRes.json().catch(() => ({}))) as { name?: string };
      const map = (await mapRes.json().catch(() => ({}))) as HubMap;
      const mine = map.servers?.find((server) => server.serverDid === serverDid);
      results.push({
        baseUrl,
        name: info.name || baseUrl,
        mapVersion: map.mapVersion ?? 0,
        member: Boolean(mine && mine.status === 'active'),
        ackedMapVersion: mine?.ackedMapVersion,
      });
    } catch {
      results.push({ baseUrl, name: baseUrl, mapVersion: 0, member: false });
    }
  }
  return results;
}

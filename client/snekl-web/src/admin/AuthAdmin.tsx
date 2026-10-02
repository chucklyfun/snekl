import { type FormEvent, useEffect, useMemo, useState } from 'react';
import { applyMemberProof } from '../deviceKey';
import { adminFetch, memberToken } from './api';
import { Checklist, type CheckItem } from './Checklist';

type ServerInfo = { name: string; baseUrl: string; did: string; jwksUrl: string };
type Operator = { id: string; did: string; username: string; hasPassword: boolean };
type Partner = { partnerBaseUrl: string; isMutual: boolean; createdAt: string };

export function AuthAdmin() {
  const [error, setError] = useState('');
  const [flash, setFlash] = useState('');
  const [server, setServer] = useState<ServerInfo | null>(null);
  const [operators, setOperators] = useState<Operator[]>([]);
  const [partners, setPartners] = useState<Partner[]>([]);
  const [opUser, setOpUser] = useState('');
  const [partnerUrl, setPartnerUrl] = useState('');

  const load = () => {
    setError('');
    Promise.all([
      adminFetch<ServerInfo>('auth', '/api/auth/server'),
      adminFetch<Operator[]>('auth', '/api/auth/admin/operators', { admin: true }),
      fetchPartners(),
    ])
      .then(([s, o, p]) => {
        setServer(s);
        setOperators(o);
        setPartners(p);
      })
      .catch((err) => setError(err.message));
  };

  useEffect(load, []);

  const checks: CheckItem[] = useMemo(
    () => [
      {
        id: 'ops',
        title: 'Operators',
        detail: operators.length ? `${operators.length} operator(s).` : 'Claim this auth server.',
        status: operators.length ? 'done' : 'todo',
      },
      {
        id: 'identity',
        title: 'Public identity',
        detail: server ? `${server.name} · ${server.did}` : 'Loading…',
        status: server?.did ? 'done' : 'todo',
      },
      {
        id: 'partners',
        title: 'Ring partners',
        detail: partners.length
          ? partners.map((p) => `${p.partnerBaseUrl} (${p.isMutual ? 'mutual' : 'pending'})`).join(', ')
          : 'Optional. Add another auth server URL for ring trust.',
        status: partners.some((p) => p.isMutual) ? 'done' : 'todo',
      },
    ],
    [operators, server, partners],
  );

  const addPartner = async (e: FormEvent) => {
    e.preventDefault();
    const token = memberToken();
    if (!token) {
      setError('Account session missing. Log out and in again.');
      return;
    }
    try {
      const url = new URL('/api/partners/trust', window.location.origin).href;
      const headers: Record<string, string> = { 'Content-Type': 'application/json' };
      await applyMemberProof(headers, 'POST', url, token);
      const res = await fetch(url, {
        method: 'POST',
        headers,
        body: JSON.stringify({ partnerBaseUrl: partnerUrl }),
      });
      const body = await res.json().catch(() => ({}));
      if (!res.ok) throw new Error(body.error || 'Partner request failed');
      setPartnerUrl('');
      setFlash('Partner trust started.');
      load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed');
    }
  };

  return (
    <>
      <Checklist items={checks} />
      {flash && <p className="card-meta">{flash}</p>}
      {error && <div className="error-banner">{error}</div>}

      <div className="card">
        <h2 className="card-title">This auth server</h2>
        {server && (
          <p className="mono card-meta">
            {server.name} · {server.baseUrl} · {server.did}
            <br />
            JWKS {server.jwksUrl}
          </p>
        )}
      </div>

      <form
        className="card"
        onSubmit={async (e) => {
          e.preventDefault();
          await adminFetch('auth', '/api/auth/admin/operators', {
            admin: true,
            method: 'POST',
            body: JSON.stringify({ username: opUser }),
          });
          setOpUser('');
          load();
        }}
      >
        <h2 className="card-title">Operators</h2>
        <p className="card-meta">Invite by username on this auth server. Password is local to this host.</p>
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
                await adminFetch('auth', `/api/auth/admin/operators/${o.id}`, { admin: true, method: 'DELETE' });
                load();
              }}
            >
              Remove
            </button>
          </div>
        ))}
      </form>

      <form className="card" onSubmit={addPartner}>
        <h2 className="card-title">Ring partners</h2>
        <p className="card-meta">Establish mutual trust with another Account server.</p>
        <div className="inline-form">
          <input
            value={partnerUrl}
            onChange={(e) => setPartnerUrl(e.target.value)}
            placeholder="http://localhost:5101"
            required
          />
          <button type="submit" className="btn-primary">
            Add partner
          </button>
        </div>
        {partners.map((p) => (
          <p key={p.partnerBaseUrl} className="card-meta">
            {p.partnerBaseUrl} · {p.isMutual ? 'mutual' : 'pending'}
          </p>
        ))}
      </form>
    </>
  );
}

async function fetchPartners(): Promise<Partner[]> {
  const token = memberToken();
  if (!token) return [];
  const url = new URL('/api/partners/trust', window.location.origin).href;
  const headers: Record<string, string> = {};
  await applyMemberProof(headers, 'GET', url, token);
  const res = await fetch(url, { headers });
  if (!res.ok) return [];
  return res.json();
}

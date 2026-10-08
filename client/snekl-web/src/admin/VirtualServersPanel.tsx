import { type FormEvent, useEffect, useState } from 'react';
import { adminFetch, setContentServerId } from './api';

type VirtualServer = {
  id: string;
  name: string;
  did: string;
  baseUrl: string;
  ownerUsername?: string | null;
  disabledAt?: string | null;
  createdAt: string;
};

type HostNest = {
  id: string;
  name: string;
  slug: string;
  parentForumId?: string | null;
  topSlug?: string | null;
  locked: boolean;
  hidden: boolean;
  depth: number;
};

type ServerTemplate = {
  authIssuers: Array<{ name: string; issuerUrl: string; jwksUrl: string }>;
  hub: { name: string; baseUrl: string };
  roles: Array<{
    name: string;
    color?: string | null;
    position: number;
    isEveryone: boolean;
    modes: Record<string, string>;
  }>;
};

export function VirtualServersPanel({ onOpen }: { onOpen: () => void }) {
  const [servers, setServers] = useState<VirtualServer[]>([]);
  const [templateText, setTemplateText] = useState('');
  const [name, setName] = useState('');
  const [ownerUsername, setOwnerUsername] = useState('');
  const [ownerDid, setOwnerDid] = useState('');
  const [error, setError] = useState('');
  const [flash, setFlash] = useState('');
  const [nestServerId, setNestServerId] = useState('');
  const [nests, setNests] = useState<HostNest[]>([]);
  const [destinationId, setDestinationId] = useState('');

  const load = () => {
    setError('');
    Promise.all([
      adminFetch<VirtualServer[]>('content', '/api/admin/host/servers'),
      adminFetch<ServerTemplate>('content', '/api/admin/host/template'),
    ])
      .then(([rows, template]) => {
        setServers(rows);
        setTemplateText(JSON.stringify(template, null, 2));
      })
      .catch((err) => setError(err instanceof Error ? err.message : 'Could not load servers'));
  };

  useEffect(load, []);

  const spawn = async (event: FormEvent) => {
    event.preventDefault();
    setError('');
    setFlash('');
    try {
      const created = await adminFetch<VirtualServer>('content', '/api/admin/host/servers', {
        method: 'POST',
        body: JSON.stringify({ name, ownerUsername, ownerDid }),
      });
      setName('');
      setOwnerUsername('');
      setOwnerDid('');
      setContentServerId(created.id);
      setFlash(`Spawned ${created.name}.`);
      load();
      onOpen();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Spawn failed');
    }
  };

  const act = async (server: VirtualServer, action: 'disable' | 'enable' | 'purge') => {
    setError('');
    setFlash('');
    if (action === 'purge' && !window.confirm(`Purge ${server.name}? Its rows will be deleted.`)) return;
    try {
      await adminFetch('content', `/api/admin/host/servers/${server.id}/${action}`, { method: 'POST' });
      setFlash(action === 'purge' ? `Purged ${server.name}.` : `${action === 'disable' ? 'Disabled' : 'Enabled'} ${server.name}.`);
      load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Server update failed');
    }
  };

  const openNests = async (serverId: string) => {
    setError('');
    setFlash('');
    try {
      const rows = await adminFetch<HostNest[]>('content', `/api/admin/host/servers/${serverId}/nests`);
      setNestServerId(serverId);
      setNests(rows);
      setDestinationId(servers.find((server) => server.id !== serverId)?.id ?? '');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not load nests');
    }
  };

  const moveNest = async (nest: HostNest) => {
    if (!destinationId) return;
    setError('');
    setFlash('');
    const destination = servers.find((server) => server.id === destinationId);
    if (!window.confirm(`Move ${nest.name} to ${destination?.name ?? 'that server'}? It arrives locked at the top level.`)) return;
    try {
      await adminFetch('content', `/api/admin/host/servers/${nestServerId}/nests/${nest.id}/move`, {
        method: 'POST',
        body: JSON.stringify({ destinationServerId: destinationId }),
      });
      setFlash(`Moved ${nest.name}. It is locked on the destination until a server super user unlocks it.`);
      await openNests(nestServerId);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Move failed');
    }
  };

  const saveTemplate = async (event: FormEvent) => {
    event.preventDefault();
    setError('');
    setFlash('');
    try {
      const document = JSON.parse(templateText) as ServerTemplate;
      const saved = await adminFetch<ServerTemplate>('content', '/api/admin/host/template', {
        method: 'PUT',
        body: JSON.stringify(document),
      });
      setTemplateText(JSON.stringify(saved, null, 2));
      setFlash('Template saved. Existing servers are unchanged.');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Template save failed');
    }
  };

  return (
    <div className="card">
      <h2 className="card-title">Servers</h2>
      <p className="card-meta">
        Host operators add and remove virtual content servers in this database. Open one to manage it.
      </p>
      {flash && <p className="card-meta">{flash}</p>}
      {error && <div className="error-banner">{error}</div>}

      {servers.map((server) => (
        <div key={server.id} className="server-row">
          <div>
            <strong>{server.name}</strong>
            {server.disabledAt ? ' · disabled' : ''}
            <div className="card-meta">
              {server.ownerUsername ? `@${server.ownerUsername}` : 'No owner'} · {server.did}
            </div>
          </div>
          <div className="server-row-actions">
            <button
              type="button"
              className="btn-primary"
              onClick={() => {
                setContentServerId(server.id);
                onOpen();
                setFlash(`Opened ${server.name}.`);
              }}
            >
              Open
            </button>
            <button type="button" className="btn-ghost" onClick={() => void openNests(server.id)}>
              Nests
            </button>
            {server.disabledAt ? (
              <button type="button" className="btn-ghost" onClick={() => act(server, 'enable')}>
                Enable
              </button>
            ) : (
              <button type="button" className="btn-ghost" onClick={() => act(server, 'disable')}>
                Disable
              </button>
            )}
            <button type="button" className="btn-ghost" disabled={!server.disabledAt} onClick={() => act(server, 'purge')}>
              Purge
            </button>
          </div>
        </div>
      ))}

      {nestServerId && (
        <div className="card">
          <h3 className="card-title">Move a nest</h3>
          <p className="card-meta">
            The nest and everything under it land as a top-level nest on the destination, locked, with members and roles.
            The old address redirects. Server super users place it in the destination tree after they unlock it.
          </p>
          <div className="form-group">
            <label htmlFor="nest-destination">Destination</label>
            <select id="nest-destination" value={destinationId} onChange={(event) => setDestinationId(event.target.value)}>
              {servers
                .filter((server) => server.id !== nestServerId && !server.disabledAt)
                .map((server) => (
                  <option key={server.id} value={server.id}>
                    {server.name}
                  </option>
                ))}
            </select>
          </div>
          {nests.length === 0 && <p className="card-meta">This server has no nests.</p>}
          {nests.map((nest) => (
            <div key={nest.id} className="server-row" style={{ paddingLeft: `${nest.depth * 1.25}rem` }}>
              <div>
                <strong>{nest.name}</strong>
                <div className="card-meta">
                  /{nest.topSlug ? `${nest.topSlug}/` : ''}
                  {nest.slug}
                  {nest.locked ? ' · locked' : ''}
                  {nest.hidden ? ' · hidden' : ''}
                </div>
              </div>
              <button type="button" className="btn-ghost" disabled={!destinationId} onClick={() => void moveNest(nest)}>
                Move
              </button>
            </div>
          ))}
        </div>
      )}

      <form onSubmit={spawn}>
        <h3 className="card-title">Spawn</h3>
        <div className="form-group">
          <label>Name</label>
          <input value={name} onChange={(e) => setName(e.target.value)} required />
        </div>
        <div className="form-group">
          <label>Owner username</label>
          <input value={ownerUsername} onChange={(e) => setOwnerUsername(e.target.value)} required />
        </div>
        <div className="form-group">
          <label>Owner DID</label>
          <input value={ownerDid} onChange={(e) => setOwnerDid(e.target.value)} placeholder="did:key:…" required />
        </div>
        <button type="submit" className="btn-primary">Spawn server</button>
      </form>

      <form onSubmit={saveTemplate}>
        <h3 className="card-title">Template</h3>
        <p className="card-meta">JSON applied only to the next spawn. It is not a server.</p>
        <div className="form-group">
          <textarea rows={14} value={templateText} onChange={(e) => setTemplateText(e.target.value)} spellCheck={false} />
        </div>
        <button type="submit" className="btn-ghost">Save template</button>
      </form>
    </div>
  );
}

import { useEffect, useMemo, useState } from 'react';
import { Link, useParams } from 'react-router';
import { createContentClient } from '../api';
import { TransferOwnershipForm } from '../components/TransferOwnershipForm';
import { useAuth } from '../AuthContext';
import { useServerRegistry } from '../ServerRegistryContext';
import { decodeServerDid, resolveServerBaseUrl, serverSettingsPath } from '../serverDid';

export function ServerOwnerSettingsPage() {
  const { serverDid: encodedDid } = useParams<{ serverDid: string }>();
  const { refreshMe } = useAuth();
  const { resolveBaseUrl } = useServerRegistry();
  const [isServerOwner, setIsServerOwner] = useState(false);
  const [ownerUsername, setOwnerUsername] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');

  const serverDid = encodedDid ? decodeServerDid(encodedDid) : '';
  const baseUrl = resolveServerBaseUrl(serverDid, resolveBaseUrl);
  const client = useMemo(() => (baseUrl ? createContentClient(baseUrl) : null), [baseUrl]);

  const load = () => {
    if (!client) {
      setLoading(false);
      return;
    }
    setLoading(true);
    Promise.all([client.getServerPermissions(), client.getNode().catch(() => null)])
      .then(([perms, node]) => {
        setIsServerOwner(Boolean(perms.isServerOwner));
        setOwnerUsername(node?.ownerUsername ?? null);
      })
      .catch((err) => setError(err instanceof Error ? err.message : 'Failed to load'))
      .finally(() => setLoading(false));
  };

  useEffect(load, [client]);

  return (
    <div>
      <Link to={serverDid ? serverSettingsPath(serverDid) : '/'} style={{ color: 'var(--muted)' }}>
        ← Server settings
      </Link>
      <h1 className="page-title">Server ownership</h1>
      <p className="page-subtitle">
        Sealed control of this node. Only the current owner can transfer it; other users cannot revoke these
        powers.
      </p>

      {error && <div className="error-banner">{error}</div>}
      {success && (
        <div className="card" style={{ borderColor: 'var(--accent)', marginBottom: '1rem' }}>
          {success}
        </div>
      )}

      {loading && <p className="card-meta">Loading…</p>}

      {!loading && !isServerOwner && (
        <section className="card">
          <p className="card-meta">
            {ownerUsername
              ? `This page is only for the server owner (@${ownerUsername}).`
              : 'This page is only for the server owner.'}
          </p>
        </section>
      )}

      {!loading && isServerOwner && client && (
        <section className="card">
          <h2 className="card-title">Transfer ownership</h2>
          <p className="card-meta">
            {ownerUsername ? `You are the server owner (@${ownerUsername}).` : 'You are the server owner.'}
          </p>
          <TransferOwnershipForm
            id="server-owner"
            resourceName="this server"
            confirmDetail="Only you can transfer ownership. The new owner will hold sealed server powers."
            onTransfer={async (username) => {
              setError('');
              const node = await client.transferServerOwnership(username);
              setOwnerUsername(node.ownerUsername ?? username);
              setIsServerOwner(false);
              setSuccess(`Transferred server ownership to @${node.ownerUsername ?? username}.`);
              await refreshMe();
            }}
          />
        </section>
      )}
    </div>
  );
}

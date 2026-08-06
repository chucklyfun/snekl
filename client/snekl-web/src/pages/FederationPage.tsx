import { useEffect, useState } from 'react';
import { Link } from 'react-router';
import { api } from '../api';
import type { NodeInfo, Peer } from '../types';

export function FederationPage() {
  const [node, setNode] = useState<NodeInfo | null>(null);
  const [peers, setPeers] = useState<Peer[]>([]);
  const [error, setError] = useState('');

  useEffect(() => {
    Promise.all([api.getNode(), api.getPeers()])
      .then(([nodeInfo, peerList]) => {
        setNode(nodeInfo);
        setPeers(peerList);
      })
      .catch((err) => setError(err.message));
  }, []);

  return (
    <div>
      <h1 className="page-title">Federation</h1>
      <p className="page-subtitle">
        This node connects to other Snekl nodes for server-side content sync. Peer management is handled by{' '}
        <Link to="/admin/login">site administrators</Link>.
      </p>

      {error && <div className="error-banner">{error}</div>}

      {node && (
        <div className="card">
          <h2 className="card-title">This node</h2>
          <div className="grid-2">
            <div>
              <strong>Name:</strong> {node.name}
            </div>
            <div>
              <strong>URL:</strong> {node.baseUrl}
            </div>
            <div>
              <strong>ID:</strong> <code>{node.id}</code>
            </div>
            <div>
              <strong>DID:</strong> <code>{node.did}</code>
            </div>
            <div>
              <strong>Description:</strong> {node.description || '—'}
            </div>
          </div>
        </div>
      )}

      <div className="card">
        <h2 className="card-title">Connected peers ({peers.length})</h2>
        {peers.length === 0 && <p className="card-meta">No peers connected yet.</p>}
        {peers.map((peer) => (
          <div key={peer.id} className="server-row">
            <div>
              <strong>{peer.name}</strong>
              <div className="card-meta">{peer.baseUrl}</div>
            </div>
          </div>
        ))}
      </div>

      <div className="card">
        <h2 className="card-title">How federation works</h2>
        <ol style={{ lineHeight: 1.7, color: 'var(--muted)' }}>
          <li>Each Snekl node is an independent ASP.NET Core web service with its own PostgreSQL database.</li>
          <li>When a user creates a forum or post, it is broadcast to all subscribed peer nodes via HTTP.</li>
          <li>Peer nodes receive content in their federation inbox and store it locally with origin metadata.</li>
          <li>Users browse a unified feed — local and federated content appear together.</li>
        </ol>
      </div>
    </div>
  );
}

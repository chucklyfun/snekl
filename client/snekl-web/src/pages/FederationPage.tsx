import { type FormEvent, useEffect, useState } from 'react';
import { api } from '../api';
import type { NodeInfo, Peer } from '../types';

export function FederationPage() {
  const [node, setNode] = useState<NodeInfo | null>(null);
  const [peers, setPeers] = useState<Peer[]>([]);
  const [peerUrl, setPeerUrl] = useState('');
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');

  const load = () => {
    Promise.all([api.getNode(), api.getPeers()])
      .then(([nodeInfo, peerList]) => {
        setNode(nodeInfo);
        setPeers(peerList);
      })
      .catch((err) => setError(err.message));
  };

  useEffect(load, []);

  const handleSubscribe = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    setSuccess('');
    try {
      await api.subscribePeer(peerUrl.trim());
      setPeerUrl('');
      setSuccess('Subscribed to peer node. Content will sync automatically.');
      load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Subscription failed');
    }
  };

  const handleUnsubscribe = async (peerId: string) => {
    setError('');
    try {
      await api.unsubscribePeer(peerId);
      load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unsubscribe failed');
    }
  };

  return (
    <div>
      <h1 className="page-title">Federation</h1>
      <p className="page-subtitle">
        Connect independent Snekl nodes into a distributed network. Each node runs its own database and web services.
      </p>

      {error && <div className="error-banner">{error}</div>}
      {success && <div className="card" style={{ borderColor: 'var(--accent)', marginBottom: '1rem' }}>{success}</div>}

      {node && (
        <div className="card">
          <h2 className="card-title">This node</h2>
          <div className="grid-2">
            <div><strong>Name:</strong> {node.name}</div>
            <div><strong>URL:</strong> {node.baseUrl}</div>
            <div><strong>ID:</strong> <code>{node.id}</code></div>
            <div><strong>Description:</strong> {node.description || '—'}</div>
          </div>
        </div>
      )}

      <form onSubmit={handleSubscribe} className="card">
        <h2 className="card-title">Subscribe to a peer</h2>
        <p className="card-meta">Enter the base URL of another Snekl node (e.g. http://localhost:5002)</p>
        <div className="inline-form">
          <input
            value={peerUrl}
            onChange={(e) => setPeerUrl(e.target.value)}
            placeholder="http://localhost:5002"
            required
          />
          <button type="submit" className="btn-primary">Subscribe</button>
        </div>
      </form>

      <div className="card">
        <h2 className="card-title">Connected peers ({peers.length})</h2>
        {peers.length === 0 && <p className="card-meta">No peers connected yet.</p>}
        {peers.map((peer) => (
          <div key={peer.id} style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '0.75rem 0', borderBottom: '1px solid var(--border)' }}>
            <div>
              <strong>{peer.name}</strong>
              <div className="card-meta">{peer.baseUrl}</div>
            </div>
            <button type="button" className="btn-ghost" onClick={() => handleUnsubscribe(peer.id)}>
              Remove
            </button>
          </div>
        ))}
      </div>

      <div className="card">
        <h2 className="card-title">How federation works</h2>
        <ol style={{ lineHeight: 1.7, color: 'var(--muted)' }}>
          <li>Each Snekl node is an independent ASP.NET Core web service with its own PostgreSQL database.</li>
          <li>When you create a forum or post, it is broadcast to all subscribed peer nodes via HTTP.</li>
          <li>Peer nodes receive content in their federation inbox and store it locally with origin metadata.</li>
          <li>Users browse a unified feed — local and federated content appear together.</li>
        </ol>
      </div>
    </div>
  );
}

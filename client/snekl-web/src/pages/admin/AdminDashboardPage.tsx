import { type FormEvent, useEffect, useState } from 'react';
import { adminApi, type TrustedAuthIssuer } from '../../adminApi';
import type { NodeInfo, Peer } from '../../types';

export function AdminDashboardPage() {
  const [node, setNode] = useState<NodeInfo | null>(null);
  const [peers, setPeers] = useState<Peer[]>([]);
  const [issuers, setIssuers] = useState<TrustedAuthIssuer[]>([]);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');

  const [nodeName, setNodeName] = useState('');
  const [nodeUrl, setNodeUrl] = useState('');
  const [nodeDescription, setNodeDescription] = useState('');

  const [peerUrl, setPeerUrl] = useState('');
  const [issuerUrl, setIssuerUrl] = useState('');
  const [jwksUrl, setJwksUrl] = useState('');
  const [issuerName, setIssuerName] = useState('');

  const load = () => {
    Promise.all([adminApi.getNode(), adminApi.getPeers(), adminApi.getIssuers()])
      .then(([nodeInfo, peerList, issuerList]) => {
        setNode(nodeInfo);
        setNodeName(nodeInfo.name);
        setNodeUrl(nodeInfo.baseUrl);
        setNodeDescription(nodeInfo.description);
        setPeers(peerList);
        setIssuers(issuerList);
      })
      .catch((err) => setError(err.message));
  };

  useEffect(load, []);

  const saveNode = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    setSuccess('');
    try {
      const updated = await adminApi.updateNode({
        name: nodeName.trim(),
        baseUrl: nodeUrl.trim(),
        description: nodeDescription.trim(),
      });
      setNode(updated);
      setSuccess('Node configuration saved.');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save node');
    }
  };

  const addPeer = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    setSuccess('');
    try {
      await adminApi.subscribePeer(peerUrl.trim());
      setPeerUrl('');
      setSuccess('Peer subscribed.');
      load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to subscribe peer');
    }
  };

  const removePeer = async (peerId: string) => {
    setError('');
    try {
      await adminApi.unsubscribePeer(peerId);
      load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to remove peer');
    }
  };

  const addIssuer = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    setSuccess('');
    try {
      await adminApi.registerIssuer({
        issuerUrl: issuerUrl.trim(),
        jwksUrl: jwksUrl.trim(),
        name: issuerName.trim(),
      });
      setIssuerUrl('');
      setJwksUrl('');
      setIssuerName('');
      setSuccess('Auth issuer registered.');
      load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to register issuer');
    }
  };

  return (
    <div>
      <h1 className="page-title">Site configuration</h1>
      <p className="page-subtitle">
        Manage this content node, federation peers, and trusted auth issuers. User posting and profiles are handled
        through normal user accounts.
      </p>

      {error && <div className="error-banner">{error}</div>}
      {success && (
        <div className="card" style={{ borderColor: 'var(--accent)', marginBottom: '1rem' }}>
          {success}
        </div>
      )}

      <form onSubmit={saveNode} className="card">
        <h2 className="card-title">Node identity</h2>
        {node && (
          <p className="card-meta">
            ID: <code>{node.id}</code> · DID: <code>{node.did}</code>
          </p>
        )}
        <div className="form-group">
          <label htmlFor="node-name">Name</label>
          <input id="node-name" value={nodeName} onChange={(e) => setNodeName(e.target.value)} required />
        </div>
        <div className="form-group">
          <label htmlFor="node-url">Base URL</label>
          <input id="node-url" value={nodeUrl} onChange={(e) => setNodeUrl(e.target.value)} required />
        </div>
        <div className="form-group">
          <label htmlFor="node-description">Description</label>
          <textarea
            id="node-description"
            value={nodeDescription}
            onChange={(e) => setNodeDescription(e.target.value)}
          />
        </div>
        <button type="submit" className="btn-primary">
          Save node config
        </button>
      </form>

      <form onSubmit={addPeer} className="card">
        <h2 className="card-title">Federation peers</h2>
        <p className="card-meta">Connect to other Snekl nodes for server-side content sync.</p>
        <div className="inline-form">
          <input
            value={peerUrl}
            onChange={(e) => setPeerUrl(e.target.value)}
            placeholder="http://localhost:5002"
            required
          />
          <button type="submit" className="btn-primary">
            Add peer
          </button>
        </div>
        {peers.map((peer) => (
          <div key={peer.id} className="server-row">
            <div>
              <strong>{peer.name}</strong>
              <div className="card-meta">{peer.baseUrl}</div>
            </div>
            <button type="button" className="btn-ghost" onClick={() => removePeer(peer.id)}>
              Remove
            </button>
          </div>
        ))}
      </form>

      <form onSubmit={addIssuer} className="card">
        <h2 className="card-title">Trusted auth issuers</h2>
        <p className="card-meta">Auth servers whose JWTs this node accepts for user login.</p>
        <div className="form-group">
          <label htmlFor="issuer-name">Name</label>
          <input id="issuer-name" value={issuerName} onChange={(e) => setIssuerName(e.target.value)} required />
        </div>
        <div className="form-group">
          <label htmlFor="issuer-url">Issuer URL</label>
          <input id="issuer-url" value={issuerUrl} onChange={(e) => setIssuerUrl(e.target.value)} required />
        </div>
        <div className="form-group">
          <label htmlFor="jwks-url">JWKS URL</label>
          <input id="jwks-url" value={jwksUrl} onChange={(e) => setJwksUrl(e.target.value)} required />
        </div>
        <button type="submit" className="btn-primary">
          Register issuer
        </button>
        {issuers.map((issuer) => (
          <div key={issuer.id} className="server-row">
            <div>
              <strong>{issuer.name}</strong>
              <div className="card-meta">
                {issuer.issuerUrl} · JWKS {issuer.jwksUrl}
              </div>
            </div>
          </div>
        ))}
      </form>
    </div>
  );
}

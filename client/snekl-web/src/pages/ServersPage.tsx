import { type FormEvent, useState } from 'react';
import { Link } from 'react-router';
import { useAuth } from '../AuthContext';
import { useServerRegistry } from '../ServerRegistryContext';
import { shortServerDid } from '../serverDid';

export function ServersPage() {
  const { user } = useAuth();
  const { servers, presets, loading, subscribe, unsubscribe, setStatus } = useServerRegistry();
  const [baseUrl, setBaseUrl] = useState('');
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');

  const handleSubscribe = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    setSuccess('');
    try {
      const membership = await subscribe(baseUrl.trim());
      setBaseUrl('');
      setSuccess(`Subscribed to ${membership.name}.`);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Subscription failed');
    }
  };

  const handleUnsubscribe = async (serverDid: string) => {
    setError('');
    setSuccess('');
    try {
      await unsubscribe(serverDid);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unsubscribe failed');
    }
  };

  const handleStatusChange = async (serverDid: string, status: string) => {
    setError('');
    try {
      await setStatus(serverDid, status);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to update status');
    }
  };

  if (!user) {
    return (
      <div className="card">
        <p>
          Please <Link to="/login">log in</Link> to manage your server subscriptions.
        </p>
      </div>
    );
  }

  return (
    <div>
      <h1 className="page-title">Servers</h1>
      <p className="page-subtitle">
        Subscribe to content servers to mix their forums in your home feed — like subreddits on Reddit.
      </p>

      {error && <div className="error-banner">{error}</div>}
      {success && (
        <div className="card" style={{ borderColor: 'var(--accent)', marginBottom: '1rem' }}>
          {success}
        </div>
      )}

      <form onSubmit={handleSubscribe} className="card">
        <h2 className="card-title">Add a server</h2>
        <p className="card-meta">
          Enter the base URL of a Snekl content node. Local dev uses <code>http://</code> (not https).
          Beta node: <code>http://localhost:5002</code>
        </p>
        <div className="inline-form">
          <input
            value={baseUrl}
            onChange={(e) => setBaseUrl(e.target.value)}
            placeholder="http://localhost:5002"
            required
          />
          <button type="submit" className="btn-primary">
            Subscribe
          </button>
        </div>
      </form>

      <div className="card">
        <h2 className="card-title">Your subscriptions</h2>
        {loading && <p className="card-meta">Loading...</p>}
        {!loading && servers.length === 0 && (
          <p className="card-meta">No servers subscribed yet.</p>
        )}
        {servers.map((server) => (
          <div key={server.serverDid} className="server-row">
            <div>
              <strong>{server.name}</strong>
              <div className="card-meta">
                <code>{shortServerDid(server.serverDid)}</code> · {server.baseUrl} · subscribed{' '}
                {new Date(server.subscribedAt).toLocaleDateString()}
              </div>
            </div>
            <div className="server-row-actions">
              <select
                value={server.status}
                onChange={(e) => handleStatusChange(server.serverDid, e.target.value)}
                aria-label={`Status for ${server.name}`}
              >
                {presets.map((preset) => (
                  <option key={preset.preset} value={preset.preset}>
                    {preset.label}
                  </option>
                ))}
              </select>
              <button
                type="button"
                className="btn-ghost"
                onClick={() => handleUnsubscribe(server.serverDid)}
              >
                Remove
              </button>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

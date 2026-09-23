import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router';
import { createContentClient } from '../api';
import { useServerRegistry } from '../ServerRegistryContext';
import type { ExtensionInstall } from '../types';

export function ExtensionsPage() {
  const { activeServers } = useServerRegistry();
  const [installs, setInstalls] = useState<ExtensionInstall[]>([]);
  const [error, setError] = useState('');
  const origin = activeServers[0];
  const client = useMemo(() => (origin ? createContentClient(origin.baseUrl) : null), [origin]);

  useEffect(() => {
    if (!client) return;
    client
      .getExtensions()
      .then(setInstalls)
      .catch((err) => setError(err instanceof Error ? err.message : 'Failed to load extensions'));
  }, [client]);

  const unique = useMemo(() => {
    const latest = new Map<string, ExtensionInstall>();
    for (const item of installs) {
      const key = item.clientId.toLowerCase();
      const current = latest.get(key);
      if (!current || item.createdAt > current.createdAt) latest.set(key, item);
    }
    return [...latest.values()];
  }, [installs]);
  const framed = unique.filter((item) => item.iframeUrl);
  const listed = unique.filter((item) => !item.iframeUrl);

  return (
    <div>
      <h1 className="page-title">Extensions</h1>
      <p className="page-subtitle">
        Apps authorized on this node. Iframes are browser sources; Faker and other bots may only appear as a name and
        admin link.
      </p>
      {error && <div className="error-banner">{error}</div>}
      {!origin && (
        <p className="card-meta">
          Add a content server on <Link to="/servers">Servers</Link> to see installs.
        </p>
      )}
      {listed.map((item) => (
        <div key={item.id} className="card">
          <h2 className="card-title">{item.appName}</h2>
          <p className="card-meta">
            {item.resourceType === 'forum' ? item.forumName || 'Forum' : 'Whole server'}
          </p>
          {item.adminUrl && (
            <p>
              <a href={item.adminUrl}>Open admin</a>
            </p>
          )}
        </div>
      ))}
      {framed.map((item) => (
        <div key={item.id} className="card">
          <h2 className="card-title">{item.appName}</h2>
          <iframe
            title={item.appName}
            src={item.iframeUrl!}
            style={{ width: '100%', minHeight: 320, border: 0, borderRadius: 8, background: '#111' }}
            sandbox="allow-scripts allow-same-origin allow-forms"
          />
        </div>
      ))}
      {origin && unique.length === 0 && !error && (
        <p className="card-meta">No extensions authorized on this node yet.</p>
      )}
    </div>
  );
}

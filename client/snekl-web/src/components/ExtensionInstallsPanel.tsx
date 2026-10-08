import { useEffect, useMemo, useState } from 'react';
import type { ExtensionInstall } from '../types';

type ExtensionsClient = {
  getExtensions: () => Promise<ExtensionInstall[]>;
};

export function ExtensionInstallsPanel({
  client,
  scope,
  forumId,
  emptyHint,
}: {
  client: ExtensionsClient | null;
  /** server = whole-server installs; forum = installs for one forum */
  scope: 'server' | 'forum';
  forumId?: string | null;
  emptyHint?: string;
}) {
  const [installs, setInstalls] = useState<ExtensionInstall[]>([]);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    if (!client) return;
    setLoading(true);
    setError('');
    client
      .getExtensions()
      .then(setInstalls)
      .catch((err) => setError(err instanceof Error ? err.message : 'Failed to load extensions'))
      .finally(() => setLoading(false));
  }, [client]);

  const unique = useMemo(() => {
    const latest = new Map<string, ExtensionInstall>();
    for (const item of installs) {
      const matches =
        scope === 'server'
          ? item.resourceType !== 'forum'
          : item.resourceType === 'forum' && item.forumId === forumId;
      if (!matches) continue;
      const key = item.clientId.toLowerCase();
      const current = latest.get(key);
      if (!current || item.createdAt > current.createdAt) latest.set(key, item);
    }
    return [...latest.values()];
  }, [installs, scope, forumId]);

  const framed = unique.filter((item) => item.iframeUrl);
  const listed = unique.filter((item) => !item.iframeUrl);

  return (
    <section className="card" id="extensions">
      <h2 className="card-title">Extensions</h2>
      <p className="card-meta">
        {scope === 'server'
          ? 'Apps authorized for this whole server. New installs start from the extension’s authorize link.'
          : 'Apps authorized for this nest. New installs start from the extension’s authorize link.'}
      </p>
      {error && <div className="error-banner">{error}</div>}
      {loading && <p className="card-meta">Loading…</p>}
      {!loading &&
        listed.map((item) => (
          <div key={item.id} className="server-row" style={{ marginTop: '0.75rem' }}>
            <div>
              <strong>{item.appName}</strong>
              <div className="card-meta">
                {scope === 'forum' ? 'Nest install' : 'Server install'}
              </div>
            </div>
            {item.adminUrl && (
              <div className="server-row-actions">
                <a href={item.adminUrl} className="btn-ghost">
                  Open admin
                </a>
              </div>
            )}
          </div>
        ))}
      {!loading &&
        framed.map((item) => (
          <div key={item.id} style={{ marginTop: '0.75rem' }}>
            <h3 className="card-title" style={{ fontSize: '1.05rem' }}>
              {item.appName}
            </h3>
            <iframe
              title={item.appName}
              src={item.iframeUrl!}
              style={{ width: '100%', minHeight: 320, border: 0, borderRadius: 8, background: '#111' }}
              sandbox="allow-scripts allow-same-origin allow-forms"
            />
          </div>
        ))}
      {!loading && unique.length === 0 && !error && (
        <p className="card-meta">{emptyHint ?? 'No extensions authorized here yet.'}</p>
      )}
    </section>
  );
}

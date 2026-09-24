import { useEffect, useMemo, useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { createContentClient } from '../api';
import { useAuth } from '../AuthContext';
import { useServerRegistry } from '../ServerRegistryContext';
import { getAuthBaseUrl } from '../hosts';
import type { ExtensionInstall, ExtensionInstallOptions } from '../types';

type OAuthApp = {
  clientId: string;
  name: string;
  adminUrl?: string | null;
  iframeUrl?: string | null;
  redirectUris: string[];
};

const DEFAULT_SERVER = ['ViewServer', 'CreateForum'];
const DEFAULT_FORUM = ['SeeForum', 'ViewForum', 'CreateThread', 'Reply', 'EditOwnPost', 'DeleteOwnPost'];

function contentLabel(url: string) {
  if (url.endsWith(':5002')) return 'Snekl Beta';
  if (url.endsWith(':5001')) return 'Snekl Alpha';
  return url || 'this server';
}

export function AuthorizeExtensionPage() {
  const { user, loading } = useAuth();
  const { activeServers } = useServerRegistry();
  const [params] = useSearchParams();
  const [error, setError] = useState('');
  const [app, setApp] = useState<OAuthApp | null>(null);
  const [options, setOptions] = useState<ExtensionInstallOptions | null>(null);
  const [resource, setResource] = useState<'server' | 'forum'>('server');
  const [forumId, setForumId] = useState('');
  const [busy, setBusy] = useState(false);

  const clientId = params.get('client_id') || '';
  const redirectUri = params.get('redirect_uri') || '';
  const state = params.get('state') || '';
  const codeChallenge = params.get('code_challenge') || '';
  const method = params.get('code_challenge_method') || 'S256';
  const contentUrl = params.get('content') || activeServers[0]?.baseUrl || '';
  const authUrl = params.get('auth') || getAuthBaseUrl();
  const returnTo = `/extensions/authorize?${params.toString()}`;

  const content = useMemo(
    () => (contentUrl ? createContentClient(contentUrl) : null),
    [contentUrl],
  );

  useEffect(() => {
    if (!clientId) return;
    fetch(`${authUrl.replace(/\/$/, '')}/api/oauth/apps/${encodeURIComponent(clientId)}`)
      .then(async (res) => {
        const body = await res.json();
        if (!res.ok) throw new Error(body.error || 'Unknown application.');
        setApp(body);
      })
      .catch((err) => setError(err instanceof Error ? err.message : 'Failed to load app'));
  }, [authUrl, clientId]);

  useEffect(() => {
    if (!user || !content) return;
    content
      .getExtensionInstallOptions(clientId || undefined)
      .then((opts) => {
        setOptions(opts);
        if (opts.existingInstall) return;
        if (!opts.canInstallServer) {
          setResource('forum');
          const first = opts.forums.find((f) => f.canInstall);
          if (first) setForumId(first.id);
        }
      })
      .catch((err) => setError(err instanceof Error ? err.message : 'Failed to load install options'));
  }, [user, content, clientId]);

  if (loading) return <p className="card-meta">Loading…</p>;
  if (!user) {
    return (
      <div className="card">
        <h1 className="page-title">Authorize extension</h1>
        <p>
          <Link to={`/login?return=${encodeURIComponent(returnTo)}`}>Log in</Link> as a forum or server admin
          to authorize this app. It will not post as you.
        </p>
      </div>
    );
  }

  const submit = async () => {
    if (!content || !app) return;
    setBusy(true);
    setError('');
    try {
      const install = options?.existingInstall
        ? options.existingInstall
        : await content.createExtensionInstall({
            clientId,
            appName: app.name,
            adminUrl: app.adminUrl,
            iframeUrl: app.iframeUrl,
            resourceType: resource,
            forumId: resource === 'forum' ? forumId : null,
            serverPermissions: DEFAULT_SERVER,
            forumPermissions: DEFAULT_FORUM,
          });
      const res = await fetch(`${authUrl.replace(/\/$/, '')}/api/oauth/authorize`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${localStorage.getItem('snekl_token') || ''}`,
        },
        body: JSON.stringify({
          clientId,
          redirectUri,
          codeChallenge,
          codeChallengeMethod: method,
          contentBaseUrl: contentUrl,
          installId: install.id,
          state,
        }),
      });
      const body = await res.json();
      if (!res.ok) throw new Error(body.error || 'Authorize failed');
      window.location.href = body.redirectTo;
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Authorize failed');
      setBusy(false);
    }
  };

  return (
    <div className="card" style={{ maxWidth: 560, margin: '0 auto' }}>
      <h1 className="page-title">Authorize {app?.name || 'extension'}</h1>
      <p className="page-subtitle">
        {options?.existingInstall ? (
          <>
            Reconnect {app?.name || 'this app'} on {contentLabel(contentUrl)}. Scope stays as already granted; it still
            operates as its own bots, not as {user.effectiveDisplayName}.
          </>
        ) : (
          <>
            You are allowing this app onto a resource you admin on {contentLabel(contentUrl)}. It will operate as its own
            bots, not as {user.effectiveDisplayName}.
          </>
        )}
      </p>
      {error && <div className="error-banner">{error}</div>}
      {options && (
        <>
          {options.existingInstall ? (
            <ExistingInstallSummary install={options.existingInstall} />
          ) : (
            <>
              <div className="form-group">
                <label>
                  <input
                    type="radio"
                    name="resource"
                    checked={resource === 'server'}
                    disabled={!options.canInstallServer}
                    onChange={() => setResource('server')}
                  />{' '}
                  This whole server
                </label>
              </div>
              <div className="form-group">
                <label>
                  <input
                    type="radio"
                    name="resource"
                    checked={resource === 'forum'}
                    onChange={() => setResource('forum')}
                  />{' '}
                  One forum I manage
                </label>
              </div>
              {resource === 'forum' && (
                <div className="form-group">
                  <label htmlFor="forum">Forum</label>
                  <select id="forum" value={forumId} onChange={(e) => setForumId(e.target.value)}>
                    {options.forums
                      .filter((f) => f.canInstall)
                      .map((f) => (
                        <option key={f.id} value={f.id}>
                          {f.name}
                        </option>
                      ))}
                  </select>
                </div>
              )}
              <p className="card-meta">
                Granted permissions are clipped to what you can already do on that resource. Super User is never
                delegated.
              </p>
            </>
          )}
          <button type="button" className="btn-primary" disabled={busy || !clientId} onClick={() => void submit()}>
            {busy ? 'Authorizing…' : options.existingInstall ? 'Reconnect' : 'Authorize'}
          </button>
        </>
      )}
    </div>
  );
}

function ExistingInstallSummary({ install }: { install: ExtensionInstall }) {
  const scope =
    install.resourceType === 'forum' ? install.forumName || 'one forum' : 'this whole server';
  return (
    <p className="card-meta">
      Already authorized on {scope}. Reconnecting issues a new token without changing that grant.
    </p>
  );
}

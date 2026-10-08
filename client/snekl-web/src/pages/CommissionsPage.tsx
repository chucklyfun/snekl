import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Navigate, useSearchParams } from 'react-router';
import { apiFor } from '../api';
import { useAuth } from '../AuthContext';
import { postCommissionsSession } from '../extensionSession';
import { frameOriginMatches, rewriteFrameUrl } from '../hosts';
import { useServerRegistry } from '../ServerRegistryContext';

export function CommissionsPage() {
  const { user, loading } = useAuth();
  const { activeServers } = useServerRegistry();
  const contentBaseUrl = activeServers[0]?.baseUrl;
  const serverId = activeServers[0]?.nodeId;
  const api = useMemo(() => (contentBaseUrl ? apiFor(contentBaseUrl) : null), [contentBaseUrl]);
  const [params] = useSearchParams();
  const listing = params.get('listing');
  const frame = useRef<HTMLIFrameElement>(null);
  const frameOrigin = useRef<string | null>(null);
  const signedIn = useRef(false);
  const [iframeUrl, setIframeUrl] = useState<string | null>(null);
  const [error, setError] = useState('');

  useEffect(() => {
    if (!api) return;
    api
      .getExtensions()
      .then((installs) => {
        const match = installs.find((item) => item.clientId === 'snekl-commissions' && item.iframeUrl);
        setIframeUrl(match?.iframeUrl ?? null);
        if (!match) setError('Commissions is not installed on this node yet. A node admin connects it from the extension.');
      })
      .catch((err) => setError(err instanceof Error ? err.message : 'Failed to load extensions'));
  }, [api]);

  const frameUrl = iframeUrl ? rewriteFrameUrl(iframeUrl) : null;

  const sendSession = useCallback(async () => {
    const target = frame.current?.contentWindow;
    if (!target || !contentBaseUrl || !frameUrl || loading) return;
    const reported = frameOrigin.current;
    const origin =
      reported && frameOriginMatches(reported, frameUrl) ? reported : new URL(frameUrl).origin;
    if (!user) {
      if (!signedIn.current) return;
      signedIn.current = false;
      target.postMessage({ type: 'snekl-commissions-sign-out' }, origin);
      return;
    }
    signedIn.current = true;
    await postCommissionsSession(target, origin, contentBaseUrl, serverId);
  }, [contentBaseUrl, frameUrl, loading, serverId, user]);

  useEffect(() => {
    void sendSession();
  }, [sendSession]);

  useEffect(() => {
    frameOrigin.current = null;
  }, [frameUrl]);

  useEffect(() => {
    const onMessage = (event: MessageEvent) => {
      const target = frame.current?.contentWindow;
      if (!target || event.source !== target || !frameUrl || !frameOriginMatches(event.origin, frameUrl)) return;
      const data = event.data as { type?: string; error?: string } | null;
      if (data?.type === 'snekl-commissions-ready') {
        frameOrigin.current = event.origin;
        void sendSession();
      } else if (data?.type === 'snekl-commissions-session-ok') {
        setError('');
      } else if (data?.type === 'snekl-commissions-session-failed') {
        setError(data.error || 'Commissions could not use this sign-in.');
      }
    };
    window.addEventListener('message', onMessage);
    return () => window.removeEventListener('message', onMessage);
  }, [frameUrl, sendSession]);

  if (loading) {
    return (
      <div>
        <h1 className="page-title">Commissions</h1>
        <p className="card-meta">Loading...</p>
      </div>
    );
  }

  if (!user) return <Navigate to="/login?return=%2Fcommissions" replace />;

  if (!contentBaseUrl) {
    return <p className="card-meta">Subscribe to a content server on Servers to open commissions.</p>;
  }

  const src = frameUrl
    ? `${frameUrl.replace(/\/?$/, '/')}embed/requests?content=${encodeURIComponent(contentBaseUrl.replace(/\/$/, ''))}${listing ? `#listing=${encodeURIComponent(listing)}` : ''}`
    : '';

  return (
    <div>
      <h1 className="page-title">Commissions</h1>
      {error && <p className="error">{error}</p>}
      {src && (
        <iframe
          ref={frame}
          title="Snekl Commissions"
          src={src}
          onLoad={() => void sendSession()}
          style={{ width: '100%', minHeight: 720, border: 0, borderRadius: 8, background: '#111' }}
          sandbox="allow-scripts allow-same-origin allow-forms"
        />
      )}
    </div>
  );
}

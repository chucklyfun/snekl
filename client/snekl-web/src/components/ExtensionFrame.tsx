import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Link } from 'react-router';
import { apiFor } from '../api';
import { useAuth } from '../AuthContext';
import { postCommissionsSession } from '../extensionSession';
import { frameOriginMatches, rewriteFrameUrl } from '../hosts';
import type { ExtensionView } from '../forumLayoutEditor';
import type { ExtensionInstall } from '../types';

const VIEWS = ['storefront', 'studio', 'desk', 'requests'] as const;

function isView(value: unknown): value is ExtensionView {
  return VIEWS.some((view) => view === value);
}

export function ExtensionFrame({
  settings,
  contentBaseUrl,
}: {
  settings?: Record<string, unknown>;
  contentBaseUrl?: string | null;
}) {
  const { user, loading } = useAuth();
  const clientId = typeof settings?.clientId === 'string' ? settings.clientId : '';
  const view = isView(settings?.view) ? settings.view : null;
  const artistUserId = typeof settings?.artistUserId === 'string' ? settings.artistUserId : '';
  const api = useMemo(() => (contentBaseUrl ? apiFor(contentBaseUrl) : null), [contentBaseUrl]);
  const frame = useRef<HTMLIFrameElement>(null);
  const frameOrigin = useRef<string | null>(null);
  const signedIn = useRef(false);
  const [install, setInstall] = useState<ExtensionInstall | null>(null);
  const [error, setError] = useState('');
  const [sessionError, setSessionError] = useState('');

  useEffect(() => {
    if (!api || !clientId) return;
    api
      .getExtensions()
      .then((installs) => {
        const match = installs.find((item) => item.clientId === clientId && item.iframeUrl) ?? null;
        setInstall(match);
        if (!match) setError('This extension is not installed on this node yet.');
      })
      .catch((err) => setError(err instanceof Error ? err.message : 'Failed to load extensions'));
  }, [api, clientId]);

  const src = useMemo(() => {
    if (!install?.iframeUrl || !view || !contentBaseUrl) return '';
    if (view === 'storefront' && !artistUserId) return '';
    const path = view === 'storefront' ? `embed/store/${encodeURIComponent(artistUserId)}` : `embed/${view}`;
    const base = rewriteFrameUrl(install.iframeUrl).replace(/\/?$/, '/');
    return `${base}${path}?content=${encodeURIComponent(contentBaseUrl.replace(/\/$/, ''))}`;
  }, [artistUserId, contentBaseUrl, install, view]);

  const sendSession = useCallback(async () => {
    const target = frame.current?.contentWindow;
    if (!target || !src || !contentBaseUrl || clientId !== 'snekl-commissions' || loading) return;
    const reported = frameOrigin.current;
    const origin = reported && frameOriginMatches(reported, src) ? reported : new URL(src).origin;
    if (!user) {
      if (!signedIn.current) return;
      signedIn.current = false;
      target.postMessage({ type: 'snekl-commissions-sign-out' }, origin);
      return;
    }
    signedIn.current = true;
    await postCommissionsSession(target, origin, contentBaseUrl);
  }, [clientId, contentBaseUrl, loading, src, user]);

  useEffect(() => {
    void sendSession();
  }, [sendSession]);

  useEffect(() => {
    frameOrigin.current = null;
  }, [src]);

  useEffect(() => {
    if (!src || clientId !== 'snekl-commissions') return;
    const onMessage = (event: MessageEvent) => {
      const target = frame.current?.contentWindow;
      if (!target || event.source !== target || !frameOriginMatches(event.origin, src)) return;
      const data = event.data as { type?: string; error?: string } | null;
      if (data?.type === 'snekl-commissions-ready') {
        frameOrigin.current = event.origin;
        void sendSession();
      } else if (data?.type === 'snekl-commissions-session-failed') {
        setSessionError(data.error || 'Commissions could not use this sign-in.');
      } else if (data?.type === 'snekl-commissions-session-ok') {
        setSessionError('');
      }
    };
    window.addEventListener('message', onMessage);
    return () => window.removeEventListener('message', onMessage);
  }, [clientId, sendSession, src]);

  if (!clientId || !view) return <p className="card-meta">Choose an extension and a page for this region.</p>;
  if (clientId === 'snekl-commissions' && loading) return <p className="card-meta">Loading...</p>;
  if (clientId === 'snekl-commissions' && !user) {
    return (
      <div className="card">
        <p>
          Please <Link to={`/login?return=${encodeURIComponent(`${window.location.pathname}${window.location.search}`)}`}>log in</Link> to use commissions.
        </p>
      </div>
    );
  }
  if (!contentBaseUrl) return <p className="card-meta">Subscribe to a content server to open this extension.</p>;
  if (error) return <p className="error">{error}</p>;
  if (!src) return <p className="card-meta">Choose an artist for this storefront.</p>;

  return (
    <>
      {sessionError && <p className="error">{sessionError}</p>}
      <iframe
        ref={frame}
        title={install?.appName || 'Extension'}
        src={src}
        onLoad={() => void sendSession()}
        style={{ width: '100%', minHeight: 720, border: 0, borderRadius: 8, background: '#111' }}
        sandbox="allow-scripts allow-same-origin allow-forms"
      />
    </>
  );
}

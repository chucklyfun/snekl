import { useEffect, useState } from 'react';
import type { AdminBootstrap } from '../types';
import { AdminLayout } from './AdminLayout';
import { AuthAdmin } from './AuthAdmin';
import { ContentAdmin } from './ContentAdmin';
import { HubAdmin } from './HubAdmin';
import { OperatorGate } from './OperatorGate';
import { ADMIN_KIND_UI, adminToken, clearAdminSession, loadBootstrap, type AdminKind } from './api';

export function AdminApp() {
  const [bootstrap, setBootstrap] = useState<AdminBootstrap | null>(null);
  const [error, setError] = useState('');
  const [ready, setReady] = useState(Boolean(adminToken()));

  useEffect(() => {
    loadBootstrap()
      .then(setBootstrap)
      .catch((err) => setError(err instanceof Error ? err.message : 'Failed to load host'));
  }, []);

  if (error) {
    return (
      <div className="app-shell admin-shell">
        <main className="app-main">
          <div className="error-banner">{error}</div>
        </main>
      </div>
    );
  }

  if (!bootstrap) {
    return (
      <div className="app-shell admin-shell">
        <main className="app-main">
          <p className="card-meta">Loading this host…</p>
        </main>
      </div>
    );
  }

  const kind = bootstrap.kind as AdminKind;
  const title = ADMIN_KIND_UI[kind].title;

  return (
    <AdminLayout
      kind={kind}
      name={bootstrap.name}
      title={title}
      meta={bootstrap.publicBaseUrl}
      onLogout={
        ready
          ? () => {
              clearAdminSession();
              setReady(false);
            }
          : undefined
      }
    >
      {!ready ? (
        <OperatorGate
          kind={kind}
          authLoginUrl={bootstrap.authLoginUrl}
          hostLabel={`${bootstrap.name} · ${bootstrap.publicBaseUrl}`}
          onReady={() => setReady(true)}
        />
      ) : kind === 'hub' ? (
        <HubAdmin />
      ) : kind === 'auth' ? (
        <AuthAdmin />
      ) : (
        <ContentAdmin />
      )}
    </AdminLayout>
  );
}

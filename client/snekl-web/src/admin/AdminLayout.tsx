import { type ReactNode, useEffect } from 'react';
import { ADMIN_KIND_UI, type AdminKind } from './api';

export function AdminLayout({
  kind,
  name,
  title,
  meta,
  onLogout,
  children,
}: {
  kind: AdminKind;
  name: string;
  title: string;
  meta?: string;
  onLogout?: () => void;
  children: ReactNode;
}) {
  const ui = ADMIN_KIND_UI[kind];

  useEffect(() => {
    const previous = document.title;
    document.title = `${name} · ${ui.title}`;
    return () => {
      document.title = previous;
    };
  }, [name, ui.title]);

  return (
    <div className="app-shell admin-shell" data-admin={kind}>
      <header className="app-header admin-header">
        <div className="header-bar">
          <span className="brand">
            <span className="brand-mark">{ui.mark}</span>
            {name}
            <span className="admin-badge">{ui.badge}</span>
          </span>
          {onLogout && (
            <button type="button" className="btn-ghost" onClick={onLogout}>
              Log out
            </button>
          )}
        </div>
      </header>
      <main className="app-main">
        <p className="card-meta" style={{ marginBottom: '0.5rem' }}>
          {title}
          {meta ? ` · ${meta}` : ''}
        </p>
        {children}
      </main>
    </div>
  );
}

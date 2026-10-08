import { type FormEvent, useEffect, useRef, useState } from 'react';
import { Link, NavLink, useMatch, useNavigate, useSearchParams } from 'react-router';
import { createContentClient } from './api';
import { useAuth } from './AuthContext';
import { useServerRegistry } from './ServerRegistryContext';
import { replyAlertsChangedEvent } from './replyAlerts';
import { useReplyAlertPush } from './replyAlertPush';
import { ForumBrowser } from './components/ForumBrowser';
import { decodeServerDid, resolveServerBaseUrl } from './serverDid';
import { adminUrl, getAuthBaseUrl } from './hosts';
import './Layout.css';

function navClassName({ isActive }: { isActive: boolean }) {
  return isActive ? 'nav-link nav-link-active' : 'nav-link';
}

function HeaderSearch({ onNavigate }: { onNavigate: () => void }) {
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const [q, setQ] = useState(params.get('q') ?? '');

  useEffect(() => {
    setQ(params.get('q') ?? '');
  }, [params]);

  const onSubmit = (event: FormEvent) => {
    event.preventDefault();
    const next = q.trim();
    onNavigate();
    navigate(next ? `/search?q=${encodeURIComponent(next)}` : '/search');
  };

  return (
    <form className="header-search" onSubmit={onSubmit} role="search">
      <input
        type="search"
        value={q}
        onChange={(event) => setQ(event.target.value)}
        placeholder="Search posts"
        aria-label="Search posts"
      />
    </form>
  );
}

export function Layout({ children }: { children: React.ReactNode }) {
  const { user, logout } = useAuth();
  const { federations, resolveBaseUrl, servers } = useServerRegistry();
  const serverRoute = useMatch('/s/:serverDid/*');
  const [menuOpen, setMenuOpen] = useState(false);
  const [forumsOpen, setForumsOpen] = useState(false);
  const [userMenuOpen, setUserMenuOpen] = useState(false);
  const [unreadAlerts, setUnreadAlerts] = useState(0);
  const userMenuRef = useRef<HTMLDivElement>(null);
  const navigate = useNavigate();
  const serverKey = servers.map((server) => server.serverDid).join('|');
  useReplyAlertPush(user ? servers : []);

  const contextServerDid = serverRoute?.params.serverDid
    ? decodeServerDid(serverRoute.params.serverDid)
    : null;

  useEffect(() => {
    if (!userMenuOpen) return;
    const onPointerDown = (event: MouseEvent) => {
      if (userMenuRef.current && !userMenuRef.current.contains(event.target as Node)) {
        setUserMenuOpen(false);
      }
    };
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setUserMenuOpen(false);
    };
    document.addEventListener('mousedown', onPointerDown);
    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.removeEventListener('mousedown', onPointerDown);
      document.removeEventListener('keydown', onKeyDown);
    };
  }, [userMenuOpen]);

  useEffect(() => {
    if (!user || servers.length === 0) {
      setUnreadAlerts(0);
      return;
    }

    let cancelled = false;
    const load = () => {
      void Promise.all(
        servers.map(async (server) => {
          try {
            const result = await createContentClient(server.baseUrl).getReplyAlertUnreadCount();
            return result.count;
          } catch {
            return 0;
          }
        }),
      ).then((counts) => {
        if (!cancelled) setUnreadAlerts(counts.reduce((sum, count) => sum + count, 0));
      });
    };

    load();
    window.addEventListener('focus', load);
    window.addEventListener(replyAlertsChangedEvent, load);
    return () => {
      cancelled = true;
      window.removeEventListener('focus', load);
      window.removeEventListener(replyAlertsChangedEvent, load);
    };
  }, [user, serverKey, servers]);

  const closeMenus = () => {
    setMenuOpen(false);
    setUserMenuOpen(false);
    setForumsOpen(false);
  };

  return (
    <div className="app-shell">
      <header className="app-header">
        <div className="header-bar">
          <Link to="/" className="brand" onClick={closeMenus}>
            <span className="brand-mark">S</span>
            Snekl
          </Link>
          <HeaderSearch onNavigate={closeMenus} />
          <div className="header-actions">
          <button
            type="button"
            className="nav-toggle"
            aria-expanded={menuOpen}
            aria-controls="primary-nav"
            onClick={() => setMenuOpen((open) => !open)}
          >
            Menu
          </button>
          {user && (
            <button
              type="button"
              className="nav-toggle forums-toggle"
              aria-expanded={forumsOpen}
              aria-controls="forum-browser"
              onClick={() => setForumsOpen((open) => !open)}
            >
              Nests
            </button>
          )}
          </div>
        </div>
        <nav id="primary-nav" className={`nav-links${menuOpen ? ' nav-links-open' : ''}`}>
          <NavLink to="/" end className={navClassName} onClick={closeMenus}>
            Home
          </NavLink>
          {user && (
            <NavLink to="/alerts" className={navClassName} onClick={closeMenus}>
              Alerts
              {unreadAlerts > 0 && <span className="nav-badge">{unreadAlerts}</span>}
            </NavLink>
          )}
          <NavLink to="/nests" end className={navClassName} onClick={closeMenus}>
            Browse nests
          </NavLink>
          <NavLink to="/lists" className={navClassName} onClick={closeMenus}>
            Lists
          </NavLink>
          <NavLink to="/commissions" className={navClassName} onClick={closeMenus}>
            Commissions
          </NavLink>
          <NavLink to="/temp" className={navClassName} onClick={closeMenus}>
            Temp
          </NavLink>
          {user ? (
            <div className="user-menu" ref={userMenuRef}>
              <button
                type="button"
                className="user-menu-trigger"
                aria-expanded={userMenuOpen}
                aria-haspopup="menu"
                onClick={() => setUserMenuOpen((open) => !open)}
              >
                {user.effectiveDisplayName}
              </button>
              {userMenuOpen && (
                <div className="user-menu-panel" role="menu">
                  <Link to="/account" role="menuitem" onClick={closeMenus}>
                    Account
                  </Link>
                  <Link to={`/users/${user.username}`} role="menuitem" onClick={closeMenus}>
                    Profile
                  </Link>
                  <Link to="/commissions" role="menuitem" onClick={closeMenus}>
                    Commissions
                  </Link>
                  <Link to="/following" role="menuitem" onClick={closeMenus}>
                    Following
                  </Link>
                  <Link to="/messages" role="menuitem" onClick={closeMenus}>
                    Messages
                  </Link>
                  <Link to="/alerts" role="menuitem" onClick={closeMenus}>
                    Alerts
                  </Link>
                  <Link to="/servers" role="menuitem" onClick={closeMenus}>
                    Servers
                  </Link>
                  <Link to="/admin" role="menuitem" onClick={closeMenus}>
                    Admins
                  </Link>
                  <a href={adminUrl(getAuthBaseUrl())} role="menuitem" onClick={closeMenus}>
                    Auth admin
                  </a>
                  {federations[0] && (
                    <a href={adminUrl(federations[0].baseUrl)} role="menuitem" onClick={closeMenus}>
                      Hub admin
                    </a>
                  )}
                  {contextServerDid && resolveServerBaseUrl(contextServerDid, resolveBaseUrl) && (
                    <a
                      href={adminUrl(resolveServerBaseUrl(contextServerDid, resolveBaseUrl)!)}
                      role="menuitem"
                      onClick={closeMenus}
                    >
                      Content admin
                    </a>
                  )}
                  <button
                    type="button"
                    className="user-menu-logout"
                    role="menuitem"
                    onClick={() => {
                      closeMenus();
                      logout();
                      navigate('/');
                    }}
                  >
                    Log out
                  </button>
                </div>
              )}
            </div>
          ) : (
            <>
              <NavLink to="/login" className={navClassName} onClick={closeMenus}>
                Log in
              </NavLink>
              <Link to="/register" className="btn-primary" onClick={closeMenus}>
                Sign up
              </Link>
            </>
          )}
        </nav>
      </header>
      <div className={`app-body${user ? ' app-body-with-nav' : ''}`}>
        {user && (
          <>
            {forumsOpen && (
              <button
                type="button"
                className="forum-browser-backdrop"
                aria-label="Close nest browser"
                onClick={() => setForumsOpen(false)}
              />
            )}
            <aside
              id="forum-browser"
              className={`forum-browser${forumsOpen ? ' forum-browser-open' : ''}`}
            >
              <ForumBrowser onNavigate={() => setForumsOpen(false)} />
            </aside>
          </>
        )}
        <main className="app-main">{children}</main>
      </div>
      <footer className="app-footer">
        Open source federated nest network — each node runs independently.{' '}
        <Link to="/admin">Admins</Link>
        {' · '}
        <Link to="/temp">Temp</Link>
      </footer>
    </div>
  );
}

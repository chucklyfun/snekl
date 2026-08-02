import { Link } from 'react-router';
import { hasPermission } from './api';
import { useAuth } from './AuthContext';
import { useServerRegistry } from './ServerRegistryContext';
import { createForumPath, serverSettingsPath } from './serverDid';
import './Layout.css';

export function Layout({ children }: { children: React.ReactNode }) {
  const { user, logout, isSuperUser, serverPermissions } = useAuth();
  const { activeServers } = useServerRegistry();

  const primaryServer = activeServers[0];
  const canCreateForum = isSuperUser || hasPermission(serverPermissions, 'CreateForum');
  const canManageServer =
    isSuperUser ||
    hasPermission(serverPermissions, 'ManageServerRoles') ||
    hasPermission(serverPermissions, 'ManageServerMembers');

  return (
    <div className="app-shell">
      <header className="app-header">
        <Link to="/" className="brand">
          <span className="brand-mark">S</span>
          Snekl
        </Link>
        <nav className="nav-links">
          <Link to="/">Forums</Link>
          <Link to="/servers">Servers</Link>
          <Link to="/lists">Lists</Link>
          <Link to="/federation">Federation</Link>
          {user && primaryServer && canCreateForum && (
            <Link to={createForumPath(primaryServer.serverDid)}>New forum</Link>
          )}
          {user && primaryServer && canManageServer && (
            <Link to={serverSettingsPath(primaryServer.serverDid)}>Server settings</Link>
          )}
          {user ? (
            <>
              <Link to={`/users/${user.username}`} className="user-badge">{user.effectiveDisplayName}</Link>
              <button type="button" className="btn-ghost" onClick={logout}>
                Log out
              </button>
            </>
          ) : (
            <>
              <Link to="/login">Log in</Link>
              <Link to="/register" className="btn-primary-link">Sign up</Link>
            </>
          )}
        </nav>
      </header>
      <main className="app-main">{children}</main>
      <footer className="app-footer">
        Open source federated forum network — each node runs independently.{' '}
        <Link to="/admin/login">Site admin</Link>
      </footer>
    </div>
  );
}

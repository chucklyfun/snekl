import { Link } from 'react-router';
import { useAuth } from './AuthContext';
import './Layout.css';

export function Layout({ children }: { children: React.ReactNode }) {
  const { user, logout } = useAuth();

  return (
    <div className="app-shell">
      <header className="app-header">
        <Link to="/" className="brand">
          <span className="brand-mark">S</span>
          Snekl
        </Link>
        <nav className="nav-links">
          <Link to="/">Forums</Link>
          <Link to="/lists">Lists</Link>
          <Link to="/federation">Federation</Link>
          {user && <Link to="/forums/new">New forum</Link>}
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
        Open source federated forum network — each node runs independently.
      </footer>
    </div>
  );
}

import { Link, Outlet } from 'react-router';
import { useAdminAuth } from './AdminAuthContext';
import './Layout.css';

export function AdminLayout() {
  const { admin, logout } = useAdminAuth();

  return (
    <div className="app-shell admin-shell">
      <header className="app-header admin-header">
        <Link to="/admin" className="brand">
          <span className="brand-mark admin-mark">A</span>
          Site Admin
        </Link>
        <nav className="nav-links">
          <Link to="/admin">Dashboard</Link>
          <Link to="/">View site</Link>
          {admin && (
            <>
              <span className="user-badge">{admin.username}</span>
              <button type="button" className="btn-ghost" onClick={logout}>
                Log out
              </button>
            </>
          )}
        </nav>
      </header>
      <main className="app-main">
        <Outlet />
      </main>
      <footer className="app-footer">
        Site administration — separate from user accounts. Admins configure the node; users post and browse.
      </footer>
    </div>
  );
}

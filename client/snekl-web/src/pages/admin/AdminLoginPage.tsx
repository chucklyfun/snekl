import { type FormEvent, useEffect, useState } from 'react';
import { Link, useNavigate } from 'react-router';
import { useAdminAuth } from '../../AdminAuthContext';

export function AdminLoginPage() {
  const { login, admin } = useAdminAuth();
  const navigate = useNavigate();
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');

  useEffect(() => {
    if (admin) navigate('/admin', { replace: true });
  }, [admin, navigate]);

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    try {
      await login(username, password);
      navigate('/admin');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Admin login failed');
    }
  };

  return (
    <div className="app-shell admin-shell">
      <main className="app-main">
        <div className="card" style={{ maxWidth: 420, margin: '2rem auto' }}>
          <h1 className="page-title">Site admin login</h1>
          <p style={{ color: 'var(--muted)', marginBottom: '1rem' }}>
            Operator access for node configuration and federation. This is separate from user login — admins do not
            post as forum members.
          </p>
          {error && <div className="error-banner">{error}</div>}
          <form onSubmit={handleSubmit}>
            <div className="form-group">
              <label htmlFor="admin-username">Admin username</label>
              <input
                id="admin-username"
                value={username}
                onChange={(e) => setUsername(e.target.value)}
                autoComplete="username"
                required
              />
            </div>
            <div className="form-group">
              <label htmlFor="admin-password">Password</label>
              <input
                id="admin-password"
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                autoComplete="current-password"
                required
              />
            </div>
            <button type="submit" className="btn-primary">
              Log in as admin
            </button>
          </form>
          <p style={{ marginTop: '1rem', color: 'var(--muted)' }}>
            <Link to="/">← Back to site</Link> · <Link to="/login">User login</Link>
          </p>
        </div>
      </main>
    </div>
  );
}

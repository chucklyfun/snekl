import { type FormEvent, useState } from 'react';
import { Link, useNavigate } from 'react-router';
import { useAuth } from '../AuthContext';
import { authApi } from '../api';

const devPasswordResetEnabled =
  import.meta.env.VITE_ALLOW_DEV_PASSWORD_RESET === 'true' || import.meta.env.DEV;

export function LoginPage() {
  const { login } = useAuth();
  const navigate = useNavigate();
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  const [showDevReset, setShowDevReset] = useState(false);
  const [newPassword, setNewPassword] = useState('');

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    setSuccess('');
    try {
      await login(username, password);
      navigate('/');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Login failed');
    }
  };

  const handleDevReset = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    setSuccess('');
    try {
      const result = await authApi.devResetPassword(username.trim(), newPassword);
      setSuccess(result.message);
      setPassword(newPassword);
      setNewPassword('');
      setShowDevReset(false);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Password reset failed');
    }
  };

  return (
    <div className="card" style={{ maxWidth: 420, margin: '0 auto' }}>
      <h1 className="page-title">Log in</h1>
      <p style={{ color: 'var(--muted)', marginBottom: '1rem' }}>
        Sign in via the auth server. Your content node trusts the returned token.
      </p>
      {error && <div className="error-banner">{error}</div>}
      {success && <div className="card" style={{ borderColor: 'var(--accent)', marginBottom: '1rem' }}>{success}</div>}
      <form onSubmit={handleSubmit}>
        <div className="form-group">
          <label htmlFor="username">Username</label>
          <input id="username" value={username} onChange={(e) => setUsername(e.target.value)} required />
        </div>
        <div className="form-group">
          <label htmlFor="password">Password</label>
          <input id="password" type="password" value={password} onChange={(e) => setPassword(e.target.value)} required />
        </div>
        <button type="submit" className="btn-primary">Log in</button>
      </form>

      {devPasswordResetEnabled && (
        <div style={{ marginTop: '1.5rem', paddingTop: '1rem', borderTop: '1px solid var(--border)' }}>
          {!showDevReset ? (
            <button
              type="button"
              className="btn-ghost"
              onClick={() => {
                setShowDevReset(true);
                setError('');
                setSuccess('');
              }}
            >
              Dev: reset password
            </button>
          ) : (
            <form onSubmit={handleDevReset}>
              <p style={{ color: 'var(--muted)', marginBottom: '0.75rem', fontSize: '0.9rem' }}>
                Test-only reset. No email required. Uses the username above.
              </p>
              <div className="form-group">
                <label htmlFor="newPassword">New password</label>
                <input
                  id="newPassword"
                  type="password"
                  value={newPassword}
                  onChange={(e) => setNewPassword(e.target.value)}
                  required
                  minLength={6}
                />
              </div>
              <div style={{ display: 'flex', gap: '0.5rem' }}>
                <button type="submit" className="btn-primary">Set new password</button>
                <button type="button" className="btn-ghost" onClick={() => setShowDevReset(false)}>
                  Cancel
                </button>
              </div>
            </form>
          )}
        </div>
      )}

      <p style={{ marginTop: '1rem', color: 'var(--muted)' }}>
        No account? <Link to="/register">Sign up</Link>
      </p>
    </div>
  );
}

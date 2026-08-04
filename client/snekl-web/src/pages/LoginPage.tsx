import { type FormEvent, useState } from 'react';
import { Link, useLocation, useNavigate } from 'react-router';
import { useAuth } from '../AuthContext';
import { authApi } from '../api';

const devPasswordResetEnabled =
  import.meta.env.VITE_ALLOW_DEV_PASSWORD_RESET === 'true' || import.meta.env.DEV;

export function LoginPage() {
  const { login } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const successMessage = (location.state as { message?: string } | null)?.message;
  const [email, setEmail] = useState('');
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
      await login(email, password);
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
      const result = await authApi.devResetPassword(email.trim(), newPassword);
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
        Sign in with the email and password from signup (auth server at {import.meta.env.VITE_AUTH_URL ?? 'http://localhost:5100'}).
      </p>
      {successMessage && (
        <div style={{ marginBottom: '1rem', color: 'var(--accent)' }}>{successMessage}</div>
      )}
      {error && <div className="error-banner">{error}</div>}
      {success && <div className="card" style={{ borderColor: 'var(--accent)', marginBottom: '1rem' }}>{success}</div>}
      <form onSubmit={handleSubmit}>
        <div className="form-group">
          <label htmlFor="email">Email</label>
          <input id="email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} required autoComplete="email" />
        </div>
        <div className="form-group">
          <label htmlFor="password">Password</label>
          <input id="password" type="password" value={password} onChange={(e) => setPassword(e.target.value)} required />
          <p style={{ marginTop: '0.35rem', fontSize: '0.875rem' }}>
            <Link to="/forgot-password">Forgot password?</Link>
          </p>
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
                Test-only reset. No email verification required. Uses the email above — must match signup exactly.
                If login says no account exists, sign up again at /register.
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
        {' · '}
        <Link to="/admin/login">Site admin login</Link>
      </p>
    </div>
  );
}

import { Capacitor } from '@capacitor/core';
import { type FormEvent, useState } from 'react';
import { Link, useLocation, useNavigate } from 'react-router';
import { useAuth } from '../AuthContext';
import { ApiRequestError, TwoFactorRequiredError, authApi } from '../api';
import { AuthServerField } from '../components/AuthServerField';
import { setAuthBaseUrl } from '../hosts';

const devPasswordResetEnabled =
  !Capacitor.isNativePlatform() &&
  (import.meta.env.VITE_ALLOW_DEV_PASSWORD_RESET === 'true' || import.meta.env.DEV);

export function LoginPage() {
  const { login, completeTwoFactorLogin } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const successMessage = (location.state as { message?: string } | null)?.message;
  const returnTo = new URLSearchParams(location.search).get('return');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [challengeToken, setChallengeToken] = useState<string | null>(null);
  const [twoFactorCode, setTwoFactorCode] = useState('');
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  const [showDevReset, setShowDevReset] = useState(false);
  const [newPassword, setNewPassword] = useState('');
  const [authUrl, setAuthUrl] = useState('');

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    setSuccess('');
    if (!authUrl) return;
    try {
      setAuthBaseUrl(authUrl);
      await login(email, password);
      const next = returnTo && returnTo.startsWith('/') && !returnTo.startsWith('//') ? returnTo : '/';
      navigate(next);
    } catch (err) {
      if (err instanceof TwoFactorRequiredError) {
        setChallengeToken(err.challengeToken);
        setTwoFactorCode('');
        return;
      }
      setError(err instanceof Error ? err.message : 'Login failed');
    }
  };

  const handleTwoFactor = async (e: FormEvent) => {
    e.preventDefault();
    if (!challengeToken) return;
    setError('');
    setSuccess('');
    try {
      await completeTwoFactorLogin(challengeToken, twoFactorCode);
      const next = returnTo && returnTo.startsWith('/') && !returnTo.startsWith('//') ? returnTo : '/';
      navigate(next);
    } catch (err) {
      if (err instanceof ApiRequestError && err.code === 'two_factor_expired') {
        setChallengeToken(null);
        setTwoFactorCode('');
      }
      setError(err instanceof Error ? err.message : 'Login failed');
    }
  };

  const handleDevReset = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    setSuccess('');
    if (!authUrl) return;
    try {
      setAuthBaseUrl(authUrl);
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
        Sign in with your Account. Federations and content servers load from that account after login.
      </p>
      {successMessage && (
        <div style={{ marginBottom: '1rem', color: 'var(--accent)' }}>{successMessage}</div>
      )}
      {error && <div className="error-banner">{error}</div>}
      {success && <div className="card" style={{ borderColor: 'var(--accent)', marginBottom: '1rem' }}>{success}</div>}
      {challengeToken ? (
        <form onSubmit={handleTwoFactor}>
          <p style={{ color: 'var(--muted)', marginBottom: '1rem' }}>
            Enter the 6-digit code from your authenticator app, or a backup code.
          </p>
          <div className="form-group">
            <label htmlFor="two-factor-code">Authentication code</label>
            <input
              id="two-factor-code"
              value={twoFactorCode}
              onChange={(e) => setTwoFactorCode(e.target.value)}
              autoComplete="one-time-code"
              inputMode="text"
              autoFocus
              required
            />
          </div>
          <div style={{ display: 'flex', gap: '0.5rem' }}>
            <button type="submit" className="btn-primary">Verify</button>
            <button
              type="button"
              className="btn-ghost"
              onClick={() => {
                setChallengeToken(null);
                setTwoFactorCode('');
                setError('');
              }}
            >
              Back
            </button>
          </div>
        </form>
      ) : (
      <form onSubmit={handleSubmit}>
        <AuthServerField id="auth-url" value={authUrl} onChange={setAuthUrl} />
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
        <button type="submit" className="btn-primary" disabled={!authUrl}>Log in</button>
      </form>
      )}

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
        <Link to="/admin">Admins</Link>
      </p>
    </div>
  );
}

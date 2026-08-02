import { type FormEvent, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router';
import { authApi } from '../api';

export function ResetPasswordPage() {
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const token = searchParams.get('token') ?? '';

  const [password, setPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [error, setError] = useState('');
  const [submitting, setSubmitting] = useState(false);

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError('');

    if (!token) {
      setError('Missing reset token. Use the link from your email.');
      return;
    }

    if (password !== confirmPassword) {
      setError('Passwords do not match.');
      return;
    }

    setSubmitting(true);
    try {
      await authApi.resetPassword(token, password);
      navigate('/login', { state: { message: 'Password updated. You can log in now.' } });
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Reset failed');
    } finally {
      setSubmitting(false);
    }
  };

  if (!token) {
    return (
      <div className="card" style={{ maxWidth: 420, margin: '0 auto' }}>
        <h1 className="page-title">Reset password</h1>
        <div className="error-banner">Missing reset token. Request a new link from the forgot password page.</div>
        <p style={{ marginTop: '1rem', color: 'var(--muted)' }}>
          <Link to="/forgot-password">Request reset link</Link>
          {' · '}
          <Link to="/login">Log in</Link>
        </p>
      </div>
    );
  }

  return (
    <div className="card" style={{ maxWidth: 420, margin: '0 auto' }}>
      <h1 className="page-title">Choose a new password</h1>
      {error && <div className="error-banner">{error}</div>}
      <form onSubmit={handleSubmit}>
        <div className="form-group">
          <label htmlFor="password">New password</label>
          <input
            id="password"
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
            minLength={6}
            autoComplete="new-password"
          />
        </div>
        <div className="form-group">
          <label htmlFor="confirmPassword">Confirm password</label>
          <input
            id="confirmPassword"
            type="password"
            value={confirmPassword}
            onChange={(e) => setConfirmPassword(e.target.value)}
            required
            minLength={6}
            autoComplete="new-password"
          />
        </div>
        <button type="submit" className="btn-primary" disabled={submitting}>
          {submitting ? 'Updating…' : 'Update password'}
        </button>
      </form>
      <p style={{ marginTop: '1rem', color: 'var(--muted)' }}>
        <Link to="/login">Back to log in</Link>
      </p>
    </div>
  );
}

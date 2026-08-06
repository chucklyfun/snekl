import { type FormEvent, useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { authApi } from '../api';

export function VerifyEmailPage() {
  const [searchParams] = useSearchParams();
  const token = searchParams.get('token') ?? '';
  const [message, setMessage] = useState('');
  const [error, setError] = useState('');
  const [submitting, setSubmitting] = useState(false);

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    if (!token) return;
    setError('');
    setMessage('');
    setSubmitting(true);
    try {
      const result = await authApi.verifyEmail(token);
      setMessage(result.message);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Verification failed');
    } finally {
      setSubmitting(false);
    }
  };

  if (!token) {
    return (
      <div className="card" style={{ maxWidth: 420, margin: '0 auto' }}>
        <h1 className="page-title">Verify email</h1>
        <div className="error-banner">Missing verification token. Open the link from your email.</div>
        <p style={{ marginTop: '1rem', color: 'var(--muted)' }}>
          <Link to="/account">Account settings</Link>
          {' · '}
          <Link to="/login">Log in</Link>
        </p>
      </div>
    );
  }

  return (
    <div className="card" style={{ maxWidth: 420, margin: '0 auto' }}>
      <h1 className="page-title">Verify email</h1>
      {error && <div className="error-banner">{error}</div>}
      {message ? (
        <>
          <div style={{ marginBottom: '1rem', color: 'var(--accent)' }}>{message}</div>
          <p style={{ color: 'var(--muted)' }}>
            <Link to="/login">Log in</Link>
          </p>
        </>
      ) : (
        <>
          <p style={{ color: 'var(--muted)', marginBottom: '1rem' }}>
            Confirm your email address to secure your account and enable password recovery.
          </p>
          <form onSubmit={handleSubmit}>
            <button type="submit" className="btn-primary" disabled={submitting}>
              {submitting ? 'Verifying…' : 'Verify email'}
            </button>
          </form>
        </>
      )}
    </div>
  );
}

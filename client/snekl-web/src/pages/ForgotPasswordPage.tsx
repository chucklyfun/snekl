import { type FormEvent, useState } from 'react';
import { Link } from 'react-router';
import { authApi } from '../api';

export function ForgotPasswordPage() {
  const [email, setEmail] = useState('');
  const [message, setMessage] = useState('');
  const [error, setError] = useState('');
  const [submitting, setSubmitting] = useState(false);

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    setMessage('');
    setSubmitting(true);
    try {
      const result = await authApi.forgotPassword(email);
      setMessage(result.message);
      setEmail('');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Request failed');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="card" style={{ maxWidth: 420, margin: '0 auto' }}>
      <h1 className="page-title">Forgot password</h1>
      <p style={{ color: 'var(--muted)', marginBottom: '1rem' }}>
        Enter the email on your account. If it exists, we will send a reset link.
      </p>
      {error && <div className="error-banner">{error}</div>}
      {message && (
        <div style={{ marginBottom: '1rem', color: 'var(--accent)' }}>{message}</div>
      )}
      <form onSubmit={handleSubmit}>
        <div className="form-group">
          <label htmlFor="email">Email</label>
          <input
            id="email"
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            required
            autoComplete="email"
          />
        </div>
        <button type="submit" className="btn-primary" disabled={submitting}>
          {submitting ? 'Sending…' : 'Send reset link'}
        </button>
      </form>
      <p style={{ marginTop: '1rem', color: 'var(--muted)' }}>
        <Link to="/login">Back to log in</Link>
      </p>
    </div>
  );
}

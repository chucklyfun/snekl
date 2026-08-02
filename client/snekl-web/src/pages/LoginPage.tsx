import { type FormEvent, useState } from 'react';
import { Link, useLocation, useNavigate } from 'react-router';
import { useAuth } from '../AuthContext';

export function LoginPage() {
  const { login } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const successMessage = (location.state as { message?: string } | null)?.message;
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    try {
      await login(email, password);
      navigate('/');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Login failed');
    }
  };

  return (
    <div className="card" style={{ maxWidth: 420, margin: '0 auto' }}>
      <h1 className="page-title">Log in</h1>
      <p style={{ color: 'var(--muted)', marginBottom: '1rem' }}>
        Sign in via the auth server. Your content node trusts the returned token.
      </p>
      {successMessage && (
        <div style={{ marginBottom: '1rem', color: 'var(--accent)' }}>{successMessage}</div>
      )}
      {error && <div className="error-banner">{error}</div>}
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
      <p style={{ marginTop: '1rem', color: 'var(--muted)' }}>
        No account? <Link to="/register">Sign up</Link>
        {' · '}
        <Link to="/admin/login">Site admin login</Link>
      </p>
    </div>
  );
}

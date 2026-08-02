import { type FormEvent, useEffect, useState } from 'react';
import { Link, useNavigate } from 'react-router';
import { authApi } from '../api';
import { useAuth } from '../AuthContext';
import type { PersonCluster } from '../types';

export function AccountSettingsPage() {
  const { user, refreshMe } = useAuth();
  const navigate = useNavigate();
  const [account, setAccount] = useState<PersonCluster | null>(null);
  const [username, setUsername] = useState('');
  const [email, setEmail] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [bio, setBio] = useState('');
  const [disambiguationTag, setDisambiguationTag] = useState('');
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [resending, setResending] = useState(false);

  useEffect(() => {
    if (!user) return;
    authApi
      .me()
      .then((person) => {
        setAccount(person);
        setUsername(person.canonicalUsername);
        setEmail(person.email);
        setDisplayName(person.displayName);
        setBio(person.bio);
        setDisambiguationTag(person.disambiguationTag ?? '');
      })
      .catch((err) => setError(err instanceof Error ? err.message : 'Failed to load account'));
  }, [user]);

  const handleResendVerification = async () => {
    setError('');
    setMessage('');
    setResending(true);
    try {
      const result = await authApi.resendVerificationEmail();
      setMessage(result.message);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to send verification email');
    } finally {
      setResending(false);
    }
  };

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    setMessage('');
    setSubmitting(true);
    try {
      const result = await authApi.updateProfile({
        username,
        email,
        displayName,
        bio,
        disambiguationTag: disambiguationTag.trim() || null,
      });
      localStorage.setItem('snekl_token', result.accessToken);
      setAccount(result.person);
      setUsername(result.person.canonicalUsername);
      setEmail(result.person.email);
      setDisplayName(result.person.displayName);
      setBio(result.person.bio);
      setDisambiguationTag(result.person.disambiguationTag ?? '');
      await refreshMe();
      setMessage('Account updated.');
      if (result.person.canonicalUsername !== user?.username) {
        navigate(`/users/${result.person.canonicalUsername}`, { replace: true });
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Update failed');
    } finally {
      setSubmitting(false);
    }
  };

  if (!user) {
    return (
      <div className="card" style={{ maxWidth: 520, margin: '0 auto' }}>
        <p>
          <Link to="/login">Log in</Link> to manage your account.
        </p>
      </div>
    );
  }

  if (!account) {
    return <p>Loading account…</p>;
  }

  return (
    <div className="card" style={{ maxWidth: 520, margin: '0 auto' }}>
      <h1 className="page-title">Account settings</h1>
      <p style={{ color: 'var(--muted)', marginBottom: '1rem' }}>
        Update your login email, public username, and profile details on the auth server.
      </p>
      {error && <div className="error-banner">{error}</div>}
      {message && <div style={{ marginBottom: '1rem', color: 'var(--accent)' }}>{message}</div>}
      {!account.emailVerified && (
        <div className="card profile-section" style={{ marginBottom: '1rem', padding: '1rem' }}>
          <strong>Email not verified</strong>
          <p style={{ color: 'var(--muted)', margin: '0.5rem 0 0.75rem' }}>
            Verify your email to claim it exclusively and enable password recovery.
            In development, check auth server logs for the verification link.
          </p>
          <button type="button" className="btn-primary" onClick={handleResendVerification} disabled={resending}>
            {resending ? 'Sending…' : 'Resend verification email'}
          </button>
        </div>
      )}
      <form onSubmit={handleSubmit}>
        <div className="form-group">
          <label htmlFor="email">Email (used to log in)</label>
          <input
            id="email"
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            required
            autoComplete="email"
          />
        </div>
        <div className="form-group">
          <label htmlFor="username">Username</label>
          <input
            id="username"
            value={username}
            onChange={(e) => setUsername(e.target.value)}
            required
            autoComplete="username"
          />
        </div>
        <div className="form-group">
          <label htmlFor="displayName">Display name</label>
          <input
            id="displayName"
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
            required
          />
        </div>
        <div className="form-group">
          <label htmlFor="bio">Bio</label>
          <textarea id="bio" value={bio} onChange={(e) => setBio(e.target.value)} rows={3} />
        </div>
        <div className="form-group">
          <label htmlFor="disambiguationTag">Disambiguation tag</label>
          <input
            id="disambiguationTag"
            value={disambiguationTag}
            onChange={(e) => setDisambiguationTag(e.target.value)}
            placeholder="Optional · shown when your username matches someone else's"
          />
        </div>
        <button type="submit" className="btn-primary" disabled={submitting}>
          {submitting ? 'Saving…' : 'Save changes'}
        </button>
      </form>
    </div>
  );
}

import { type FormEvent, useState } from 'react';
import { ApiRequestError, TwoFactorRequiredError } from '../api';
import type { AdminKind } from './api';
import { accountLogin, completeAccountTwoFactor, operatorLogin, operatorStatus, setAdminToken, setMemberToken } from './api';

export function OperatorGate({
  kind,
  authLoginUrl,
  hostLabel,
  onReady,
}: {
  kind: AdminKind;
  authLoginUrl: string;
  hostLabel: string;
  onReady: () => void;
}) {
  const [email, setEmail] = useState('');
  const [accountPass, setAccountPass] = useState('');
  const [adminPass, setAdminPass] = useState('');
  const [challengeToken, setChallengeToken] = useState<string | null>(null);
  const [twoFactorCode, setTwoFactorCode] = useState('');
  const [state, setState] = useState<string | null>(null);
  const [error, setError] = useState('');

  const continueWithSession = async (accessToken: string) => {
    setMemberToken(accessToken);
    const status = await operatorStatus(kind);
    if (status.state === 'forbidden') throw new Error(`You are not an operator on this ${kind} host.`);
    setState(status.state);
  };

  const continueAccount = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    try {
      const session = await accountLogin(authLoginUrl, email, accountPass);
      await continueWithSession(session.accessToken);
    } catch (err) {
      if (err instanceof TwoFactorRequiredError) {
        setChallengeToken(err.challengeToken);
        setTwoFactorCode('');
        return;
      }
      setError(err instanceof Error ? err.message : 'Login failed');
    }
  };

  const submitTwoFactor = async (e: FormEvent) => {
    e.preventDefault();
    if (!challengeToken) return;
    setError('');
    try {
      const session = await completeAccountTwoFactor(authLoginUrl, challengeToken, twoFactorCode);
      setChallengeToken(null);
      await continueWithSession(session.accessToken);
    } catch (err) {
      if (err instanceof ApiRequestError && err.code === 'two_factor_expired') {
        setChallengeToken(null);
        setTwoFactorCode('');
      }
      setError(err instanceof Error ? err.message : 'Login failed');
    }
  };

  const elevate = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    try {
      const result = await operatorLogin(kind, state || 'ready', adminPass);
      setAdminToken(result.token);
      onReady();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Admin login failed');
    }
  };

  const copy =
    state === 'unclaimed'
      ? `This ${kind} host has no operators yet. Choose an admin password that stays on this machine.`
      : state === 'setupRequired'
        ? 'You were invited. Set an admin password for this host only.'
        : `Enter your admin password for this ${kind} host. Account login proves who you are; this password is only for administering this machine.`;

  return (
    <div className="card" style={{ maxWidth: 480, margin: '0 auto' }}>
      <h1 className="page-title">Operator login</h1>
      <p className="page-subtitle">{hostLabel}</p>
      {error && <div className="error-banner">{error}</div>}
      {!state && challengeToken ? (
        <form onSubmit={submitTwoFactor}>
          <p className="card-meta" style={{ marginBottom: '1rem' }}>
            Enter the 6-digit code from your authenticator app, or a backup code.
          </p>
          <div className="form-group">
            <label htmlFor="two-factor-code">Authentication code</label>
            <input
              id="two-factor-code"
              value={twoFactorCode}
              onChange={(e) => setTwoFactorCode(e.target.value)}
              autoComplete="one-time-code"
              required
            />
          </div>
          <button type="submit" className="btn-primary">
            Verify
          </button>
        </form>
      ) : !state ? (
        <form onSubmit={continueAccount}>
          <p className="card-meta" style={{ marginBottom: '1rem' }}>
            Sign in with a regular Account, then the operator password for <em>this</em> server.
          </p>
          <div className="form-group">
            <label htmlFor="email">Email</label>
            <input id="email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} required />
          </div>
          <div className="form-group">
            <label htmlFor="account-pass">Account password</label>
            <input
              id="account-pass"
              type="password"
              value={accountPass}
              onChange={(e) => setAccountPass(e.target.value)}
              required
            />
          </div>
          <button type="submit" className="btn-primary">
            Continue
          </button>
        </form>
      ) : (
        <form onSubmit={elevate}>
          <p className="card-meta" style={{ marginBottom: '1rem' }}>
            {copy}
          </p>
          <div className="form-group">
            <label htmlFor="admin-pass">Admin password</label>
            <input
              id="admin-pass"
              type="password"
              value={adminPass}
              onChange={(e) => setAdminPass(e.target.value)}
              required
              minLength={8}
            />
          </div>
          <button type="submit" className="btn-primary">
            {state === 'unclaimed' ? `Claim this ${kind} server` : state === 'setupRequired' ? 'Set admin password' : 'Log in'}
          </button>
        </form>
      )}
    </div>
  );
}

import { type FormEvent, useState } from 'react';
import type { AdminKind } from './api';
import { accountLogin, operatorLogin, operatorStatus, setAdminToken, setMemberToken } from './api';

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
  const [state, setState] = useState<string | null>(null);
  const [error, setError] = useState('');

  const continueAccount = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    try {
      const session = await accountLogin(authLoginUrl, email, accountPass);
      setMemberToken(session.accessToken);
      const status = await operatorStatus(kind);
      if (status.state === 'forbidden') throw new Error(`You are not an operator on this ${kind} host.`);
      setState(status.state);
    } catch (err) {
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
      {!state ? (
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

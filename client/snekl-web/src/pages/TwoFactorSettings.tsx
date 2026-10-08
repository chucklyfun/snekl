import { type FormEvent, useEffect, useState } from 'react';
import { authApi } from '../api';

function groupSecret(secret: string) {
  return secret.replace(/(.{4})/g, '$1 ').trim();
}

export function TwoFactorSettings() {
  const [enabled, setEnabled] = useState(false);
  const [remaining, setRemaining] = useState(0);
  const [loading, setLoading] = useState(true);
  const [setup, setSetup] = useState<{ secret: string; otpAuthUri: string } | null>(null);
  const [confirmCode, setConfirmCode] = useState('');
  const [disablePassword, setDisablePassword] = useState('');
  const [disableCode, setDisableCode] = useState('');
  const [regenPassword, setRegenPassword] = useState('');
  const [regenCode, setRegenCode] = useState('');
  const [recoveryCodes, setRecoveryCodes] = useState<string[] | null>(null);
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');
  const [busy, setBusy] = useState(false);

  const load = () => {
    setLoading(true);
    authApi
      .twoFactorStatus()
      .then((status) => {
        setEnabled(status.enabled);
        setRemaining(status.recoveryCodesRemaining);
      })
      .catch((err) => setError(err instanceof Error ? err.message : 'Could not load two-factor status'))
      .finally(() => setLoading(false));
  };

  useEffect(() => {
    load();
  }, []);

  const startSetup = async () => {
    setError('');
    setMessage('');
    setRecoveryCodes(null);
    setBusy(true);
    try {
      const next = await authApi.twoFactorSetup();
      setSetup(next);
      setConfirmCode('');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not start setup');
    } finally {
      setBusy(false);
    }
  };

  const confirm = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    setMessage('');
    setBusy(true);
    try {
      const result = await authApi.twoFactorConfirm(confirmCode);
      setRecoveryCodes(result.recoveryCodes);
      setSetup(null);
      setConfirmCode('');
      setEnabled(true);
      setRemaining(result.recoveryCodes.length);
      setMessage('Two-factor authentication is on. Save the backup codes below. They are shown only once.');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not confirm the code');
    } finally {
      setBusy(false);
    }
  };

  const disable = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    setMessage('');
    setBusy(true);
    try {
      const result = await authApi.twoFactorDisable(disablePassword, disableCode);
      setEnabled(false);
      setRemaining(0);
      setDisablePassword('');
      setDisableCode('');
      setRecoveryCodes(null);
      setMessage(result.message);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not turn off two-factor authentication');
    } finally {
      setBusy(false);
    }
  };

  const regenerate = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    setMessage('');
    setBusy(true);
    try {
      const result = await authApi.twoFactorRegenerateCodes(regenPassword, regenCode);
      setRecoveryCodes(result.recoveryCodes);
      setRemaining(result.recoveryCodes.length);
      setRegenPassword('');
      setRegenCode('');
      setMessage('New backup codes replace the old ones. Save them now.');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not create new backup codes');
    } finally {
      setBusy(false);
    }
  };

  const copyCodes = async () => {
    if (!recoveryCodes) return;
    await navigator.clipboard.writeText(recoveryCodes.join('\n'));
    setMessage('Backup codes copied.');
  };

  return (
    <section className="card profile-section" style={{ marginTop: '1.5rem', padding: '1rem' }}>
      <h2 style={{ marginTop: 0 }}>Two-factor authentication</h2>
      <p style={{ color: 'var(--muted)' }}>
        Optional authenticator-app codes at login. Password reset does not turn this off. If you lose the app and the
        backup codes, this account cannot be signed in.
      </p>
      {error && <div className="error-banner">{error}</div>}
      {message && <div style={{ marginBottom: '1rem', color: 'var(--accent)' }}>{message}</div>}
      {loading ? (
        <p>Loading…</p>
      ) : setup ? (
        <form onSubmit={confirm}>
          <p>Add this key to your authenticator app. The account name is your email, and the type is time-based (6 digits).</p>
          <p style={{ fontFamily: 'ui-monospace, monospace', letterSpacing: '0.04em' }}>{groupSecret(setup.secret)}</p>
          <p style={{ display: 'flex', gap: '0.5rem', flexWrap: 'wrap' }}>
            <button
              type="button"
              className="btn-ghost"
              onClick={() => void navigator.clipboard.writeText(setup.secret)}
            >
              Copy key
            </button>
            <a href={setup.otpAuthUri}>Open in authenticator app</a>
          </p>
          <div className="form-group">
            <label htmlFor="totp-confirm">Code from the app</label>
            <input
              id="totp-confirm"
              value={confirmCode}
              onChange={(e) => setConfirmCode(e.target.value)}
              inputMode="numeric"
              autoComplete="one-time-code"
              required
            />
          </div>
          <div style={{ display: 'flex', gap: '0.5rem' }}>
            <button type="submit" className="btn-primary" disabled={busy}>Confirm</button>
            <button type="button" className="btn-ghost" onClick={() => setSetup(null)} disabled={busy}>Cancel</button>
          </div>
        </form>
      ) : enabled ? (
        <div>
          <p>On. {remaining} backup {remaining === 1 ? 'code' : 'codes'} left.</p>
          {recoveryCodes && (
            <div style={{ marginBottom: '1rem' }}>
              <ul style={{ fontFamily: 'ui-monospace, monospace', paddingLeft: '1.2rem' }}>
                {recoveryCodes.map((recovery) => (
                  <li key={recovery}>{recovery}</li>
                ))}
              </ul>
              <button type="button" className="btn-ghost" onClick={() => void copyCodes()}>Copy codes</button>
            </div>
          )}
          <form onSubmit={disable} style={{ marginBottom: '1rem' }}>
            <h3>Turn off</h3>
            <div className="form-group">
              <label htmlFor="totp-disable-password">Password</label>
              <input
                id="totp-disable-password"
                type="password"
                value={disablePassword}
                onChange={(e) => setDisablePassword(e.target.value)}
                autoComplete="current-password"
                required
              />
            </div>
            <div className="form-group">
              <label htmlFor="totp-disable-code">Authenticator or backup code</label>
              <input
                id="totp-disable-code"
                value={disableCode}
                onChange={(e) => setDisableCode(e.target.value)}
                autoComplete="one-time-code"
                required
              />
            </div>
            <button type="submit" className="btn-ghost" disabled={busy}>Turn off</button>
          </form>
          <form onSubmit={regenerate}>
            <h3>New backup codes</h3>
            <p style={{ color: 'var(--muted)' }}>Requires the current authenticator code, not a backup code.</p>
            <div className="form-group">
              <label htmlFor="totp-regen-password">Password</label>
              <input
                id="totp-regen-password"
                type="password"
                value={regenPassword}
                onChange={(e) => setRegenPassword(e.target.value)}
                autoComplete="current-password"
                required
              />
            </div>
            <div className="form-group">
              <label htmlFor="totp-regen-code">Authenticator code</label>
              <input
                id="totp-regen-code"
                value={regenCode}
                onChange={(e) => setRegenCode(e.target.value)}
                inputMode="numeric"
                autoComplete="one-time-code"
                required
              />
            </div>
            <button type="submit" className="btn-ghost" disabled={busy}>Replace backup codes</button>
          </form>
        </div>
      ) : (
        <button type="button" className="btn-primary" onClick={() => void startSetup()} disabled={busy}>
          Set up authenticator app
        </button>
      )}
    </section>
  );
}

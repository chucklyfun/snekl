import { type FormEvent, useState } from 'react';
import { ApiRequestError } from '../api';
import { useAuth } from '../AuthContext';

export function TransferOwnershipForm({
  id,
  resourceName,
  confirmDetail,
  onTransfer,
}: {
  id: string;
  resourceName: string;
  confirmDetail: string;
  onTransfer: (username: string) => Promise<void>;
}) {
  const { reauthenticate } = useAuth();
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [twoFactorCode, setTwoFactorCode] = useState('');
  const [needsTwoFactor, setNeedsTwoFactor] = useState(false);
  const [busy, setBusy] = useState(false);
  const [localError, setLocalError] = useState('');

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    const handle = username.trim().replace(/^@/, '');
    if (!handle || !password || busy) return;
    if (!window.confirm(`Transfer “${resourceName}” to @${handle}? ${confirmDetail}`)) return;
    setBusy(true);
    setLocalError('');
    try {
      await reauthenticate(password, needsTwoFactor ? twoFactorCode : undefined);
      await onTransfer(handle);
      setUsername('');
      setPassword('');
    } catch (err) {
      if (err instanceof ApiRequestError && (err.code === 'two_factor_required' || err.code === 'two_factor_invalid')) {
        setNeedsTwoFactor(true);
      }
      setLocalError(err instanceof Error ? err.message : 'Transfer failed');
    } finally {
      setBusy(false);
    }
  };

  return (
    <form className="list-transfer" onSubmit={(e) => void submit(e)}>
      <div className="form-group">
        <label htmlFor={`transfer-${id}`}>Transfer ownership</label>
        <p className="card-meta">
          Confirm your Account password first. The new owner must already have signed in on this server; if they
          have not, ask them to log in so their account can be refreshed from the auth server.
        </p>
        <div className="inline-form" style={{ flexWrap: 'wrap', gap: '0.5rem' }}>
          <input
            id={`transfer-${id}`}
            value={username}
            onChange={(e) => setUsername(e.target.value)}
            placeholder="@username"
            autoComplete="off"
            required
          />
          <input
            id={`transfer-password-${id}`}
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            placeholder="Your password"
            autoComplete="current-password"
            required
          />
          {needsTwoFactor && (
            <input
              id={`transfer-totp-${id}`}
              value={twoFactorCode}
              onChange={(e) => setTwoFactorCode(e.target.value)}
              placeholder="Authenticator code"
              autoComplete="one-time-code"
              required
            />
          )}
          <button type="submit" className="btn-ghost" disabled={busy}>
            Transfer
          </button>
        </div>
        {localError && (
          <p className="card-meta" style={{ color: 'var(--danger, #c44)', marginTop: '0.5rem' }}>
            {localError}
          </p>
        )}
      </div>
    </form>
  );
}

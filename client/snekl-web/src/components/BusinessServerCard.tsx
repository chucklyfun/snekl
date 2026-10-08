import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router';
import {
  applyCreatedServer,
  type BusinessAccountSummary,
  createBusinessServerErrorMessage,
  pickBusinessCardState,
  shouldReloadAfterError,
} from '../businessHosting';
import { BusinessHostingRequestError, createBusinessServer, fetchMyBusinessAccounts } from '../businessHostingApi';
import { serverOwnerSettingsPath, shortServerDid } from '../serverDid';

/**
 * "Hosted server" card for business account owners. Hidden for members without a business account.
 * Shows pending / approved / rejected / has-a-server, and offers "Create hosted server" once approved.
 */
export function BusinessServerCard() {
  const [accounts, setAccounts] = useState<BusinessAccountSummary[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  const load = useCallback(() => {
    fetchMyBusinessAccounts()
      .then(setAccounts)
      // Older auth servers have no business endpoint; the card simply stays hidden.
      .catch(() => setAccounts([]));
  }, []);

  useEffect(load, [load]);

  const state = pickBusinessCardState(accounts);
  if (state.kind === 'none') return null;
  const { business } = state;

  const handleCreate = async () => {
    setBusy(true);
    setError('');
    try {
      const created = await createBusinessServer(business.id);
      setAccounts((current) => applyCreatedServer(current ?? [], business.id, created));
    } catch (err) {
      if (err instanceof BusinessHostingRequestError) {
        setError(createBusinessServerErrorMessage(err.status, err.body));
        if (shouldReloadAfterError(err.body)) load();
      } else {
        setError(err instanceof Error ? err.message : 'Could not create the server.');
      }
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="card" aria-labelledby="business-server-title">
      <h2 className="card-title" id="business-server-title">
        Hosted server
      </h2>
      <p className="card-meta">
        Business account: <strong>{business.businessName}</strong>
      </p>

      {error && <div className="error-banner">{error}</div>}

      {state.kind === 'pending' && (
        <p className="card-meta">
          Waiting for approval. You can create your hosted server once the business account is approved.
        </p>
      )}

      {state.kind === 'rejected' && (
        <p className="card-meta">This business account was not approved, so a hosted server is not available.</p>
      )}

      {state.kind === 'approved' && (
        <div className="server-row">
          <div className="card-meta">Approved. Create your server on this host; you will be its owner.</div>
          <div className="server-row-actions">
            <button type="button" className="btn-primary" onClick={handleCreate} disabled={busy}>
              {busy ? 'Creating...' : 'Create hosted server'}
            </button>
          </div>
        </div>
      )}

      {state.kind === 'has-server' && (
        <div className="server-row">
          <div className="card-meta">
            Your hosted server is ready.
            {business.serverDid && (
              <>
                {' '}
                <code>{shortServerDid(business.serverDid)}</code>
              </>
            )}
          </div>
          {business.serverDid && (
            <div className="server-row-actions">
              <Link to={serverOwnerSettingsPath(business.serverDid)} className="btn-ghost">
                Owner settings
              </Link>
            </div>
          )}
        </div>
      )}
    </section>
  );
}

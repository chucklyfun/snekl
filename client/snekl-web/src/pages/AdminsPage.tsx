import { Link } from 'react-router';
import { adminUrl, getAuthBaseUrl } from '../hosts';
import { useAuth } from '../AuthContext';
import { useServerRegistry } from '../ServerRegistryContext';

export function AdminsPage() {
  const { user } = useAuth();
  const { servers, federations } = useServerRegistry();
  const auth = getAuthBaseUrl();

  return (
    <div>
      <h1 className="page-title">Admins</h1>
      <p className="page-subtitle">
        Each host has its own operator UI. Open the URL for the machine you run — tokens do not carry across hosts.
      </p>

      <div className="card">
        <h2 className="card-title">Auth admin</h2>
        <p className="card-meta">Identity server for this client session.</p>
        <p>
          <a href={adminUrl(auth)}>{adminUrl(auth)}</a>
        </p>
      </div>

      <div className="card">
        <h2 className="card-title">Hub / federation admin</h2>
        {federations.length === 0 && (
          <p className="card-meta">
            {user ? (
              <>
                Add a federation URL on <Link to="/servers">Servers</Link>.
              </>
            ) : (
              <>
                <Link to="/login">Log in</Link> to load federations from your account.
              </>
            )}
          </p>
        )}
        {federations.map((fed) => (
          <div key={fed.baseUrl} className="server-row">
            <div>
              <strong>{fed.name}</strong>
              <div className="card-meta">{fed.baseUrl}</div>
            </div>
            <a className="btn-ghost" href={adminUrl(fed.baseUrl)}>
              Open admin
            </a>
          </div>
        ))}
      </div>

      <div className="card">
        <h2 className="card-title">Content admin</h2>
        {servers.length === 0 && (
          <p className="card-meta">
            Subscribe to a content origin on <Link to="/servers">Servers</Link>, then open its admin.
          </p>
        )}
        {servers.map((server) => (
          <div key={server.serverDid} className="server-row">
            <div>
              <strong>{server.name}</strong>
              <div className="card-meta">{server.baseUrl}</div>
            </div>
            <a className="btn-ghost" href={adminUrl(server.baseUrl)}>
              Open admin
            </a>
          </div>
        ))}
      </div>
    </div>
  );
}

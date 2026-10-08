import { adminUrl, getAuthBaseUrl } from '../hosts';

const LOCAL_ADMINS = [
  { label: 'Auth admin', baseUrl: 'http://localhost:5100' },
  { label: 'Hub admin', baseUrl: 'http://localhost:5200' },
  { label: 'Content admin (alpha)', baseUrl: 'http://localhost:5001' },
  { label: 'Content admin (beta)', baseUrl: 'http://localhost:5002' },
];

export function TempPage() {
  const rememberedAuth = getAuthBaseUrl();
  const extras =
    rememberedAuth &&
    !LOCAL_ADMINS.some((item) => item.baseUrl === rememberedAuth)
      ? [{ label: 'Auth admin (this client)', baseUrl: rememberedAuth }]
      : [];

  return (
    <div>
      <h1 className="page-title">Temp</h1>
      <p className="page-subtitle">
        Scratch links for local operator UIs. Each host has its own admin session.
      </p>
      <div className="card">
        <h2 className="card-title">Admin</h2>
        {[...extras, ...LOCAL_ADMINS].map((item) => {
          const href = adminUrl(item.baseUrl);
          return (
            <div key={href} className="server-row">
              <div>
                <strong>{item.label}</strong>
                <div className="card-meta">{href}</div>
              </div>
              <a className="btn-ghost" href={href}>
                Open
              </a>
            </div>
          );
        })}
      </div>
    </div>
  );
}

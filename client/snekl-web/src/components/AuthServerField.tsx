import { useEffect, useRef, useState } from 'react';
import { fetchTrustedAuthIssuers } from '../api';
import { authServerLabel, pickAuthServer, uniqueAuthServers } from '../authServers';
import { getAuthBaseUrl } from '../hosts';

export function AuthServerField({
  id,
  value,
  onChange,
}: {
  id: string;
  value: string;
  onChange: (url: string) => void;
}) {
  const [options, setOptions] = useState<ReturnType<typeof uniqueAuthServers>>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const onChangeRef = useRef(onChange);
  onChangeRef.current = onChange;

  useEffect(() => {
    let cancelled = false;
    const remembered = getAuthBaseUrl();
    fetchTrustedAuthIssuers()
      .then((issuers) => {
        if (cancelled) return;
        const next = uniqueAuthServers(issuers);
        setOptions(next);
        onChangeRef.current(pickAuthServer(next, remembered));
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Could not load auth servers');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  return (
    <div className="form-group">
      <label htmlFor={id}>Auth server</label>
      {loading ? (
        <p className="card-meta">Loading auth servers…</p>
      ) : error ? (
        <p className="error-banner">{error}</p>
      ) : options.length === 0 ? (
        <p className="card-meta">This content host has no auth servers configured.</p>
      ) : (
        <select id={id} value={value} onChange={(e) => onChange(e.target.value)} required>
          {options.map((issuer) => (
            <option key={issuer.issuerUrl} value={issuer.issuerUrl}>
              {authServerLabel(issuer)}
            </option>
          ))}
        </select>
      )}
    </div>
  );
}

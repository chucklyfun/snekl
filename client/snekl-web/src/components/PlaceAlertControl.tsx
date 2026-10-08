import { useEffect, useState } from 'react';
import type { createContentClient } from '../api';
import type { PlaceAlertMode } from '../types';

type ContentClient = ReturnType<typeof createContentClient>;

export function PlaceAlertControl({
  client,
  place,
}: {
  client: ContentClient;
  place: { kind: 'nest'; id: string } | { kind: 'watercooler'; id: string };
}) {
  const [mode, setMode] = useState<PlaceAlertMode>('off');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const label = place.kind === 'nest' ? 'Nest alerts' : 'Watercooler alerts';

  useEffect(() => {
    let cancelled = false;
    const load =
      place.kind === 'nest'
        ? client.getNestAlertPreference(place.id)
        : client.getWatercoolerAlertPreference(place.id);
    load
      .then((preference) => {
        if (!cancelled) setMode(preference.mode);
      })
      .catch(() => {});
    return () => {
      cancelled = true;
    };
  }, [client, place.kind, place.id]);

  return (
    <label className="thread-alert-setting">
      {label}
      <select
        aria-label={label}
        value={mode}
        disabled={busy}
        onChange={(event) => {
          const next = event.target.value;
          if (next !== 'off' && next !== 'posts' && next !== 'all') return;
          setBusy(true);
          setError('');
          const save =
            place.kind === 'nest'
              ? client.setNestAlertPreference(place.id, next)
              : client.setWatercoolerAlertPreference(place.id, next);
          void save
            .then((preference) => setMode(preference.mode))
            .catch((err: unknown) => setError(err instanceof Error ? err.message : 'Could not save alerts'))
            .finally(() => setBusy(false));
        }}
      >
        <option value="off">Off</option>
        <option value="posts">New posts</option>
        <option value="all">Everything</option>
      </select>
      {error && <span className="card-meta">{error}</span>}
    </label>
  );
}

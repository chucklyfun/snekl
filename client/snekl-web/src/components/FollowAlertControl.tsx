import { useState } from 'react';
import { useFollows } from '../FollowsContext';
import type { FollowAlertMode } from '../types';

function isFollowAlertMode(value: string): value is FollowAlertMode {
  switch (value) {
    case 'off':
    case 'posts':
    case 'notable':
    case 'all':
      return true;
    default:
      return false;
  }
}

export function FollowAlertControl({ did }: { did: string }) {
  const { follows, setFollowAlert } = useFollows();
  const stored = follows.find((person) => person.did === did)?.alertMode ?? 'off';
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  return (
    <label className="thread-alert-setting">
      Follow alerts
      <select
        aria-label="Follow alerts"
        value={stored}
        disabled={busy}
        onChange={(event) => {
          const next = event.target.value;
          if (!isFollowAlertMode(next)) return;
          setBusy(true);
          setError('');
          void setFollowAlert(did, next)
            .catch((err: unknown) => setError(err instanceof Error ? err.message : 'Could not save alerts'))
            .finally(() => setBusy(false));
        }}
      >
        <option value="off">Off</option>
        <option value="posts">New posts</option>
        <option value="notable">Vote milestones</option>
        <option value="all">Posts and votes</option>
      </select>
      {error && <span className="card-meta">{error}</span>}
    </label>
  );
}

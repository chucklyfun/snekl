import { useEffect, useState } from 'react';
import type { ChangeLogEntry } from '../types';

function historyEntityLabel(entityType: string): string {
  return entityType.replaceAll('Forum', 'Nest');
}

function prettyJson(raw: string | null | undefined): string | null {
  if (!raw) return null;
  try {
    return JSON.stringify(JSON.parse(raw), null, 2);
  } catch {
    return raw;
  }
}

export function ChangeHistoryList({
  load,
}: {
  load: () => Promise<ChangeLogEntry[]>;
}) {
  const [rows, setRows] = useState<ChangeLogEntry[]>([]);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError('');
    void load()
      .then((items) => {
        if (!cancelled) setRows(items);
      })
      .catch((err) => {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Could not load history');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [load]);

  if (loading) return <p className="card-meta">Loading history…</p>;
  if (error) return <div className="error-banner">{error}</div>;
  if (rows.length === 0) return <p className="card-meta">No recorded changes yet.</p>;

  return (
    <div className="change-history">
      {rows.map((row) => {
        const before = prettyJson(row.beforeJson);
        const after = prettyJson(row.afterJson);
        return (
          <article key={row.id} className="change-history-item">
            <div className="card-meta">
              @{row.actorUsername} · {row.action} · {historyEntityLabel(row.entityType)} ·{' '}
              {new Date(row.createdAt).toLocaleString()}
            </div>
            <p className="post-body">{row.summary}</p>
            {(before || after) && (
              <details>
                <summary>Details</summary>
                {before && (
                  <pre className="change-history-json">
                    <span className="card-meta">Before</span>
                    {before}
                  </pre>
                )}
                {after && (
                  <pre className="change-history-json">
                    <span className="card-meta">After</span>
                    {after}
                  </pre>
                )}
              </details>
            )}
          </article>
        );
      })}
    </div>
  );
}

import { useState } from 'react';

export type CheckStatus = 'todo' | 'done' | 'attention';

export type CheckItem = {
  id: string;
  title: string;
  detail: string;
  status: CheckStatus;
};

const COLLAPSE_KEY = 'snekl_admin_next_up_collapsed';

function loadCollapsed(): boolean {
  try {
    return localStorage.getItem(COLLAPSE_KEY) === '1';
  } catch {
    return false;
  }
}

export function Checklist({ items }: { items: CheckItem[] }) {
  const [collapsed, setCollapsed] = useState(loadCollapsed);

  const toggle = () => {
    setCollapsed((current) => {
      const next = !current;
      try {
        localStorage.setItem(COLLAPSE_KEY, next ? '1' : '0');
      } catch {
        /* ignore quota / private mode */
      }
      return next;
    });
  };

  return (
    <div className="card">
      <div className="checklist-header">
        <h2 className="card-title">Next up</h2>
        <CollectedStatus items={items} />
        <button
          type="button"
          className="btn-ghost"
          aria-expanded={!collapsed}
          aria-controls="next-up-status"
          onClick={toggle}
        >
          {collapsed ? 'Expand' : 'Collapse'}
        </button>
      </div>
      {!collapsed && (
        <div id="next-up-status">
          <p className="card-meta">Skip, redo, or come back later — nothing here is a one-shot wizard.</p>
          <ul className="checklist">
            {items.map((item) => (
              <li key={item.id}>
                <span className="checklist-status" style={{ color: colorFor(item.status) }}>
                  {item.status}
                </span>
                <div>
                  <strong>{item.title}</strong>
                  <div className="card-meta">{item.detail}</div>
                </div>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}

function CollectedStatus({ items }: { items: CheckItem[] }) {
  const counts = { done: 0, todo: 0, attention: 0 };
  for (const item of items) counts[item.status] += 1;

  const parts = (['attention', 'todo', 'done'] as const).filter((status) => counts[status] > 0);
  if (parts.length === 0) {
    return <p className="card-meta checklist-summary">No items yet.</p>;
  }

  return (
    <p className="card-meta checklist-summary" aria-label="Collected status">
      {parts.map((status, index) => (
        <span key={status}>
          {index > 0 ? ' · ' : ''}
          <span style={{ color: colorFor(status), fontWeight: 700 }}>
            {counts[status]} {status}
          </span>
        </span>
      ))}
    </p>
  );
}

function colorFor(status: CheckStatus) {
  if (status === 'done') return '#86efac';
  if (status === 'attention') return 'var(--danger)';
  return 'var(--muted)';
}

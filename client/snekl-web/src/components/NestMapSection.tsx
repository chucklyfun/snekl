import { useEffect, useState } from 'react';
import { Link } from 'react-router';
import { forumPath } from '../serverDid';
import type { NestMapNode } from '../types';

type NestMapClient = {
  getNestMap: () => Promise<NestMapNode[]>;
  placeNest: (forumId: string, parentForumId: string | null, sortOrder: number) => Promise<void>;
  setNestLocked: (forumId: string, value: boolean) => Promise<void>;
  setNestHidden: (forumId: string, value: boolean) => Promise<void>;
  purgeNest: (forumId: string) => Promise<void>;
};

export function NestMapSection({
  client,
  serverDid,
  onChanged,
}: {
  client: NestMapClient;
  serverDid: string;
  onChanged: () => void;
}) {
  const [nodes, setNodes] = useState<NestMapNode[]>([]);
  const [selected, setSelected] = useState('');
  const [parentId, setParentId] = useState('');
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');

  const load = () => {
    client
      .getNestMap()
      .then((rows) => {
        setNodes(rows);
        setSelected((current) => {
          const next = rows.some((row) => row.id === current) ? current : rows[0]?.id ?? '';
          const chosen = rows.find((row) => row.id === next);
          setParentId(chosen?.parentForumId ?? '');
          return next;
        });
      })
      .catch((err) => setError(err instanceof Error ? err.message : 'Could not load the nest map'));
  };

  useEffect(load, [client]);

  const node = nodes.find((row) => row.id === selected);
  const blocked = new Set<string>();
  if (node) {
    const children = new Map<string, string[]>();
    for (const row of nodes) {
      if (!row.parentForumId) continue;
      const list = children.get(row.parentForumId) ?? [];
      list.push(row.id);
      children.set(row.parentForumId, list);
    }
    const stack = [node.id];
    while (stack.length > 0) {
      const id = stack.pop()!;
      if (blocked.has(id)) continue;
      blocked.add(id);
      for (const child of children.get(id) ?? []) stack.push(child);
    }
  }

  const run = async (action: () => Promise<void>, message: string) => {
    setError('');
    setSuccess('');
    try {
      await action();
      setSuccess(message);
      load();
      onChanged();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Nest update failed');
    }
  };

  return (
    <section className="card">
      <h2 className="card-title">Nest map</h2>
      <p className="card-meta">
        Arrange nests at any depth. Public addresses use the top-level nest and the nest itself. Lock, hide, and
        purge apply to the selected nest and the nests under it. Hide is a preliminary delete. Purge removes a hidden nest.
      </p>
      {error && <div className="error-banner">{error}</div>}
      {success && <p className="card-meta">{success}</p>}
      {nodes.length === 0 && <p className="card-meta">No nests on this server yet.</p>}
      {nodes.map((row) => (
        <div key={row.id} className="server-row" style={{ paddingLeft: `${row.depth * 1.25}rem` }}>
          <div>
            <button type="button" className={row.id === selected ? 'btn-primary' : 'btn-ghost'} onClick={() => {
              setSelected(row.id);
              setParentId(row.parentForumId ?? '');
            }}>
              {row.name}
            </button>
            <div className="card-meta">
              /{row.topSlug ? `${row.topSlug}/` : ''}
              {row.slug}
              {row.locked ? ' · locked' : ''}
              {row.hidden ? ' · hidden' : ''}
            </div>
          </div>
          <div className="server-row-actions">
            <Link to={forumPath(serverDid, row.slug, row.topSlug ?? undefined)} className="btn-ghost">
              Open
            </Link>
          </div>
        </div>
      ))}
      {node && (
        <form
          onSubmit={(event) => {
            event.preventDefault();
            const siblings = nodes
              .filter((row) => (row.parentForumId ?? '') === parentId && row.id !== node.id)
              .sort((a, b) => a.sortOrder - b.sortOrder);
            void run(() => client.placeNest(node.id, parentId || null, siblings.length), `Moved ${node.name}.`);
          }}
        >
          <div className="form-group">
            <label htmlFor="nest-parent">Place {node.name} under</label>
            <select id="nest-parent" value={parentId} onChange={(event) => setParentId(event.target.value)}>
              <option value="">Top level</option>
              {nodes
                .filter((row) => !blocked.has(row.id))
                .map((row) => (
                  <option key={row.id} value={row.id}>
                    {'· '.repeat(row.depth)}
                    {row.name}
                  </option>
                ))}
            </select>
          </div>
          <div className="server-row-actions">
            <button type="submit" className="btn-primary">
              Move
            </button>
            <button
              type="button"
              className="btn-ghost"
              onClick={() => {
                const siblings = nodes
                  .filter((row) => (row.parentForumId ?? '') === (node.parentForumId ?? ''))
                  .sort((a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name));
                const index = siblings.findIndex((row) => row.id === node.id);
                if (index <= 0) return;
                void run(
                  () => client.placeNest(node.id, node.parentForumId ?? null, index - 1),
                  `Moved ${node.name} up.`,
                );
              }}
            >
              Up
            </button>
            <button
              type="button"
              className="btn-ghost"
              onClick={() => {
                const siblings = nodes
                  .filter((row) => (row.parentForumId ?? '') === (node.parentForumId ?? ''))
                  .sort((a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name));
                const index = siblings.findIndex((row) => row.id === node.id);
                if (index < 0 || index >= siblings.length - 1) return;
                void run(
                  () => client.placeNest(node.id, node.parentForumId ?? null, index + 1),
                  `Moved ${node.name} down.`,
                );
              }}
            >
              Down
            </button>
            <button
              type="button"
              className="btn-ghost"
              onClick={() => void run(() => client.setNestLocked(node.id, !node.locked), node.locked ? `Unlocked ${node.name}.` : `Locked ${node.name}.`)}
            >
              {node.locked ? 'Unlock' : 'Lock'}
            </button>
            <button
              type="button"
              className="btn-ghost"
              onClick={() => void run(() => client.setNestHidden(node.id, !node.hidden), node.hidden ? `Restored ${node.name}.` : `Hid ${node.name}.`)}
            >
              {node.hidden ? 'Restore' : 'Hide'}
            </button>
            <button
              type="button"
              className="btn-ghost"
              disabled={!node.hidden}
              onClick={() => {
                if (!window.confirm(`Purge ${node.name} and the nests under it? This cannot be undone.`)) return;
                void run(() => client.purgeNest(node.id), `Purged ${node.name}.`);
              }}
            >
              Purge
            </button>
          </div>
        </form>
      )}
    </section>
  );
}

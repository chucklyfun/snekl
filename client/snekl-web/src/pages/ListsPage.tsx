import { type FormEvent, useEffect, useState } from 'react';
import { api } from '../api';
import { useAuth } from '../AuthContext';
import type { CuratedList } from '../types';

export function ListsPage() {
  const { user } = useAuth();
  const [lists, setLists] = useState<CuratedList[]>([]);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');

  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [behavior, setBehavior] = useState('Highlight');
  const [emoji, setEmoji] = useState('★');

  const [entryListId, setEntryListId] = useState('');
  const [targetUsername, setTargetUsername] = useState('');
  const [tooltip, setTooltip] = useState('');
  const [severity, setSeverity] = useState(2);

  const load = () => {
    api.getLists().then(setLists).catch((e) => setError(e.message));
  };

  useEffect(load, []);

  const createList = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    try {
      await api.createList({ name, description, defaultBehavior: behavior, defaultFlairEmoji: emoji, isPublic: true });
      setName('');
      setDescription('');
      setSuccess('List created.');
      load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed');
    }
  };

  const subscribe = async (listId: string) => {
    try {
      await api.subscribeList(listId, 'Highlight');
      setSuccess('Subscribed to list.');
      load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed');
    }
  };

  const addEntry = async (e: FormEvent) => {
    e.preventDefault();
    if (!entryListId || !targetUsername) return;
    setError('');
    try {
      const target = await api.getUser(targetUsername);
      await api.addListEntry(entryListId, {
        targetUserId: target.id,
        severity,
        tooltipText: tooltip,
        flairEmoji: emoji,
        usernameColor: severity >= 3 ? '#f59e0b' : undefined,
      });
      setSuccess(`Added @${targetUsername} to list.`);
      setTargetUsername('');
      setTooltip('');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed');
    }
  };

  return (
    <div>
      <h1 className="page-title">Curated lists</h1>
      <p className="page-subtitle">
        Community-managed highlight, verify, and warning lists. Subscribe to lists you trust — annotations appear on posts and profiles.
      </p>

      {error && <div className="error-banner">{error}</div>}
      {success && <div className="card" style={{ borderColor: 'var(--accent)', marginBottom: '1rem' }}>{success}</div>}

      {user && (
        <form onSubmit={createList} className="card">
          <h2 className="card-title">Create a list</h2>
          <div className="form-group">
            <label htmlFor="listName">Name</label>
            <input id="listName" value={name} onChange={(e) => setName(e.target.value)} required />
          </div>
          <div className="form-group">
            <label htmlFor="listDesc">Description</label>
            <textarea id="listDesc" value={description} onChange={(e) => setDescription(e.target.value)} />
          </div>
          <div className="grid-2">
            <div className="form-group">
              <label htmlFor="behavior">Default behavior</label>
              <select id="behavior" value={behavior} onChange={(e) => setBehavior(e.target.value)}>
                <option value="Highlight">Highlight</option>
                <option value="Verify">Verify</option>
                <option value="Warn">Warn</option>
                <option value="Mute">Mute</option>
                <option value="Hide">Hide</option>
              </select>
            </div>
            <div className="form-group">
              <label htmlFor="listEmoji">Default badge</label>
              <input id="listEmoji" value={emoji} onChange={(e) => setEmoji(e.target.value)} maxLength={4} />
            </div>
          </div>
          <button type="submit" className="btn-primary">Create list</button>
        </form>
      )}

      {user && lists.some((l) => l.isOwner) && (
        <form onSubmit={addEntry} className="card">
          <h2 className="card-title">Add user to your list</h2>
          <div className="form-group">
            <label htmlFor="entryList">List</label>
            <select id="entryList" value={entryListId} onChange={(e) => setEntryListId(e.target.value)} required>
              <option value="">Select a list you own</option>
              {lists.filter((l) => l.isOwner).map((l) => (
                <option key={l.id} value={l.id}>{l.name}</option>
              ))}
            </select>
          </div>
          <div className="grid-2">
            <div className="form-group">
              <label htmlFor="targetUser">Username</label>
              <input id="targetUser" value={targetUsername} onChange={(e) => setTargetUsername(e.target.value)} placeholder="username" required />
            </div>
            <div className="form-group">
              <label htmlFor="severity">Severity (0–4)</label>
              <input id="severity" type="number" min={0} max={4} value={severity} onChange={(e) => setSeverity(Number(e.target.value))} />
            </div>
          </div>
          <div className="form-group">
            <label htmlFor="tooltip">Tooltip explanation</label>
            <input id="tooltip" value={tooltip} onChange={(e) => setTooltip(e.target.value)} required placeholder="Why this label applies" />
          </div>
          <button type="submit" className="btn-primary">Add entry</button>
        </form>
      )}

      <div className="card">
        <h2 className="card-title">Available lists ({lists.length})</h2>
        {lists.length === 0 && <p className="card-meta">No lists yet.</p>}
        {lists.map((list) => (
          <div key={list.id} style={{ padding: '0.75rem 0', borderBottom: '1px solid var(--border)' }}>
            <strong>{list.defaultFlairEmoji} {list.name}</strong>
            {list.isOwner && <span className="badge" style={{ marginLeft: '0.5rem' }}>owner</span>}
            <div className="card-meta">{list.description}</div>
            <div className="card-meta">{list.entryCount} entries · {list.subscriberCount} subscribers · {list.defaultBehavior}</div>
            {user && !list.isOwner && (
              <button type="button" className="btn-ghost" style={{ marginTop: '0.5rem' }} onClick={() => subscribe(list.id)}>
                Subscribe
              </button>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}

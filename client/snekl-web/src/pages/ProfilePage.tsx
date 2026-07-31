import { type FormEvent, useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { api } from '../api';
import { UserByline } from '../components/UserPresentation';
import { useAuth } from '../AuthContext';
import type { StatusPreset, UserPresentation } from '../types';
import '../components/UserPresentation.css';

export function ProfilePage() {
  const { username } = useParams<{ username: string }>();
  const { user: me, refreshMe } = useAuth();
  const [profile, setProfile] = useState<UserPresentation | null>(null);
  const [presets, setPresets] = useState<StatusPreset[]>([]);
  const [error, setError] = useState('');
  const [saved, setSaved] = useState('');

  const isOwnProfile = me?.username === username;

  const [flairEmoji, setFlairEmoji] = useState('');
  const [flairLabel, setFlairLabel] = useState('');
  const [nameColor, setNameColor] = useState('#4f8cff');
  const [nameStyle, setNameStyle] = useState('normal');

  const [selectedPreset, setSelectedPreset] = useState('custom');
  const [statusMessage, setStatusMessage] = useState('');
  const [statusLink, setStatusLink] = useState('');
  const [eventName, setEventName] = useState('');
  const [durationHours, setDurationHours] = useState(8);

  useEffect(() => {
    if (!username) return;
    api.getUser(username).then(setProfile).catch((e) => setError(e.message));
    api.getStatusPresets().then(setPresets).catch(() => {});
  }, [username]);

  useEffect(() => {
    if (!profile || !isOwnProfile) return;
    setFlairEmoji(profile.flair?.emoji ?? '');
    setFlairLabel(profile.flair?.label ?? '');
    setNameColor(profile.flair?.nameColor ?? '#4f8cff');
    setNameStyle(profile.flair?.nameStyle ?? 'normal');
  }, [profile, isOwnProfile]);

  const saveFlair = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    try {
      const updated = await api.updateFlair({
        emoji: flairEmoji || undefined,
        label: flairLabel || undefined,
        nameColor: nameColor || undefined,
        nameStyle: nameStyle || undefined,
      });
      setProfile(updated);
      await refreshMe();
      setSaved('Flair saved.');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save flair');
    }
  };

  const applyPreset = (preset: StatusPreset) => {
    setSelectedPreset(preset.preset);
    setStatusMessage(preset.defaultMessage);
    setDurationHours(preset.defaultDurationHours);
  };

  const saveStatus = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    const expiresAt = new Date(Date.now() + durationHours * 3600_000).toISOString();
    const preset = presets.find((p) => p.preset === selectedPreset);
    try {
      await api.setStatus({
        preset: selectedPreset,
        message: statusMessage || undefined,
        linkUrl: statusLink || undefined,
        eventName: eventName || undefined,
        expiresAt,
        avatarOverlay: preset
          ? {
              kind: preset.defaultOverlayKind,
              emoji: preset.defaultEmoji ?? undefined,
              color: preset.defaultOverlayColor ?? undefined,
              ariaLabel: preset.label,
            }
          : undefined,
      });
      if (username) setProfile(await api.getUser(username));
      await refreshMe();
      setSaved('Status updated.');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to set status');
    }
  };

  const clearStatus = async () => {
    await api.clearStatus();
    if (username) setProfile(await api.getUser(username));
    await refreshMe();
    setSaved('Status cleared.');
  };

  if (!profile) return <p>Loading profile...</p>;

  return (
    <div>
      <div className="card">
        <UserByline presentation={profile} />
        {profile.bio && <p style={{ marginTop: '1rem' }}>{profile.bio}</p>}
      </div>

      {error && <div className="error-banner">{error}</div>}
      {saved && <div className="card" style={{ borderColor: 'var(--accent)' }}>{saved}</div>}

      {isOwnProfile && (
        <>
          <form onSubmit={saveFlair} className="card profile-section">
            <h2 className="card-title">Your flair</h2>
            <p className="card-meta">Emoji or graphics that appear on your posts — separate from community list badges.</p>
            <div className="grid-2">
              <div className="form-group">
                <label htmlFor="flairEmoji">Emoji</label>
                <input id="flairEmoji" value={flairEmoji} onChange={(e) => setFlairEmoji(e.target.value)} placeholder="🐱" maxLength={8} />
              </div>
              <div className="form-group">
                <label htmlFor="flairLabel">Screen reader label</label>
                <input id="flairLabel" value={flairLabel} onChange={(e) => setFlairLabel(e.target.value)} placeholder="Cat lover" />
              </div>
              <div className="form-group">
                <label htmlFor="nameColor">Username color</label>
                <input id="nameColor" type="color" value={nameColor} onChange={(e) => setNameColor(e.target.value)} />
              </div>
              <div className="form-group">
                <label htmlFor="nameStyle">Name style</label>
                <select id="nameStyle" value={nameStyle} onChange={(e) => setNameStyle(e.target.value)}>
                  <option value="normal">Normal</option>
                  <option value="bold">Bold</option>
                  <option value="italic">Italic</option>
                </select>
              </div>
            </div>
            <button type="submit" className="btn-primary">Save flair</button>
          </form>

          <form onSubmit={saveStatus} className="card profile-section">
            <h2 className="card-title">Temporary status</h2>
            <p className="card-meta">Statuses expire automatically and can add avatar overlays.</p>

            <div className="preset-grid">
              {presets.filter((p) => p.preset !== 'custom').map((p) => (
                <button
                  key={p.preset}
                  type="button"
                  className={`preset-btn ${selectedPreset === p.preset ? 'active' : ''}`}
                  onClick={() => applyPreset(p)}
                >
                  <span className="preset-emoji">{p.defaultEmoji}</span>
                  {p.label}
                </button>
              ))}
            </div>

            <div className="form-group">
              <label htmlFor="statusMessage">Message</label>
              <input id="statusMessage" value={statusMessage} onChange={(e) => setStatusMessage(e.target.value)} placeholder="Live on Twitch..." />
            </div>
            <div className="grid-2">
              <div className="form-group">
                <label htmlFor="statusLink">Link (optional)</label>
                <input id="statusLink" value={statusLink} onChange={(e) => setStatusLink(e.target.value)} placeholder="https://..." />
              </div>
              <div className="form-group">
                <label htmlFor="eventName">Event name (optional)</label>
                <input id="eventName" value={eventName} onChange={(e) => setEventName(e.target.value)} />
              </div>
            </div>
            <div className="form-group">
              <label htmlFor="duration">Duration (hours)</label>
              <input id="duration" type="number" min={1} max={168} value={durationHours} onChange={(e) => setDurationHours(Number(e.target.value))} />
            </div>
            <div style={{ display: 'flex', gap: '0.5rem' }}>
              <button type="submit" className="btn-primary">Set status</button>
              {profile.activeStatus && (
                <button type="button" className="btn-ghost" onClick={clearStatus}>Clear status</button>
              )}
            </div>
          </form>
        </>
      )}
    </div>
  );
}

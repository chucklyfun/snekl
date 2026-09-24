import { type FormEvent, useEffect, useMemo, useState } from 'react';
import { Link, useParams } from 'react-router';
import { apiFor } from '../api';
import { UserByline } from '../components/UserPresentation';
import { useAuth } from '../AuthContext';
import { useFollows } from '../FollowsContext';
import { useServerRegistry } from '../ServerRegistryContext';
import type { DirectMessageEligibility, StatusPreset, UserPresentation } from '../types';
import '../components/UserPresentation.css';

export function ProfilePage() {
  const { username } = useParams<{ username: string }>();
  const { user: me, did: myDid, refreshMe } = useAuth();
  const { isFollowing, follow, unfollow } = useFollows();
  const { activeServers } = useServerRegistry();
  const contentBaseUrl = activeServers[0]?.baseUrl;
  const api = useMemo(() => (contentBaseUrl ? apiFor(contentBaseUrl) : null), [contentBaseUrl]);
  const [profile, setProfile] = useState<UserPresentation | null>(null);
  const [presets, setPresets] = useState<StatusPreset[]>([]);
  const [eligibility, setEligibility] = useState<DirectMessageEligibility | null>(null);
  const [requestMessage, setRequestMessage] = useState('');
  const [requestForumId, setRequestForumId] = useState('');
  const [error, setError] = useState('');
  const [saved, setSaved] = useState('');

  const isOwnProfile = me?.username === username;
  const canFollow = Boolean(me && profile?.did && profile.did !== myDid);

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
    if (!username || !api) return;
    api.getUser(username).then(setProfile).catch((e) => setError(e.message));
    api.getStatusPresets().then(setPresets).catch(() => {});
  }, [username, api]);

  const loadEligibility = () => {
    if (!username || !api || !me || isOwnProfile) {
      setEligibility(null);
      return;
    }
    api
      .getDirectMessageEligibility(username)
      .then((next) => {
        setEligibility(next);
        setRequestForumId('');
      })
      .catch(() => setEligibility(null));
  };

  useEffect(() => {
    loadEligibility();
  }, [username, api, me, isOwnProfile]);

  useEffect(() => {
    if (!profile || !isOwnProfile) return;
    setFlairEmoji(profile.flair?.emoji ?? '');
    setFlairLabel(profile.flair?.label ?? '');
    setNameColor(profile.flair?.nameColor ?? '#4f8cff');
    setNameStyle(profile.flair?.nameStyle ?? 'normal');
  }, [profile, isOwnProfile]);

  const saveFlair = async (e: FormEvent) => {
    e.preventDefault();
    if (!api) return;
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
    if (!api) return;
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
    if (!api) return;
    await api.clearStatus();
    if (username) setProfile(await api.getUser(username));
    await refreshMe();
    setSaved('Status cleared.');
  };

  if (!api) {
    return <p className="card-meta">Subscribe to a content server on Servers to view profiles.</p>;
  }

  if (!profile) return <p>Loading profile...</p>;

  return (
    <div>
      <div className="card">
        <UserByline presentation={profile} />
        {profile.bio && <p style={{ marginTop: '1rem' }}>{profile.bio}</p>}
        {canFollow && profile.did && (
          <div style={{ marginTop: '1rem' }}>
            {isFollowing(profile.did) ? (
              <button
                type="button"
                className="btn-ghost"
                onClick={() => {
                  setError('');
                  void unfollow(profile.did!).catch((err) =>
                    setError(err instanceof Error ? err.message : 'Failed to unfollow'),
                  );
                }}
              >
                Unfollow
              </button>
            ) : (
              <button
                type="button"
                className="btn-primary"
                onClick={() => {
                  setError('');
                  void follow({
                    did: profile.did!,
                    displayName: profile.displayName,
                    username: profile.username,
                  }).catch((err) =>
                    setError(err instanceof Error ? err.message : 'Failed to follow'),
                  );
                }}
              >
                Follow
              </button>
            )}
          </div>
        )}
        {me && !isOwnProfile && eligibility && (
          <div style={{ marginTop: '1rem' }}>
            {eligibility.existingConversationId ? (
              <Link className="btn-primary" to={`/messages/${eligibility.existingConversationId}`}>
                Open conversation
              </Link>
            ) : eligibility.pendingRequest && eligibility.pendingRequest.target.username === me.username ? (
              <div className="server-row-actions">
                <button
                  type="button"
                  className="btn-primary"
                  onClick={() => {
                    if (!api || !eligibility.pendingRequest) return;
                    const pending = eligibility.pendingRequest;
                    void api
                      .acceptDirectMessageRequest(pending.id)
                      .then(async (accepted) => {
                        if (pending.requester.did) {
                          await follow({
                            did: pending.requester.did,
                            displayName: pending.requester.displayName,
                            username: pending.requester.username,
                          }).catch(() => undefined);
                        }
                        loadEligibility();
                        setSaved('Follow-back accepted. You can talk in Messages.');
                        return accepted;
                      })
                      .catch((err) =>
                        setError(err instanceof Error ? err.message : 'Failed to accept request'),
                      );
                  }}
                >
                  Follow back and chat
                </button>
                <button
                  type="button"
                  className="btn-ghost"
                  onClick={() => {
                    if (!api || !eligibility.pendingRequest) return;
                    void api
                      .declineDirectMessageRequest(eligibility.pendingRequest.id)
                      .then(loadEligibility)
                      .catch((err) =>
                        setError(err instanceof Error ? err.message : 'Failed to decline'),
                      );
                  }}
                >
                  Decline
                </button>
              </div>
            ) : eligibility.pendingRequest ? (
              <p className="card-meta">Follow-back requested. Waiting for them to accept so you can DM.</p>
            ) : eligibility.canRequest ? (
              <form
                onSubmit={(event) => {
                  event.preventDefault();
                  if (!api || !username) return;
                  if (!requestForumId) {
                    setError('Choose the forum whose permission you are using for this request.');
                    return;
                  }
                  setError('');
                  void api
                    .createDirectMessageRequest({
                      targetUsername: username,
                      forumId: requestForumId,
                      message: requestMessage || undefined,
                    })
                    .then(() => {
                      setSaved('Follow-back requested. They can accept to open a DM.');
                      loadEligibility();
                    })
                    .catch((err) =>
                      setError(err instanceof Error ? err.message : 'Failed to request follow-back'),
                    );
                }}
              >
                <p className="card-meta">
                  Choose the forum whose RequestDirectMessage permission you are using. Forums can keep
                  this off new members until they have a trusted role.
                </p>
                <div className="form-group">
                  <label htmlFor="dm-forum">Forum</label>
                  <select
                    id="dm-forum"
                    value={requestForumId}
                    onChange={(event) => setRequestForumId(event.target.value)}
                    required
                  >
                    <option value="" disabled>
                      Select a forum
                    </option>
                    {eligibility.eligibleForums.map((forum) => (
                      <option key={forum.id} value={forum.id}>
                        {forum.parentSlug ? `${forum.parentSlug} / ${forum.name}` : forum.name}
                      </option>
                    ))}
                  </select>
                </div>
                <div className="form-group">
                  <label htmlFor="dm-intro">Note (optional)</label>
                  <input
                    id="dm-intro"
                    value={requestMessage}
                    onChange={(event) => setRequestMessage(event.target.value)}
                    placeholder="Why you want to talk"
                  />
                </div>
                <button type="submit" className="btn-primary">
                  Request follow-back to DM
                </button>
              </form>
            ) : (
              <p className="card-meta">{eligibility.reason}</p>
            )}
          </div>
        )}
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

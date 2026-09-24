import { type FormEvent, useEffect, useMemo, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { apiFor } from '../api';
import { useAuth } from '../AuthContext';
import { useFollows } from '../FollowsContext';
import { useServerRegistry } from '../ServerRegistryContext';
import type {
  DirectConversation,
  DirectConversationRequest,
  DirectMessageForumPreferences,
  DirectMessageInbox,
} from '../types';

export function MessagesPage() {
  const { conversationId } = useParams<{ conversationId?: string }>();
  const { user } = useAuth();
  const { follow } = useFollows();
  const { activeServers } = useServerRegistry();
  const contentBaseUrl = activeServers[0]?.baseUrl;
  const api = useMemo(() => (contentBaseUrl ? apiFor(contentBaseUrl) : null), [contentBaseUrl]);
  const navigate = useNavigate();
  const [inbox, setInbox] = useState<DirectMessageInbox | null>(null);
  const [preferences, setPreferences] = useState<DirectMessageForumPreferences | null>(null);
  const [conversation, setConversation] = useState<DirectConversation | null>(null);
  const [draft, setDraft] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  const loadInbox = () => {
    if (!api) return;
    api.getDirectMessageInbox().then(setInbox).catch((err) => setError(err.message));
    api.getDirectMessageForumPreferences().then(setPreferences).catch(() => setPreferences(null));
  };

  useEffect(() => {
    loadInbox();
  }, [api]);

  useEffect(() => {
    if (!api || !conversationId) {
      setConversation(null);
      return;
    }
    api
      .getDirectConversation(conversationId)
      .then(setConversation)
      .catch((err) => setError(err instanceof Error ? err.message : 'Failed to load conversation'));
  }, [api, conversationId]);

  if (!user) {
    return (
      <div className="card">
        <p>
          Please <Link to="/login">log in</Link> to read direct messages.
        </p>
      </div>
    );
  }

  if (!api) {
    return <p className="card-meta">Subscribe to a content server on Servers to use DMs.</p>;
  }

  const accept = async (item: DirectConversationRequest) => {
    setBusy(true);
    setError('');
    try {
      const accepted = await api.acceptDirectMessageRequest(item.id);
      if (item.requester.did) {
        await follow({
          did: item.requester.did,
          displayName: item.requester.displayName,
          username: item.requester.username,
        }).catch(() => undefined);
      }
      loadInbox();
      if (accepted.conversationId) navigate(`/messages/${accepted.conversationId}`);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to accept');
    } finally {
      setBusy(false);
    }
  };

  const setForumMode = async (forumId: string, mode: string) => {
    if (!api) return;
    setBusy(true);
    setError('');
    try {
      await api.setDirectMessageForumPreference(forumId, mode);
      loadInbox();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to update forum request handling');
    } finally {
      setBusy(false);
    }
  };

  const send = async (event: FormEvent) => {
    event.preventDefault();
    if (!conversationId || !draft.trim()) return;
    setBusy(true);
    setError('');
    try {
      const message = await api.sendDirectMessage(conversationId, draft.trim());
      setConversation((current) =>
        current ? { ...current, messages: [...current.messages, message] } : current,
      );
      setDraft('');
      loadInbox();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to send');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div>
      <h1 className="page-title">Messages</h1>
      <p className="page-subtitle">
        Follow-back requests unlock DMs. Forums grant <code>RequestDirectMessage</code> on a role —
        typically not @everyone or new members — so communities can keep solicitation behind
        reputation.
      </p>
      {error && <div className="error-banner">{error}</div>}

      {inbox && inbox.incomingRequests.length > 0 && (
        <section className="card">
          <h2 className="card-title">Follow-back requests</h2>
          {inbox.incomingRequests.map((item) => (
            <div key={item.id} className="server-row">
              <div>
                <Link to={`/users/${item.requester.username}`}>{item.requester.displayName}</Link>
                <div className="card-meta">
                  in {item.forum.name}
                  {item.message ? ` — ${item.message}` : ''}
                </div>
              </div>
              <div className="server-row-actions">
                <button type="button" className="btn-primary" disabled={busy} onClick={() => void accept(item)}>
                  Follow back and chat
                </button>
                <button
                  type="button"
                  className="btn-ghost"
                  disabled={busy}
                  onClick={() => {
                    void api.declineDirectMessageRequest(item.id).then(loadInbox);
                  }}
                >
                  Decline
                </button>
                <button
                  type="button"
                  className="btn-ghost"
                  disabled={busy}
                  onClick={() => void setForumMode(item.forum.id, 'ignore')}
                >
                  Ignore this forum
                </button>
                <button
                  type="button"
                  className="btn-ghost"
                  disabled={busy}
                  onClick={() => void setForumMode(item.forum.id, 'reject')}
                >
                  Reject this forum
                </button>
              </div>
            </div>
          ))}
        </section>
      )}

      {inbox && inbox.outgoingRequests.length > 0 && (
        <section className="card">
          <h2 className="card-title">Waiting for follow-back</h2>
          {inbox.outgoingRequests.map((item) => (
            <div key={item.id} className="server-row">
              <div>
                <Link to={`/users/${item.target.username}`}>{item.target.displayName}</Link>
                <div className="card-meta">in {item.forum.name}</div>
              </div>
              <button
                type="button"
                className="btn-ghost"
                onClick={() => {
                  void api.cancelDirectMessageRequest(item.id).then(loadInbox);
                }}
              >
                Cancel
              </button>
            </div>
          ))}
        </section>
      )}

      <div className="grid-2">
        <section className="card">
          <h2 className="card-title">Conversations</h2>
          {inbox && inbox.conversations.length === 0 && (
            <p className="card-meta">No DMs yet. Request a follow-back from someone&apos;s profile.</p>
          )}
          {inbox?.conversations.map((item) => (
            <Link
              key={item.id}
              to={`/messages/${item.id}`}
              className="server-row"
              style={{ textDecoration: 'none', color: 'inherit' }}
            >
              <div>
                <strong>{item.peer.displayName}</strong>
                <div className="card-meta">{item.lastMessagePreview ?? 'No messages yet'}</div>
              </div>
            </Link>
          ))}
        </section>

        {conversation && (
          <section className="card">
            <h2 className="card-title">
              <Link to={`/users/${conversation.peer.username}`}>{conversation.peer.displayName}</Link>
            </h2>
            <div style={{ display: 'flex', flexDirection: 'column', gap: '0.75rem', marginBottom: '1rem' }}>
              {conversation.messages.map((message) => (
                <div key={message.id}>
                  <div className="card-meta">
                    {message.author.username} · {new Date(message.createdAt).toLocaleString()}
                  </div>
                  <div>{message.body}</div>
                </div>
              ))}
            </div>
            <form onSubmit={send}>
              <div className="form-group">
                <label htmlFor="dm-draft">Message</label>
                <textarea
                  id="dm-draft"
                  value={draft}
                  onChange={(event) => setDraft(event.target.value)}
                  rows={3}
                  required
                />
              </div>
              <button type="submit" className="btn-primary" disabled={busy}>
                Send
              </button>
            </form>
          </section>
        )}
      </div>

      <section className="card">
        <h2 className="card-title">Requests from forums</h2>
        <p className="card-meta">
          Ignore hides follow-back requests from that forum. Reject tells the sender you are not
          accepting requests from it. Accept is the default.
        </p>
        {(!preferences || preferences.forums.length === 0) && (
          <p className="card-meta">Join a forum or receive a request to configure this list.</p>
        )}
        {preferences?.forums.map((item) => (
          <div key={item.forum.id} className="server-row">
            <div>
              <strong>{item.forum.name}</strong>
              <div className="card-meta">
                {item.forum.parentSlug ? `${item.forum.parentSlug} / ${item.forum.slug}` : item.forum.slug}
              </div>
            </div>
            <label>
              <span className="card-meta">Handling</span>
              <select
                value={item.mode === 'ignore' || item.mode === 'reject' ? item.mode : 'none'}
                disabled={busy}
                onChange={(event) => void setForumMode(item.forum.id, event.target.value)}
              >
                <option value="none">Accept</option>
                <option value="ignore">Ignore</option>
                <option value="reject">Reject</option>
              </select>
            </label>
          </div>
        ))}
      </section>
    </div>
  );
}

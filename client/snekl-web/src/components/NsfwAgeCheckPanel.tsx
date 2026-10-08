import { type FormEvent, useState } from 'react';
import { Link } from 'react-router';
import type { Forum, NsfwAgeCheckStatus } from '../types';

type AgeCheckClient = {
  submitNsfwAgeCheck: (input: { dateOfBirth: string; forumId?: string }) => Promise<NsfwAgeCheckStatus>;
};

export function forumAgeStatus(forum: Forum): NsfwAgeCheckStatus {
  return {
    enforced: Boolean(forum.nsfwAgeCheckEnforced) || Boolean(forum.nsfwAgeCheckBlocksEntry),
    requiredByServer: Boolean(forum.nsfwAgeCheckRequiredByServer),
    requiredByForum: Boolean(forum.nsfwAgeCheckRequired),
    minimumAge: forum.nsfwMinimumAge ?? 18,
    viewerAttested: Boolean(forum.viewerNsfwAgeAttested),
    forumExplanation: forum.nsfwAgeCheckGateExplanation ?? forum.nsfwAgeCheckExplanation ?? null,
    blocksEntry: Boolean(forum.nsfwAgeCheckBlocksEntry),
  };
}

export function NsfwAgeCheckPanel({
  client,
  forumId,
  status,
  loggedIn,
  onCompleted,
  showIfComplete = false,
  pendingNsfw = false,
}: {
  client: AgeCheckClient;
  forumId?: string;
  status: NsfwAgeCheckStatus;
  loggedIn: boolean;
  onCompleted: (next: NsfwAgeCheckStatus) => void;
  showIfComplete?: boolean;
  pendingNsfw?: boolean;
}) {
  const [dateOfBirth, setDateOfBirth] = useState('');
  const [confirmed, setConfirmed] = useState(false);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  if (status.viewerAttested) {
    if (!showIfComplete) return null;
    return (
      <section className="card nsfw-age-card">
        <h2 className="card-title">Age check</h2>
        <p className="card-meta">
          You confirmed you are at least {status.minimumAge} on this server.
        </p>
      </section>
    );
  }

  if (!status.enforced && !pendingNsfw && !status.blocksEntry) return null;

  const source = status.blocksEntry
    ? 'Confirm your age before entering or joining this nest.'
    : status.requiredByServer
      ? 'This server requires an age check before NSFW images.'
      : 'NSFW images stay covered until you confirm you meet the minimum age.';

  if (!loggedIn) {
    return (
      <section className="card nsfw-age-card">
        <h2 className="card-title">NSFW age check</h2>
        <p className="card-meta">{source}</p>
        {status.forumExplanation && <p>{status.forumExplanation}</p>}
        <p>
          <Link to="/login">Log in</Link> and confirm you are {status.minimumAge}+ to continue.
        </p>
      </section>
    );
  }

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    if (!confirmed) {
      setError('Confirm that the date is yours and that you meet the minimum age.');
      return;
    }
    setBusy(true);
    try {
      const next = await client.submitNsfwAgeCheck({
        dateOfBirth,
        forumId,
      });
      onCompleted(next);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Age check failed');
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="card nsfw-age-card">
      <h2 className="card-title">Confirm your age</h2>
      <p className="card-meta">
        {source} Your date of birth is used only to verify age and is not stored.
      </p>
      {status.forumExplanation && <p>{status.forumExplanation}</p>}
      {error && <div className="error-banner">{error}</div>}
      <form onSubmit={(e) => void submit(e)}>
        <div className="form-group">
          <label htmlFor="nsfw-age-dob">Date of birth</label>
          <input
            id="nsfw-age-dob"
            type="date"
            required
            value={dateOfBirth}
            onChange={(e) => setDateOfBirth(e.target.value)}
          />
        </div>
        <div className="form-group">
          <label>
            <input type="checkbox" checked={confirmed} onChange={(e) => setConfirmed(e.target.checked)} /> I
            confirm I am at least {status.minimumAge} years old.
          </label>
        </div>
        <button type="submit" className="btn-primary" disabled={busy}>
          {busy ? 'Checking…' : `Confirm I am ${status.minimumAge}+`}
        </button>
      </form>
    </section>
  );
}

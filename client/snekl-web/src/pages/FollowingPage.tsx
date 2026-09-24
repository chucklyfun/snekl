import { Link } from 'react-router';
import { useAuth } from '../AuthContext';
import { useFollows } from '../FollowsContext';

export function FollowingPage() {
  const { user } = useAuth();
  const { follows, loading, unfollow } = useFollows();

  if (!user) {
    return (
      <div className="card">
        <p>
          Please <Link to="/login">log in</Link> to manage who you follow.
        </p>
      </div>
    );
  }

  return (
    <div>
      <h1 className="page-title">Following</h1>
      <p className="page-subtitle">
        One-way follows live on your Account with federations and forum favorites. A Following feed
        can use this list later, including watercooler posts from people you follow.
      </p>

      <div className="card">
        {loading && <p className="card-meta">Loading...</p>}
        {!loading && follows.length === 0 && (
          <p className="card-meta">You are not following anyone yet. Open a profile to follow them.</p>
        )}
        {follows.map((person) => (
          <div key={person.did} className="server-row">
            <div>
              <strong>{person.displayName || person.username || person.did}</strong>
              <div className="card-meta">
                {person.username ? (
                  <Link to={`/users/${person.username}`}>{person.username}</Link>
                ) : null}
                {person.username ? ' · ' : null}
                <code>{person.did}</code>
              </div>
            </div>
            <div className="server-row-actions">
              <button type="button" className="btn-ghost" onClick={() => void unfollow(person.did)}>
                Unfollow
              </button>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

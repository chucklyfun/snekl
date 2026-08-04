import { useEffect, useState } from 'react';
import { Link } from 'react-router';
import { createContentClient } from '../api';
import { useAuth } from '../AuthContext';

interface UpvoteButtonProps {
  postId: string;
  baseUrl: string;
  upvoteCount: number;
  viewerHasUpvoted: boolean;
  onChange?: (upvoteCount: number, viewerHasUpvoted: boolean) => void;
  compact?: boolean;
}

export function UpvoteButton({
  postId,
  baseUrl,
  upvoteCount,
  viewerHasUpvoted,
  onChange,
  compact = false,
}: UpvoteButtonProps) {
  const { user } = useAuth();
  const [count, setCount] = useState(upvoteCount);
  const [upvoted, setUpvoted] = useState(viewerHasUpvoted);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    setCount(upvoteCount);
    setUpvoted(viewerHasUpvoted);
  }, [upvoteCount, viewerHasUpvoted]);

  const toggle = async () => {
    if (busy) return;
    setBusy(true);
    try {
      const client = createContentClient(baseUrl);
      const result = await client.toggleUpvote(postId);
      setCount(result.upvoteCount);
      setUpvoted(result.viewerHasUpvoted);
      onChange?.(result.upvoteCount, result.viewerHasUpvoted);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className={`upvote-btn${upvoted ? ' upvote-btn-active' : ''}${compact ? ' upvote-btn-compact' : ''}`}>
      {user ? (
        <button
          type="button"
          className="upvote-btn-inner"
          onClick={(e) => {
            e.preventDefault();
            e.stopPropagation();
            void toggle();
          }}
          disabled={busy}
          aria-pressed={upvoted}
          aria-label={upvoted ? 'Remove upvote' : 'Upvote'}
        >
          <span className="upvote-arrow" aria-hidden="true">
            ▲
          </span>
          <span className="upvote-count">{count}</span>
        </button>
      ) : (
        <Link to="/login" className="upvote-btn-inner" title="Log in to upvote" onClick={(e) => e.stopPropagation()}>
          <span className="upvote-arrow" aria-hidden="true">
            ▲
          </span>
          <span className="upvote-count">{count}</span>
        </Link>
      )}
    </div>
  );
}

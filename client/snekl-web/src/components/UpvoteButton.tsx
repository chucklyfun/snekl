import { useEffect, useState } from 'react';
import { Link } from 'react-router';
import { createContentClient } from '../api';
import { useAuth } from '../AuthContext';
import type { UserPresentation } from '../types';

interface UpvoteButtonProps {
  postId: string;
  baseUrl: string;
  upvoteCount: number;
  viewerHasUpvoted: boolean;
  author?: UserPresentation;
  canUpvote?: boolean;
  onChange?: (upvoteCount: number, viewerHasUpvoted: boolean) => void;
  compact?: boolean;
}

function isOwnPost(user: UserPresentation | null, author?: UserPresentation): boolean {
  if (!user || !author) return false;
  if (user.did && author.did && user.did === author.did) return true;
  return user.id === author.id;
}

export function UpvoteButton({
  postId,
  baseUrl,
  upvoteCount,
  viewerHasUpvoted,
  author,
  canUpvote,
  onChange,
  compact = false,
}: UpvoteButtonProps) {
  const { user } = useAuth();
  const [count, setCount] = useState(upvoteCount);
  const [upvoted, setUpvoted] = useState(viewerHasUpvoted);
  const [busy, setBusy] = useState(false);
  const ownPost = isOwnPost(user, author) || (user != null && canUpvote === false);
  const blocked = ownPost && !upvoted;

  useEffect(() => {
    setCount(upvoteCount);
    setUpvoted(viewerHasUpvoted);
  }, [upvoteCount, viewerHasUpvoted]);

  const toggle = async () => {
    if (busy || blocked) return;
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
    <div
      className={`upvote-btn${upvoted ? ' upvote-btn-active' : ''}${compact ? ' upvote-btn-compact' : ''}${blocked ? ' upvote-btn-own' : ''}`}
    >
      {user ? (
        <button
          type="button"
          className="upvote-btn-inner"
          onClick={(e) => {
            e.preventDefault();
            e.stopPropagation();
            void toggle();
          }}
          disabled={busy || blocked}
          title={blocked ? "You can't upvote your own post" : undefined}
          aria-pressed={upvoted}
          aria-label={blocked ? "You can't upvote your own post" : upvoted ? 'Remove upvote' : 'Upvote'}
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

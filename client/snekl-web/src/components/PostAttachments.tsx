import { useEffect, useState } from 'react';
import { createContentClient } from '../api';
import type { MediaAttachment, Post } from '../types';

type PostAttachmentsProps = {
  post: Post;
  baseUrl: string;
  onChanged: () => void;
};

export function PostAttachments({ post, baseUrl, onChanged }: PostAttachmentsProps) {
  const attachments = post.attachments ?? [];
  if (attachments.length === 0) return null;

  return (
    <div className="post-attachments">
      {attachments.map((att) => (
        <AttachmentCard
          key={att.id}
          postId={post.id}
          attachment={att}
          baseUrl={baseUrl}
          canMark={Boolean(post.viewerActions?.canMarkNsfwMedia)}
          onChanged={onChanged}
        />
      ))}
    </div>
  );
}

function AttachmentCard({
  postId,
  attachment,
  baseUrl,
  canMark,
  onChanged,
}: {
  postId: string;
  attachment: MediaAttachment;
  baseUrl: string;
  canMark: boolean;
  onChanged: () => void;
}) {
  const [revealedUrl, setRevealedUrl] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [coverDraft, setCoverDraft] = useState(attachment.coverText ?? '');
  const [editingCover, setEditingCover] = useState(false);

  useEffect(() => {
    setCoverDraft(attachment.coverText ?? '');
  }, [attachment.coverText]);

  useEffect(() => {
    return () => {
      if (revealedUrl) URL.revokeObjectURL(revealedUrl);
    };
  }, [revealedUrl]);

  const reveal = async () => {
    if (!attachment.url) return;
    setBusy(true);
    setError('');
    try {
      const next = await createContentClient(baseUrl).fetchMediaBlobUrl(attachment.id);
      setRevealedUrl((prev) => {
        if (prev) URL.revokeObjectURL(prev);
        return next;
      });
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not open image');
    } finally {
      setBusy(false);
    }
  };

  const saveNsfw = async (isNsfw: boolean) => {
    setBusy(true);
    setError('');
    try {
      await createContentClient(baseUrl).setMediaNsfw(postId, attachment.id, {
        isNsfw,
        coverText: isNsfw ? coverDraft.trim() : null,
      });
      setEditingCover(false);
      onChanged();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not update NSFW mark');
    } finally {
      setBusy(false);
    }
  };

  const coverForm = canMark && editingCover && (
    <div className="nsfw-mark-form">
      <label>
        Cover sentence
        <input
          value={coverDraft}
          onChange={(e) => setCoverDraft(e.target.value)}
          maxLength={140}
          required
        />
      </label>
      <button
        type="button"
        className="btn-primary"
        disabled={busy || !coverDraft.trim()}
        onClick={() => void saveNsfw(true)}
      >
        Save cover
      </button>
      <button
        type="button"
        className="btn-ghost"
        onClick={() => {
          setCoverDraft(attachment.coverText ?? '');
          setEditingCover(false);
        }}
        disabled={busy}
      >
        Cancel
      </button>
    </div>
  );

  if (attachment.isNsfw && !revealedUrl) {
    return (
      <div className="post-attachment nsfw-attachment">
        {attachment.censorUrl ? (
          <img src={attachment.censorUrl} alt="" className="nsfw-censor" />
        ) : (
          <div className="nsfw-censor nsfw-censor-empty" />
        )}
        <div className="nsfw-veil">
          <p className="nsfw-cover">{attachment.coverText || 'Hidden image'}</p>
          {attachment.url ? (
            <button type="button" className="btn-primary" onClick={() => void reveal()} disabled={busy}>
              {busy ? 'Opening…' : 'Show original'}
            </button>
          ) : (
            <p className="card-meta">Confirm your age to view this image.</p>
          )}
          {canMark && !editingCover && (
            <>
              <button type="button" className="btn-ghost" onClick={() => setEditingCover(true)} disabled={busy}>
                Edit cover
              </button>
              <button type="button" className="btn-ghost" onClick={() => void saveNsfw(false)} disabled={busy}>
                Remove NSFW mark
              </button>
            </>
          )}
          {coverForm}
        </div>
        {error && <div className="error-banner">{error}</div>}
      </div>
    );
  }

  const src = revealedUrl ?? attachment.url;
  if (!src) return null;

  return (
    <div className="post-attachment">
      <a href={src} target="_blank" rel="noreferrer">
        <img src={src} alt={attachment.coverText ?? ''} loading="lazy" />
      </a>
      {canMark && !editingCover && (
        <button type="button" className="btn-ghost nsfw-mark-btn" onClick={() => setEditingCover(true)}>
          {attachment.isNsfw ? 'Edit cover' : 'Mark NSFW'}
        </button>
      )}
      {attachment.isNsfw && canMark && !editingCover && (
        <button type="button" className="btn-ghost nsfw-mark-btn" onClick={() => void saveNsfw(false)} disabled={busy}>
          Remove NSFW mark
        </button>
      )}
      {coverForm}
      {error && <div className="error-banner">{error}</div>}
    </div>
  );
}

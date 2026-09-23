import { type FormEvent, useEffect, useId, useRef, useState } from 'react';
import { insertEmoteToken, catalogEmoteShortcode, type CatalogEmote, type EmoteHome } from '../emotes';
import { imageFilesFromDataTransfer, imageFilesFromList } from '../imageFiles';
import type { MediaAttachment, Post } from '../types';
import { EmoteAutocomplete } from './EmoteAutocomplete';
import { EmotePicker } from './EmotePicker';
import { ImageUploadField } from './ImageUploadField';

type CreatePostDialogProps = {
  open: boolean;
  forumName: string;
  busy?: boolean;
  error?: string;
  onClose: () => void;
  onSubmit: (input: {
    title: string;
    message: string;
    mediaAssetIds: string[];
    nsfwMedia: { mediaAssetId: string; coverText: string }[];
    replyPolicy: 'everyone' | 'following';
  }) => Promise<Post | void>;
  uploadMedia: (file: File) => Promise<MediaAttachment>;
  emotes?: CatalogEmote[];
  home?: EmoteHome;
  manageHref?: string;
};

type PendingImage = {
  localId: string;
  file: File;
  previewUrl: string;
  asset?: MediaAttachment;
  uploading: boolean;
  error?: string;
  nsfw: boolean;
  coverText: string;
};

export function CreatePostDialog({
  open,
  forumName,
  busy = false,
  error = '',
  onClose,
  onSubmit,
  uploadMedia,
  emotes = [],
  home,
  manageHref,
}: CreatePostDialogProps) {
  const titleId = useId();
  const dialogRef = useRef<HTMLDivElement>(null);
  const messageRef = useRef<HTMLTextAreaElement>(null);
  const [title, setTitle] = useState('');
  const [message, setMessage] = useState('');
  const [images, setImages] = useState<PendingImage[]>([]);
  const [localError, setLocalError] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [repliesFollowingOnly, setRepliesFollowingOnly] = useState(false);
  const emoteMenuOpen = useRef(false);

  useEffect(() => {
    if (!open) return;
    setTitle('');
    setMessage('');
    setLocalError('');
    setRepliesFollowingOnly(false);
    setImages((prev) => {
      for (const img of prev) URL.revokeObjectURL(img.previewUrl);
      return [];
    });

    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && !submitting && !busy && !emoteMenuOpen.current) onClose();
    };
    window.addEventListener('keydown', onKey);
    queueMicrotask(() => dialogRef.current?.querySelector<HTMLElement>('input,textarea,button')?.focus());
    return () => window.removeEventListener('keydown', onKey);
  }, [open, onClose, submitting, busy]);

  useEffect(() => {
    return () => {
      setImages((prev) => {
        for (const img of prev) URL.revokeObjectURL(img.previewUrl);
        return prev;
      });
    };
  }, []);

  if (!open) return null;

  const anyUploading = images.some((img) => img.uploading);
  const uploadFailed = images.some((img) => img.error);

  const handleFiles = async (incoming: File[]) => {
    if (incoming.length === 0) return;
    setLocalError('');
    const selected = imageFilesFromList(incoming);
    if (selected.length === 0) {
      setLocalError('Only image files are supported.');
      return;
    }

    const pending: PendingImage[] = selected.map((file) => ({
      localId: crypto.randomUUID(),
      file,
      previewUrl: URL.createObjectURL(file),
      uploading: true,
      nsfw: false,
      coverText: '',
    }));

    setImages((prev) => [...prev, ...pending]);

    for (const item of pending) {
      try {
        const asset = await uploadMedia(item.file);
        setImages((prev) =>
          prev.map((img) =>
            img.localId === item.localId ? { ...img, asset, uploading: false } : img,
          ),
        );
      } catch (err) {
        setImages((prev) =>
          prev.map((img) =>
            img.localId === item.localId
              ? {
                  ...img,
                  uploading: false,
                  error: err instanceof Error ? err.message : 'Upload failed',
                }
              : img,
          ),
        );
      }
    }
  };

  const removeImage = (localId: string) => {
    setImages((prev) => {
      const target = prev.find((img) => img.localId === localId);
      if (target) URL.revokeObjectURL(target.previewUrl);
      return prev.filter((img) => img.localId !== localId);
    });
  };

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    if (!title.trim() || !message.trim() || anyUploading || uploadFailed) return;
    const nsfwMissingCover = images.some((img) => img.nsfw && !img.coverText.trim());
    if (nsfwMissingCover) {
      setLocalError('NSFW images need a cover sentence.');
      return;
    }
    setLocalError('');
    setSubmitting(true);
    try {
      await onSubmit({
        title: title.trim(),
        message: message.trim(),
        mediaAssetIds: images.filter((img) => img.asset).map((img) => img.asset!.id),
        nsfwMedia: images
          .filter((img) => img.asset && img.nsfw)
          .map((img) => ({ mediaAssetId: img.asset!.id, coverText: img.coverText.trim() })),
        replyPolicy: repliesFollowingOnly ? 'following' : 'everyone',
      });
    } catch (err) {
      setLocalError(err instanceof Error ? err.message : 'Failed to create post');
    } finally {
      setSubmitting(false);
    }
  };

  const disabled = submitting || busy || anyUploading || uploadFailed;

  return (
    <div
      className="modal-overlay"
      role="presentation"
      onMouseDown={(e) => {
        if (e.target === e.currentTarget && !disabled) onClose();
      }}
    >
      <div
        className="modal-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        ref={dialogRef}
      >
        <div className="modal-header">
          <h2 id={titleId} className="card-title" style={{ margin: 0 }}>
            Create a post
          </h2>
          <button type="button" className="btn-ghost" onClick={onClose} disabled={disabled} aria-label="Close">
            Close
          </button>
        </div>
        <p className="card-meta" style={{ marginTop: 0 }}>
          Posting in {forumName}
        </p>

        {(error || localError) && <div className="error-banner">{error || localError}</div>}

        <form
          onSubmit={handleSubmit}
          onDragOver={(e) => e.preventDefault()}
          onDrop={(e) => {
            const files = imageFilesFromDataTransfer(e.dataTransfer);
            if (files.length === 0) return;
            e.preventDefault();
            void handleFiles(files);
          }}
          onPaste={(e) => {
            const files = imageFilesFromDataTransfer(e.clipboardData);
            if (files.length === 0) return;
            e.preventDefault();
            void handleFiles(files);
          }}
        >
          <div className="form-group">
            <label htmlFor="create-post-title">Thread title</label>
            <input
              id="create-post-title"
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              placeholder="Give your thread a clear title"
              maxLength={200}
              required
              disabled={disabled}
            />
          </div>
          <div className="form-group">
            <label htmlFor="create-post-body">Original post</label>
            <EmoteAutocomplete
              emotes={emotes}
              home={home}
              value={message}
              onChange={setMessage}
              disabled={disabled}
              onMenuOpenChange={(openMenu) => {
                emoteMenuOpen.current = openMenu;
              }}
            >
              {(bind) => (
                <textarea
                  id="create-post-body"
                  ref={(el) => {
                    bind.ref(el);
                    messageRef.current = el;
                  }}
                  value={message}
                  onChange={bind.onChange}
                  onKeyDown={bind.onKeyDown}
                  onSelect={bind.onSelect}
                  onClick={bind.onClick}
                  onBlur={bind.onBlur}
                  placeholder="Write the opening post… Type : for emotes or use the picker"
                  required
                  disabled={disabled}
                  rows={6}
                />
              )}
            </EmoteAutocomplete>
            <EmotePicker
              emotes={emotes}
              home={home}
              disabled={disabled}
              manageHref={manageHref}
              onPickEmote={(emote) => {
                const el = messageRef.current;
                const start = el?.selectionStart ?? message.length;
                const end = el?.selectionEnd ?? message.length;
                const token = emote.imageUrl
                  ? catalogEmoteShortcode(emote, emotes, home)
                  : emote.skinsUnicode ?? catalogEmoteShortcode(emote, emotes, home);
                const { next, caret } = insertEmoteToken(message, start, end, token);
                setMessage(next);
                queueMicrotask(() => {
                  el?.focus();
                  el?.setSelectionRange(caret, caret);
                });
              }}
            />
          </div>

          <div className="form-group">
            <label htmlFor="create-post-images">Images</label>
            <ImageUploadField
              id="create-post-images"
              multiple
              disabled={disabled}
              onFiles={(files) => void handleFiles(files)}
            />
            {images.length > 0 && (
              <div className="create-post-previews">
                {images.map((img) => (
                  <div key={img.localId} className="create-post-preview">
                    <img src={img.previewUrl} alt="" />
                    {img.uploading && <span className="create-post-preview-status">Uploading…</span>}
                    {img.error && <span className="create-post-preview-status create-post-preview-error">{img.error}</span>}
                    <button
                      type="button"
                      className="btn-ghost create-post-preview-remove"
                      onClick={() => removeImage(img.localId)}
                      disabled={submitting || busy}
                    >
                      Remove
                    </button>
                    <label className="create-post-nsfw">
                      <input
                        type="checkbox"
                        checked={img.nsfw}
                        disabled={submitting || busy}
                        onChange={(e) =>
                          setImages((prev) =>
                            prev.map((item) =>
                              item.localId === img.localId ? { ...item, nsfw: e.target.checked } : item,
                            ),
                          )
                        }
                      />
                      NSFW
                    </label>
                    {img.nsfw && (
                      <input
                        className="create-post-cover"
                        value={img.coverText}
                        maxLength={140}
                        placeholder="Cover sentence"
                        disabled={submitting || busy}
                        onChange={(e) =>
                          setImages((prev) =>
                            prev.map((item) =>
                              item.localId === img.localId ? { ...item, coverText: e.target.value } : item,
                            ),
                          )
                        }
                      />
                    )}
                  </div>
                ))}
              </div>
            )}
          </div>

          <div className="form-group">
            <label className="create-post-nsfw">
              <input
                type="checkbox"
                checked={repliesFollowingOnly}
                disabled={disabled}
                onChange={(e) => setRepliesFollowingOnly(e.target.checked)}
              />
              Only people you follow can reply
            </label>
          </div>

          <div className="modal-actions">
            <button type="button" className="btn-ghost" onClick={onClose} disabled={disabled}>
              Cancel
            </button>
            <button type="submit" className="btn-primary" disabled={disabled || !title.trim() || !message.trim()}>
              {submitting || busy ? 'Posting…' : 'Post thread'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

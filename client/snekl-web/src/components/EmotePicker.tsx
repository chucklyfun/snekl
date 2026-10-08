import { useEffect, useId, useMemo, useRef, useState } from 'react';
import { Link } from 'react-router';
import { catalogEmoteLabel, selectorEmotes, type CatalogEmote, type EmoteHome } from '../emotes';

export function EmotePicker({
  emotes,
  home,
  disabled,
  onPickEmote,
  label = '😊 Emotes',
  manageHref,
}: {
  emotes: CatalogEmote[];
  home?: EmoteHome;
  disabled?: boolean;
  onPickEmote: (emote: CatalogEmote) => void;
  label?: string;
  manageHref?: string;
}) {
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);
  const titleId = useId();
  const custom = useMemo(
    () => selectorEmotes(emotes ?? [], home?.forumId),
    [emotes, home?.forumId],
  );

  useEffect(() => {
    if (!open) return;
    const onPointer = (e: PointerEvent) => {
      if (!rootRef.current?.contains(e.target as Node)) setOpen(false);
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false);
    };
    window.addEventListener('pointerdown', onPointer);
    window.addEventListener('keydown', onKey);
    return () => {
      window.removeEventListener('pointerdown', onPointer);
      window.removeEventListener('keydown', onKey);
    };
  }, [open]);

  return (
    <div className="emote-picker" ref={rootRef}>
      <button
        type="button"
        className="btn-ghost emote-picker-toggle"
        disabled={disabled}
        aria-expanded={open}
        aria-haspopup="dialog"
        aria-label="Choose an emote"
        onClick={() => setOpen((value) => !value)}
      >
        {label}
      </button>
      {open && (
        <div className="emote-picker-panel" role="dialog" aria-labelledby={titleId}>
          <p id={titleId} className="card-meta" style={{ marginTop: 0 }}>
            Custom reactions
          </p>
          {custom.length > 0 ? (
            <div className="emote-picker-grid">
              {custom.map((emote) => (
                <button
                  key={emote.id}
                  type="button"
                  className="emote-picker-item"
                  title={catalogEmoteLabel(emote)}
                  onClick={() => {
                    onPickEmote(emote);
                    setOpen(false);
                  }}
                >
                  {emote.imageUrl ? (
                    <img src={emote.imageUrl} alt={emote.name} />
                  ) : (
                    <span>{emote.skinsUnicode ?? catalogEmoteLabel(emote)}</span>
                  )}
                </button>
              ))}
            </div>
          ) : (
            <p className="card-meta">
              No custom reactions yet.{' '}
              {manageHref ? (
                <Link to={manageHref} onClick={() => setOpen(false)}>
                  Add them in settings
                </Link>
              ) : (
                <>Nest managers add them under Settings → Emotes.</>
              )}
            </p>
          )}
        </div>
      )}
    </div>
  );
}

import { useEffect, useRef, useState, type CSSProperties, type ReactNode } from 'react';
import type { ListAnnotation, Overlay } from '../types';
import './UserPresentation.css';

export function UserAvatar({
  presentation,
  size = 40,
}: {
  presentation: { effectiveDisplayName: string; avatarOverlay?: Overlay | null; annotations: ListAnnotation[] };
  size?: number;
}) {
  const overlay = presentation.avatarOverlay;
  const initial = presentation.effectiveDisplayName.charAt(0).toUpperCase();
  const ringClass = overlay?.kind === 'PulseRing' ? 'avatar-ring avatar-ring-pulse' : overlay?.kind === 'Ring' ? 'avatar-ring' : '';

  return (
    <div className="avatar-wrap" style={{ width: size, height: size }}>
      <div
        className={`avatar ${ringClass}`}
        style={{
          width: size,
          height: size,
          borderColor: overlay?.color ?? undefined,
          fontSize: size * 0.42,
        }}
        aria-label={overlay?.ariaLabel ?? presentation.effectiveDisplayName}
      >
        {initial}
      </div>
      {overlay?.kind === 'Corner' || overlay?.kind === 'Strip' ? (
        <span className="avatar-corner" style={{ background: overlay.color ?? 'var(--accent)' }}>
          {overlay.emoji ?? '●'}
        </span>
      ) : null}
      {presentation.annotations.slice(0, 1).map((a) =>
        a.avatarOverlay && !overlay ? (
          <span key={a.listId} className="avatar-corner avatar-corner-list" title={a.tooltipText}>
            {a.flairEmoji ?? '★'}
          </span>
        ) : null,
      )}
    </div>
  );
}

export function FlairBadge({ emoji, iconUrl, title }: { emoji?: string | null; iconUrl?: string | null; title?: string }) {
  if (!emoji && !iconUrl) return null;
  return (
    <span className="flair-badge" title={title}>
      {iconUrl ? <img src={iconUrl} alt="" width={16} height={16} /> : emoji}
    </span>
  );
}

export function AnnotationTooltip({ annotation, children }: { annotation: ListAnnotation; children: ReactNode }) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLSpanElement>(null);

  useEffect(() => {
    if (!open) return;
    const close = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener('click', close);
    return () => document.removeEventListener('click', close);
  }, [open]);

  return (
    <span
      ref={ref}
      className={`annotation-badge severity-${annotation.severity}`}
      onMouseEnter={() => setOpen(true)}
      onMouseLeave={() => setOpen(false)}
      onFocus={() => setOpen(true)}
      onBlur={() => setOpen(false)}
      tabIndex={0}
      role="button"
    >
      {children}
      {open && (
        <span className="annotation-popover" role="tooltip">
          <strong>{annotation.listName}</strong>
          <span>{annotation.tooltipText}</span>
          <small>Severity {annotation.severity}</small>
        </span>
      )}
    </span>
  );
}

export function UserByline({
  presentation,
  timestamp,
  showStatus = true,
}: {
  presentation: {
    effectiveDisplayName: string;
    username: string;
    effectiveNameColor?: string | null;
    effectiveNameStyle?: string | null;
    flair?: { emoji?: string | null; iconUrl?: string | null; label?: string | null } | null;
    activeStatus?: {
      message?: string | null;
      emoji?: string | null;
      linkUrl?: string | null;
      expiresAt: string;
    } | null;
    annotations: ListAnnotation[];
    isFederated: boolean;
    avatarOverlay?: Overlay | null;
  };
  timestamp?: string;
  showStatus?: boolean;
}) {
  const nameStyle: CSSProperties = {
    color: presentation.effectiveNameColor ?? undefined,
    fontWeight: presentation.effectiveNameStyle?.includes('bold') ? 700 : undefined,
    fontStyle: presentation.effectiveNameStyle?.includes('italic') ? 'italic' : undefined,
  };

  const status = presentation.activeStatus;
  const statusLine = status?.message || status?.emoji;

  return (
    <div className="user-byline">
      <div className="byline-row">
        <UserAvatar presentation={presentation} size={36} />
        <div className="byline-text">
          <div className="byline-names">
            <span className="display-name" style={nameStyle}>
              {presentation.effectiveDisplayName}
            </span>
            {presentation.flair ? (
              <FlairBadge
                emoji={presentation.flair.emoji}
                iconUrl={presentation.flair.iconUrl}
                title={presentation.flair.label ?? 'User flair'}
              />
            ) : null}
            {presentation.annotations.map((a) => (
              <AnnotationTooltip key={a.listId} annotation={a}>
                <FlairBadge emoji={a.flairEmoji} iconUrl={a.flairIconUrl} />
              </AnnotationTooltip>
            ))}
            {presentation.isFederated ? <span className="badge badge-federated">federated</span> : null}
          </div>
          <div className="byline-meta">
            @{presentation.username}
            {timestamp ? <> · {new Date(timestamp).toLocaleString()}</> : null}
          </div>
          {showStatus && statusLine ? (
            <div className="status-line">
              {status?.emoji ? <span>{status.emoji} </span> : null}
              {status?.linkUrl ? (
                <a href={status.linkUrl} target="_blank" rel="noreferrer">
                  {statusLine}
                </a>
              ) : (
                <span>{statusLine}</span>
              )}
              {status ? (
                <span className="status-expiry"> · until {new Date(status.expiresAt).toLocaleString()}</span>
              ) : null}
            </div>
          ) : null}
        </div>
      </div>
    </div>
  );
}

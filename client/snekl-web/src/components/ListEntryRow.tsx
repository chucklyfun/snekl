import { useEffect, useState } from 'react';
import { badgeIconUrl, type CatalogEmote } from '../emotes';
import type { ListEntry } from '../types';
import { EmoteAutocomplete, EmoteGlyph } from './EmoteAutocomplete';
import { IconButton } from './IconButton';
import { InlineEditRow } from './InlineEditRow';

export type EntryDraft = { username: string; label: string; tooltip: string; emoji: string };

export function iconForBadge(value: string, emotes: CatalogEmote[]): string | undefined {
  return badgeIconUrl(value, emotes);
}

export function ListEntryRow({
  entry,
  creating = false,
  defaultEmoji,
  emotes,
  canEdit = false,
  onSave,
  onRemove,
  onCancel,
  canIgnore = false,
  onIgnore,
}: {
  entry?: ListEntry;
  creating?: boolean;
  defaultEmoji?: string | null;
  emotes: CatalogEmote[];
  canEdit?: boolean;
  onSave?: (draft: EntryDraft, existing?: ListEntry) => Promise<void>;
  onRemove?: (entry: ListEntry) => void;
  onCancel?: () => void;
  canIgnore?: boolean;
  onIgnore?: (entry: ListEntry) => void;
}) {
  const [username, setUsername] = useState(entry?.targetUsername ?? '');
  const [label, setLabel] = useState(entry?.label ?? '');
  const [tooltip, setTooltip] = useState(entry?.tooltipText ?? '');
  const [emoji, setEmoji] = useState(entry?.flairEmoji ?? defaultEmoji ?? '★');
  const fieldId = entry?.id ?? 'new';

  useEffect(() => {
    if (creating || !entry) return;
    setUsername(entry.targetUsername);
    setLabel(entry.label);
    setTooltip(entry.tooltipText);
    setEmoji(entry.flairEmoji ?? defaultEmoji ?? '★');
  }, [creating, defaultEmoji, entry]);

  return (
    <InlineEditRow
      creating={creating}
      canEdit={canEdit}
      ignored={entry?.isIgnoredByViewer}
      view={
        entry ? (
          <span>
            <span className="flair-box list-flair-box">
              <EmoteGlyph value={entry.flairEmoji ?? defaultEmoji} emotes={emotes} />
            </span>{' '}
            <strong>@{entry.targetUsername}</strong> · {entry.label}
            <div className="card-meta">
              {entry.tooltipText}
              {entry.isIgnoredByViewer ? ' · hidden for you' : ''}
            </div>
          </span>
        ) : null
      }
      viewActions={
        canIgnore && entry ? (
          <IconButton
            icon={entry.isIgnoredByViewer ? 'eye' : 'eyeOff'}
            label={entry.isIgnoredByViewer ? 'Show' : 'Hide'}
            onClick={() => onIgnore?.(entry)}
          />
        ) : null
      }
      fieldsClassName={`list-browser-entry-fields${creating ? ' is-create' : ''}`}
      preface={!creating ? <div className="list-browser-entry-user">@{entry?.targetUsername}</div> : null}
      onSave={async () => {
        if (!onSave) return;
        await onSave({ username, label, tooltip, emoji }, entry);
      }}
      onCancel={() => {
        if (entry) {
          setUsername(entry.targetUsername);
          setLabel(entry.label);
          setTooltip(entry.tooltipText);
          setEmoji(entry.flairEmoji ?? defaultEmoji ?? '★');
        }
        onCancel?.();
      }}
      onRemove={entry ? () => onRemove?.(entry) : undefined}
    >
      {creating && (
        <div className="form-group">
          <label htmlFor={`entry-user-${fieldId}`}>Username</label>
          <input
            id={`entry-user-${fieldId}`}
            value={username}
            onChange={(e) => setUsername(e.target.value)}
            placeholder="@username"
            required
            autoFocus
          />
        </div>
      )}
      <div className="form-group">
        <label htmlFor={`entry-label-${fieldId}`}>Label</label>
        <input
          id={`entry-label-${fieldId}`}
          list="label-suggestions"
          value={label}
          onChange={(e) => setLabel(e.target.value)}
          required
          placeholder="Verified, Celebrity, Warning…"
        />
      </div>
      <div className="form-group list-browser-entry-badge">
        <label htmlFor={`entry-emoji-${fieldId}`}>Badge</label>
        <EmoteAutocomplete emotes={emotes} value={emoji} onChange={setEmoji} mode="field">
          {(bind) => (
            <input
              id={`entry-emoji-${fieldId}`}
              ref={bind.ref}
              value={emoji}
              onChange={bind.onChange}
              onKeyDown={bind.onKeyDown}
              onSelect={bind.onSelect}
              onClick={bind.onClick}
              onBlur={bind.onBlur}
              maxLength={40}
            />
          )}
        </EmoteAutocomplete>
      </div>
      <div className="form-group">
        <label htmlFor={`entry-tip-${fieldId}`}>Description</label>
        <input
          id={`entry-tip-${fieldId}`}
          value={tooltip}
          onChange={(e) => setTooltip(e.target.value)}
          required
          placeholder="Why this label applies"
        />
      </div>
    </InlineEditRow>
  );
}

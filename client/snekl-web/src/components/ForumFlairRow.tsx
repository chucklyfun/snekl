import { useEffect, useState } from 'react';
import type { ForumFlairTemplate } from '../types';
import { plainEmoteGlyph, type CatalogEmote } from '../emotes';
import { EmoteAutocomplete, EmoteGlyph } from './EmoteAutocomplete';
import { InlineEditRow } from './InlineEditRow';

export type FlairDraft = { emoji: string; label: string };

export function forumFlairLabel(flair: Pick<ForumFlairTemplate, 'emoji' | 'label'>) {
  return [flair.emoji, flair.label].filter(Boolean).join(' ') || 'Untitled flair';
}

/** Shortcode plus the Unicode glyph, for a native `<select>` that cannot render emote images. */
export function forumFlairSelectLabel(
  flair: Pick<ForumFlairTemplate, 'emoji' | 'label'>,
  emotes: CatalogEmote[] = [],
) {
  const stored = flair.emoji?.trim() ?? '';
  const glyph = plainEmoteGlyph(stored, emotes);
  const parts = [glyph && glyph !== stored ? glyph : null, stored, flair.label].filter(Boolean);
  return parts.join(' ') || 'Untitled flair';
}

export function ForumFlairRow({
  template,
  creating = false,
  emotes,
  canEdit = false,
  onSave,
  onRemove,
  onCancel,
}: {
  template?: ForumFlairTemplate;
  creating?: boolean;
  emotes: CatalogEmote[];
  canEdit?: boolean;
  onSave?: (draft: FlairDraft, existing?: ForumFlairTemplate) => Promise<void>;
  onRemove?: (template: ForumFlairTemplate) => void;
  onCancel?: () => void;
}) {
  const [emoji, setEmoji] = useState(template?.emoji ?? '★');
  const [label, setLabel] = useState(template?.label ?? '');
  const fieldId = template?.id ?? 'new';

  useEffect(() => {
    if (creating || !template) return;
    setEmoji(template.emoji ?? '★');
    setLabel(template.label ?? '');
  }, [creating, template]);

  return (
    <InlineEditRow
      creating={creating}
      canEdit={canEdit}
      view={
        template ? (
          <span>
            <span className="flair-box user-flair-box">
              <EmoteGlyph value={template.emoji} emotes={emotes} />
            </span>{' '}
            <strong>{template.label || 'Untitled flair'}</strong>
          </span>
        ) : null
      }
      fieldsClassName={`list-browser-entry-fields${creating ? ' is-create' : ''}`}
      onSave={async () => {
        if (!onSave) return;
        await onSave({ emoji, label }, template);
      }}
      onCancel={() => {
        if (template) {
          setEmoji(template.emoji ?? '★');
          setLabel(template.label ?? '');
        }
        onCancel?.();
      }}
      onRemove={template ? () => onRemove?.(template) : undefined}
    >
      <div className="form-group">
        <label htmlFor={`flair-label-${fieldId}`}>Label</label>
        <input
          id={`flair-label-${fieldId}`}
          value={label}
          onChange={(e) => setLabel(e.target.value)}
          placeholder="Moderator, OG…"
          autoFocus={creating}
        />
      </div>
      <div className="form-group list-browser-entry-badge">
        <label htmlFor={`flair-emoji-${fieldId}`}>Badge</label>
        <EmoteAutocomplete emotes={emotes} value={emoji} onChange={setEmoji} mode="field">
          {(bind) => (
            <input
              id={`flair-emoji-${fieldId}`}
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
    </InlineEditRow>
  );
}

import { type DragEvent, useId, useRef, useState } from 'react';
import {
  catalogEmoteLabel,
  defaultEmojiForCode,
  defaultHighlightSlots,
  EMOTE_CODE_PATTERN,
  emojiMatchKey,
  occupiesDefaultHighlightSlot,
  sortCatalogEmotes,
  storedEmoteCode,
  type CatalogEmote,
} from '../emotes';
import { imageFilesFromDataTransfer } from '../imageFiles';
import { ImageUploadField } from './ImageUploadField';
import { InlineEditRow } from './InlineEditRow';

export type EmoteWriteInput = {
  code?: string;
  name?: string;
  mediaAssetId?: string;
  skinsUnicode?: string | null;
  clearSkin?: boolean;
  highlighted?: boolean;
};

export function EmoteCatalogEditor({
  emotes,
  forumId,
  canManage,
  busy,
  onUpload,
  onCreate,
  onUpdate,
  onReorder,
  onDelete,
}: {
  emotes: CatalogEmote[];
  forumId?: string | null;
  canManage: boolean;
  busy?: boolean;
  onUpload: (file: File) => Promise<{ id: string }>;
  onCreate: (input: EmoteWriteInput & { code: string; mediaAssetId: string }) => Promise<void>;
  onUpdate?: (emoteId: string, input: EmoteWriteInput) => Promise<void>;
  onReorder?: (ids: string[]) => Promise<void>;
  onDelete: (emoteId: string) => Promise<void>;
}) {
  const [composing, setComposing] = useState(false);
  const [error, setError] = useState('');
  const dragId = useRef<string | null>(null);

  if (!canManage && emotes.length === 0) {
    return (
      <section id="emotes" className="card">
        <h2 className="card-title">Emotes</h2>
        <p className="card-meta">
          Custom images for :code: in posts and as reactions. You need ManageForum to add nest emotes,
          or ManageServerSettings on the server page for emotes that work everywhere.
        </p>
      </section>
    );
  }

  const toggleHighlighted = async (emote: CatalogEmote) => {
    if (!onUpdate) return;
    setError('');
    try {
      await onUpdate(emote.id, { highlighted: !emote.highlighted });
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not update highlighted reaction');
    }
  };

  const moveEmote = async (fromId: string, toId: string) => {
    if (!onReorder || fromId === toId) return;
    const ordered = sortCatalogEmotes(emotes);
    const from = ordered.findIndex((emote) => emote.id === fromId);
    const to = ordered.findIndex((emote) => emote.id === toId);
    if (from < 0 || to < 0) return;
    const next = [...ordered];
    const [item] = next.splice(from, 1);
    next.splice(to, 0, item);
    setError('');
    try {
      await onReorder(next.map((emote) => emote.id));
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not reorder emotes');
    }
  };

  const ordered = sortCatalogEmotes(emotes);
  const highlightIds = new Set(defaultHighlightSlots(emotes, forumId).map((emote) => emote.id));
  const highlights = ordered.filter((emote) => highlightIds.has(emote.id));
  const customs = ordered.filter((emote) => !highlightIds.has(emote.id));
  const canDrag = canManage && Boolean(onReorder) && !composing;

  const renderRow = (emote: CatalogEmote) => (
    <EmoteCatalogRow
      key={emote.id}
      emote={emote}
      canManage={canManage}
      catalogBusy={busy}
      canDrag={canDrag}
      isHighlight={occupiesDefaultHighlightSlot(emote, emotes, forumId)}
      onUpload={onUpload}
      onUpdate={onUpdate}
      onDelete={onDelete}
      onToggleHighlighted={() => void toggleHighlighted(emote)}
      onDragStart={(event: DragEvent<HTMLElement>) => {
        dragId.current = emote.id;
        event.dataTransfer.effectAllowed = 'move';
      }}
      onDragOver={(event: DragEvent<HTMLElement>) => {
        if (!dragId.current || dragId.current === emote.id) return;
        event.preventDefault();
        event.dataTransfer.dropEffect = 'move';
      }}
      onDrop={(event: DragEvent<HTMLElement>) => {
        event.preventDefault();
        const fromId = dragId.current;
        dragId.current = null;
        if (fromId) void moveEmote(fromId, emote.id);
      }}
    />
  );

  return (
    <section id="emotes" className="card list-browser">
      <div className="list-browser-header">
        <div>
          <h2 className="card-title">Emotes</h2>
          <p className="card-meta">
            The six default chips stay on every post. An image whose shortcode matches one of those emoji
            replaces that chip. Other custom images live under Custom reactions and the 😊 picker.
          </p>
        </div>
        {canManage && (
          <div className="list-browser-entry-actions">
            <button
              type="button"
              className="btn-primary"
              disabled={busy || composing}
              onClick={() => {
                setError('');
                setComposing(true);
              }}
            >
              Add emote
            </button>
          </div>
        )}
      </div>
      {error && <div className="error-banner">{error}</div>}
      {emotes.length === 0 && !composing && <p className="card-meta">None yet.</p>}
      <ul className="list-browser-entries emote-catalog-list">
        {composing && (
          <EmoteCatalogRow
            creating
            canManage
            catalogBusy={busy}
            onUpload={onUpload}
            onCreate={onCreate}
            onCancel={() => setComposing(false)}
          />
        )}
        {highlights.length > 0 && (
          <li className="emote-catalog-group">
            <h3 className="emote-catalog-group-title">Highlighted reactions</h3>
          </li>
        )}
        {highlights.map(renderRow)}
        {customs.length > 0 && (
          <li className="emote-catalog-group">
            <h3 className="emote-catalog-group-title">Custom reactions</h3>
          </li>
        )}
        {customs.map(renderRow)}
      </ul>
    </section>
  );
}

function EmoteCatalogRow({
  emote,
  creating = false,
  canManage,
  catalogBusy,
  canDrag = false,
  isHighlight = false,
  onUpload,
  onCreate,
  onUpdate,
  onDelete,
  onToggleHighlighted,
  onCancel,
  onDragStart,
  onDragOver,
  onDrop,
}: {
  emote?: CatalogEmote;
  creating?: boolean;
  canManage: boolean;
  catalogBusy?: boolean;
  canDrag?: boolean;
  isHighlight?: boolean;
  onUpload: (file: File) => Promise<{ id: string }>;
  onCreate?: (input: EmoteWriteInput & { code: string; mediaAssetId: string }) => Promise<void>;
  onUpdate?: (emoteId: string, input: EmoteWriteInput) => Promise<void>;
  onDelete?: (emoteId: string) => Promise<void>;
  onToggleHighlighted?: () => void;
  onCancel?: () => void;
  onDragStart?: (event: DragEvent<HTMLElement>) => void;
  onDragOver?: (event: DragEvent<HTMLElement>) => void;
  onDrop?: (event: DragEvent<HTMLElement>) => void;
}) {
  const [code, setCode] = useState(emote?.code ?? '');
  const [name, setName] = useState(emote?.name ?? '');
  const [file, setFile] = useState<File | null>(null);
  const codeId = useId();
  const replaced = defaultEmojiForCode(storedEmoteCode(code));

  const parseDraft = () => {
    const shortcode = storedEmoteCode(code);
    if (!EMOTE_CODE_PATTERN.test(shortcode)) {
      throw new Error('Shortcode must be 2–32 lowercase letters, digits, or underscore.');
    }
    return { shortcode, mappedEmoji: defaultEmojiForCode(shortcode) ?? null };
  };

  return (
    <InlineEditRow
      className={`list-browser-entry emote-catalog-row${isHighlight ? ' is-highlighted' : ''}`}
      creating={creating}
      canEdit={creating ? canManage && Boolean(onCreate) : canManage && Boolean(onUpdate)}
      view={
        emote ? (
          <span className="emote-catalog-row-main">
            <EmoteGlyph emote={emote} />
            <span>
              <strong>{catalogEmoteLabel(emote)}</strong>
              <div className="card-meta">{emote.name}</div>
            </span>
          </span>
        ) : null
      }
      viewActions={(startEdit) => (
        <>
          {emote && canManage && onUpdate && isHighlight ? (
            <label className="emote-highlight-toggle">
              <input
                type="checkbox"
                checked={Boolean(emote.highlighted)}
                disabled={catalogBusy}
                onChange={onToggleHighlighted}
              />
              Highlight
            </label>
          ) : !canManage && isHighlight ? (
            <span className="card-meta">Highlight</span>
          ) : null}
          {canManage && onUpdate && (
            <button type="button" className="btn-ghost" onClick={startEdit}>
              Edit
            </button>
          )}
        </>
      )}
      includeEdit={false}
      fieldsClassName="list-browser-entry-fields emote-catalog-fields"
      saveDisabled={creating && !file}
      onSave={async () => {
        if (!code.trim()) throw new Error('Shortcode is required.');
        const { shortcode, mappedEmoji } = parseDraft();
        if (emote) {
          if (!onUpdate) return;
          const asset = file ? await onUpload(file) : null;
          const previousMapped = defaultEmojiForCode(emote.code) ?? null;
          const currentSkin = emote.skinsUnicode ?? null;
          const skinIsPreviousDefault = Boolean(
            currentSkin && previousMapped && emojiMatchKey(currentSkin) === emojiMatchKey(previousMapped),
          );
          await onUpdate(emote.id, {
            code: shortcode,
            name: name.trim() || shortcode,
            mediaAssetId: asset?.id,
            skinsUnicode: mappedEmoji ?? undefined,
            clearSkin: !mappedEmoji && skinIsPreviousDefault,
          });
        } else {
          if (!onCreate || !file) return;
          const asset = await onUpload(file);
          await onCreate({
            code: shortcode,
            name: name.trim() || undefined,
            mediaAssetId: asset.id,
            skinsUnicode: mappedEmoji ?? undefined,
          });
        }
        setFile(null);
      }}
      onCancel={() => {
        setCode(emote?.code ?? '');
        setName(emote?.name ?? '');
        setFile(null);
        onCancel?.();
      }}
      onRemove={emote ? () => void onDelete?.(emote.id) : undefined}
      formProps={{
        onPaste: (e) => {
          const files = imageFilesFromDataTransfer(e.clipboardData);
          if (files.length === 0) return;
          e.preventDefault();
          setFile(files[0] ?? null);
        },
        onDragOver: (e) => e.preventDefault(),
        onDrop: (e) => {
          const files = imageFilesFromDataTransfer(e.dataTransfer);
          if (files.length === 0) return;
          e.preventDefault();
          setFile(files[0] ?? null);
        },
      }}
      draggable={canDrag}
      onDragStart={onDragStart}
      onDragOver={onDragOver}
      onDrop={onDrop}
    >
      <ImageUploadField
        compact
        file={file}
        previewUrl={file ? null : emote?.imageUrl || undefined}
        fallback={
          emote && !emote.imageUrl ? <EmoteGlyph emote={emote} className="emote-edit-glyph" /> : undefined
        }
        placeholder={emote ? 'Replace image' : undefined}
        onFiles={(files) => setFile(files[0] ?? null)}
      />
      <input
        id={codeId}
        value={code}
        onChange={(e) => setCode(e.target.value.toLowerCase())}
        placeholder="love or :love.slug.server:"
        maxLength={96}
        size={10}
        required
        autoFocus
      />
      <input
        value={name}
        onChange={(e) => setName(e.target.value)}
        placeholder="Description"
        maxLength={64}
        size={12}
      />
      {replaced && (file || emote?.imageUrl) ? (
        <label className="emote-inline-emoji" htmlFor={codeId} title={`Replaces ${replaced}`}>
          <span className="visually-hidden">Default emoji this shortcode replaces</span>
          {replaced}
        </label>
      ) : null}
    </InlineEditRow>
  );
}

function EmoteGlyph({ emote, className }: { emote: CatalogEmote; className?: string }) {
  if (emote.imageUrl) {
    return <img className={className} src={emote.imageUrl} alt={emote.name} />;
  }
  return (
    <span className={className} aria-hidden="true">
      {emote.skinsUnicode ?? catalogEmoteLabel(emote)}
    </span>
  );
}

import { useEffect, useMemo, useState } from 'react';
import { createContentClient } from '../api';
import { useAuth } from '../AuthContext';
import {
  hasEmoteImage,
  highlightedReactionImageUrl,
  defaultHighlightSlots,
  highlightedSlotKeys,
  reactionKey,
  reactionUnicode,
  emojiMatchKey,
  replacesHighlightedReaction,
  type CatalogEmote,
} from '../emotes';
import type { PostReaction } from '../types';
import { EmotePicker } from './EmotePicker';

function emptyReaction(emote: CatalogEmote): PostReaction {
  const useEmote = hasEmoteImage(emote);
  const unicode = reactionUnicode(emote);
  return {
    key: useEmote ? reactionKey({ emoteId: emote.id }) : reactionKey({ emoji: unicode }),
    emoteId: useEmote ? emote.id : null,
    code: emote.code,
    emoji: useEmote ? null : unicode ?? null,
    imageUrl: useEmote ? emote.imageUrl : null,
    count: 0,
    viewerReacted: false,
  };
}

export function ReactionBar({
  postId,
  baseUrl,
  reactions,
  emotes = [],
  forumId,
  disabled,
  manageHref,
  onChange,
}: {
  postId: string;
  baseUrl: string;
  reactions: PostReaction[];
  emotes?: CatalogEmote[];
  forumId?: string | null;
  disabled?: boolean;
  manageHref?: string;
  onChange?: (next: PostReaction[]) => void;
}) {
  const { user } = useAuth();
  const [busy, setBusy] = useState(false);
  const [rows, setRows] = useState(reactions);

  useEffect(() => {
    setRows(reactions);
  }, [reactions]);

  const chips = useMemo(() => {
    const byKey = new Map((rows ?? []).map((row) => [row.key, row]));
    const emoteList = emotes ?? [];
    const slots = defaultHighlightSlots(emoteList, forumId);
    const slotUnicodes = new Set(
      slots.map((emote) => reactionUnicode(emote)).filter((unicode): unicode is string => Boolean(unicode)).map(emojiMatchKey),
    );
    const slotIds = new Set(slots.map((emote) => emote.id));
    const highlighted = slots.map((emote) => {
      const unicode = reactionUnicode(emote);
      const placeholder = unicode
        ? {
            key: reactionKey({ emoji: unicode }),
            emoteId: null,
            code: emote.code,
            emoji: unicode,
            imageUrl: null,
            count: 0,
            viewerReacted: false,
          }
        : emptyReaction(emote);
      const existing =
        byKey.get(placeholder.key) ??
        byKey.get(reactionKey({ emoteId: emote.id })) ??
        (unicode ? byKey.get(reactionKey({ emoji: unicode })) : undefined);
      const displayUrl = existing?.imageUrl || highlightedReactionImageUrl(emote, emoteList) || emote.imageUrl || null;
      return {
        ...placeholder,
        ...existing,
        key: placeholder.key,
        imageUrl: displayUrl,
        emoji: unicode ?? existing?.emoji ?? placeholder.emoji,
        emoteId: unicode ? null : (existing?.emoteId ?? placeholder.emoteId),
        code: existing?.code ?? emote.code,
      };
    });
    const shownKeys = new Set(highlighted.map((row) => row.key));
    const extra = (rows ?? []).filter((row) => {
      if (shownKeys.has(row.key)) return false;
      if (row.emoji && slotUnicodes.has(emojiMatchKey(row.emoji))) return false;
      const match = emoteList.find((emote) => emote.id === row.emoteId);
      if (match && slotIds.has(match.id)) return false;
      if (match && replacesHighlightedReaction(match, highlightedSlotKeys(slots))) return false;
      return true;
    });
    return [...highlighted, ...extra];
  }, [rows, emotes, forumId]);

  const toggle = async (payload: { emoteId?: string; emoji?: string }) => {
    if (!user || busy || disabled) return;
    setBusy(true);
    try {
      const next = await createContentClient(baseUrl).toggleReaction(postId, payload);
      setRows(next);
      onChange?.(next);
    } finally {
      setBusy(false);
    }
  };

  const toggleChip = (row: PostReaction) => {
    if (row.emoteId) return void toggle({ emoteId: row.emoteId });
    if (row.emoji) return void toggle({ emoji: row.emoji });
  };

  return (
    <div
      className="reaction-bar"
      onClick={(e) => {
        e.preventDefault();
        e.stopPropagation();
      }}
    >
      {chips.map((row, index) => (
        <button
          key={`${row.key}:${row.emoteId ?? row.code ?? index}`}
          type="button"
          className={`reaction-chip${row.viewerReacted ? ' reaction-chip-active' : ''}${row.count === 0 ? ' reaction-chip-empty' : ''}`}
          disabled={!user || busy || disabled}
          aria-pressed={row.viewerReacted}
          title={row.code ? `:${row.code}:` : row.emoji ?? 'Reaction'}
          onClick={() => toggleChip(row)}
        >
          {row.imageUrl?.trim() ? <img src={row.imageUrl} alt="" /> : <span className="emoji-glyph">{row.emoji}</span>}
          {row.count > 0 && <span>{row.count}</span>}
        </button>
      ))}
      {user && !disabled && (
        <EmotePicker
          emotes={emotes}
          home={{ forumId }}
          disabled={busy}
          label="😊"
          manageHref={manageHref}
          onPickEmote={(emote) =>
            void toggle(emote.imageUrl ? { emoteId: emote.id } : { emoji: emote.skinsUnicode ?? undefined })
          }
        />
      )}
    </div>
  );
}

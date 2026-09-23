export const DEFAULT_REACTION_EMOJIS = ['👍', '❤️', '😂', '😮', '😢', '🎉', '🔥', '👀'] as const;

export const DEFAULT_REACTION_META: Record<(typeof DEFAULT_REACTION_EMOJIS)[number], { code: string; name: string }> = {
  '👍': { code: 'thumbsup', name: 'Thumbs up' },
  '❤️': { code: 'heart', name: 'Heart' },
  '😂': { code: 'joy', name: 'Joy' },
  '😮': { code: 'open_mouth', name: 'Surprised' },
  '😢': { code: 'cry', name: 'Cry' },
  '🎉': { code: 'tada', name: 'Party' },
  '🔥': { code: 'fire', name: 'Fire' },
  '👀': { code: 'eyes', name: 'Eyes' },
};

export const DEFAULT_EMOTE_QUALIFIER = 'default';

const DEFAULT_EMOJI_BY_CODE: Record<string, string> = {
  ...Object.fromEntries(DEFAULT_REACTION_EMOJIS.map((emoji) => [DEFAULT_REACTION_META[emoji].code, emoji])),
  love: '❤️',
};

export function defaultEmojiForCode(code: string): string | undefined {
  return DEFAULT_EMOJI_BY_CODE[code.toLowerCase()];
}

export const EMOTE_CODE_PATTERN = /^[a-z0-9_]{2,32}$/;
export const EMOTE_TOKEN_PATTERN = /:([a-z0-9_]{2,32}(?:\.[a-z0-9_]{1,32}){0,2}):/gi;
const EMOTE_SHORTCODE_ZWSP = '\u200B';
const OPEN_EMOTE_QUERY = /:((?:[a-z0-9_]+\.){0,2}[a-z0-9_]*)$/i;

export type EmoteScope = 'forum' | 'server';

export type ParsedEmoteToken = {
  code: string;
  forumQual: string | null;
  serverQual: string | null;
};

export type EmoteHome = {
  forumId?: string | null;
  serverDid?: string | null;
};

export function isUnicodeEmoji(raw: string): boolean {
  const emoji = raw.trim();
  if (!emoji || emoji.length > 16) return false;
  let hasVisible = false;
  for (const char of emoji) {
    const code = char.codePointAt(0) ?? 0;
    if (code < 32 || char === ':' || char === '/' || char === '\\') return false;
    if (/\s/.test(char) || /[0-9a-zA-Z_]/.test(char)) return false;
    hasVisible = true;
  }
  return hasVisible;
}

export function findEmoteTokenSpan(text: string, caret: number): { start: number; end: number } | null {
  const open = findOpenEmoteQuery(text, caret);
  if (open) return { start: open.start, end: open.end };
  const token = new RegExp(EMOTE_TOKEN_PATTERN.source, 'gi');
  let match: RegExpExecArray | null;
  while ((match = token.exec(text)) !== null) {
    const start = match.index;
    const end = start + match[0].length;
    if (caret >= start && caret <= end) return { start, end };
  }
  return null;
}

export function emoteShortcode(code: string): string {
  return `:${code}:`;
}

export function emoteScope(emote: CatalogEmote): EmoteScope {
  return emote.forumId ? 'forum' : 'server';
}

export function parseEmoteToken(inner: string): ParsedEmoteToken {
  const parts = inner.trim().toLowerCase().split('.');
  return {
    code: parts[0] ?? '',
    forumQual: parts.length > 1 ? parts[1] : null,
    serverQual: parts.length > 2 ? parts[2] : null,
  };
}

function forumQualifier(emote: CatalogEmote, home?: EmoteHome): string {
  if (!emote.forumId) return 'server';
  if (home?.forumId && emote.forumId === home.forumId) return 'forum';
  return (emote.forumSlug ?? emote.forumId).toLowerCase();
}

function canonicalForumQualifier(emote: CatalogEmote): string {
  if (!emote.forumId) return 'server';
  return (emote.forumSlug ?? 'forum').toLowerCase();
}

function serverQualifier(emote: CatalogEmote): string {
  return (emote.serverPrefix ?? '').toLowerCase();
}

/** Bare name stored on the emote (`love`). */
export function storedEmoteCode(raw: string): string {
  return parseEmoteToken(raw.replace(/^:+/, '').replace(/:+$/, '')).code;
}

/** Portable form written into posts: `:love.slug.server:` */
export function emoteQualifiedCode(emote: CatalogEmote, _emotes: CatalogEmote[] = [], _home?: EmoteHome): string {
  const code = emote.code.toLowerCase();
  const parts = [code, canonicalForumQualifier(emote)];
  const prefix = serverQualifier(emote);
  if (prefix) parts.push(prefix);
  return parts.join('.');
}

export function catalogEmoteShortcode(emote: CatalogEmote, emotes: CatalogEmote[], home?: EmoteHome): string {
  return emoteShortcode(emoteQualifiedCode(emote, emotes, home));
}

/** Visible label in pickers and catalogs (`:love:`), not the portable insert form. */
export function catalogEmoteLabel(emote: CatalogEmote): string {
  return emoteShortcode(storedEmoteCode(emote.code));
}

/** Suggestion grid: bare `:love:` unless another insertable emote (or a built-in) shares the name. */
export function catalogEmoteSuggestionCode(emote: CatalogEmote, emotes: CatalogEmote[], home?: EmoteHome): string {
  const code = storedEmoteCode(emote.code);
  const peers = insertableEmotes(emotes).filter((item) => storedEmoteCode(item.code) === code);
  if (peers.length > 1 || defaultEmojiForCode(code)) {
    return emoteQualifiedCode(emote, emotes, home);
  }
  return code;
}

export function resolveCatalogEmote(inner: string, emotes: CatalogEmote[], home?: EmoteHome): CatalogEmote | undefined {
  const parsed = parseEmoteToken(inner);
  if (!parsed.code) return undefined;
  let matches = emotes.filter((emote) => emote.code.toLowerCase() === parsed.code);
  if (parsed.serverQual) {
    matches = matches.filter((emote) => serverQualifier(emote) === parsed.serverQual);
  }
  if (parsed.forumQual === DEFAULT_EMOTE_QUALIFIER) {
    return undefined;
  }
  if (parsed.forumQual === 'server') {
    matches = matches.filter((emote) => !emote.forumId);
  } else if (parsed.forumQual === 'forum') {
    matches = matches.filter((emote) => Boolean(emote.forumId) && (!home?.forumId || emote.forumId === home.forumId));
  } else if (parsed.forumQual) {
    matches = matches.filter((emote) => (emote.forumSlug ?? '').toLowerCase() === parsed.forumQual);
  }
  return (
    matches.find((emote) => home?.forumId && emote.forumId === home.forumId) ??
    matches.find((emote) => emote.forumId) ??
    matches[0]
  );
}

export function hasEmoteImage(emote: Pick<CatalogEmote, 'imageUrl'> | null | undefined): boolean {
  return Boolean(emote?.imageUrl?.trim());
}

export function insertableEmotes(emotes: CatalogEmote[]): CatalogEmote[] {
  return emotes.filter((emote) => emote.canInsert !== false);
}

/** Custom image emotes a composer can insert. Unicode-only reaction placeholders stay in the default list. */
export function insertableCatalogEmotes(emotes: CatalogEmote[]): CatalogEmote[] {
  return insertableEmotes(emotes).filter(hasEmoteImage);
}

export function sortCatalogEmotes(emotes: CatalogEmote[]): CatalogEmote[] {
  return [...emotes].sort(
    (a, b) =>
      (a.sortOrder ?? 0) - (b.sortOrder ?? 0) ||
      (a.code ?? '').localeCompare(b.code ?? '') ||
      (a.id ?? '').localeCompare(b.id ?? ''),
  );
}

export function reactionUnicode(emote: Pick<CatalogEmote, 'code' | 'skinsUnicode'>): string | undefined {
  if (emote.skinsUnicode) return emote.skinsUnicode;
  if (!emote.code) return undefined;
  return defaultEmojiForCode(storedEmoteCode(emote.code));
}

export const DEFAULT_HIGHLIGHT_CODES = ['thumbsup', 'heart', 'joy', 'open_mouth', 'cry', 'tada'] as const;

function emoteMatchesHighlightCode(emote: CatalogEmote, code: (typeof DEFAULT_HIGHLIGHT_CODES)[number]): boolean {
  const stored = storedEmoteCode(emote.code);
  if (stored === code) return true;
  if (code === 'heart' && stored === 'love') return true;
  const emoji = defaultEmojiForCode(code);
  return Boolean(emoji && emote.skinsUnicode && emojiMatchKey(emote.skinsUnicode) === emojiMatchKey(emoji));
}

function pickHighlightSlot(pool: CatalogEmote[], code: (typeof DEFAULT_HIGHLIGHT_CODES)[number]): CatalogEmote | undefined {
  const matches = pool.filter((emote) => emoteMatchesHighlightCode(emote, code));
  return matches.find((emote) => hasEmoteImage(emote)) ?? matches.find((emote) => emote.highlighted) ?? matches[0];
}

/** Always the six built-in reaction slots, with catalog replacements when present. */
export function defaultHighlightSlots(emotes: CatalogEmote[] = [], forumId?: string | null): CatalogEmote[] {
  const list = emotes ?? [];
  const forum = forumId ? list.filter((emote) => emote.forumId === forumId) : [];
  const server = list.filter((emote) => !emote.forumId);
  const pool = forum.length > 0 ? forum : server;
  return DEFAULT_HIGHLIGHT_CODES.map((code) => {
    const chosen = pickHighlightSlot(pool, code) ?? pickHighlightSlot(server, code);
    if (chosen) return chosen;
    const emoji = defaultEmojiForCode(code)!;
    const meta = DEFAULT_REACTION_META[emoji];
    return {
      id: `default:${code}`,
      code,
      name: meta.name,
      imageUrl: '',
      isAnimated: false,
      skinsUnicode: emoji,
      highlighted: true,
    };
  });
}

export function highlightedSlotKeys(emotes: CatalogEmote[] = []): Set<string> {
  return new Set(
    (emotes ?? [])
      .map((emote) => reactionUnicode(emote))
      .filter((unicode): unicode is string => Boolean(unicode))
      .map((unicode) => emojiMatchKey(unicode)),
  );
}

export function replacesHighlightedReaction(emote: CatalogEmote, slots: Set<string>): boolean {
  const mapped = [emote.skinsUnicode, defaultEmojiForCode(storedEmoteCode(emote.code))].filter(
    (unicode): unicode is string => Boolean(unicode),
  );
  return mapped.some((unicode) => slots.has(emojiMatchKey(unicode)));
}

export function highlightedReactionImageUrl(emote: CatalogEmote, catalog: CatalogEmote[]): string | null {
  if (hasEmoteImage(emote)) return emote.imageUrl;
  const unicode = reactionUnicode(emote);
  if (!unicode) return null;
  const skin = skinForEmoji(catalog, unicode);
  return skin && hasEmoteImage(skin) ? skin.imageUrl : null;
}

export function highlightedReactions(emotes: CatalogEmote[] = [], forumId?: string | null): CatalogEmote[] {
  const highlighted = sortCatalogEmotes((emotes ?? []).filter((emote) => emote.highlighted));
  if (!forumId) return highlighted.filter((emote) => !emote.forumId);
  const forum = highlighted.filter((emote) => emote.forumId === forumId);
  return forum.length > 0 ? forum : highlighted.filter((emote) => !emote.forumId);
}

function occupiedHighlightImageUrls(emotes: CatalogEmote[], forumId?: string | null): Set<string> {
  const list = emotes ?? [];
  return new Set(
    defaultHighlightSlots(list, forumId)
      .map((slot) => highlightedReactionImageUrl(slot, list))
      .filter((url): url is string => Boolean(url)),
  );
}

export function occupiesDefaultHighlightSlot(
  emote: CatalogEmote,
  emotes: CatalogEmote[] = [],
  forumId?: string | null,
): boolean {
  return defaultHighlightSlots(emotes, forumId).some((slot) => slot.id === emote.id);
}

/** Custom image emotes for the picker. Replacements stay on the six highlight chips. */
export function selectorEmotes(emotes: CatalogEmote[] = [], forumId?: string | null): CatalogEmote[] {
  const list = emotes ?? [];
  const slots = defaultHighlightSlots(list, forumId);
  const slotIds = new Set(slots.map((emote) => emote.id));
  const slotUnicodes = highlightedSlotKeys(slots);
  const occupiedUrls = occupiedHighlightImageUrls(list, forumId);
  return sortCatalogEmotes(
    insertableCatalogEmotes(list).filter((emote) => {
      if (slotIds.has(emote.id)) return false;
      if (replacesHighlightedReaction(emote, slotUnicodes)) return false;
      if (occupiedUrls.has(emote.imageUrl)) return false;
      return true;
    }),
  );
}

/** Visible :code: that does not match EMOTE_TOKEN_PATTERN. */
export function escapedEmoteShortcode(code: string): string {
  return `:${EMOTE_SHORTCODE_ZWSP}${code}${EMOTE_SHORTCODE_ZWSP}:`;
}

export type CatalogEmote = {
  id: string;
  code: string;
  name: string;
  imageUrl: string;
  forumId?: string | null;
  forumSlug?: string | null;
  forumName?: string | null;
  serverDid?: string | null;
  serverPrefix?: string | null;
  isAnimated: boolean;
  skinsUnicode?: string | null;
  canInsert?: boolean;
  sortOrder?: number;
  highlighted?: boolean;
};

export type MessagePart =
  | { type: 'text'; value: string }
  | { type: 'emote'; code: string; emote?: CatalogEmote; unicode?: string };

export function emojiMatchKey(emoji: string): string {
  return [...emoji].filter((char) => char !== '\uFE0F').join('');
}

export function defaultReactionMeta(emoji: string): { code: string; name: string } | undefined {
  const exact = DEFAULT_REACTION_META[emoji as keyof typeof DEFAULT_REACTION_META];
  if (exact) return exact;
  const key = emojiMatchKey(emoji);
  const match = DEFAULT_REACTION_EMOJIS.find((item) => emojiMatchKey(item) === key);
  return match ? DEFAULT_REACTION_META[match] : undefined;
}

export function skinForEmoji(emotes: CatalogEmote[], emoji: string): CatalogEmote | undefined {
  const key = emojiMatchKey(emoji);
  const matches = emotes.filter((emote) => emote.skinsUnicode && emojiMatchKey(emote.skinsUnicode) === key);
  return (
    matches.find((emote) => emote.forumId && hasEmoteImage(emote)) ??
    matches.find((emote) => !emote.forumId && hasEmoteImage(emote)) ??
    matches.find((emote) => Boolean(emote.forumId)) ??
    matches.find((emote) => !emote.forumId)
  );
}

export function applyEmoteSkinUpdate(
  emotes: CatalogEmote[],
  updated: CatalogEmote,
  input: { skinsUnicode?: string | null; clearSkin?: boolean },
): CatalogEmote[] {
  const claimed = input.clearSkin ? null : (updated.skinsUnicode ?? input.skinsUnicode ?? null);
  const claimedKey = claimed ? emojiMatchKey(claimed) : null;
  return emotes.map((emote) => {
    if (emote.id === updated.id) return { ...emote, ...updated };
    if (claimedKey && emote.skinsUnicode && emojiMatchKey(emote.skinsUnicode) === claimedKey) {
      return { ...emote, skinsUnicode: null };
    }
    return emote;
  });
}

/** Image URL to persist on a flair/list badge. `.default` and unicode-only marks store none. */
export function badgeIconUrl(value: string, emotes: CatalogEmote[] = [], home?: EmoteHome): string | undefined {
  const token = /^:([^:\s]+):$/.exec(value.trim())?.[1];
  if (!token) return undefined;
  const part = resolveEmotePart(token, emotes, home);
  return part.emote && hasEmoteImage(part.emote) ? part.emote.imageUrl : undefined;
}

/** Built-in emoji when the badge is an explicit `:code.default:` shortcode. */
export function defaultEmoteGlyph(value?: string | null): string | null {
  const token = /^:([^:\s]+):$/.exec(value?.trim() ?? '')?.[1];
  if (!token) return null;
  const parsed = parseEmoteToken(token);
  if (parsed.forumQual !== DEFAULT_EMOTE_QUALIFIER) return null;
  return defaultEmojiForCode(parsed.code) ?? null;
}

/** Unicode for plain-text controls. Native options can paint this; they cannot paint emote images. */
export function plainEmoteGlyph(value?: string | null, emotes: CatalogEmote[] = []): string | null {
  const stored = value?.trim() ?? '';
  if (!stored) return null;
  const token = /^:([^:\s]+):$/.exec(stored)?.[1];
  if (!token) return isUnicodeEmoji(stored) ? stored : null;
  const part = resolveEmotePart(token, emotes);
  return part.emote?.skinsUnicode ?? part.unicode ?? null;
}

export function resolveEmotePart(
  inner: string,
  emotes: CatalogEmote[],
  home?: EmoteHome,
): Extract<MessagePart, { type: 'emote' }> {
  const parsed = parseEmoteToken(inner);
  const unicode = defaultEmojiForCode(parsed.code);
  if (parsed.forumQual === DEFAULT_EMOTE_QUALIFIER) {
    return { type: 'emote', code: inner, unicode };
  }
  const emote = resolveCatalogEmote(inner, emotes, home);
  if (emote) return { type: 'emote', code: inner, emote };
  if (!unicode) return { type: 'emote', code: inner };
  const skin = skinForEmoji(emotes, unicode);
  return skin
    ? { type: 'emote', code: inner, emote: skin }
    : { type: 'emote', code: inner, unicode };
}

export function parseMessageWithEmotes(message: string, emotes: CatalogEmote[], home?: EmoteHome): MessagePart[] {
  const parts: MessagePart[] = [];
  const token = new RegExp(EMOTE_TOKEN_PATTERN.source, 'gi');
  let last = 0;
  let match: RegExpExecArray | null;
  while ((match = token.exec(message)) !== null) {
    if (match.index > last) {
      parts.push(...splitSkinnedText(message.slice(last, match.index), emotes));
    }
    const inner = match[1];
    parts.push(resolveEmotePart(inner, emotes, home));
    last = match.index + match[0].length;
  }
  if (last < message.length) {
    parts.push(...splitSkinnedText(message.slice(last), emotes));
  }
  return parts.length > 0 ? parts : [{ type: 'text', value: message }];
}

function splitSkinnedText(text: string, emotes: CatalogEmote[]): MessagePart[] {
  const needles = [...new Map(
    emotes
      .filter((emote) => emote.skinsUnicode)
      .map((emote) => [emote.skinsUnicode!, emote] as const),
  ).keys()].sort((a, b) => b.length - a.length);
  if (needles.length === 0) return text ? [{ type: 'text', value: text }] : [];

  const parts: MessagePart[] = [];
  let cursor = 0;
  while (cursor < text.length) {
    let found: { at: number; needle: string } | null = null;
    for (const needle of needles) {
      const at = text.indexOf(needle, cursor);
      if (at === -1) continue;
      if (!found || at < found.at || (at === found.at && needle.length > found.needle.length)) {
        found = { at, needle };
      }
    }
    if (!found) {
      parts.push({ type: 'text', value: text.slice(cursor) });
      break;
    }
    if (found.at > cursor) {
      parts.push({ type: 'text', value: text.slice(cursor, found.at) });
    }
    const skin = skinForEmoji(emotes, found.needle);
    parts.push({ type: 'emote', code: found.needle, emote: skin });
    cursor = found.at + found.needle.length;
  }
  return parts;
}

export type EmoteSuggestion =
  | { kind: 'catalog'; emote: CatalogEmote; token: string }
  | { kind: 'unicode'; emoji: string; token: string; code: string; name: string; skin?: CatalogEmote };

export function findOpenEmoteQuery(
  text: string,
  caret: number,
): { start: number; end: number; query: string } | null {
  if (caret < 0 || caret > text.length) return null;
  const before = text.slice(0, caret);
  const match = OPEN_EMOTE_QUERY.exec(before);
  if (!match) return null;
  const start = before.length - match[0].length;
  if (match[1] === '' && start > 0 && /[a-z0-9_]/i.test(text.charAt(start - 1))) {
    return null;
  }
  return { start, end: caret, query: match[1].toLowerCase() };
}

export function findEmoteQuery(
  text: string,
  caret: number,
  mode: 'token' | 'field' = 'token',
): { start: number; end: number; query: string } | null {
  const open = findOpenEmoteQuery(text, caret);
  if (open) return open;
  if (mode !== 'field') return null;
  const match = /^(\s*)([a-z0-9_.]{1,80})(\s*)$/i.exec(text);
  if (!match) return null;
  const start = match[1].length;
  return { start, end: start + match[2].length, query: match[2].toLowerCase() };
}

export function applyEmoteQueryCompletion(
  text: string,
  start: number,
  end: number,
  token: string,
): { next: string; caret: number } {
  const next = `${text.slice(0, start)}${token}${text.slice(end)}`;
  return { next, caret: start + token.length };
}

export function matchEmoteSuggestions(
  query: string,
  emotes: CatalogEmote[],
  limit = 12,
  home?: EmoteHome,
): EmoteSuggestion[] {
  const q = query.toLowerCase();
  const parsed = parseEmoteToken(q);
  const catalog: EmoteSuggestion[] = insertableCatalogEmotes(emotes)
    .filter((emote) => {
      if (parsed.forumQual === DEFAULT_EMOTE_QUALIFIER) return false;
      if (parsed.forumQual === 'server' && emote.forumId) return false;
      if (parsed.forumQual === 'forum' && (!emote.forumId || (home?.forumId && emote.forumId !== home.forumId)))
        return false;
      if (parsed.forumQual && parsed.forumQual !== 'forum' && parsed.forumQual !== 'server') {
        const qual = forumQualifier(emote, home);
        if (!qual.startsWith(parsed.forumQual) && !(emote.forumSlug ?? '').toLowerCase().startsWith(parsed.forumQual))
          return false;
      }
      if (parsed.serverQual && !serverQualifier(emote).startsWith(parsed.serverQual)) return false;
      return true;
    })
    .map((emote) => {
      const code = emote.code.toLowerCase();
      const qualified = emoteQualifiedCode(emote, emotes, home);
      const name = emote.name.toLowerCase();
      let rank = 0;
      if (!parsed.code) rank = 1;
      else if (code.startsWith(parsed.code) || qualified.startsWith(q)) rank = 3;
      else if (name.startsWith(parsed.code) || name.split(/\s+/).some((part) => part.startsWith(parsed.code))) rank = 2;
      return { emote, rank };
    })
    .filter((row) => row.rank > 0)
    .sort(
      (a, b) =>
        b.rank - a.rank ||
        Number(Boolean(b.emote.forumId)) - Number(Boolean(a.emote.forumId)) ||
        a.emote.code.localeCompare(b.emote.code),
    )
    .map((row) => ({
      kind: 'catalog' as const,
      emote: row.emote,
      token: catalogEmoteShortcode(row.emote, emotes, home),
    }));

  const unicode: EmoteSuggestion[] = [];
  const wantDefault = parsed.forumQual === DEFAULT_EMOTE_QUALIFIER;
  if (!parsed.forumQual || wantDefault) {
    for (const emoji of DEFAULT_REACTION_EMOJIS) {
      const meta = DEFAULT_REACTION_META[emoji];
      const codes = Object.entries(DEFAULT_EMOJI_BY_CODE)
        .filter(([, value]) => value === emoji)
        .map(([item]) => item);
      const name = meta.name.toLowerCase();
      if (
        parsed.code &&
        !codes.some((item) => item.startsWith(parsed.code)) &&
        !name.startsWith(parsed.code) &&
        !name.split(/\s+/).some((part) => part.startsWith(parsed.code))
      ) {
        continue;
      }
      const overridden = emotes.some((emote) => codes.includes(emote.code.toLowerCase()));
      const tokenCode = codes.find((item) => item === parsed.code) ?? meta.code;
      const useDefaultToken = wantDefault || overridden;
      const token = useDefaultToken ? emoteShortcode(`${tokenCode}.${DEFAULT_EMOTE_QUALIFIER}`) : emoji;
      const skin = useDefaultToken ? undefined : skinForEmoji(emotes, emoji);
      unicode.push({
        kind: 'unicode',
        emoji,
        token,
        code: tokenCode,
        name: meta.name,
        // `.default` is the built-in glyph. Image-less reaction placeholders are not skins.
        skin: skin && hasEmoteImage(skin) ? skin : undefined,
      });
    }
  }

  const merged = q ? [...unicode, ...catalog] : [...unicode.slice(0, 4), ...catalog];
  return merged.slice(0, limit);
}

export function insertEmoteToken(
  text: string,
  selectionStart: number,
  selectionEnd: number,
  token: string,
): { next: string; caret: number } {
  if (selectionStart === selectionEnd) {
    const span = findEmoteTokenSpan(text, selectionStart);
    if (span) return applyEmoteQueryCompletion(text, span.start, span.end, token);
  }
  const before = text.slice(0, selectionStart);
  const after = text.slice(selectionEnd);
  const needsSpaceBefore = before.length > 0 && !/\s$/.test(before);
  const prefix = needsSpaceBefore ? ' ' : '';
  const inserted = `${prefix}${token}`;
  const next = `${before}${inserted}${after}`;
  return { next, caret: before.length + inserted.length };
}

export function reactionKey(input: { emoteId?: string | null; emoji?: string | null }): string {
  if (input.emoteId) return `e:${input.emoteId}`;
  return `u:${input.emoji ?? ''}`;
}

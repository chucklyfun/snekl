import {
  type ChangeEvent,
  type KeyboardEvent,
  type ReactNode,
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react';
import {
  applyEmoteQueryCompletion,
  catalogEmoteSuggestionCode,
  DEFAULT_EMOTE_QUALIFIER,
  findEmoteQuery,
  hasEmoteImage,
  matchEmoteSuggestions,
  parseEmoteToken,
  resolveEmotePart,
  skinForEmoji,
  type CatalogEmote,
  type EmoteHome,
  type EmoteSuggestion,
} from '../emotes';

type FieldEl = HTMLInputElement | HTMLTextAreaElement;

export function EmoteAutocomplete({
  emotes,
  home,
  value,
  onChange,
  disabled,
  mode = 'token',
  onMenuOpenChange,
  children,
}: {
  emotes: CatalogEmote[];
  home?: EmoteHome;
  value: string;
  onChange: (next: string) => void;
  disabled?: boolean;
  mode?: 'token' | 'field';
  onMenuOpenChange?: (open: boolean) => void;
  children: (bind: {
    ref: (el: FieldEl | null) => void;
    onChange: (event: ChangeEvent<FieldEl>) => void;
    onKeyDown: (event: KeyboardEvent<FieldEl>) => void;
    onSelect: () => void;
    onClick: () => void;
    onBlur: () => void;
  }) => ReactNode;
}) {
  const fieldRef = useRef<FieldEl | null>(null);
  const [caret, setCaret] = useState(value.length);
  const [active, setActive] = useState(0);
  const [dismissed, setDismissed] = useState(false);

  const query = useMemo(
    () => (disabled ? null : findEmoteQuery(value, caret, mode)),
    [caret, disabled, mode, value],
  );
  const suggestions = useMemo(
    () => (query && !dismissed ? matchEmoteSuggestions(query.query, emotes, 12, home) : []),
    [dismissed, emotes, home, query],
  );
  const open = suggestions.length > 0;

  useEffect(() => {
    onMenuOpenChange?.(open);
  }, [open, onMenuOpenChange]);

  useEffect(() => {
    setActive(0);
  }, [query?.start, query?.query]);

  const syncCaret = useCallback(() => {
    const el = fieldRef.current;
    if (!el) return;
    setCaret(el.selectionStart ?? value.length);
    setDismissed(false);
  }, [value.length]);

  const complete = useCallback(
    (item: EmoteSuggestion) => {
      if (!query) return;
      const { next, caret: nextCaret } = applyEmoteQueryCompletion(value, query.start, query.end, item.token);
      onChange(next);
      setDismissed(true);
      queueMicrotask(() => {
        const el = fieldRef.current;
        el?.focus();
        el?.setSelectionRange(nextCaret, nextCaret);
        setCaret(nextCaret);
      });
    },
    [onChange, query, value],
  );

  const onKeyDown = (event: KeyboardEvent<FieldEl>) => {
    if (!open) return;
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        setActive((index) => (index + 1) % suggestions.length);
        return;
      case 'ArrowUp':
        event.preventDefault();
        setActive((index) => (index - 1 + suggestions.length) % suggestions.length);
        return;
      case 'Enter':
      case 'Tab':
        event.preventDefault();
        complete(suggestions[active] ?? suggestions[0]);
        return;
      case 'Escape':
        event.preventDefault();
        event.stopPropagation();
        setDismissed(true);
        return;
      default:
        return;
    }
  };

  return (
    <div className="emote-autocomplete">
      {children({
        ref: (el) => {
          fieldRef.current = el;
        },
        onChange: (event) => {
          onChange(event.target.value);
          setCaret(event.target.selectionStart ?? event.target.value.length);
          setDismissed(false);
        },
        onKeyDown,
        onSelect: syncCaret,
        onClick: syncCaret,
        onBlur: () => {
          window.setTimeout(() => setDismissed(true), 120);
        },
      })}
      {open && (
        <ul className="emote-autocomplete-menu" role="listbox" aria-label="Emote suggestions">
          {suggestions.map((item, index) => {
            const selected = index === active;
            const key = suggestionKey(item);
            const meaning = suggestionMeaning(item);
            return (
              <li key={key} role="presentation">
                <button
                  type="button"
                  role="option"
                  aria-selected={selected}
                  className={`emote-autocomplete-item${selected ? ' is-active' : ''}`}
                  onMouseDown={(event) => {
                    event.preventDefault();
                    complete(item);
                  }}
                >
                  <span className="emote-autocomplete-text">
                    <ShortcodeLabel code={suggestionCode(item, emotes, home)} />
                    {meaning ? <span className="emote-autocomplete-name">{meaning}</span> : null}
                  </span>
                  <span className="emote-autocomplete-glyph">
                    <SuggestionPreview item={item} />
                  </span>
                </button>
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}

function suggestionKey(item: EmoteSuggestion): string {
  switch (item.kind) {
    case 'catalog':
      return `c:${item.emote.id}`;
    case 'unicode':
      return `u:${item.emoji}`;
    default: {
      const exhaustive: never = item;
      return exhaustive;
    }
  }
}

function suggestionCode(item: EmoteSuggestion, emotes: CatalogEmote[], home?: EmoteHome): string {
  switch (item.kind) {
    case 'catalog':
      return catalogEmoteSuggestionCode(item.emote, emotes, home);
    case 'unicode':
      return item.token.startsWith(':') ? item.token.replaceAll(':', '') : item.code;
    default: {
      const exhaustive: never = item;
      return exhaustive;
    }
  }
}

function suggestionMeaning(item: EmoteSuggestion): string | null {
  switch (item.kind) {
    case 'catalog': {
      const name = item.emote.name.trim();
      const parsed = parseEmoteToken(item.token.replaceAll(':', ''));
      const scope =
        parsed.forumQual === 'server'
          ? 'Server'
          : parsed.forumQual === 'forum'
            ? 'Forum'
            : parsed.forumQual
              ? item.emote.forumName ?? parsed.forumQual
              : null;
      const meaning = name && name.toLowerCase() !== item.emote.code ? name : null;
      return [meaning, scope].filter(Boolean).join(' · ') || null;
    }
    case 'unicode':
      return item.token.includes(`.${DEFAULT_EMOTE_QUALIFIER}:`)
        ? [item.name, 'Default'].filter(Boolean).join(' · ')
        : item.name.toLowerCase() !== item.code
          ? item.name
          : null;
    default: {
      const exhaustive: never = item;
      return exhaustive;
    }
  }
}

function ShortcodeLabel({ code }: { code: string }) {
  return <span className="emote-autocomplete-code" data-code={code} aria-label={`shortcode ${code}`} />;
}

function SuggestionPreview({ item }: { item: EmoteSuggestion }) {
  switch (item.kind) {
    case 'catalog':
      return <CatalogEmotePreview emote={item.emote} />;
    case 'unicode':
      if (item.skin && hasEmoteImage(item.skin)) return <img src={item.skin.imageUrl} alt="" />;
      return <span className="emoji-glyph">{item.emoji}</span>;
    default: {
      const exhaustive: never = item;
      return exhaustive;
    }
  }
}

function CatalogEmotePreview({ emote }: { emote: CatalogEmote }) {
  if (hasEmoteImage(emote)) return <img src={emote.imageUrl} alt="" />;
  if (emote.skinsUnicode) return <span className="emoji-glyph">{emote.skinsUnicode}</span>;
  return null;
}

export function EmoteGlyph({
  value,
  emotes,
  className = 'inline-emote',
}: {
  value?: string | null;
  emotes: CatalogEmote[];
  className?: string;
}) {
  if (!value) return null;
  const token = /^:([^:\s]+):$/.exec(value ?? '');
  if (token) {
    const part = resolveEmotePart(token[1], emotes);
    if (part.emote && hasEmoteImage(part.emote)) {
      return <img className={className} src={part.emote.imageUrl} alt={value} title={value} />;
    }
    if (part.emote?.skinsUnicode) return <span className="emoji-glyph">{part.emote.skinsUnicode}</span>;
    if (part.unicode) return <span className="emoji-glyph">{part.unicode}</span>;
  }
  const skin = skinForEmoji(emotes, value);
  if (skin && hasEmoteImage(skin)) {
    return <img className={className} src={skin.imageUrl} alt={value} title={value} />;
  }
  if (skin?.skinsUnicode) return <span className="emoji-glyph">{skin.skinsUnicode}</span>;
  return <span className="emoji-glyph">{value}</span>;
}

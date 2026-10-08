import { describe, expect, it } from 'vitest';
import {
  applyEmoteQueryCompletion,
  applyEmoteSkinUpdate,
  badgeIconUrl,
  catalogEmoteShortcode,
  catalogEmoteSuggestionCode,
  defaultEmoteGlyph,
  plainEmoteGlyph,
  defaultHighlightSlots,
  escapedEmoteShortcode,
  findEmoteQuery,
  findOpenEmoteQuery,
  highlightedReactionImageUrl,
  highlightedReactions,
  insertEmoteToken,
  isUnicodeEmoji,
  matchEmoteSuggestions,
  parseMessageWithEmotes,
  occupiesDefaultHighlightSlot,
  selectorEmotes,
  storedEmoteCode,
} from './emotes';

describe('parseMessageWithEmotes', () => {
  const emotes = [
    {
      id: '1',
      code: 'pog',
      name: 'Pog',
      imageUrl: 'http://example/pog.png',
      isAnimated: false,
    },
  ];

  it('splits text around known shortcodes', () => {
    const parts = parseMessageWithEmotes('wow :pog: nice', emotes);
    expect(parts).toEqual([
      { type: 'text', value: 'wow ' },
      { type: 'emote', code: 'pog', emote: emotes[0] },
      { type: 'text', value: ' nice' },
    ]);
  });

  it('does not treat escaped shortcodes as emotes', () => {
    const escaped = escapedEmoteShortcode('pog');
    expect(escaped).not.toMatch(/:[a-z0-9_]{2,32}:/);
    expect(parseMessageWithEmotes(`wow ${escaped} nice`, emotes)).toEqual([
      { type: 'text', value: `wow ${escaped} nice` },
    ]);
  });

  it('keeps unknown shortcodes as emote parts without catalog entries', () => {
    const parts = parseMessageWithEmotes(':missing:', emotes);
    expect(parts).toEqual([{ type: 'emote', code: 'missing', emote: undefined }]);
  });

  it('applies a chip skin and clears it from other emotes', () => {
    const other = {
      id: '2',
      code: 'up',
      name: 'Up',
      imageUrl: 'http://example/up.png',
      isAnimated: false,
      skinsUnicode: '👍',
    };
    const next = applyEmoteSkinUpdate(
      [emotes[0], other],
      { ...emotes[0], skinsUnicode: '👍' },
      { skinsUnicode: '👍' },
    );
    expect(next[0].skinsUnicode).toBe('👍');
    expect(next[1].skinsUnicode).toBeNull();
  });

  it('replaces skinned unicode in message text', () => {
    const skinned = [{ ...emotes[0], skinsUnicode: '👍' }];
    const parts = parseMessageWithEmotes('nice 👍', skinned);
    expect(parts).toEqual([
      { type: 'text', value: 'nice ' },
      { type: 'emote', code: '👍', emote: skinned[0] },
    ]);
  });

  it('prefers the nest emote for a shared code and honors :emoji.forum: / :emoji.server: suffixes', () => {
    const forum = { ...emotes[0], id: 'f', forumId: 'forum-1' };
    const server = { ...emotes[0], id: 's', forumId: null, imageUrl: 'http://example/server-pog.png' };
    const catalog = [server, forum];
    const home = { forumId: 'forum-1' };
    expect(parseMessageWithEmotes(':pog:', catalog, home)[0]).toMatchObject({ emote: forum });
    expect(parseMessageWithEmotes(':pog.forum:', catalog, home)[0]).toMatchObject({ emote: forum });
    expect(parseMessageWithEmotes(':pog.server:', catalog, home)[0]).toMatchObject({ emote: server });
    expect(catalogEmoteShortcode(forum, catalog, home)).toBe(':pog.forum:');
    expect(catalogEmoteShortcode(server, catalog, home)).toBe(':pog.server:');
  });

  it('uses a bare suggestion label unless the same code needs to be distinguished', () => {
    const forum = { ...emotes[0], id: 'f', forumId: 'forum-1', forumSlug: 'garden', serverPrefix: 'home' };
    const server = { ...emotes[0], id: 's', forumId: null, serverPrefix: 'home' };
    expect(catalogEmoteSuggestionCode(forum, [forum])).toBe('pog');
    expect(catalogEmoteSuggestionCode(forum, [forum, server])).toBe('pog.garden.home');
    expect(catalogEmoteSuggestionCode(server, [forum, server])).toBe('pog.server.home');
    const love = { ...emotes[0], code: 'love' };
    expect(catalogEmoteSuggestionCode(love, [love])).toBe('love.server');
  });

  it('keeps highlighted reactions out of the selector order', () => {
    const heart = {
      ...emotes[0],
      id: 'h',
      code: 'heart',
      imageUrl: '',
      highlighted: true,
      sortOrder: 0,
      forumId: 'forum-1',
    };
    const pog = { ...emotes[0], id: 'p', code: 'pog', highlighted: false, sortOrder: 1, forumId: 'forum-1' };
    expect(highlightedReactions([heart, pog], 'forum-1').map((item) => item.id)).toEqual(['h']);
    expect(selectorEmotes([heart, pog]).map((item) => item.id)).toEqual(['p']);
  });

  it('keeps a replacement for a highlighted emoji out of the custom selector', () => {
    const heart = {
      ...emotes[0],
      id: 'h',
      code: 'heart',
      name: 'Heart',
      imageUrl: '',
      highlighted: true,
      skinsUnicode: '❤️',
      forumId: 'forum-1',
    };
    const sharon = {
      ...emotes[0],
      id: 's',
      code: 'love',
      name: 'Sharon Snacklefox',
      imageUrl: 'http://example/sharon.png',
      highlighted: false,
      skinsUnicode: '❤️',
      forumId: 'forum-1',
    };
    const pog = { ...emotes[0], id: 'p', code: 'pog', highlighted: false, forumId: 'forum-1' };
    expect(selectorEmotes([heart, sharon, pog]).map((item) => item.id)).toEqual(['p']);
    expect(selectorEmotes([heart, sharon, pog], 'forum-1').map((item) => item.id)).toEqual(['p']);
    expect(highlightedReactionImageUrl(heart, [heart, sharon, pog])).toBe('http://example/sharon.png');
  });

  it('always fills the six default reaction slots and uses a replacement on heart', () => {
    const sharon = {
      ...emotes[0],
      id: 's',
      code: 'love',
      name: 'Sharon Snacklefox',
      imageUrl: 'http://example/sharon.png',
      highlighted: false,
      skinsUnicode: '❤️',
      forumId: 'forum-1',
    };
    const pog = { ...emotes[0], id: 'p', code: 'pog', highlighted: false, forumId: 'forum-1' };
    const slots = defaultHighlightSlots([sharon, pog], 'forum-1');
    expect(slots.map((item) => storedEmoteCode(item.code))).toEqual([
      'thumbsup',
      'love',
      'joy',
      'open_mouth',
      'cry',
      'tada',
    ]);
    expect(slots[1]?.id).toBe('s');
    expect(selectorEmotes([sharon, pog], 'forum-1').map((item) => item.id)).toEqual(['p']);
  });

  it('puts highlighted custom image emotes in the picker, not extra highlight slots', () => {
    const sharon = {
      ...emotes[0],
      id: 's',
      code: 'love',
      imageUrl: 'http://example/sharon.png',
      highlighted: true,
      skinsUnicode: '❤️',
      forumId: 'forum-1',
    };
    const bonk = {
      ...emotes[0],
      id: 'b',
      code: 'bonk',
      imageUrl: 'http://example/bonk.png',
      highlighted: true,
      skinsUnicode: null,
      forumId: 'forum-1',
    };
    const otherForum = {
      ...emotes[0],
      id: 'x',
      code: 'wheeze',
      imageUrl: 'http://example/wheeze.png',
      highlighted: true,
      forumId: 'forum-2',
    };
    const catalog = [sharon, bonk, otherForum];
    expect(occupiesDefaultHighlightSlot(sharon, catalog, 'forum-1')).toBe(true);
    expect(occupiesDefaultHighlightSlot(bonk, catalog, 'forum-1')).toBe(false);
    expect(selectorEmotes(catalog, 'forum-1').map((item) => item.id)).toEqual(['b', 'x']);
  });

  it('stores the bare name when a full server shortcode is pasted', () => {
    expect(storedEmoteCode(':Love.Garden.Home:')).toBe('love');
    expect(storedEmoteCode('love')).toBe('love');
  });

  it('overrides default shortcodes and keeps :code.default: as the built-in emoji', () => {
    const custom = { ...emotes[0], code: 'love', skinsUnicode: '❤️' };
    expect(parseMessageWithEmotes(':love:', [custom])[0]).toMatchObject({ emote: custom });
    expect(parseMessageWithEmotes(':love.default:', [custom])[0]).toEqual({
      type: 'emote',
      code: 'love.default',
      unicode: '❤️',
    });
    expect(parseMessageWithEmotes(':heart:', [])[0]).toMatchObject({ unicode: '❤️' });
  });

  it('does not persist a custom image for :code.default: badges', () => {
    const custom = { ...emotes[0], code: 'thumbsup', imageUrl: 'http://example/fox.png', skinsUnicode: '👍' };
    expect(badgeIconUrl(':thumbsup.default:', [custom])).toBeUndefined();
    expect(badgeIconUrl(':pog:', emotes)).toBe('http://example/pog.png');
    expect(defaultEmoteGlyph(':thumbsup.default:')).toBe('👍');
    expect(defaultEmoteGlyph(':pog:')).toBeNull();
    expect(plainEmoteGlyph(':thumbsup.default:')).toBe('👍');
    expect(plainEmoteGlyph(':pog:', emotes)).toBeNull();
    expect(plainEmoteGlyph(':pog:', [{ ...emotes[0], skinsUnicode: '😮' }])).toBe('😮');
    expect(plainEmoteGlyph('😂')).toBe('😂');
  });

  it('always inserts the full server shortcode for a catalog emote', () => {
    const forum = { ...emotes[0], forumId: 'forum-1', forumSlug: 'garden', serverPrefix: 'home' };
    expect(catalogEmoteShortcode(forum, [forum])).toBe(':pog.garden.home:');
  });

  it('qualifies with nest slug and server prefix across catalogs', () => {
    const home = { forumId: 'forum-1', serverDid: 'did:web:home' };
    const local = {
      ...emotes[0],
      id: 'f',
      forumId: 'forum-1',
      forumSlug: 'general',
      serverDid: 'did:web:home',
      serverPrefix: 'home',
    };
    const remote = {
      ...emotes[0],
      id: 'r',
      forumId: 'forum-2',
      forumSlug: 'lounge',
      serverDid: 'did:web:other',
      serverPrefix: 'other',
    };
    const catalog = [local, remote];
    expect(parseMessageWithEmotes(':pog.lounge.other:', catalog, home)[0]).toMatchObject({ emote: remote });
    expect(catalogEmoteShortcode(local, catalog, home)).toBe(':pog.general.home:');
    expect(catalogEmoteShortcode(remote, catalog, home)).toBe(':pog.lounge.other:');
  });
});

describe('insertEmoteToken', () => {
  it('inserts at the caret and adds a leading space when needed', () => {
    const result = insertEmoteToken('hello', 5, 5, ':pog:');
    expect(result.next).toBe('hello :pog:');
    expect(result.caret).toBe(11);
  });

  it('updates an in-progress or complete shortcode instead of inserting beside it', () => {
    expect(insertEmoteToken('wow :po', 7, 7, ':pog:')).toEqual({ next: 'wow :pog:', caret: 9 });
    expect(insertEmoteToken('wow :old: more', 9, 9, ':pog:')).toEqual({ next: 'wow :pog: more', caret: 9 });
  });

  it('accepts unicode for inline skins and rejects shortcodes', () => {
    expect(isUnicodeEmoji('🍕')).toBe(true);
    expect(isUnicodeEmoji('❤️')).toBe(true);
    expect(isUnicodeEmoji(':pog:')).toBe(false);
    expect(isUnicodeEmoji('pog')).toBe(false);
  });
});

describe('emote autocomplete', () => {
  const emotes = [
    {
      id: '1',
      code: 'pog',
      name: 'Pog',
      imageUrl: 'http://example/pog.png',
      isAnimated: false,
    },
    {
      id: '2',
      code: 'party',
      name: 'Party parrot',
      imageUrl: 'http://example/party.png',
      isAnimated: true,
    },
  ];

  it('opens on a partial :code.forum at the caret', () => {
    expect(findOpenEmoteQuery('wow :pog.for', 12)).toEqual({ start: 4, end: 12, query: 'pog.for' });
  });

  it('suggests qualified shortcodes when nest and server share a code', () => {
    const catalog = [
      { ...emotes[0], id: 'f', forumId: 'forum-1' },
      { ...emotes[0], id: 's', forumId: null },
    ];
    const home = { forumId: 'forum-1' };
    const matches = matchEmoteSuggestions('pog', catalog, 12, home).filter((item) => item.kind === 'catalog');
    expect(matches.map((item) => item.kind === 'catalog' && item.token)).toEqual([':pog.forum:', ':pog.server:']);
    const scoped = matchEmoteSuggestions('pog.server', catalog, 12, home).filter((item) => item.kind === 'catalog');
    expect(scoped).toHaveLength(1);
    expect(scoped[0]?.kind === 'catalog' && scoped[0].token).toBe(':pog.server:');
    const defaults = matchEmoteSuggestions('love.default', []).filter((item) => item.kind === 'unicode');
    expect(defaults.some((item) => item.kind === 'unicode' && item.token === ':love.default:')).toBe(true);
  });

  it('omits view-only emotes from insert suggestions', () => {
    const catalog = [
      { ...emotes[0], id: 'f', forumId: 'forum-1', canInsert: true },
      { ...emotes[0], id: 'other', forumId: 'forum-2', forumSlug: 'lounge', canInsert: false },
    ];
    const matches = matchEmoteSuggestions('pog', catalog, 12, { forumId: 'forum-1' }).filter(
      (item) => item.kind === 'catalog',
    );
    expect(matches).toHaveLength(1);
    expect(matches[0]?.kind === 'catalog' && matches[0].emote.id).toBe('f');
  });

  it('does not reopen after a completed shortcode', () => {
    expect(findOpenEmoteQuery('wow :pog:', 9)).toBeNull();
  });

  it('matches field mode without a colon', () => {
    expect(findEmoteQuery('po', 2, 'field')).toEqual({ start: 0, end: 2, query: 'po' });
  });

  it('ranks code prefix matches and completes the query span', () => {
    const matches = matchEmoteSuggestions('po', emotes);
    expect(matches.some((item) => item.kind === 'catalog' && item.emote.code === 'pog')).toBe(true);
    const result = applyEmoteQueryCompletion('wow :po', 4, 7, ':pog:');
    expect(result).toEqual({ next: 'wow :pog:', caret: 9 });
  });

  it('labels built-in chips with a text shortcode', () => {
    const matches = matchEmoteSuggestions('thumb', []);
    const thumb = matches.find((item) => item.kind === 'unicode' && item.emoji === '👍');
    expect(thumb?.kind === 'unicode' && thumb.code).toBe('thumbsup');
  });

  it('previews built-in emoji for :code.default: even when a custom skin exists', () => {
    const seeded = {
      id: 't',
      code: 'thumbsup',
      name: 'Thumbs up',
      imageUrl: '',
      isAnimated: false,
      skinsUnicode: '👍',
      highlighted: true,
    };
    const customHeart = {
      ...emotes[0],
      id: 'h',
      code: 'heart',
      name: 'Heart',
      imageUrl: 'http://example/heart.png',
      skinsUnicode: '❤️',
    };
    const matches = matchEmoteSuggestions('', [seeded, customHeart]);
    const thumb = matches.find((item) => item.kind === 'unicode' && item.emoji === '👍');
    const heart = matches.find((item) => item.kind === 'unicode' && item.emoji === '❤️');
    expect(thumb?.kind === 'unicode' && thumb.token).toBe(':thumbsup.default:');
    expect(thumb?.kind === 'unicode' ? thumb.skin : 'missing').toBeUndefined();
    expect(heart?.kind === 'unicode' && heart.token).toBe(':heart.default:');
    expect(heart?.kind === 'unicode' ? heart.skin : 'missing').toBeUndefined();
  });

  it('hides image-less default reactions from catalog suggestions', () => {
    const seeded = [
      {
        id: 't1',
        code: 'thumbsup',
        name: 'Thumbs up',
        imageUrl: '',
        isAnimated: false,
        skinsUnicode: '👍',
        highlighted: true,
        forumId: 'sandbox',
        forumSlug: 'sandbox-6f1ec0',
        forumName: 'Sandbox',
        serverPrefix: 'snekl_alpha',
      },
      {
        id: 'o1',
        code: 'open_mouth',
        name: 'Surprised',
        imageUrl: '',
        isAnimated: false,
        skinsUnicode: '😮',
        highlighted: true,
        forumId: 'sandbox',
        forumSlug: 'sandbox-6f1ec0',
        forumName: 'Sandbox',
        serverPrefix: 'snekl_alpha',
      },
    ];
    const custom = {
      ...emotes[0],
      id: 'img',
      code: 'thumbsup',
      name: 'Thumbs up',
      imageUrl: 'http://example/up.png',
      forumId: 'forum-1',
      forumSlug: 'garden',
      serverPrefix: 'home',
    };
    const matches = matchEmoteSuggestions('th', [...seeded, custom]);
    expect(matches.some((item) => item.kind === 'catalog' && item.emote.id === 't1')).toBe(false);
    expect(matches.some((item) => item.kind === 'catalog' && item.emote.code === 'open_mouth')).toBe(false);
    expect(matches.some((item) => item.kind === 'unicode' && item.emoji === '😮')).toBe(false);
    expect(matches.some((item) => item.kind === 'unicode' && item.token === ':thumbsup.default:')).toBe(true);
    expect(matches.some((item) => item.kind === 'catalog' && item.emote.id === 'img')).toBe(true);
  });
});

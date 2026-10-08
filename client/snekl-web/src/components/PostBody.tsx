import { catalogEmoteShortcode, parseMessageWithEmotes, type CatalogEmote, type EmoteHome } from '../emotes';

export function PostBody({
  message,
  emotes = [],
  home,
  className = 'post-body',
}: {
  message: string;
  emotes?: CatalogEmote[];
  home?: EmoteHome;
  className?: string;
}) {
  const parts = parseMessageWithEmotes(message, emotes, home);
  const hasTokens = parts.some((part) => part.type === 'emote');
  if (!hasTokens) {
    return <p className={className}>{message}</p>;
  }

  return (
    <p className={`${className} post-body-emotes`}>
      {parts.map((part, index) => {
        if (part.type === 'text') return <span key={index}>{part.value}</span>;
        if (part.emote) {
          const shortcode = catalogEmoteShortcode(part.emote, emotes, home);
          if (!part.emote.imageUrl && part.emote.skinsUnicode) {
            return (
              <span key={index} title={shortcode}>
                {part.emote.skinsUnicode}
              </span>
            );
          }
          return (
            <img
              key={index}
              className="inline-emote"
              src={part.emote.imageUrl}
              alt={part.emote.skinsUnicode && part.code === part.emote.skinsUnicode ? part.code : shortcode}
              title={
                part.emote.skinsUnicode
                  ? `${part.emote.skinsUnicode} · ${shortcode}`
                  : shortcode
              }
            />
          );
        }
        if (part.unicode) return <span key={index}>{part.unicode}</span>;
        return <span key={index}>:{part.code}:</span>;
      })}
    </p>
  );
}

export function HoverExpandHint({ text }: { text: string }) {
  return (
    <span className="hover-expand-hint">
      <span className="hover-expand-hint-snip">({text})</span>
      <span className="hover-expand-hint-full">{text}</span>
    </span>
  );
}

const PERMISSION_HINTS: Record<string, string> = {
  RequestDirectMessage: 'follow-back to DM; keep off new members',
  UseOtherForumEmotes: 'insert emotes from other forums on this server',
  UseOtherServerEmotes: 'insert emotes from other servers you belong to',
};

export function PermissionHint({ name }: { name: string }) {
  const text = PERMISSION_HINTS[name];
  return text ? <HoverExpandHint text={text} /> : null;
}

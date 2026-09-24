export function ForumAvatar({
  name,
  src,
  size = 24,
}: {
  name: string;
  src?: string | null;
  size?: number;
}) {
  const initial = name.trim().charAt(0).toUpperCase() || '?';
  return (
    <span
      className="forum-avatar"
      style={{ width: size, height: size, fontSize: size * 0.42 }}
      aria-hidden={true}
    >
      {src ? <img src={src} alt="" /> : initial}
    </span>
  );
}

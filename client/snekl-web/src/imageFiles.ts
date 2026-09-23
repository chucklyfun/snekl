export const DEFAULT_IMAGE_ACCEPT = 'image/jpeg,image/png,image/webp,image/gif';

const TYPE_ALIASES: Record<string, string> = {
  'image/jpg': 'image/jpeg',
};

const EXTENSION_TYPES: Record<string, string> = {
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
  '.webp': 'image/webp',
  '.gif': 'image/gif',
};

function allowedTypes(accept: string): Set<string> {
  return new Set(
    accept
      .split(',')
      .map((part) => TYPE_ALIASES[part.trim().toLowerCase()] ?? part.trim().toLowerCase())
      .filter(Boolean),
  );
}

function fileType(file: File): string {
  const raw = (TYPE_ALIASES[file.type] ?? file.type).toLowerCase();
  if (raw) return raw;
  const name = file.name.toLowerCase();
  const ext = Object.keys(EXTENSION_TYPES).find((suffix) => name.endsWith(suffix));
  return ext ? EXTENSION_TYPES[ext] : '';
}

export function isAcceptedImageFile(file: File, accept = DEFAULT_IMAGE_ACCEPT): boolean {
  const type = fileType(file);
  return type.startsWith('image/') && allowedTypes(accept).has(type);
}

export function imageFilesFromList(
  list: Iterable<File> | ArrayLike<File> | null | undefined,
  accept = DEFAULT_IMAGE_ACCEPT,
): File[] {
  if (!list) return [];
  return Array.from(list).filter((file) => isAcceptedImageFile(file, accept));
}

export function imageFilesFromDataTransfer(
  data: DataTransfer | null | undefined,
  accept = DEFAULT_IMAGE_ACCEPT,
): File[] {
  if (!data) return [];
  const fromItems: File[] = [];
  for (const item of Array.from(data.items ?? [])) {
    if (item.kind !== 'file') continue;
    const file = item.getAsFile();
    if (file) fromItems.push(file);
  }
  return imageFilesFromList(fromItems.length > 0 ? fromItems : data.files, accept);
}

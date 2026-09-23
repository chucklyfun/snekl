import { describe, expect, it } from 'vitest';
import { imageFilesFromList, isAcceptedImageFile } from './imageFiles';

function image(name: string, type: string) {
  return new File([new Uint8Array([1, 2, 3])], name, { type });
}

describe('isAcceptedImageFile', () => {
  it('accepts common image types', () => {
    expect(isAcceptedImageFile(image('a.png', 'image/png'))).toBe(true);
    expect(isAcceptedImageFile(image('a.jpg', 'image/jpeg'))).toBe(true);
    expect(isAcceptedImageFile(image('a.gif', 'image/gif'))).toBe(true);
  });

  it('rejects non-images', () => {
    expect(isAcceptedImageFile(image('a.txt', 'text/plain'))).toBe(false);
  });

  it('uses the filename when the MIME type is missing', () => {
    expect(isAcceptedImageFile(image('shot.webp', ''))).toBe(true);
    expect(isAcceptedImageFile(image('notes.txt', ''))).toBe(false);
  });
});

describe('imageFilesFromList', () => {
  it('keeps only accepted images', () => {
    const files = imageFilesFromList([
      image('a.png', 'image/png'),
      image('b.txt', 'text/plain'),
      image('c.webp', 'image/webp'),
    ]);
    expect(files.map((file) => file.name)).toEqual(['a.png', 'c.webp']);
  });
});

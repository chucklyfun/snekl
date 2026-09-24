import { useState } from 'react';
import { ImageUploadField } from './ImageUploadField';
import { ForumAvatar } from './ForumAvatar';

export function ForumAppearanceFields({
  name,
  avatarUrl,
  bannerUrl,
  disabled,
  onUpload,
  onSetAvatar,
  onSetBanner,
  onClearAvatar,
  onClearBanner,
}: {
  name: string;
  avatarUrl?: string | null;
  bannerUrl?: string | null;
  disabled?: boolean;
  onUpload: (file: File) => Promise<{ id: string }>;
  onSetAvatar: (mediaId: string) => Promise<void>;
  onSetBanner: (mediaId: string) => Promise<void>;
  onClearAvatar: () => Promise<void>;
  onClearBanner: () => Promise<void>;
}) {
  const [busy, setBusy] = useState<'avatar' | 'banner' | null>(null);
  const [error, setError] = useState('');

  const save = async (kind: 'avatar' | 'banner', files: File[]) => {
    const file = files[0];
    if (!file) return;
    setError('');
    setBusy(kind);
    try {
      const asset = await onUpload(file);
      if (kind === 'avatar') await onSetAvatar(asset.id);
      else await onSetBanner(asset.id);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not save image');
    } finally {
      setBusy(null);
    }
  };

  const clear = async (kind: 'avatar' | 'banner') => {
    setError('');
    setBusy(kind);
    try {
      if (kind === 'avatar') await onClearAvatar();
      else await onClearBanner();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not remove image');
    } finally {
      setBusy(null);
    }
  };

  return (
    <section className="card">
      <h2 className="card-title">Appearance</h2>
      <p className="card-meta">
        The profile image sits beside the forum name in the left list. The banner is the wide image at
        the top of the forum page.
      </p>
      {error && <div className="error-banner">{error}</div>}
      <div className="forum-appearance-grid">
        <div>
          <p className="forum-appearance-label">Profile image</p>
          <div className="forum-appearance-avatar-row">
            <ForumAvatar name={name} src={avatarUrl} size={48} />
            <ImageUploadField
              compact
              disabled={disabled || busy === 'avatar'}
              placeholder="Choose a square image"
              onFiles={(files) => void save('avatar', files)}
            />
          </div>
          {avatarUrl && (
            <button
              type="button"
              className="btn-ghost"
              disabled={disabled || busy === 'avatar'}
              onClick={() => void clear('avatar')}
            >
              Remove profile image
            </button>
          )}
        </div>
        <div>
          <p className="forum-appearance-label">Banner</p>
          {bannerUrl && (
            <div className="forum-appearance-banner-preview">
              <img src={bannerUrl} alt="" />
            </div>
          )}
          <ImageUploadField
            disabled={disabled || busy === 'banner'}
            placeholder="Choose a wide image"
            onFiles={(files) => void save('banner', files)}
          />
          {bannerUrl && (
            <button
              type="button"
              className="btn-ghost"
              disabled={disabled || busy === 'banner'}
              onClick={() => void clear('banner')}
            >
              Remove banner
            </button>
          )}
        </div>
      </div>
    </section>
  );
}

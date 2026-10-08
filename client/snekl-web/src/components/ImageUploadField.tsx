import { type ReactNode, useEffect, useId, useRef, useState } from 'react';
import {
  DEFAULT_IMAGE_ACCEPT,
  imageFilesFromDataTransfer,
  imageFilesFromList,
} from '../imageFiles';

export function ImageUploadField({
  id,
  accept = DEFAULT_IMAGE_ACCEPT,
  multiple = false,
  disabled = false,
  file = null,
  compact = false,
  placeholder,
  previewUrl,
  fallback,
  onFiles,
}: {
  id?: string;
  accept?: string;
  multiple?: boolean;
  disabled?: boolean;
  file?: File | null;
  compact?: boolean;
  placeholder?: string;
  previewUrl?: string | null;
  fallback?: ReactNode;
  onFiles: (files: File[]) => void;
}) {
  const generatedId = useId();
  const inputId = id ?? generatedId;
  const inputRef = useRef<HTMLInputElement>(null);
  const [dragOver, setDragOver] = useState(false);
  const [filePreview, setFilePreview] = useState<string | null>(null);
  const dragDepth = useRef(0);

  useEffect(() => {
    if (!file && inputRef.current) inputRef.current.value = '';
  }, [file]);

  useEffect(() => {
    if (!file) {
      setFilePreview(null);
      return;
    }
    const url = URL.createObjectURL(file);
    setFilePreview(url);
    return () => URL.revokeObjectURL(url);
  }, [file]);

  const emit = (files: File[]) => {
    if (disabled || files.length === 0) return;
    onFiles(multiple ? files : files.slice(0, 1));
  };

  const preview = filePreview ?? previewUrl ?? null;

  return (
    <label
      className={`image-upload${compact ? ' image-upload-compact' : ''}${dragOver ? ' is-dragover' : ''}${disabled ? ' is-disabled' : ''}`}
      title={
        file
          ? file.name
          : (placeholder ?? (multiple ? 'Choose, drop, or paste images' : 'Choose, drop, or paste an image'))
      }
      onDragEnter={(e) => {
        e.preventDefault();
        if (disabled) return;
        dragDepth.current += 1;
        setDragOver(true);
      }}
      onDragOver={(e) => {
        e.preventDefault();
        e.dataTransfer.dropEffect = disabled ? 'none' : 'copy';
      }}
      onDragLeave={() => {
        dragDepth.current -= 1;
        if (dragDepth.current <= 0) {
          dragDepth.current = 0;
          setDragOver(false);
        }
      }}
      onDrop={(e) => {
        e.preventDefault();
        e.stopPropagation();
        dragDepth.current = 0;
        setDragOver(false);
        emit(imageFilesFromDataTransfer(e.dataTransfer, accept));
      }}
      onPaste={(e) => {
        const files = imageFilesFromDataTransfer(e.clipboardData, accept);
        if (files.length === 0) return;
        e.preventDefault();
        e.stopPropagation();
        emit(files);
      }}
    >
      <input
        id={inputId}
        ref={inputRef}
        className="visually-hidden"
        type="file"
        accept={accept}
        multiple={multiple}
        disabled={disabled}
        onChange={(e) => {
          emit(imageFilesFromList(e.target.files, accept));
          e.target.value = '';
        }}
      />
      {preview ? <img className="image-upload-preview" src={preview} alt="" /> : fallback}
      <span className="image-upload-copy">
        {file
          ? file.name
          : (placeholder ??
            (multiple ? 'Choose, drop, or paste images' : 'Choose, drop, or paste an image'))}
      </span>
    </label>
  );
}

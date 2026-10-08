import {
  type FormEvent,
  type FormHTMLAttributes,
  type HTMLAttributes,
  type ReactNode,
  useState,
} from 'react';
import { IconButton } from './IconButton';

type RowTag = 'li' | 'div';

export function InlineEditRow({
  creating = false,
  canEdit = false,
  as: Tag = 'li',
  className = 'list-browser-entry',
  ignored = false,
  view,
  viewActions,
  includeEdit = true,
  includeRemove = true,
  preface,
  fieldsClassName = 'list-browser-entry-fields',
  afterFields,
  extraEditActions,
  saveDisabled = false,
  saveLabel = 'Save',
  onSave,
  onCancel,
  onRemove,
  removeLabel = 'Remove',
  formProps,
  children,
  ...rowProps
}: {
  creating?: boolean;
  canEdit?: boolean;
  as?: RowTag;
  className?: string;
  ignored?: boolean;
  view?: ReactNode;
  viewActions?: ReactNode | ((startEdit: () => void) => ReactNode);
  includeEdit?: boolean;
  includeRemove?: boolean;
  preface?: ReactNode;
  fieldsClassName?: string;
  afterFields?: ReactNode;
  extraEditActions?: ReactNode;
  saveDisabled?: boolean;
  saveLabel?: string;
  onSave: () => Promise<void> | void;
  onCancel?: () => void;
  onRemove?: () => void;
  removeLabel?: string;
  formProps?: FormHTMLAttributes<HTMLFormElement>;
  children: ReactNode;
} & HTMLAttributes<HTMLElement>) {
  const [editing, setEditing] = useState(creating);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const showEditor = creating || (editing && canEdit);

  const save = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    setBusy(true);
    try {
      await onSave();
      if (!creating) setEditing(false);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not save');
    } finally {
      setBusy(false);
    }
  };

  const cancel = () => {
    setError('');
    if (creating) {
      onCancel?.();
      return;
    }
    setEditing(false);
    onCancel?.();
  };

  const extraView = typeof viewActions === 'function' ? viewActions(() => setEditing(true)) : viewActions;
  const showViewActions = Boolean(
    extraView || (canEdit && includeEdit) || (canEdit && includeRemove && onRemove),
  );

  const actions = showEditor ? (
    <>
      <IconButton
        type="submit"
        variant="primary"
        icon="check"
        label={busy ? 'Saving' : saveLabel}
        disabled={busy || saveDisabled}
      />
      <IconButton icon="x" label="Cancel" disabled={busy} onClick={cancel} />
      {extraEditActions}
    </>
  ) : (
    <>
      {canEdit && includeEdit && (
        <IconButton icon="pencil" label="Edit" onClick={() => setEditing(true)} />
      )}
      {extraView}
      {canEdit && includeRemove && onRemove && (
        <IconButton icon="trash" label={removeLabel} variant="danger" onClick={onRemove} />
      )}
    </>
  );

  const body = (
    <>
      <div className="list-browser-entry-main">
        {showEditor ? (
          <>
            {error && <div className="error-banner">{error}</div>}
            {preface}
            <div className={fieldsClassName}>{children}</div>
          </>
        ) : (
          view
        )}
      </div>
      {(showEditor || showViewActions) && (
        <span className="list-browser-entry-actions">{actions}</span>
      )}
      {showEditor && afterFields ? <div className="list-browser-entry-extra">{afterFields}</div> : null}
    </>
  );

  return (
    <Tag
      className={`${className}${ignored ? ' is-ignored' : ''}${showEditor ? ' list-browser-entry-editing' : ''}`}
      {...rowProps}
    >
      {showEditor ? (
        <form
          className={`list-browser-entry-form${afterFields ? ' has-extra' : ''}`}
          {...formProps}
          onSubmit={save}
        >
          {body}
        </form>
      ) : (
        body
      )}
    </Tag>
  );
}

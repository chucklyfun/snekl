import { useEffect, useId, useRef } from 'react';
import { CreateForumComposer } from './CreateForumComposer';
import type { EligibleServer } from '../createForumEligibility';
import type { Forum } from '../types';

type CreateForumDialogProps = {
  open: boolean;
  eligibleServers: EligibleServer[];
  checkErrors: string[];
  permissionsLoading: boolean;
  urlServerDid?: string;
  urlServerCanCreate: boolean | null;
  urlServerCanRequest: boolean | null;
  onClose: () => void;
  onCreated: (forum: Forum, serverDid: string) => void;
};

export function CreateForumDialog({
  open,
  eligibleServers,
  checkErrors,
  permissionsLoading,
  urlServerDid,
  urlServerCanCreate,
  urlServerCanRequest,
  onClose,
  onCreated,
}: CreateForumDialogProps) {
  const titleId = useId();
  const dialogRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', onKey);
    queueMicrotask(() => dialogRef.current?.querySelector<HTMLElement>('input,select,textarea,button')?.focus());
    return () => window.removeEventListener('keydown', onKey);
  }, [open, onClose]);

  if (!open) return null;

  const canCreate = urlServerDid
    ? urlServerCanCreate === true
    : (eligibleServers[0]?.canCreateTopLevel ?? false);
  const title = canCreate ? 'New forum' : 'Request a forum';

  return (
    <div
      className="modal-overlay"
      role="presentation"
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div
        className="modal-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        ref={dialogRef}
      >
        <div className="modal-header">
          <h2 id={titleId} className="card-title" style={{ margin: 0 }}>
            {title}
          </h2>
          <button type="button" className="btn-ghost" onClick={onClose} aria-label="Close">
            Close
          </button>
        </div>
        <CreateForumComposer
          eligibleServers={eligibleServers}
          checkErrors={checkErrors}
          permissionsLoading={permissionsLoading}
          urlServerDid={urlServerDid}
          urlServerCanCreate={urlServerCanCreate}
          urlServerCanRequest={urlServerCanRequest}
          showHeading={false}
          onCreated={onCreated}
          onCancel={onClose}
        />
      </div>
    </div>
  );
}

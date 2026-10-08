import {
  useEffect,
  useMemo,
  useRef,
  useState,
  type CSSProperties,
  type DragEvent as ReactDragEvent,
  type KeyboardEvent as ReactKeyboardEvent,
  type PointerEvent as ReactPointerEvent,
} from 'react';
import type { ExtensionInstall, ForumPageRegion, WatercoolerSummary } from '../types';
import {
  CHROME_LAYOUT_VIEW_OPTIONS,
  FORUM_LAYOUT_VIEW_OPTIONS,
  PAGE_LAYOUT_VIEW_OPTIONS,
  addChildRegion,
  cloneRegion,
  clampedColSpan,
  containerGapPx,
  createDefaultChromeLayout,
  createDefaultHomeLayout,
  countType,
  deleteRegion,
  findParent,
  findRegion,
  getChildSize,
  GRID_MAX_COLUMNS,
  GRID_MIN_COLUMNS,
  gridColumnCount,
  insertTypeAt,
  isContainerType,
  layoutCanSave,
  layoutWarnings,
  moveRegion,
  moveRegionTo,
  resizePairInTree,
  setChildSize,
  setRegionSetting,
  setRegionType,
  splitRegion,
  stackBelowPx,
  EXTENSION_VIEWS,
} from '../forumLayoutEditor';
import './ForumLayout.css';

const extensionViewLabel = {
  storefront: 'Storefront',
  studio: 'My studio',
  desk: 'My desk',
  requests: 'My requests',
} as const;

const DND_TYPE = 'application/x-snekl-layout-type';
const DND_REGION = 'application/x-snekl-layout-region';

type DropTarget = { parentId: string; index: number };

export function ForumLayoutEditor({
  initialLayout,
  watercoolers,
  busy,
  mode = 'page',
  contentBaseUrl,
  loadExtensions,
  onSave,
}: {
  initialLayout: ForumPageRegion;
  watercoolers: WatercoolerSummary[];
  busy?: boolean;
  /** page = home/extra page body; chrome = locked shared sections around every page */
  mode?: 'page' | 'chrome';
  contentBaseUrl?: string | null;
  loadExtensions?: () => Promise<ExtensionInstall[]>;
  onSave: (layout: ForumPageRegion) => Promise<void>;
}) {
  const [root, setRoot] = useState(() => cloneRegion(initialLayout));
  const [selectedId, setSelectedId] = useState(initialLayout.id);
  const [error, setError] = useState('');
  const [previewWidth, setPreviewWidth] = useState(900);
  const [dropTarget, setDropTarget] = useState<DropTarget | null>(null);
  const [dragPayload, setDragPayload] = useState<
    { kind: 'type'; typeKey: string } | { kind: 'region'; regionId: string } | null
  >(null);
  const editorRef = useRef<HTMLDivElement>(null);
  const [installs, setInstalls] = useState<ExtensionInstall[]>([]);
  const [artists, setArtists] = useState<{ userId: string; username: string; displayName: string }[]>([]);
  const selected = findRegion(root, selectedId) ?? root;
  const warnings = useMemo(() => layoutWarnings(root, mode), [root, mode]);
  const canSave = layoutCanSave(root, mode);
  const selectedSize = getChildSize(selected);
  const parentOfSelected = findParent(root, selected.id);
  const viewOptions = mode === 'chrome' ? CHROME_LAYOUT_VIEW_OPTIONS : PAGE_LAYOUT_VIEW_OPTIONS;

  const loadExtensionsRef = useRef(loadExtensions);
  loadExtensionsRef.current = loadExtensions;
  useEffect(() => {
    const load = loadExtensionsRef.current;
    if (!load) return;
    load()
      .then(setInstalls)
      .catch(() => setInstalls([]));
  }, [contentBaseUrl]);

  const commissionsInstall = installs.find((item) => item.clientId === 'snekl-commissions' && item.iframeUrl);

  useEffect(() => {
    if (!commissionsInstall?.iframeUrl || !contentBaseUrl) return;
    const url = `${commissionsInstall.iframeUrl.replace(/\/?$/, '/')}api/artists?content=${encodeURIComponent(contentBaseUrl.replace(/\/$/, ''))}`;
    fetch(url)
      .then((res) => (res.ok ? res.json() : []))
      .then((rows: { userId: string; username: string; displayName: string }[]) => setArtists(rows))
      .catch(() => setArtists([]));
  }, [commissionsInstall?.iframeUrl, contentBaseUrl]);

  const labelFor = (typeKey: string) =>
    FORUM_LAYOUT_VIEW_OPTIONS.find((o) => o.typeKey === typeKey)?.label ?? typeKey;

  const applyDrop = (target: DropTarget, dataTransfer: DataTransfer) => {
    const typeKey = dataTransfer.getData(DND_TYPE) || (dragPayload?.kind === 'type' ? dragPayload.typeKey : '');
    const regionId =
      dataTransfer.getData(DND_REGION) || (dragPayload?.kind === 'region' ? dragPayload.regionId : '');
    if (typeKey) {
      if (!viewOptions.some((o) => o.typeKey === typeKey)) return;
      const inserted = insertTypeAt(root, typeKey, target.parentId, target.index);
      setRoot(inserted.root);
      setSelectedId(inserted.regionId);
      setDragPayload(null);
      return;
    }
    if (regionId) {
      setRoot((prev) => moveRegionTo(prev, regionId, target.parentId, target.index));
      setSelectedId(regionId);
    }
    setDragPayload(null);
  };

  const removeSelected = () => {
    if (selected.id === root.id) return;
    if (mode === 'chrome' && selected.typeKey === 'page-body' && countType(root, 'page-body') <= 1) {
      setError('Shared chrome must keep exactly one page-body slot.');
      return;
    }
    setRoot((prev) => deleteRegion(prev, selected.id));
    setSelectedId(root.id);
  };

  const onEditorKeyDown = (e: ReactKeyboardEvent) => {
    if (busy) return;
    const tag = (e.target as HTMLElement | null)?.tagName;
    if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return;
    if (e.key === 'Delete' || e.key === 'Backspace') {
      if (selected.id === root.id) return;
      e.preventDefault();
      removeSelected();
    }
  };

  return (
    <div
      ref={editorRef}
      className="forum-layout-editor"
      tabIndex={0}
      onKeyDown={onEditorKeyDown}
    >
      <p className="card-meta">
        {mode === 'chrome'
          ? 'Lock header, sidebars, or other views around every page. Keep exactly one page-body slot — try columns with page-body on the left and cooler-list / subforum-grid on the right.'
          : 'Drag views from the palette onto the canvas. Drag regions to reorder. Delete/Backspace removes the selection (not the root). Soft warnings do not block save.'}
      </p>

      <div className="form-group">
        <label htmlFor="layout-preview-width">Preview width ({previewWidth}px)</label>
        <input
          id="layout-preview-width"
          type="range"
          min={320}
          max={1200}
          value={previewWidth}
          disabled={busy}
          onChange={(e) => setPreviewWidth(Number(e.target.value))}
        />
      </div>

      <div className="forum-layout-editor-grid">
        <div className="card forum-layout-palette" style={{ padding: '0.75rem' }}>
          <h3 className="card-title" style={{ fontSize: '1rem' }}>
            Add
          </h3>
          <p className="card-meta">Drag onto a stack, columns, or grid region.</p>
          <ul className="forum-layout-palette-list">
            {viewOptions.map((opt) => (
              <li key={opt.typeKey}>
                <button
                  type="button"
                  className="btn-ghost forum-layout-palette-item"
                  draggable={!busy}
                  disabled={busy}
                  onDragStart={(e) => {
                    e.dataTransfer.setData(DND_TYPE, opt.typeKey);
                    e.dataTransfer.setData('text/plain', opt.typeKey);
                    e.dataTransfer.effectAllowed = 'copy';
                    setDragPayload({ kind: 'type', typeKey: opt.typeKey });
                  }}
                  onDragEnd={() => setDragPayload(null)}
                >
                  {opt.label}
                </button>
              </li>
            ))}
          </ul>
        </div>

        <div className="forum-layout-editor-canvas-wrap">
          <div className="forum-layout-editor-canvas" style={{ width: previewWidth }}>
            <LayoutCanvas
              region={root}
              rootId={root.id}
              selectedId={selected.id}
              labelFor={labelFor}
              previewWidth={previewWidth}
              busy={busy}
              dropTarget={dropTarget}
              dragging={Boolean(dragPayload)}
              onSelect={setSelectedId}
              onRegionDragStart={(regionId) => setDragPayload({ kind: 'region', regionId })}
              onRegionDragEnd={() => setDragPayload(null)}
              onDropTarget={setDropTarget}
              onDrop={(target, dt) => {
                applyDrop(target, dt);
                setDropTarget(null);
              }}
              onResizePair={(parentId, leftId, rightId, deltaPx, width) => {
                setRoot((prev) => resizePairInTree(prev, parentId, leftId, rightId, deltaPx, width));
              }}
            />
          </div>
        </div>

        <div className="card" style={{ padding: '0.75rem' }}>
          <h3 className="card-title" style={{ fontSize: '1rem' }}>
            Selected region
          </h3>
          <div className="form-group">
            <label htmlFor="layout-type">View / container</label>
            <select
              id="layout-type"
              value={selected.typeKey}
              disabled={busy}
              onChange={(e) => {
                setRoot((prev) => setRegionType(prev, selected.id, e.target.value));
              }}
            >
              {viewOptions.map((opt) => (
                <option key={opt.typeKey} value={opt.typeKey}>
                  {opt.label}
                </option>
              ))}
            </select>
          </div>

          {selected.typeKey === 'page-body' && (
            <p className="card-meta">
              Page body is the slot where each page’s layout is injected. Keep exactly one in shared chrome.
            </p>
          )}

          {parentOfSelected?.typeKey === 'grid' && (
            <div className="form-group">
              <label htmlFor="layout-col-span">
                Column span (of {gridColumnCount(parentOfSelected)})
              </label>
              <input
                id="layout-col-span"
                type="number"
                min={1}
                max={gridColumnCount(parentOfSelected)}
                value={clampedColSpan(selected, gridColumnCount(parentOfSelected))}
                disabled={busy}
                onChange={(e) => {
                  const value = Number(e.target.value);
                  const max = gridColumnCount(parentOfSelected);
                  if (!Number.isInteger(value) || value < 1 || value > max) return;
                  setRoot((prev) => setRegionSetting(prev, selected.id, 'colSpan', value));
                }}
              />
            </div>
          )}

          {parentOfSelected && parentOfSelected.typeKey !== 'grid' && (
            <div className="form-group">
              <label htmlFor="layout-size-mode">Size in parent</label>
              <div className="inline-form" style={{ gap: '0.5rem', flexWrap: 'wrap' }}>
                <select
                  id="layout-size-mode"
                  value={selectedSize.mode}
                  disabled={busy}
                  onChange={(e) =>
                    setRoot((prev) =>
                      setChildSize(prev, selected.id, {
                        mode: e.target.value === 'px' ? 'px' : 'fr',
                        size: e.target.value === 'px' ? 240 : 1,
                      }),
                    )
                  }
                >
                  <option value="fr">Flexible (fr)</option>
                  <option value="px">Fixed (px)</option>
                </select>
                <input
                  type="number"
                  min={selectedSize.mode === 'px' ? 40 : 0.1}
                  step={selectedSize.mode === 'px' ? 1 : 0.1}
                  value={selectedSize.size}
                  disabled={busy}
                  aria-label="Size value"
                  onChange={(e) => {
                    const value = Number(e.target.value);
                    if (!(value > 0)) return;
                    setRoot((prev) => setChildSize(prev, selected.id, { size: value }));
                  }}
                />
              </div>
            </div>
          )}

          {selected.typeKey === 'grid' && (
            <div className="form-group">
              <label htmlFor="layout-grid-columns">Columns</label>
              <input
                id="layout-grid-columns"
                type="number"
                min={GRID_MIN_COLUMNS}
                max={GRID_MAX_COLUMNS}
                value={gridColumnCount(selected)}
                disabled={busy}
                onChange={(e) => {
                  const value = Number(e.target.value);
                  if (!Number.isInteger(value) || value < GRID_MIN_COLUMNS || value > GRID_MAX_COLUMNS) return;
                  setRoot((prev) => setRegionSetting(prev, selected.id, 'columns', value));
                }}
              />
            </div>
          )}

          {(selected.typeKey === 'columns' || selected.typeKey === 'grid') && (
            <div className="form-group">
              <label htmlFor="layout-stack-below">
                {selected.typeKey === 'grid'
                  ? 'Collapse grid below (px, 0 = never)'
                  : 'Collapse columns below (px, 0 = never)'}
              </label>
              <input
                id="layout-stack-below"
                type="number"
                min={0}
                value={stackBelowPx(selected)}
                disabled={busy}
                onChange={(e) => {
                  const value = Number(e.target.value);
                  if (Number.isNaN(value) || value < 0) return;
                  setRoot((prev) => setRegionSetting(prev, selected.id, 'stackBelow', value));
                }}
              />
            </div>
          )}

          {(selected.typeKey === 'columns' || selected.typeKey === 'stack' || selected.typeKey === 'grid') && (
            <div className="form-group">
              <label htmlFor="layout-gap">Gap (px)</label>
              <input
                id="layout-gap"
                type="number"
                min={0}
                value={containerGapPx(selected)}
                disabled={busy}
                onChange={(e) => {
                  const value = Number(e.target.value);
                  if (Number.isNaN(value) || value < 0) return;
                  setRoot((prev) => setRegionSetting(prev, selected.id, 'gap', value));
                }}
              />
            </div>
          )}

          {selected.typeKey === 'forum-header' && (
            <>
              <label className="inline-form" style={{ gap: '0.5rem' }}>
                <input
                  type="checkbox"
                  checked={selected.settings?.showBanner !== false}
                  disabled={busy}
                  onChange={(e) =>
                    setRoot((prev) => setRegionSetting(prev, selected.id, 'showBanner', e.target.checked))
                  }
                />
                Show banner
              </label>
              <label className="inline-form" style={{ gap: '0.5rem', marginTop: '0.5rem' }}>
                <input
                  type="checkbox"
                  checked={selected.settings?.showDescription !== false}
                  disabled={busy}
                  onChange={(e) =>
                    setRoot((prev) =>
                      setRegionSetting(prev, selected.id, 'showDescription', e.target.checked),
                    )
                  }
                />
                Show description
              </label>
            </>
          )}

          {selected.typeKey === 'watercooler-view' && (
            <div className="form-group">
              <label htmlFor="layout-cooler">Watercooler</label>
              <select
                id="layout-cooler"
                value={typeof selected.settings?.watercoolerId === 'string' ? selected.settings.watercoolerId : ''}
                disabled={busy}
                onChange={(e) =>
                  setRoot((prev) => setRegionSetting(prev, selected.id, 'watercoolerId', e.target.value || null))
                }
              >
                <option value="">Select a room…</option>
                {watercoolers.map((cooler) => (
                  <option key={cooler.id} value={cooler.id}>
                    {cooler.name}
                  </option>
                ))}
              </select>
            </div>
          )}

          {selected.typeKey === 'extension' && (
            <>
              <div className="form-group">
                <label htmlFor="layout-extension">Extension</label>
                <select
                  id="layout-extension"
                  value={typeof selected.settings?.clientId === 'string' ? selected.settings.clientId : ''}
                  disabled={busy}
                  onChange={(e) =>
                    setRoot((prev) => setRegionSetting(prev, selected.id, 'clientId', e.target.value))
                  }
                >
                  <option value="">Select an install…</option>
                  {installs
                    .filter((item) => item.iframeUrl)
                    .map((item) => (
                      <option key={item.clientId} value={item.clientId}>
                        {item.appName}
                      </option>
                    ))}
                </select>
              </div>
              <div className="form-group">
                <label htmlFor="layout-extension-view">Page</label>
                <select
                  id="layout-extension-view"
                  value={typeof selected.settings?.view === 'string' ? selected.settings.view : 'storefront'}
                  disabled={busy}
                  onChange={(e) => setRoot((prev) => setRegionSetting(prev, selected.id, 'view', e.target.value))}
                >
                  {EXTENSION_VIEWS.map((view) => (
                    <option key={view} value={view}>
                      {extensionViewLabel[view]}
                    </option>
                  ))}
                </select>
              </div>
              {selected.settings?.view === 'storefront' && (
                <div className="form-group">
                  <label htmlFor="layout-artist">Artist</label>
                  <select
                    id="layout-artist"
                    value={typeof selected.settings?.artistUserId === 'string' ? selected.settings.artistUserId : ''}
                    disabled={busy}
                    onChange={(e) =>
                      setRoot((prev) => setRegionSetting(prev, selected.id, 'artistUserId', e.target.value))
                    }
                  >
                    <option value="">Select an artist…</option>
                    {artists.map((artist) => (
                      <option key={artist.userId} value={artist.userId}>
                        {artist.displayName} (@{artist.username})
                      </option>
                    ))}
                  </select>
                </div>
              )}
            </>
          )}

          {selected.typeKey === 'forum-view' && (
            <div className="form-group">
              <label htmlFor="layout-sort">Default sort</label>
              <select
                id="layout-sort"
                value={typeof selected.settings?.sort === 'string' ? selected.settings.sort : 'hot'}
                disabled={busy}
                onChange={(e) => setRoot((prev) => setRegionSetting(prev, selected.id, 'sort', e.target.value))}
              >
                <option value="hot">Hot</option>
                <option value="new">New</option>
              </select>
            </div>
          )}

          <div className="server-row-actions" style={{ marginTop: '0.75rem', flexWrap: 'wrap' }}>
            <button
              type="button"
              className="btn-ghost"
              disabled={busy}
              onClick={() => setRoot((prev) => splitRegion(prev, selected.id, 'stack'))}
            >
              Split vertical
            </button>
            <button
              type="button"
              className="btn-ghost"
              disabled={busy}
              onClick={() => setRoot((prev) => splitRegion(prev, selected.id, 'columns'))}
            >
              Split columns
            </button>
            <button
              type="button"
              className="btn-ghost"
              disabled={busy}
              onClick={() => setRoot((prev) => splitRegion(prev, selected.id, 'grid'))}
            >
              Split grid
            </button>
            {isContainerType(selected.typeKey) && (
              <button
                type="button"
                className="btn-ghost"
                disabled={busy}
                onClick={() => setRoot((prev) => addChildRegion(prev, selected.id))}
              >
                Add region
              </button>
            )}
            <button
              type="button"
              className="btn-ghost"
              disabled={busy}
              onClick={() => setRoot((prev) => moveRegion(prev, selected.id, -1))}
            >
              Move up
            </button>
            <button
              type="button"
              className="btn-ghost"
              disabled={busy}
              onClick={() => setRoot((prev) => moveRegion(prev, selected.id, 1))}
            >
              Move down
            </button>
            {selected.id !== root.id && (
              <button type="button" className="btn-ghost" disabled={busy} onClick={removeSelected}>
                Delete
              </button>
            )}
          </div>
        </div>
      </div>

      {warnings.length > 0 && (
        <ul className="card-meta" style={{ marginTop: '0.75rem' }}>
          {warnings.map((w) => (
            <li key={w}>{w}</li>
          ))}
        </ul>
      )}
      {error && <div className="error-banner">{error}</div>}

      <div className="server-row-actions" style={{ marginTop: '1rem' }}>
        <button
          type="button"
          className="btn-primary"
          disabled={busy || !canSave}
          onClick={() => {
            setError('');
            void onSave(root).catch((err) =>
              setError(err instanceof Error ? err.message : 'Could not save layout'),
            );
          }}
        >
          {busy ? 'Saving…' : 'Save layout'}
        </button>
        <button
          type="button"
          className="btn-ghost"
          disabled={busy}
          onClick={() => {
            const next =
              mode === 'chrome'
                ? createDefaultChromeLayout()
                : createDefaultHomeLayout(watercoolers[0]?.id ?? null);
            setRoot(next);
            setSelectedId(next.id);
          }}
        >
          Reset to default
        </button>
      </div>
    </div>
  );
}

type CanvasProps = {
  region: ForumPageRegion;
  rootId: string;
  selectedId: string;
  labelFor: (typeKey: string) => string;
  previewWidth: number;
  busy?: boolean;
  dropTarget: DropTarget | null;
  dragging: boolean;
  onSelect: (id: string) => void;
  onRegionDragStart: (regionId: string) => void;
  onRegionDragEnd: () => void;
  onDropTarget: (target: DropTarget | null) => void;
  onDrop: (target: DropTarget, dataTransfer: DataTransfer) => void;
  onResizePair: (
    parentId: string,
    leftId: string,
    rightId: string,
    deltaPx: number,
    containerWidth: number,
  ) => void;
};

function acceptDrag(e: ReactDragEvent, dragging: boolean) {
  if (
    dragging ||
    [...e.dataTransfer.types].includes(DND_TYPE) ||
    [...e.dataTransfer.types].includes(DND_REGION) ||
    [...e.dataTransfer.types].includes('text/plain')
  ) {
    e.preventDefault();
    e.dataTransfer.dropEffect = [...e.dataTransfer.types].includes(DND_TYPE) ? 'copy' : 'move';
    return true;
  }
  return false;
}

function DropSlot({
  parentId,
  index,
  active,
  vertical,
  busy,
  dragging,
  onDropTarget,
  onDrop,
}: {
  parentId: string;
  index: number;
  active: boolean;
  vertical?: boolean;
  busy?: boolean;
  dragging: boolean;
  onDropTarget: (target: DropTarget | null) => void;
  onDrop: (target: DropTarget, dataTransfer: DataTransfer) => void;
}) {
  const target = { parentId, index };
  return (
    <div
      className={`forum-layout-drop-slot${vertical ? ' is-vertical' : ''}${active ? ' is-active' : ''}`}
      onDragEnter={(e) => {
        if (busy || !acceptDrag(e, dragging)) return;
        onDropTarget(target);
      }}
      onDragOver={(e) => {
        if (busy || !acceptDrag(e, dragging)) return;
        onDropTarget(target);
      }}
      onDragLeave={() => onDropTarget(null)}
      onDrop={(e) => {
        if (busy) return;
        e.preventDefault();
        e.stopPropagation();
        onDrop(target, e.dataTransfer);
      }}
    />
  );
}

function LayoutCanvas(props: CanvasProps) {
  const {
    region,
    rootId,
    selectedId,
    labelFor,
    previewWidth,
    busy,
    dropTarget,
    dragging,
    onSelect,
    onRegionDragStart,
    onRegionDragEnd,
    onDropTarget,
    onDrop,
  } = props;
  const selected = region.id === selectedId;
  const kids = region.children ?? [];
  const canDrag = region.id !== rootId && !busy;

  const boxProps = {
    className: `forum-layout-editor-box${selected ? ' is-selected' : ''}${
      dropTarget?.parentId === region.id && isContainerType(region.typeKey) ? ' is-drop-hover' : ''
    }`,
    draggable: canDrag,
    onDragStart: (e: ReactDragEvent) => {
      if (!canDrag) return;
      e.stopPropagation();
      e.dataTransfer.setData(DND_REGION, region.id);
      e.dataTransfer.setData('text/plain', region.id);
      e.dataTransfer.effectAllowed = 'move';
      onRegionDragStart(region.id);
    },
    onDragEnd: () => onRegionDragEnd(),
    onClick: (e: React.MouseEvent) => {
      e.stopPropagation();
      onSelect(region.id);
    },
    onDragOver: (e: ReactDragEvent) => {
      if (!isContainerType(region.typeKey) || busy) return;
      if (!acceptDrag(e, dragging)) return;
      e.stopPropagation();
      onDropTarget({ parentId: region.id, index: kids.length });
    },
    onDrop: (e: ReactDragEvent) => {
      if (!isContainerType(region.typeKey) || busy) return;
      e.preventDefault();
      e.stopPropagation();
      onDrop({ parentId: region.id, index: kids.length }, e.dataTransfer);
    },
  };

  if (region.typeKey === 'columns') {
    const gap = containerGapPx(region);
    const stackBelow = stackBelowPx(region);
    const collapsed = stackBelow > 0 && previewWidth < stackBelow;

    return (
      <div {...boxProps}>
        <div className="forum-layout-editor-box-label">{labelFor(region.typeKey)}</div>
        <div
          style={{
            display: 'flex',
            flexDirection: collapsed ? 'column' : 'row',
            alignItems: 'stretch',
            gap: collapsed ? `${gap}px` : 0,
            padding: '0.25rem',
            minHeight: '4rem',
          }}
        >
          <DropSlot
            parentId={region.id}
            index={0}
            active={dropTarget?.parentId === region.id && dropTarget.index === 0}
            vertical={!collapsed}
            busy={busy}
            dragging={dragging}
            onDropTarget={onDropTarget}
            onDrop={onDrop}
          />
          {kids.map((child, index) => (
            <ColumnsChildWithGutter
              key={child.id}
              {...props}
              parent={region}
              child={child}
              next={collapsed ? undefined : kids[index + 1]}
              collapsed={collapsed}
              insertIndex={index + 1}
            />
          ))}
        </div>
      </div>
    );
  }

  if (region.typeKey === 'grid') {
    const gap = containerGapPx(region);
    const stackBelow = stackBelowPx(region);
    const collapsed = stackBelow > 0 && previewWidth < stackBelow;
    const columns = collapsed ? 1 : gridColumnCount(region);

    return (
      <div {...boxProps}>
        <div className="forum-layout-editor-box-label">{labelFor(region.typeKey)}</div>
        <div
          style={{
            display: 'grid',
            gridTemplateColumns: `repeat(${columns}, minmax(0, 1fr))`,
            gap: `${gap}px`,
            padding: '0.25rem',
            minHeight: '4rem',
          }}
        >
          {kids.map((child, index) => {
            const insertBefore =
              dropTarget?.parentId === region.id && dropTarget.index === index;
            return (
              <div
                key={child.id}
                className={`forum-layout-editor-grid-cell${insertBefore ? ' is-drop-before' : ''}`}
                style={{ gridColumn: `span ${clampedColSpan(child, columns)}` }}
                onDragEnter={(e) => {
                  if (busy || !acceptDrag(e, dragging)) return;
                  e.stopPropagation();
                  onDropTarget({ parentId: region.id, index });
                }}
                onDragOver={(e) => {
                  if (busy || !acceptDrag(e, dragging)) return;
                  e.stopPropagation();
                  onDropTarget({ parentId: region.id, index });
                }}
                onDrop={(e) => {
                  if (busy) return;
                  e.preventDefault();
                  e.stopPropagation();
                  onDrop({ parentId: region.id, index }, e.dataTransfer);
                }}
              >
                <LayoutCanvas {...props} region={child} />
              </div>
            );
          })}
        </div>
      </div>
    );
  }

  if (region.typeKey === 'stack') {
    const gap = containerGapPx(region);
    return (
      <div {...boxProps}>
        <div className="forum-layout-editor-box-label">{labelFor(region.typeKey)}</div>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 0, padding: '0.25rem' }}>
          <DropSlot
            parentId={region.id}
            index={0}
            active={dropTarget?.parentId === region.id && dropTarget.index === 0}
            busy={busy}
            dragging={dragging}
            onDropTarget={onDropTarget}
            onDrop={onDrop}
          />
          {kids.map((child, index) => {
            const size = getChildSize(child);
            const style: CSSProperties =
              size.mode === 'px' ? { minHeight: `${size.size}px` } : { minHeight: '2.5rem' };
            return (
              <div key={child.id} style={{ display: 'flex', flexDirection: 'column', gap: `${gap}px` }}>
                <div style={style}>
                  <LayoutCanvas {...props} region={child} />
                </div>
                <DropSlot
                  parentId={region.id}
                  index={index + 1}
                  active={dropTarget?.parentId === region.id && dropTarget.index === index + 1}
                  busy={busy}
                  dragging={dragging}
                  onDropTarget={onDropTarget}
                  onDrop={onDrop}
                />
              </div>
            );
          })}
        </div>
      </div>
    );
  }

  return (
    <div
      {...boxProps}
      style={{ minHeight: getChildSize(region).mode === 'px' ? `${getChildSize(region).size}px` : '2.75rem' }}
    >
      <div className="forum-layout-editor-box-label">{labelFor(region.typeKey)}</div>
    </div>
  );
}

function ColumnsChildWithGutter({
  parent,
  child,
  next,
  collapsed,
  insertIndex,
  selectedId,
  labelFor,
  previewWidth,
  busy,
  dropTarget,
  rootId,
  dragging,
  onSelect,
  onRegionDragStart,
  onRegionDragEnd,
  onDropTarget,
  onDrop,
  onResizePair,
}: CanvasProps & {
  parent: ForumPageRegion;
  child: ForumPageRegion;
  next?: ForumPageRegion;
  collapsed?: boolean;
  insertIndex: number;
}) {
  const dragRef = useRef<{ startX: number; lastX: number } | null>(null);
  const [gutterDragging, setGutterDragging] = useState(false);
  const size = getChildSize(child);
  const childStyle: CSSProperties = collapsed
    ? { width: '100%', minWidth: 0 }
    : size.mode === 'px'
      ? { flex: `0 0 ${size.size}px`, minWidth: 0 }
      : { flex: `${size.size} ${size.size} 0`, minWidth: 0 };

  useEffect(() => {
    if (!gutterDragging) return;
    const onMove = (e: PointerEvent) => {
      const state = dragRef.current;
      if (!state || !next) return;
      const delta = e.clientX - state.lastX;
      state.lastX = e.clientX;
      if (delta !== 0) onResizePair(parent.id, child.id, next.id, delta, previewWidth);
    };
    const onUp = () => {
      dragRef.current = null;
      setGutterDragging(false);
    };
    window.addEventListener('pointermove', onMove);
    window.addEventListener('pointerup', onUp);
    return () => {
      window.removeEventListener('pointermove', onMove);
      window.removeEventListener('pointerup', onUp);
    };
  }, [gutterDragging, child.id, next, onResizePair, parent.id, previewWidth]);

  return (
    <>
      <div style={childStyle}>
        <LayoutCanvas
          region={child}
          rootId={rootId}
          selectedId={selectedId}
          labelFor={labelFor}
          previewWidth={previewWidth}
          busy={busy}
          dropTarget={dropTarget}
          dragging={dragging}
          onSelect={onSelect}
          onRegionDragStart={onRegionDragStart}
          onRegionDragEnd={onRegionDragEnd}
          onDropTarget={onDropTarget}
          onDrop={onDrop}
          onResizePair={onResizePair}
        />
      </div>
      {next && !collapsed && (
        <div
          className={`forum-layout-editor-gutter${gutterDragging ? ' is-dragging' : ''}`}
          role="separator"
          aria-orientation="vertical"
          onPointerDown={(e: ReactPointerEvent) => {
            if (busy) return;
            e.preventDefault();
            e.stopPropagation();
            dragRef.current = { startX: e.clientX, lastX: e.clientX };
            setGutterDragging(true);
          }}
        />
      )}
      <DropSlot
        parentId={parent.id}
        index={insertIndex}
        active={dropTarget?.parentId === parent.id && dropTarget.index === insertIndex}
        vertical={!collapsed}
        busy={busy}
        dragging={dragging}
        onDropTarget={onDropTarget}
        onDrop={onDrop}
      />
    </>
  );
}

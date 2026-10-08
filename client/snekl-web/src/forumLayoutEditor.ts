import type { ForumPageRegion } from './types';

export const FORUM_LAYOUT_VIEW_OPTIONS: { typeKey: string; label: string; kind: 'container' | 'leaf' }[] = [
  { typeKey: 'stack', label: 'Stack (vertical)', kind: 'container' },
  { typeKey: 'columns', label: 'Columns (horizontal)', kind: 'container' },
  { typeKey: 'grid', label: 'Grid', kind: 'container' },
  { typeKey: 'forum-header', label: 'Nest header', kind: 'leaf' },
  { typeKey: 'page-body', label: 'Page body (slot)', kind: 'leaf' },
  { typeKey: 'cooler-list', label: 'Watercooler list', kind: 'leaf' },
  { typeKey: 'watercooler-view', label: 'Watercooler view', kind: 'leaf' },
  { typeKey: 'subforum-grid', label: 'Subnest grid', kind: 'leaf' },
  { typeKey: 'forum-view', label: 'Nest threads', kind: 'leaf' },
  { typeKey: 'extension', label: 'Extension', kind: 'leaf' },
];

export const EXTENSION_VIEWS = ['storefront', 'studio', 'desk', 'requests'] as const;
export type ExtensionView = (typeof EXTENSION_VIEWS)[number];

/** Views placeable on a page layout (not shared chrome). */
export const PAGE_LAYOUT_VIEW_OPTIONS = FORUM_LAYOUT_VIEW_OPTIONS.filter(
  (o) => o.typeKey !== 'page-body',
);

/**
 * Shared chrome can lock any placeable view around the page-body slot
 * (header, sidebars with cooler-list / subforum-grid, pinned watercooler, …).
 */
export const CHROME_LAYOUT_VIEW_OPTIONS = FORUM_LAYOUT_VIEW_OPTIONS;

export function newRegionId(): string {
  if (typeof crypto !== 'undefined' && 'randomUUID' in crypto) return crypto.randomUUID();
  return `region-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

export function cloneRegion(region: ForumPageRegion): ForumPageRegion {
  return {
    id: region.id,
    typeKey: region.typeKey,
    settings: region.settings ? { ...region.settings } : {},
    children: region.children?.map(cloneRegion) ?? null,
  };
}

export function createLeaf(typeKey: string, settings: Record<string, unknown> = {}): ForumPageRegion {
  return {
    id: newRegionId(),
    typeKey,
    settings: { sizeMode: 'fr', size: 1, ...settings },
    children: null,
  };
}

export type RegionSizeMode = 'fr' | 'px';

export interface RegionSize {
  mode: RegionSizeMode;
  size: number;
}

export function getChildSize(region: ForumPageRegion): RegionSize {
  const mode = region.settings?.sizeMode === 'px' ? 'px' : 'fr';
  const raw = region.settings?.size;
  const size = typeof raw === 'number' && raw > 0 ? raw : mode === 'px' ? 200 : 1;
  return { mode, size };
}

export function setChildSize(
  root: ForumPageRegion,
  id: string,
  next: Partial<RegionSize>,
): ForumPageRegion {
  return updateRegion(root, id, (region) => {
    const current = getChildSize(region);
    const mode = next.mode ?? current.mode;
    const size = next.size ?? current.size;
    if (!(size > 0)) return region;
    return {
      ...region,
      settings: { ...(region.settings ?? {}), sizeMode: mode, size },
    };
  });
}

export function columnsTemplate(children: ForumPageRegion[]): string {
  if (children.length === 0) return 'minmax(0, 1fr)';
  return children
    .map((child) => {
      const { mode, size } = getChildSize(child);
      return mode === 'px' ? `${size}px` : `minmax(0, ${size}fr)`;
    })
    .join(' ');
}

export function stackBelowPx(container: ForumPageRegion): number {
  const raw = container.settings?.stackBelow;
  if (typeof raw === 'number' && raw >= 0) return raw;
  return 720;
}

export function containerGapPx(container: ForumPageRegion): number {
  const raw = container.settings?.gap;
  if (typeof raw === 'number' && raw >= 0) return raw;
  return 16;
}

export const GRID_MIN_COLUMNS = 1;
export const GRID_MAX_COLUMNS = 12;

export type LayoutSplit = 'stack' | 'columns' | 'grid';

export function defaultContainerSettings(typeKey: string): Record<string, unknown> {
  if (typeKey === 'columns') return { gap: 16, stackBelow: 720 };
  if (typeKey === 'grid') return { columns: 2, gap: 16, stackBelow: 720 };
  if (typeKey === 'stack') return { gap: 16 };
  return {};
}

/** Column count for a grid container. Missing or invalid values fall back to 2. */
export function gridColumnCount(container: ForumPageRegion): number {
  const raw = container.settings?.columns;
  if (typeof raw !== 'number' || !Number.isFinite(raw)) return 2;
  const columns = Math.floor(raw);
  if (columns < GRID_MIN_COLUMNS) return 2;
  return Math.min(GRID_MAX_COLUMNS, columns);
}

/** How many grid columns a child occupies. Missing values are 1. */
export function getColSpan(region: ForumPageRegion): number {
  const raw = region.settings?.colSpan;
  if (typeof raw !== 'number' || !Number.isFinite(raw) || raw < 1) return 1;
  return Math.floor(raw);
}

export function clampedColSpan(region: ForumPageRegion, columns: number): number {
  const cols = Math.max(1, Math.floor(columns));
  return Math.min(getColSpan(region), cols);
}

/**
 * Apply a horizontal (or vertical) gutter drag between two sibling regions.
 * Both fr: redistribute weights keeping sum stable.
 * Involving px: adjust the fixed side; fr sibling keeps its fr weight.
 */
export function resizePair(
  parent: ForumPageRegion,
  leftId: string,
  rightId: string,
  deltaPx: number,
  containerWidth: number,
): ForumPageRegion {
  const children = parent.children ?? [];
  const leftIdx = children.findIndex((c) => c.id === leftId);
  const rightIdx = children.findIndex((c) => c.id === rightId);
  if (leftIdx < 0 || rightIdx < 0 || leftIdx === rightIdx) return cloneRegion(parent);

  const left = cloneRegion(children[leftIdx]!);
  const right = cloneRegion(children[rightIdx]!);
  const leftSize = getChildSize(left);
  const rightSize = getChildSize(right);
  const usable = Math.max(containerWidth, 1);

  if (leftSize.mode === 'fr' && rightSize.mode === 'fr') {
    const totalFr = leftSize.size + rightSize.size;
    const leftPx = (leftSize.size / totalFr) * usable;
    const nextLeftPx = Math.min(usable - 40, Math.max(40, leftPx + deltaPx));
    const nextLeftFr = (nextLeftPx / usable) * totalFr;
    const nextRightFr = totalFr - nextLeftFr;
    left.settings = { ...(left.settings ?? {}), sizeMode: 'fr', size: roundSize(nextLeftFr) };
    right.settings = { ...(right.settings ?? {}), sizeMode: 'fr', size: roundSize(nextRightFr) };
  } else if (leftSize.mode === 'px') {
    const next = Math.min(usable - 40, Math.max(40, leftSize.size + deltaPx));
    left.settings = { ...(left.settings ?? {}), sizeMode: 'px', size: Math.round(next) };
  } else if (rightSize.mode === 'px') {
    const next = Math.min(usable - 40, Math.max(40, rightSize.size - deltaPx));
    right.settings = { ...(right.settings ?? {}), sizeMode: 'px', size: Math.round(next) };
  } else {
    // left fr, right something unexpected — treat like both fr
    left.settings = { ...(left.settings ?? {}), sizeMode: 'fr', size: leftSize.size };
  }

  const nextChildren = children.map(cloneRegion);
  nextChildren[leftIdx] = left;
  nextChildren[rightIdx] = right;
  return { ...cloneRegion(parent), children: nextChildren };
}

export function resizePairInTree(
  root: ForumPageRegion,
  parentId: string,
  leftId: string,
  rightId: string,
  deltaPx: number,
  containerWidth: number,
): ForumPageRegion {
  return updateRegion(root, parentId, (parent) =>
    resizePair(parent, leftId, rightId, deltaPx, containerWidth),
  );
}

function roundSize(n: number): number {
  return Math.round(n * 1000) / 1000;
}

export function createDefaultHomeLayout(pinnedWatercoolerId?: string | null): ForumPageRegion {
  const children: ForumPageRegion[] = [createLeaf('cooler-list')];
  if (pinnedWatercoolerId) {
    children.push(createLeaf('watercooler-view', { watercoolerId: pinnedWatercoolerId }));
  }
  children.push(createLeaf('subforum-grid'), createLeaf('forum-view'));
  return { id: newRegionId(), typeKey: 'stack', settings: {}, children };
}

export function createDefaultExtraPageLayout(): ForumPageRegion {
  return {
    id: newRegionId(),
    typeKey: 'stack',
    settings: {},
    children: [createLeaf('forum-view')],
  };
}

export function createDefaultChromeLayout(): ForumPageRegion {
  return {
    id: newRegionId(),
    typeKey: 'stack',
    settings: { gap: 16 },
    children: [createLeaf('forum-header'), createLeaf('page-body')],
  };
}

/** Inject page layout wherever chrome has a page-body leaf. */
export function composeLayout(chrome: ForumPageRegion, pageLayout: ForumPageRegion): ForumPageRegion {
  return replacePageBody(cloneRegion(chrome), pageLayout);
}

function replacePageBody(node: ForumPageRegion, pageLayout: ForumPageRegion): ForumPageRegion {
  if (node.typeKey === 'page-body') {
    const injected = cloneRegion(pageLayout);
    const sizeMode = node.settings?.sizeMode;
    const size = node.settings?.size;
    if (sizeMode !== undefined || size !== undefined) {
      injected.settings = {
        ...(injected.settings ?? {}),
        ...(sizeMode !== undefined ? { sizeMode } : {}),
        ...(size !== undefined ? { size } : {}),
      };
    }
    return injected;
  }
  return {
    ...node,
    children: node.children?.map((child) => replacePageBody(child, pageLayout)) ?? null,
  };
}

export function countType(node: ForumPageRegion, typeKey: string): number {
  const self = node.typeKey === typeKey ? 1 : 0;
  return self + (node.children ?? []).reduce((sum, child) => sum + countType(child, typeKey), 0);
}

export function isContainerType(typeKey: string): boolean {
  return typeKey === 'stack' || typeKey === 'columns' || typeKey === 'grid';
}

export function findRegion(root: ForumPageRegion, id: string): ForumPageRegion | null {
  if (root.id === id) return root;
  for (const child of root.children ?? []) {
    const found = findRegion(child, id);
    if (found) return found;
  }
  return null;
}

export function updateRegion(
  root: ForumPageRegion,
  id: string,
  updater: (region: ForumPageRegion) => ForumPageRegion,
): ForumPageRegion {
  if (root.id === id) return updater(cloneRegion(root));
  if (!root.children) return cloneRegion(root);
  return {
    ...cloneRegion(root),
    children: root.children.map((child) => updateRegion(child, id, updater)),
  };
}

export function splitRegion(
  root: ForumPageRegion,
  id: string,
  containerType: LayoutSplit,
): ForumPageRegion {
  return updateRegion(root, id, (region) => {
    const kept = isContainerType(region.typeKey)
      ? (region.children?.map(cloneRegion) ?? [])
      : [
          {
            ...cloneRegion(region),
            id: newRegionId(),
          },
        ];
    while (kept.length < 2) kept.push(createLeaf('forum-view'));
    const withSizes = kept.map((child) => {
      const sized = cloneRegion(child);
      const current = getChildSize(sized);
      sized.settings = { ...(sized.settings ?? {}), sizeMode: current.mode, size: current.size };
      return sized;
    });
    return {
      id: region.id,
      typeKey: containerType,
      settings: defaultContainerSettings(containerType),
      children: withSizes,
    };
  });
}

export function setRegionType(
  root: ForumPageRegion,
  id: string,
  typeKey: string,
): ForumPageRegion {
  return updateRegion(root, id, (region) => {
    if (isContainerType(typeKey)) {
      const children =
        region.children && region.children.length > 0
          ? region.children.map(cloneRegion)
          : [createLeaf('forum-view'), createLeaf('forum-view')];
      return { id: region.id, typeKey, settings: defaultContainerSettings(typeKey), children };
    }
    const settings: Record<string, unknown> = { ...(region.settings ?? {}) };
    if (typeKey === 'watercooler-view' && !settings.watercoolerId) {
      /* picker fills this */
    } else if (typeKey !== 'watercooler-view') {
      delete settings.watercoolerId;
    }
    if (typeKey === 'extension') {
      if (typeof settings.clientId !== 'string' || settings.clientId.length === 0) settings.clientId = 'snekl-commissions';
      if (!isExtensionView(settings.view)) settings.view = 'storefront';
    } else {
      delete settings.clientId;
      delete settings.view;
      delete settings.artistUserId;
    }
    return { id: region.id, typeKey, settings, children: null };
  });
}

export function setRegionSetting(
  root: ForumPageRegion,
  id: string,
  key: string,
  value: unknown,
): ForumPageRegion {
  return updateRegion(root, id, (region) => {
    const settings = { ...(region.settings ?? {}) };
    if (value === undefined || value === null || value === '') delete settings[key];
    else settings[key] = value;
    return { ...region, settings };
  });
}

export function addChildRegion(root: ForumPageRegion, parentId: string): ForumPageRegion {
  return updateRegion(root, parentId, (region) => {
    if (!isContainerType(region.typeKey)) return region;
    const children = [...(region.children ?? []), createLeaf('forum-view')];
    return { ...region, children };
  });
}

export function deleteRegion(root: ForumPageRegion, id: string): ForumPageRegion {
  if (root.id === id) return root;
  if (!root.children) return cloneRegion(root);
  const filtered = root.children.filter((c) => c.id !== id).map((c) => deleteRegion(c, id));
  return { ...cloneRegion(root), children: filtered };
}

export function findParent(root: ForumPageRegion, childId: string): ForumPageRegion | null {
  for (const child of root.children ?? []) {
    if (child.id === childId) return root;
    const nested = findParent(child, childId);
    if (nested) return nested;
  }
  return null;
}

export function isDescendant(root: ForumPageRegion, ancestorId: string, maybeDescendantId: string): boolean {
  const ancestor = findRegion(root, ancestorId);
  if (!ancestor) return false;
  return findRegion(ancestor, maybeDescendantId) !== null && ancestorId !== maybeDescendantId;
}

export function createRegionFromType(typeKey: string): ForumPageRegion {
  if (typeKey === 'stack' || typeKey === 'columns' || typeKey === 'grid') {
    return {
      id: newRegionId(),
      typeKey,
      settings: defaultContainerSettings(typeKey),
      children: [createLeaf('forum-view'), createLeaf('forum-view')],
    };
  }
  if (typeKey === 'extension') {
    return createLeaf(typeKey, { clientId: 'snekl-commissions', view: 'storefront' });
  }
  return createLeaf(typeKey);
}

/** Remove a region from the tree and return it (cloned). Root cannot be removed. */
export function extractRegion(
  root: ForumPageRegion,
  id: string,
): { root: ForumPageRegion; region: ForumPageRegion } | null {
  if (root.id === id) return null;
  const region = findRegion(root, id);
  if (!region) return null;
  return { root: deleteRegion(root, id), region: cloneRegion(region) };
}

export function insertChildAt(
  root: ForumPageRegion,
  parentId: string,
  child: ForumPageRegion,
  index?: number,
): ForumPageRegion {
  return updateRegion(root, parentId, (parent) => {
    if (!isContainerType(parent.typeKey)) return parent;
    const children = [...(parent.children ?? [])];
    const at = index === undefined ? children.length : Math.max(0, Math.min(index, children.length));
    children.splice(at, 0, cloneRegion(child));
    return { ...parent, children };
  });
}

/**
 * Move an existing region under a target container at index.
 * No-ops if moving onto self/descendant, or target is not a container.
 */
export function moveRegionTo(
  root: ForumPageRegion,
  regionId: string,
  targetParentId: string,
  index?: number,
): ForumPageRegion {
  if (regionId === targetParentId) return cloneRegion(root);
  if (isDescendant(root, regionId, targetParentId)) return cloneRegion(root);
  const target = findRegion(root, targetParentId);
  if (!target || !isContainerType(target.typeKey)) return cloneRegion(root);

  const parent = findParent(root, regionId);
  let insertAt = index;
  if (parent && parent.id === targetParentId && typeof index === 'number') {
    const from = parent.children?.findIndex((c) => c.id === regionId) ?? -1;
    if (from >= 0 && from < index) insertAt = index - 1;
  }

  const extracted = extractRegion(root, regionId);
  if (!extracted) return cloneRegion(root);
  return insertChildAt(extracted.root, targetParentId, extracted.region, insertAt);
}

/** Insert a new region of typeKey into a container. */
export function insertTypeAt(
  root: ForumPageRegion,
  typeKey: string,
  targetParentId: string,
  index?: number,
): { root: ForumPageRegion; regionId: string } {
  const region = createRegionFromType(typeKey);
  return {
    root: insertChildAt(root, targetParentId, region, index),
    regionId: region.id,
  };
}

export function moveRegion(
  root: ForumPageRegion,
  id: string,
  direction: -1 | 1,
): ForumPageRegion {
  if (!root.children) return cloneRegion(root);
  const index = root.children.findIndex((c) => c.id === id);
  if (index >= 0) {
    const next = index + direction;
    if (next < 0 || next >= root.children.length) return cloneRegion(root);
    const children = root.children.map(cloneRegion);
    const [item] = children.splice(index, 1);
    children.splice(next, 0, item!);
    return { ...cloneRegion(root), children };
  }
  return {
    ...cloneRegion(root),
    children: root.children.map((child) => moveRegion(child, id, direction)),
  };
}

export function layoutWarnings(root: ForumPageRegion, mode: 'page' | 'chrome' = 'page'): string[] {
  const warnings: string[] = [];
  const depth = maxDepth(root);
  if (depth > 3) warnings.push(`Layout depth is ${depth}; consider keeping splits under 3.`);
  const leaves = countLeaves(root);
  if (leaves > 8) {
    warnings.push(
      mode === 'chrome'
        ? `This chrome has ${leaves} views; large shared layouts can be hard to scan.`
        : `This page has ${leaves} views; large layouts can be hard to scan.`,
    );
  }
  if (mode === 'chrome') {
    if (countType(root, 'page-body') !== 1) {
      warnings.push('Shared chrome needs exactly one page-body slot for page content.');
    }
  } else if (!hasType(root, 'forum-view') && !hasType(root, 'watercooler-view')) {
    warnings.push('No forum-view or watercooler-view — the page may look empty of primary content.');
  }
  if (hasType(root, 'watercooler-view') && !hasWatercoolerId(root)) {
    warnings.push('A watercooler view needs a room selected before save.');
  }
  if (!extensionReady(root)) {
    warnings.push('An extension view needs an app, a page, and an artist when it is a storefront.');
  }
  if (mode === 'page' && hasType(root, 'page-body')) {
    warnings.push('page-body belongs in shared chrome, not on a page layout.');
  }
  return warnings;
}

export function layoutCanSave(root: ForumPageRegion, mode: 'page' | 'chrome' = 'page'): boolean {
  if (hasType(root, 'watercooler-view') && !hasWatercoolerId(root)) return false;
  if (!extensionReady(root)) return false;
  if (mode === 'chrome') return countType(root, 'page-body') === 1;
  if (hasType(root, 'page-body')) return false;
  return true;
}

function maxDepth(node: ForumPageRegion): number {
  if (!node.children?.length) return 1;
  return 1 + Math.max(...node.children.map(maxDepth));
}

function countLeaves(node: ForumPageRegion): number {
  if (!isContainerType(node.typeKey)) return 1;
  return (node.children ?? []).reduce((sum, child) => sum + countLeaves(child), 0);
}

function hasType(node: ForumPageRegion, typeKey: string): boolean {
  if (node.typeKey === typeKey) return true;
  return (node.children ?? []).some((child) => hasType(child, typeKey));
}

function isExtensionView(value: unknown): value is ExtensionView {
  return EXTENSION_VIEWS.some((view) => view === value);
}

function extensionReady(node: ForumPageRegion): boolean {
  if (node.typeKey === 'extension') {
    const clientId = node.settings?.clientId;
    const view = node.settings?.view;
    if (typeof clientId !== 'string' || clientId.length === 0 || !isExtensionView(view)) return false;
    if (view === 'storefront') {
      const artistUserId = node.settings?.artistUserId;
      return typeof artistUserId === 'string' && artistUserId.length > 0;
    }
    return true;
  }
  return (node.children ?? []).every((child) => extensionReady(child));
}

function hasWatercoolerId(node: ForumPageRegion): boolean {
  if (node.typeKey === 'watercooler-view') {
    const id = node.settings?.watercoolerId;
    return typeof id === 'string' && id.length > 0;
  }
  return (node.children ?? []).every((child) => hasWatercoolerId(child));
}

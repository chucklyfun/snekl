import { describe, expect, it } from 'vitest';
import {
  clampedColSpan,
  columnsTemplate,
  composeLayout,
  createLeaf,
  getColSpan,
  gridColumnCount,
  CHROME_LAYOUT_VIEW_OPTIONS,
  createDefaultChromeLayout,
  createDefaultHomeLayout,
  getChildSize,
  insertTypeAt,
  layoutCanSave,
  layoutWarnings,
  moveRegionTo,
  resizePair,
  setRegionSetting,
  setRegionType,
  splitRegion,
} from './forumLayoutEditor';

describe('forumLayoutEditor', () => {
  it('seeds the classic home stack', () => {
    const root = createDefaultHomeLayout();
    expect(root.typeKey).toBe('stack');
    expect(root.children?.map((c) => c.typeKey)).toEqual([
      'cooler-list',
      'subforum-grid',
      'forum-view',
    ]);
  });

  it('requires an artist before a storefront extension can be saved', () => {
    const root = createLeaf('extension', { clientId: 'snekl-commissions', view: 'storefront' });
    expect(layoutCanSave(root, 'page')).toBe(false);
    const ready = setRegionSetting(root, root.id, 'artistUserId', 'artist-1');
    expect(layoutCanSave(ready, 'page')).toBe(true);
  });

  it('allows browsing views in chrome palette', () => {
    const keys = CHROME_LAYOUT_VIEW_OPTIONS.map((o) => o.typeKey);
    expect(keys).toContain('cooler-list');
    expect(keys).toContain('subforum-grid');
    expect(keys).toContain('page-body');
    expect(keys).toContain('forum-header');
  });

  it('seeds chrome with header and page-body', () => {
    const root = createDefaultChromeLayout();
    expect(root.children?.map((c) => c.typeKey)).toEqual(['forum-header', 'page-body']);
    expect(layoutCanSave(root, 'chrome')).toBe(true);
  });

  it('composes chrome around a page layout', () => {
    const composed = composeLayout(createDefaultChromeLayout(), createDefaultHomeLayout());
    expect(composed.children?.[0]?.typeKey).toBe('forum-header');
    expect(composed.children?.[1]?.typeKey).toBe('stack');
    expect(composed.children?.[1]?.children?.map((c) => c.typeKey)).toEqual([
      'cooler-list',
      'subforum-grid',
      'forum-view',
    ]);
  });

  it('warns when primary views are missing', () => {
    const root = createDefaultHomeLayout();
    const headerOnly = { ...root, children: root.children!.slice(0, 1) };
    const warnings = layoutWarnings(headerOnly);
    expect(warnings.some((w) => w.includes('forum-view'))).toBe(true);
  });

  it('blocks chrome save without page-body', () => {
    const root = createDefaultChromeLayout();
    const noBody = { ...root, children: root.children!.slice(0, 1) };
    expect(layoutCanSave(noBody, 'chrome')).toBe(false);
  });

  it('blocks save until watercooler-view has a room', () => {
    let root = createDefaultHomeLayout();
    const coolerListId = root.children![0]!.id;
    root = setRegionType(root, coolerListId, 'watercooler-view');
    expect(layoutCanSave(root)).toBe(false);
    root = setRegionSetting(root, coolerListId, 'watercoolerId', 'cooler-1');
    expect(layoutCanSave(root)).toBe(true);
  });

  it('splits a leaf into a two-column grid', () => {
    const root = createDefaultHomeLayout();
    const viewId = root.children!.find((c) => c.typeKey === 'forum-view')!.id;
    const next = splitRegion(root, viewId, 'grid');
    const split = next.children!.find((c) => c.id === viewId)!;
    expect(split.typeKey).toBe('grid');
    expect(split.children).toHaveLength(2);
    expect(gridColumnCount(split)).toBe(2);
    expect(split.settings?.stackBelow).toBe(720);
    expect(getColSpan(split.children![0]!)).toBe(1);
  });

  it('clamps a column span to the grid width', () => {
    const wide = createLeaf('forum-view', { colSpan: 4 });
    expect(clampedColSpan(wide, 2)).toBe(2);
    expect(clampedColSpan(createLeaf('forum-view'), 3)).toBe(1);
  });

  it('splits a leaf into columns', () => {
    const root = createDefaultHomeLayout();
    const viewId = root.children!.find((c) => c.typeKey === 'forum-view')!.id;
    const next = splitRegion(root, viewId, 'columns');
    const split = next.children!.find((c) => c.id === viewId)!;
    expect(split.typeKey).toBe('columns');
    expect(split.children).toHaveLength(2);
    expect(split.settings?.stackBelow).toBe(720);
  });

  it('resizes fr pair while keeping weight sum', () => {
    const left = createLeaf('forum-view', { sizeMode: 'fr', size: 1 });
    const right = createLeaf('forum-view', { sizeMode: 'fr', size: 1 });
    const parent = {
      id: 'cols',
      typeKey: 'columns',
      settings: { stackBelow: 720 },
      children: [left, right],
    };
    const resized = resizePair(parent, left.id, right.id, 100, 400);
    const a = getChildSize(resized.children![0]!);
    const b = getChildSize(resized.children![1]!);
    expect(a.mode).toBe('fr');
    expect(b.mode).toBe('fr');
    expect(a.size + b.size).toBeCloseTo(2, 5);
    expect(a.size).toBeGreaterThan(1);
  });

  it('resizes fixed px side', () => {
    const left = createLeaf('forum-view', { sizeMode: 'px', size: 200 });
    const right = createLeaf('forum-view', { sizeMode: 'fr', size: 1 });
    const parent = {
      id: 'cols',
      typeKey: 'columns',
      settings: {},
      children: [left, right],
    };
    const resized = resizePair(parent, left.id, right.id, 50, 600);
    expect(getChildSize(resized.children![0]!)).toEqual({ mode: 'px', size: 250 });
    expect(getChildSize(resized.children![1]!).mode).toBe('fr');
  });

  it('builds columns template from fr and px', () => {
    const kids = [
      createLeaf('forum-view', { sizeMode: 'fr', size: 2 }),
      createLeaf('forum-view', { sizeMode: 'px', size: 180 }),
    ];
    expect(columnsTemplate(kids)).toBe('minmax(0, 2fr) 180px');
  });

  it('moves a region under another container', () => {
    const root = createDefaultHomeLayout();
    const coolerListId = root.children![0]!.id;
    const split = splitRegion(root, root.children![2]!.id, 'columns');
    const cols = split.children!.find((c) => c.typeKey === 'columns')!;
    const moved = moveRegionTo(split, coolerListId, cols.id, 0);
    const colsAfter = moved.children!.find((c) => c.id === cols.id)!;
    expect(colsAfter.children![0]!.id).toBe(coolerListId);
    expect(moved.children!.some((c) => c.id === coolerListId)).toBe(false);
  });

  it('inserts a palette type into a container', () => {
    const root = createDefaultHomeLayout();
    const { root: next, regionId } = insertTypeAt(root, 'cooler-list', root.id, 1);
    expect(next.children![1]!.id).toBe(regionId);
    expect(next.children![1]!.typeKey).toBe('cooler-list');
  });

  it('refuses to move a region into its descendant', () => {
    const root = createDefaultHomeLayout();
    const withCols = splitRegion(root, root.id, 'columns');
    // splitRegion on root replaces root type — use child split instead
    const seeded = createDefaultHomeLayout();
    const viewId = seeded.children!.find((c) => c.typeKey === 'forum-view')!.id;
    const split = splitRegion(seeded, viewId, 'stack');
    const stack = split.children!.find((c) => c.id === viewId)!;
    const unchanged = moveRegionTo(split, stack.id, stack.children![0]!.id, 0);
    expect(unchanged).toEqual(split);
  });
});

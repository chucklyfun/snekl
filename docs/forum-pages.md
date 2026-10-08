# Forum pages & layout (v1)

Each **forum** (and each **subforum**) owns a small independent **site**: named pages with first-class routes, each backed by a **subdividable layout** of views. Mods rearrange the front page — and any extra pages — via content blocks. Sites do **not** inherit from parent forums.

This replaces the thin `ForumContentBlocks` placement row and the hardcoded `ForumPage` stack.

## Goals

| Goal | v1 answer |
|------|-----------|
| Primary outcome | Mods rearrange the forum front page |
| Shape | Page builder with first-class page routes |
| Structure | Subdividable regions (`stack` / `columns` / `grid`); soft depth guidance, not a deep widget hierarchy |
| Scope | Per forum node; no parent→child layout inheritance |
| Edit gate | `ManageContentBlocks` |

## Fixed chrome (not blocks)

These stay outside the layout tree so they cannot be deleted or buried:

| Chrome | Behavior |
|--------|----------|
| **Settings** | Fixed control rendered as a small menu beside the forum name in `forum-header` (not a placeable region). Links into forum settings — Emotes and flair live there. |
| **NSFW age-gate** | Hard gate before the page renders. Failing age check does not show the layout. |

## Shared locked sections (chrome layout)

Each forum (and each subforum independently) also owns a **shared chrome** document (`IsChrome`, reserved slug `chrome`): a layout tree that wraps every page of that forum.

| Rule | Detail |
|------|--------|
| Scope | Per forum node — subforums do **not** inherit parent chrome |
| Required leaf | Exactly one `page-body` slot; each page’s layout is injected there at render time |
| Other views | Any placeable leaf may be locked in chrome (`forum-header`, `cooler-list`, `subforum-grid`, `watercooler-view`, `forum-view`, …) so sidebars and chrome persist across pages |
| Edit | Forum settings → Pages → **Shared**; same `ManageContentBlocks` gate |
| Default seed | `stack` → `forum-header` + `page-body` (mods commonly split into `columns` with page-body + a right-hand directory) |

Join / favorite live in the **`forum-header`** view (favorite as a star beside the title menu). Create-a-post lives on **`forum-view`**. New watercooler / subforum live under Settings → Forums & rooms.

Example locked sidebar:

```
stack
├── forum-header
└── columns
    ├── page-body        (main)
    └── stack            (sidebar)
        ├── cooler-list
        └── subforum-grid
```

## Concepts

| Concept | Meaning |
|---------|---------|
| **Page** | Named document under one forum: title, slug, `isDefault`, layout root. |
| **Shared chrome** | Per-forum locked layout (`IsChrome`) with a `page-body` slot; wraps every page. Subforums own their own. |
| **Region** | Node in the layout tree: either a **container** (`stack`, `columns`, `grid`) or a **leaf view**. |
| **View** | Leaf region type that renders content (threads, coolers, header, …). |
| **Home** | The page with `isDefault = true`. Forum root URL renders Compose(chrome, home). |

Empty layout must never blank a forum: missing or corrupt layout falls back to the **default home seed**.

## Routes

Independent per forum / subforum (same pattern under parent slug when nested):

| URL | Meaning |
|-----|---------|
| `/s/{serverDid}/f/{slug}` | Default page (home) |
| `/s/{serverDid}/f/{slug}/p/{pageSlug}` | Extra page |
| `/s/{serverDid}/f/{parentSlug}/{slug}` | Subforum home |
| `/s/{serverDid}/f/{parentSlug}/{slug}/p/{pageSlug}` | Subforum extra page |

Thread and watercooler permalinks are unchanged. Pages never own threads.

Reserved `pageSlug` values: do not allow `settings`, `p`, or other path segments already used under the forum.

## Permissions

| Flag | Role |
|------|------|
| `ManageContentBlocks` | Create/edit/delete pages; edit layout trees; configure view settings |
| `SeeContentBlocks` / `ViewForum` | See rendered layout (existing See/View rules still apply to cooler and forum content inside views) |
| Per-type manage flags (`ManageCoolerFeed`, `ManageCoolerList`, …) | Gate **placing** or **configuring** that view type when the catalog defines them |

Catalog types remain the source of type-specific manage permissions. New view keys should be registered in the content-block catalog (or an equivalent page-view catalog) with forum scope.

## v1 view types

| `typeKey` | Kind | Renders |
|-----------|------|---------|
| `stack` | Container | Vertical split of child regions |
| `columns` | Container | Horizontal split of child regions |
| `grid` | Container | Wrapping rows and columns. `columns` is the column count (default 2, 1–12). Children flow left to right; `colSpan` lets a child occupy more than one column. |
| `forum-header` | Leaf | Name, description, banner, join; favorite star + fixed menu (Settings / Leave / flair) beside the title |
| `page-body` | Leaf | Slot in **shared chrome only** where each page layout is injected |
| `forum-view` | Leaf | Titled thread list + Create a post (sort + infinite scroll as today) |
| `watercooler-view` | Leaf | One cooler’s stream + full composer (`cooler-feed` successor) |
| `cooler-list` | Leaf | Directory / headers of watercoolers the viewer can **See** |
| `subforum-grid` | Leaf | Subforum headers / cards |
| `extension` | Leaf | Sandboxed iframe from an installed extension |

Deferred (catalog may still list them for roles; not placeable in the page builder until later): `calendar`, `ads`, `links`.

### Leaf settings (instance JSON)

| View | Settings (v1) |
|------|----------------|
| `forum-header` | Optional: show description, show banner (defaults on) |
| `forum-view` | Default sort (`new` / `hot` / …); page size optional |
| `watercooler-view` | **Required** `watercoolerId`; optional sort override |
| `cooler-list` | Optional: filter to approved only (default on) |
| `subforum-grid` | Optional: filter to approved only (default on) |
| `extension` | `clientId` of an install; `view` of `storefront`, `studio`, `desk`, or `requests`. `storefront` also requires `artistUserId`. |
| Any child of `stack` / `columns` | `sizeMode`: `"fr"` (default) \| `"px"`; `size`: positive number (fr weight or px length) |
| Any child of `grid` | `colSpan`: positive integer (default 1), clamped to the grid’s column count |
| `columns` | `gap` (px, default 16); `stackBelow` (px breakpoint, default 720; `0` = never collapse). Below breakpoint, children stack full-width. |
| `grid` | `columns` (integer 1–12, default 2); `gap` (px, default 16); `stackBelow` (px breakpoint, default 720; `0` = never collapse). Below breakpoint, children stack in one column. |
| `stack` | `gap` (px). Child `sizeMode: "px"` applies as `min-height`; `fr` is auto-height in the viewer. |

## Layout model (subdivide)

Editor mental model: **select a region → Split or Set view**, not unlimited nested widgets.

1. A page has one **root** region.
2. **Split** replaces a leaf (or empty) region with `stack`, `columns`, or `grid` and N children (default 2).
3. **Set view** assigns a leaf `typeKey` (+ settings) to a region.
4. **Soft guidance**: warn when nest depth exceeds ~3 or region count is high; still allow save.

Under the hood the document is a tree; product language stays “subdivide the layout.”

### Default home seed

New forums get a default **home** page (`isDefault`, slug `home`) whose root is page body content only:

```
stack
├── cooler-list
├── subforum-grid
└── forum-view
```

Plus a **shared chrome** document (`IsChrome`, slug `chrome`):

```
stack
├── forum-header
└── page-body
```

Viewer renders `Compose(chrome, home)`. If the forum previously had a pinned `cooler-feed` in `ForumContentBlocks`, migration inserts a `watercooler-view` in the home stack after `cooler-list`. Creating chrome for an existing home strips a top-level `forum-header` from that home so the header is not duplicated.

## Data model (v1 locks)

| Decision | Lock |
|----------|------|
| Storage | **`LayoutJson`** on `ForumPage` (validated on write) |
| Home slug | **`IsDefault` + slug `home`**; forum root URL still `/f/{slug}` (no `/p/home` required) |
| Catalog | **Extend `ContentBlockTypes`** with page view keys; deferred types stay non-placeable via pages API |

### `ForumPage`

| Field | Notes |
|-------|--------|
| `Id` | Guid |
| `ForumId` | FK; cascade with forum |
| `Title` | Display name |
| `Slug` | Unique per forum; home uses `home`; shared chrome uses reserved `chrome` |
| `IsDefault` | Exactly one default page per forum |
| `IsChrome` | Exactly one shared chrome document per forum (locked sections + `page-body`) |
| `LayoutJson` | Validated region tree JSON |
| `CreatedAt` / `UpdatedAt` | |

Constraint: unique `(ForumId)` where `IsDefault`; unique `(ForumId)` where `IsChrome`; unique `(ForumId, Slug)`.

### Region (JSON or rows)

| Field | Notes |
|-------|--------|
| `Id` | Stable id for editor selection |
| `TypeKey` | `stack` \| `columns` \| `grid` \| leaf keys above |
| `Settings` | JSON object (schema per type) |
| `Children` | Ordered list (containers only) |

### Migration from `ForumContentBlocks`

1. Ensure each forum has a default page with the seed layout.
2. For each `cooler-feed` + `slot = main` row, insert/update a `watercooler-view` region on that home page with the same `watercoolerId`.
3. Stop writing `ForumContentBlocks` for layout; keep table only if needed for a short dual-read window, then drop or leave unused.
4. Map old type keys: `cooler-feed` → `watercooler-view`; `cooler-list` placement (if any) → `cooler-list` region (seed already includes it).

`main` / `sidebar` slots are superseded by `columns` splits.

## API (sketch)

All under the existing forum auth story; require `ManageContentBlocks` on writes.

| Method | Path | Purpose |
|--------|------|---------|
| `GET` | `.../forums/{slug}/pages/home` | Default home page + layout |
| `PUT` | `.../forums/{slug}/pages/home` | Update home layout |
| `GET` | `.../forums/{slug}/pages/chrome` | Shared chrome layout |
| `PUT` | `.../forums/{slug}/pages/chrome` | Update shared chrome (must include exactly one `page-body`) |
| `GET` | `.../forums/{slug}/pages` | List pages (title, slug, isDefault) |
| `GET` | `.../forums/{slug}/pages/{pageSlug}` | Page + layout tree (home: omit slug or use `default`) |
| `PUT` | `.../forums/{slug}/pages/{pageSlug}` | Upsert title/slug/layout (or split create/update) |
| `POST` | `.../forums/{slug}/pages` | Create page (non-default) |
| `DELETE` | `.../forums/{slug}/pages/{pageSlug}` | Delete non-default page |
| `POST` | `.../forums/{slug}/pages/{pageSlug}/set-default` | Change home |

Forum DTO embeds `homePage` and `sharedChrome` layouts on `GET forum` so the SPA can compose once. Extra named pages load by route later.

Replace `replaceForumContentBlocks` with page/layout endpoints. Pin-feed in structure settings becomes “add/configure `watercooler-view`” in the layout editor (or a shortcut that patches the home tree).

## Settings UI

Forum settings gains a **Pages** section (structure or its own nav item):

1. **Shared** — edit locked chrome (must keep one `page-body`).
2. **Home** — edit the default page body injected into that slot.
3. **Add page** — create named extra pages (title + slug); edit each from the Pages tab strip.
4. Region editor: select region; Split (`stack` / `columns` / `grid`); Set view; configure leaf settings; reorder; delete (cannot remove the sole `page-body` in chrome).

**Settings** entry remains a fixed control beside the forum name (menu → Settings / Emotes / Your flair), not a rearrangeable header-block action.

## Viewer UI

- Forum root and `/p/{pageSlug}` render Settings/age-gate + `Compose(sharedChrome, pageLayout)`.
- Missing view permission for a leaf: hide or locked placeholder for that region only; rest of the page still renders.
- Optional page nav: list non-home pages the viewer can see (simple links under the forum; not required for v1 if only home exists).

## Soft guidance (non-blocking)

Warn (do not hard-block) when:

- Nest depth &gt; 3
- More than ~8 leaf regions on one page
- Home has no `forum-view` and no `watercooler-view` (page may look empty of primary content)

## Out of scope / parked

- Page or layout inheritance from parent forums
- Calendar, ads, links as placeable page views
- Drag-from-catalog freeform hierarchy as the primary editor metaphor
- Putting Settings or age-gate inside the layout tree as rearrangeable regions (Settings may appear as a fixed menu in the header UI)
- Server-level page sites (server plazas stay separate)
- Federated remote page documents

## Relation to watercoolers.md

[watercoolers.md](watercoolers.md) defines cooler behavior and permissions; its “Forum page and blocks” section points here. Implementation checklist: [forum-pages-tasks.md](forum-pages-tasks.md).

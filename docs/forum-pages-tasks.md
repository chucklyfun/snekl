# Forum pages — implementation tasks (v1)

Source: [forum-pages.md](forum-pages.md). Companion to cooler docs: [watercoolers.md](watercoolers.md).

Effort is rough (S ≤ 0.5d, M ~1–2d, L ~3d+). Dependencies are within this plan unless noted.

---

## Phase 0 — Decisions locked before code

| # | Task | Lock | Effort |
|---|------|------|--------|
| 0.1 | Storage | **`LayoutJson` on `ForumPage`** | S |
| 0.2 | Home slug | **`IsDefault` + slug `home`**; root URL unchanged | S |
| 0.3 | Catalog | **Extend `ContentBlockTypes`**; deferred types non-placeable via pages API | S |

---

## Phase 1 — Domain & persistence

| # | Task | Depends | Done when | Effort |
|---|------|---------|-----------|--------|
| 1.1 | Add `ForumPage` entity + EF/config + unique indexes (`ForumId`+`Slug`, one default per forum) | 0.1–0.2 | Migrations/bootstrap create table | M |
| 1.2 | Define layout JSON schema (region id, typeKey, settings, children) + server-side validate on write | 0.1, 0.3 | Invalid layouts rejected with clear errors | M |
| 1.3 | Default home **seed** builder (header → cooler-list → subforum-grid → forum-view) | 1.2 | Unit test asserts seed shape | S |
| 1.4 | Ensure page on forum create (and lazy ensure for existing forums on first read) | 1.1, 1.3 | New forum always has default page | M |
| 1.5 | Migration from `ForumContentBlocks`: seed home + insert `watercooler-view` from pinned `cooler-feed` | 1.4 | Forums with pin feed keep cooler on home after migrate | M |
| 1.6 | Register catalog keys: `forum-header`, `forum-view`, `watercooler-view`, `cooler-list`, `subforum-grid`, `stack`, `columns` (+ manage perms where needed); map/alias `cooler-feed` → `watercooler-view` | 0.3 | `GET /api/content-blocks` lists them; place rules documented | M |

---

## Phase 2 — API

| # | Task | Depends | Done when | Effort |
|---|------|---------|-----------|--------|
| 2.1 | `ForumPageService`: list / get / create / update / delete / set-default; `ManageContentBlocks` on writes | 1.x | Integration tests cover happy path + auth deny | L |
| 2.2 | Controllers + DTOs under forum routes; reserved slug validation | 2.1 | OpenAPI/routes match spec table | M |
| 2.3 | Embed **default page layout** on `GET forum` (one round-trip for home) | 2.1 | Forum DTO includes home layout (or agreed summary + layout) | M |
| 2.4 | Soft-guidance metadata on save response (warnings only: depth, leaf count, no primary view) | 1.2, 2.1 | Client can show warnings; save still 2xx | S |
| 2.5 | Deprecate/stop `replaceForumContentBlocks` for layout (dual-read window optional, then remove pin path) | 1.5, 2.3 | Writes go through pages API; old endpoint gone or no-op documented | M |
| 2.6 | Per-type place/configure checks (`ManageCoolerFeed` etc.) when setting leaf typeKey | 1.6, 2.1 | Missing type manage → 403 on that change | S |

---

## Phase 3 — Viewer SPA

| # | Task | Depends | Done when | Effort |
|---|------|---------|-----------|--------|
| 3.1 | Routes: `/p/{pageSlug}` for forum + subforum; reserved slug clash avoided | 2.2 | Extra page URL loads | M |
| 3.2 | Fixed chrome: **Settings** button outside layout; age-gate before tree | — | Settings never inside header block; age fail hides layout | S |
| 3.3 | Layout renderer: `stack` / `columns` + leaf switch | 2.3 | Seed home looks like today’s stack | L |
| 3.4 | Leaf: `forum-header` (identity + join/favorite; **no** Settings) | 3.2, 3.3 | Parity with current hero minus settings control | M |
| 3.5 | Leaf: `forum-view` (thread list + sort + infinite scroll) | 3.3 | Extracted from current `ForumPage` | M |
| 3.6 | Leaf: `cooler-list` | 3.3 | Same directory behavior as today’s always-on list | M |
| 3.7 | Leaf: `subforum-grid` | 3.3 | Same as today’s subforum section | S |
| 3.8 | Leaf: `watercooler-view` (stream + composer; settings.watercoolerId) | 3.3 | Replaces pinned-feed block behavior | M |
| 3.9 | Permission: hide or lock individual leaves; rest of page still renders | 3.3–3.8 | Cooler without View doesn’t break page | S |
| 3.10 | Optional: simple links to non-home pages (can ship after home works) | 3.1 | Nav lists pages from `GET …/pages` | S |

---

## Phase 4 — Editor SPA (forum settings)

| # | Task | Depends | Done when | Effort |
|---|------|---------|-----------|--------|
| 4.1 | Settings nav: **Pages** section; gated by `ManageContentBlocks` | 2.1 | Section visible only with permission | S |
| 4.1a | Shared chrome editor + home body editor; compose on forum view | 1.2, 2.x | Subforums independent; exactly one `page-body` | M |
| 4.2 | Page list UI: create, rename, set default, delete non-default | 4.1, 2.1 | CRUD matches API rules | M |
| 4.3 | Region editor: select → Split (`stack`/`columns`) → Set view → reorder → delete | 4.2, 1.2 | Can rebuild seed and save | L |
| 4.4 | Leaf settings forms (cooler picker, sort, column weights, header toggles) | 4.3 | Required `watercoolerId` enforced in UI + API | M |
| 4.5 | Soft-guidance warnings in editor (depth, leaf count, no primary view) | 2.4, 4.3 | Warnings visible; save allowed | S |
| 4.6 | Remove structure **Pin feed** shortcut (or rewire as “add watercooler-view to home”) | 2.5, 4.3 | No orphan `ForumContentBlocks` UI | S |

---

## Phase 5 — Hardening & docs

| # | Task | Depends | Done when | Effort |
|---|------|---------|-----------|--------|
| 5.1 | Tests: seed, migration pin→view, permissions, reserved slugs, set-default uniqueness | 1–2 | CI green | M |
| 5.2 | SPA tests for layout renderer edge cases (empty children, unknown typeKey fallback) | 3.3 | Unknown leaf → safe placeholder | S |
| 5.3 | Mark `ForumContentBlocks` unused / drop after dual-read window | 2.5, 4.6 | Spec + code agree | S |
| 5.4 | Docs pass: forum-pages + watercoolers stay aligned; AGENTS/README only if routes matter for agents | — | No contradictory “cooler-feed only” guidance | S |

---

## Suggested ship slices

| Slice | Includes | User-visible | Status |
|-------|----------|--------------|--------|
| **A — Home as data** | Phases 1–2 (ensure/embed) + 3.2–3.8 for **default page only** (no `/p/` yet); renderer driven by seed/migrated layout | Same look as today; layout editable later | **Done (this PR)** |
| **B — Rearrange home** | Phase 4 editor for default page only | Mods can subdivide/rearrange front page | **Done** |
| **C — Extra pages** | 3.1, 3.10, full page CRUD | First-class `/p/{slug}` routes | Next |

Ship A→B→C keeps the primary outcome (rearrange front page) before multi-page polish.

---

## Acceptance checklist (v1 done)

- [ ] Every forum has an independent default page; subforums do not inherit parent layout
- [ ] Home seed matches: header, cooler-list, subforum-grid, forum-view (+ migrated watercooler-view when pinned)
- [ ] Settings control and age-gate are outside the block tree
- [ ] `ManageContentBlocks` required to edit pages/layouts
- [ ] Mods can split regions (`stack`/`columns`) and assign v1 leaf views with settings
- [ ] Extra pages work at `/p/{pageSlug}` (forum + subforum)
- [ ] Soft guidance warns; does not block save
- [ ] Old pin-feed / `ForumContentBlocks` layout path retired or fully superseded

---

## Out of scope (do not file as v1 tasks)

Page inheritance, calendar/ads/links/extension as placeable views, Settings/age-gate as blocks, server-level page sites, federation of page documents.

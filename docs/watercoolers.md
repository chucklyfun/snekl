# Watercoolers (v1)

Untitled, Twitter-style conversations that live **beside** forums — not as subforums and not nested under other watercoolers.

## Places

A **watercooler** has name, slug, description, roles, and ranking settings. Parent is a **server** or a **forum**. A forum may own many coolers. A cooler must not parent another cooler.

| | Server cooler (plaza) | Forum cooler (channel) |
|--|------------------------|-------------------------|
| Default upvotes | On | Off |
| Default read | Hot (New available) | Chronological stream |
| Stream direction | N/A while upvotes are on | **Newest at bottom** by default (configurable) |
| If upvotes turned off | Becomes a channel | Already a channel |
| If upvotes turned on | Already a plaza | Gains Hot/New |

Turning **upvotes off** on a plaza makes it a Discord-style channel. Turning them **on** on a forum cooler makes it plaza-like.

## Create / approve

Same flow as forums / subforums:

| Parent | Permission | Result |
|--------|------------|--------|
| Server | `CreateWatercooler` Direct | Live |
| Server | `CreateWatercooler` Request (`RequestCreateWatercooler` on the wire) | **Pending** until approved |
| Forum | `ManageForum` on that forum | Live immediately |

Creating a cooler does not create a forum. Role seed/inherit does **not** copy `CreateThread` / `Reply` onto cooler speak.

Name, description, upvotes, and newest-at-bottom are chosen **at create**. After that they live on a **dedicated settings page** (`…/coolers/{slug}/settings`), not on the room stream. Only `ManageWatercooler` (and pending-plaza `CreateWatercooler` approvers) can see the Settings link or open that page.

## Permissions

Copied from the forum See/View split. Nested coolers also require See/View on the parent forum.

| Flag | Meaning |
|------|---------|
| `SeeWatercooler` | Listed (name, description, rail, directory). Content may stay locked. |
| `ViewWatercooler` | Read the stream, chains, and quote cards (if the quote **source** is also View-able). |
| `SpeakWatercooler` | Roots, replies, and quotes (one grant; Post vs Reply is not split in v1). |
| `ManageWatercooler` | Settings, lock, pin, moderation. |
| `WatercoolerAdministrator` | Implies the flags above. |

`CreateThread` / `Reply` never imply these. Layout uses `SeeContentBlocks` / `ManageContentBlocks`.

**Split Post vs Reply** (parked): two grants so a room can allow replies but not new roots. Not in v1.

## Posts

Reuse the post graph. Cooler roots have **no title**.

| Verb | Graph | Where it shows |
|------|--------|----------------|
| **Root** | No parent | Channel: in the stream. Plaza: Hot/New as a card. |
| **Reply** | `ParentPostId` | Permalink is a Twitter **chain** (ancestors + this + replies). Channel: also a new line in the stream. Plaza: not a Hot/New root. |
| **Quote** | `QuotedPostId`; `ParentPostId` null in v1 | Always a **new root** in *this* cooler. Does not join the source reply tree. Source gets a quote count. |

Quote card: author, snippet, link. If the viewer cannot View the source, show a locked placeholder.

**v1 quote source:** any watercooler post on this node the speaker can View (including other coolers). Not titled forum threads. Not remote federation. Quote-and-reply as one action is parked.

## Forum page and blocks

Forum front pages (and extra forum pages) are a **subdividable layout** of views — see [forum-pages.md](forum-pages.md). Each forum/subforum owns its own site; Settings and the NSFW age-gate stay outside the tree. Layout edit gate: `ManageContentBlocks`.

Default **home** seed includes cooler and thread views:

| View (`typeKey`) | Behavior |
|------------------|----------|
| `cooler-list` | Directory / headers of rooms the viewer can **See** (blurred or locked if View is missing). |
| `watercooler-view` | Full channel or plaza for **one** cooler, including **full composer**. Successor to the old `cooler-feed` placement. |
| `forum-view` | Titled thread list (still on the default home unless mods remove it). |

Placing a `watercooler-view` on the home layout is how a club leads with chatter. The same page can still include `forum-view`. Older docs/APIs may say `cooler-feed`; treat that as `watercooler-view`.

## Permalinks

`/s/{serverDid}/posts/{postId}` opens a Twitter-style conversation for cooler posts: ancestors above, focus, replies below.

## Parked

- Split Post vs Reply
- Cooler-under-cooler
- Quote of titled forum threads / federated-remote quotes
- Quote-and-reply as one action
- Following-feed of plaza posts

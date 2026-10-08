# Virtual content servers

One content deployment hosts many customer servers. They share the `Snekl.Api` binaries and one PostgreSQL database. There can be more servers than processes. Auth and Hub stay single services.

Today each content process has one database and one `NodeConfig` row (local dev runs Alpha and Beta as two processes and two databases). This document is the shared-database model.

## Server

A virtual server is a `NodeConfig` row. It has its own id, DID, and signing keys. Its name and owner are set at spawn. A public URL of its own is optional; servers on one host share the content base URL.

Every content row that belongs to a server carries that server id: roles, trusted auth issuers, hub link, content peers, forums, posts, local users, and per-server super-user grants. The same person (DID) can have a separate local user on each server.

Requests select the server with `X-Snekl-Server: {serverId}`. Middleware loads that `NodeConfig`, rejects an unknown or disabled id, and scopes the request to that id. When the header is absent and exactly one enabled server exists, that server is selected so a single-server process keeps working. Two or more enabled servers require the header. Federation calls to this host send it too. Peers still store the server’s DID as its identity.

Host-operator list, disable, and purge read across servers. Everything else stays inside the active id.

## Template

Spawn reads one JSON document shipped beside the content app. The document is not a `NodeConfig`, so it has no DID and receives no traffic. The host operator can view and replace it from the Servers screen. Edits apply only to the next spawn.

The document holds trusted auth issuers, the hub to join (`name`, `baseUrl`), and server role definitions (name, position, color, `isEveryone`, permission `modes`). It does not hold a DID, keys, name, or owner.

## Spawn

From the content admin Servers screen, a host operator submits a name and an owner. One transaction:

1. Inserts `NodeConfig` with a new DID, signing keys, name, and owner.
2. Creates the owner’s local user on that server and sets `OwnerUserId`.
3. Copies auth issuers, the hub target, and role definitions from the template, with new ids.
4. Records that hub on the server (accepted hub link). The hub registry still keys a member by base URL, so a shared host URL does not announce a second DID there.
5. Leaves forums, posts, members, role assignments, and content peers empty.

The new server id comes back in the response. Opening the server means later admin requests send that id. No process starts.

## Disable and purge

Disable sets a disabled time on `NodeConfig` and leaves every other row in place. Member and federation requests for that id stop. The host operator still sees the server and can enable it again.

Purge is a separate action. It deletes a disabled server’s rows.

## Who can manage servers

| Actor | Scope |
|-------|--------|
| Host operator | List, spawn, open, edit the template, disable, enable, purge. Separate from a server super user. Usable from inside any server’s admin. |
| Server owner and server super users | The server named by `X-Snekl-Server` only. |

## Scale-out

Any content replica can serve any server. Another machine is the same binary and the same database behind a load balancer. Nothing in spawn pins a server to a process.

Caches, search documents, media keys, and realtime groups include the server id. Background delivery (hub outbox, federation) claims rows so two replicas do not send the same message twice.

## Non-goals

- Virtual Auth or Hub servers
- A required unique URL per server
- Copying forums, posts, emotes, members, or content peers at spawn
- One OS process per customer server

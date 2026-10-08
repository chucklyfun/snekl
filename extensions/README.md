# Snekl extensions

Out-of-process extensions for [Snekl](https://github.com/chucklyfun/snekl). Each app is its own server, with its own database and admin. Snekl places an extension as a sandboxed iframe on a forum page and authorizes it with the existing install grant.

Links stay in the Snekl repo as a first-party block. This repo is for apps that need their own storage.

## Commissions

Artist studios, commission requests, and payment records.

- Escrow is an agreement record (`none`, `held`, `released`, `refunded`). It does not move money.
- Adult listings use **CCBill milestones** on the artist's own merchant account (or off-platform). Adult orders must not be charged on the Snekl Stripe account.
- Dev: `http://localhost:5301` when started via `./snekl all` from the Snekl repo.

See [commissions/README.md](commissions/README.md).

## Calendar

First extension sketch. A mod installs it on a forum, places the iframe, and opens this server’s admin from that setup screen.

- Admin at `{calendarHost}/admin/`, same two-step sign-in as Snekl’s other servers: an Account identity, then a password that exists only on this server.
- Events live here, keyed by the forum placement.

## Layout

```
commissions/    Commission studios, requests, escrow records, adult CCBill milestones
calendar/       Calendar server (not implemented yet)
```

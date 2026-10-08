# Commissions

Artist studios, commission requests, escrow agreement records, and adult CCBill milestones.

## Payments

| Method | Who it is for | Money movement |
|--------|---------------|----------------|
| `off_platform` | Any listing | None — agreement only |
| `escrow` | Non-adult listings | Agreement record only (`none` / `held` / `released` / `refunded`). Does **not** move money. Do not wire adult orders to Snekl Stripe. |
| `ccbill` | Adult listings | Client pays the **artist's** CCBill FlexForms checkout one milestone at a time. Settlement goes to the artist merchant account. Snekl only signs the URL and records webhooks. |

Adult listings cannot use `escrow`. Snekl's Stripe account must never charge adult commission content.

### CCBill setup (artist)

1. Save client account, subaccount, FlexForms id, and dynamic-pricing salt in Studio.
2. Paste the shown webhook URL (`/api/ccbill/webhook`) into the CCBill admin for that merchant account.
3. Optional: enroll Snekl as an affiliate and store the affiliate id — passed as `ccbill_referer`. There is no Stripe-style application fee.

Refunds and chargebacks stay in CCBill admin; matching webhooks mark the milestone and block completion.

## Run

```bash
dotnet run --project Snekl.Commissions --no-launch-profile
```

Listens on `DOTNET_URLS` (dev orchestrator uses `http://0.0.0.0:5301`).

```bash
dotnet test
```

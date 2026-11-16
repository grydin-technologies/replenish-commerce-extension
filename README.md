# GrydIn Realtime Webhook: Dynamics 365 Commerce extension

A Commerce Runtime (CRT) extension that sends each completed sale to GrydIn Replenish the moment the Commerce Scale Unit saves it. It does not wait for the headquarters sync.

- **One-way.** The extension only sends. It never reads from or writes to anything in Dynamics.
- **Does not slow the sale.** The trigger hands the sale to a background task and returns immediately.
- **Does not lose sales.** Each sale is written to a local spool folder before it is sent, and removed once GrydIn confirms receipt. Sales that cannot be delivered are retried automatically.
- **No secrets in this repository.** The webhook URL and secret are supplied by GrydIn for each store and set in your environment (see Configuration).

## Before you start

1. **Credentials.** Ask GrydIn for the webhook URL and secret for the pilot store. They are issued per store and shared separately, never through this repository.
2. **Complete the on-hand lookup.** `Services/InventoryOnHandResolver.cs` must be completed before go-live. See "Required before go-live" below.
3. **Version.** Built against Commerce SDK package `9.53.26002.2`, target framework `net8.0`. Rebuild against the SDK version of your environment. The Commerce Scale Unit must be on the same version as or later than the SDK this is built with.

## Contents

```
GrydIn.RealtimeWebhook/
  Triggers/SaleTransactionWebhookTrigger.cs      post-trigger on SaveSalesTransactionDataRequest
  Services/SaleTransactionMapper.cs              SalesTransaction -> request body
  Services/InventoryOnHandResolver.cs            on-hand quantity (to complete, see below)
  Services/WebhookDispatcher.cs                  background send, retries, spool replay
  Services/WebhookSpool.cs                       local spool folder
  Services/WebhookSignatureService.cs            HMAC-SHA256 signing
  Services/WebhookJson.cs                        JSON settings
  Services/WebhookLogger.cs                      logging (System.Diagnostics.Trace)
  Configuration/WebhookOptions.cs                settings and environment variables
  Configuration/ChannelWebhookOptionsResolver.cs per-store URL and secret
  Models/SaleTransactionPayload.cs               request body model
```

## Install

1. Copy the `GrydIn.RealtimeWebhook` folder into `src/ScaleUnitSample` of the Microsoft `Dynamics365Commerce.ScaleUnit` sample repository, or into your own extension solution. The project imports `..\CustomizationPackage.props` from that repository.
2. Add it to the extension package you already deploy, or reference it from the sample's CommerceRuntime configuration.
3. Register the assembly in the CRT extension configuration, the same way your other CRT extensions are registered. In the Microsoft sample this is `commerceruntime.ext.config`, where a line like `<add source="assembly" value="GrydIn.RealtimeWebhook" />` goes into the existing composition list. Confirm the file name and format in your branch.
4. Build and deploy the package to a sandbox first.

## Configuration

Settings come from environment variables of the Commerce Scale Unit host, or per store from channel configuration (below). All values are supplied by you or by GrydIn; none are in the code.

| Variable | Meaning | Default |
|---|---|---|
| `GRYDIN_WEBHOOK_URL` | Ingest URL: `https://{host}/v1/webhooks/{storeId}/ingest` | none |
| `GRYDIN_WEBHOOK_SECRET` | Signing secret issued by GrydIn | none |
| `GRYDIN_WEBHOOK_MAX_RETRIES` | Immediate delivery attempts per sale | 3 |
| `GRYDIN_WEBHOOK_SPOOL_DIR` | Folder for sales waiting to be delivered | a folder under the temp path |
| `GRYDIN_WEBHOOK_REPLAY_SECONDS` | How often waiting sales are retried | 60 |

**One URL and secret per store.** GrydIn issues a separate URL and secret for each store, and one Commerce Scale Unit can serve several stores. For each store, add two channel configuration extension properties in Commerce headquarters, named exactly `GRYDIN_WEBHOOK_URL` and `GRYDIN_WEBHOOK_SECRET`, then run the channel configuration sync. A store without them uses the environment variables instead. If neither is set, the extension does nothing.

**Changing a value.** Values are cached for the life of the process. After changing a URL or secret, restart the Commerce Scale Unit.

**Spool folder.** Set `GRYDIN_WEBHOOK_SPOOL_DIR` to a folder that is writable and that survives restarts. The default is a temporary location and may not. The files contain the sale lines and the request signature, not the secret, so protect the folder as you would other sales data.

## What is sent

`POST https://{host}/v1/webhooks/{storeId}/ingest`, `Content-Type: application/json`, header `x-signature`.

```json
{
  "orderNumber": "DEMO-ORD-001",
  "items": [
    { "lineNo": "1", "partNo": "F0508/608/227", "qtySold": 1, "qtyOnHand": 8, "size": "06-07Y" }
  ]
}
```

- `orderNumber`: the transaction id. `lineNo`: line number within the order. `partNo`: item id. `size`: variant size. `qtySold`: whole units.
- **Returns** are sent like sales, with a negative `qtySold`.
- **Voided lines** (removed by the cashier before checkout) are skipped. Confirm in your test whether voided transactions reach the trigger at all.
- GrydIn maps field names per store, so the names above can be changed to match your preference in `SaleTransactionPayload.cs`, with a matching change on the GrydIn side.

### Signature

`x-signature` is the HMAC-SHA256 of the exact request body bytes (UTF-8), keyed with the store secret, as a lowercase hex string with no prefix.

Test vector (not a real secret):

```
secret:    test-secret-not-real
body:      {"orderNumber":"DEMO-ORD-001","items":[{"lineNo":"1","partNo":"F0508/608/227","qtySold":1,"qtyOnHand":8,"size":"06-07Y"}]}
signature: 95dc04668b3c4ce3e55c137cf964d9dc8162ddb23c6ac66d44a547733f2bafd8
```

```bash
printf '%s' "$BODY" | openssl dgst -sha256 -hmac "$SECRET" | sed 's/^.* //'
```

### Responses

| Status | Meaning | Extension behaviour |
|---|---|---|
| `202` | Accepted. Any lines GrydIn could not process are retried on the GrydIn side. | Done |
| `400` | No line items found, or order number unreadable | Not retried; moved to `spool\failed` |
| `401` | Signature missing or does not match | Not retried; moved to `spool\failed` |
| `404` | No webhook configured for this store | Not retried; moved to `spool\failed` |
| `408`, `429`, `5xx`, timeout, network error | Temporary | Retried |

GrydIn ignores a repeated delivery of the same order number and line number, so a retry cannot create a duplicate.

## If a delivery fails

1. The sale is retried immediately up to `GRYDIN_WEBHOOK_MAX_RETRIES` times with a short backoff.
2. If it still fails, the file stays in the spool folder and is retried every `GRYDIN_WEBHOOK_REPLAY_SECONDS`, with growing delays capped at 15 minutes, until GrydIn accepts it. The retry timer starts when the Commerce Scale Unit starts.
3. A non-retryable response moves the file to `spool\failed` with a `.reason.txt` next to it. After fixing the cause, move the file back to `spool\` to send it again.
4. Waiting sales are not deleted automatically. If GrydIn is unreachable for a long time, the folder keeps growing, so monitor it.

All events are written with `System.Diagnostics.Trace` and the prefix `[GrydIn.RealtimeWebhook]`.

## Required before go-live

**`InventoryOnHandResolver`.** A sale does not carry the on-hand quantity, so `qtyOnHand` has to come from your inventory source. Until `Resolve` returns a value, the field is left out and GrydIn will not accept the line, because it is required. Decide with your Dynamics team which source to read, whether the number is before or after the sale, and keep the lookup fast, because it runs while the sale is being saved.

## Test checklist

Run these in a sandbox with the pilot store and confirm for each that the request reaches GrydIn:

- [ ] A normal sale is sent right after it is saved, without waiting for the headquarters sync.
- [ ] A sale with several lines and several sizes.
- [ ] A return is sent with a negative quantity.
- [ ] A voided line is not sent. Check whether a fully voided transaction triggers anything.
- [ ] A suspended or recalled transaction, a customer order and a transaction with no sale lines. Note which of these reach the trigger and whether that is what you want.
- [ ] A sale made on a different store served by the same Commerce Scale Unit uses that store's URL and secret.
- [ ] With the endpoint blocked, the sale completes at normal speed and the file appears in the spool folder. When the endpoint is reachable again, the file is delivered and removed.
- [ ] A wrong secret produces a `401` and the file moves to `spool\failed`.
- [ ] If your registers can work offline, test a sale made offline. This extension runs where the Commerce runtime runs, so a register-side deployment has to be planned separately.

## Support

GrydIn: hello@grydin.co

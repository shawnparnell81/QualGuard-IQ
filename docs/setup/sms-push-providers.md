# SMS and push notification providers

QualGuard IQ can copy in-app notifications to people by text message (SMS) or push. Delivery goes through a
provider you configure under **Platform → SMS and push** (permission `api.admin`). Nothing is sent until:

1. the provider's current configuration passes **Test connection**;
2. it is **Enabled**; and
3. for a **Production** provider, a second person with `integrations.approve` (not the person who ran the test)
   approves it.

Any change to the channel, adapter, environment, endpoint, sender or secret disables the provider and clears the
approval. A failed test also disables it.

Only people who opt in themselves (account menu → **Text and push notifications**) receive messages. SMS numbers
must be in international (E.164) format. Users can opt out at any time; queued messages for an opted-out address
are skipped.

## Adapters

| Adapter | Status |
|---|---|
| Generic HTTPS gateway | Supported. Point it at your own SMS/push gateway or a vendor relay you operate. |
| Test fixture | Sandbox only. Records messages in memory, never contacts a network. Refused for production. |
| Twilio, Firebase Cloud Messaging, Apple Push | Slots only. They are not implemented in this build because no live account was available to validate them; they cannot be enabled. Use a Generic HTTPS gateway in front of the vendor. |

## Generic HTTPS gateway contract

**Connection test:** `GET {endpoint}/health` with `Authorization: Bearer {secret}` must answer 2xx.

**Send:** `POST {endpoint}` with `Authorization: Bearer {secret}` and JSON

```json
{ "id": "<delivery id>", "channel": "sms|push", "to": "+15551234567", "from": "<sender>", "title": null, "text": "QualGuard IQ: NCR-0042 assigned to you https://qms.example.com/ncr/..." }
```

* 2xx, optionally with `{"messageId": "...", "status": "sent|delivered"}`: accepted.
* 429, 5xx, timeouts and network errors: retried after 1, 5, 15 and 60 minutes up to the provider's attempt limit.
* Other 4xx: failed permanently (shown in the delivery log).

The endpoint must be HTTPS. Use `id` to de-duplicate: a retry after a timeout can repeat a message.

**Delivery receipts (optional):** set a callback token (24+ characters) on the provider, then have the gateway call

```
POST /api/messaging/callbacks/{providerId}
X-Callback-Token: <token>
{ "messageId": "<the messageId you returned>", "status": "delivered|failed|undelivered|sent", "error": null }
```

A wrong token answers 404.

## Secrets

Either store the gateway secret on the provider (encrypted at rest, write-only, never returned by the API) or
reference deployment configuration with `config:Section:Key`, for example `config:Messaging:GatewayKey` read from
`Messaging__GatewayKey` in the environment. Prefer the configuration reference in production.

## Rate limits, filters and templates

* **Messages per minute** caps what one provider sends; the rest wait for the next run (the worker runs every minute).
* **Keywords** limit a provider to notifications whose title contains one of them (for example `NCR`, `Hold`).
* **Templates** use `{app}`, `{title}`, `{body}` and `{link}` and are shortened to their maximum length
  (default 320 characters for SMS). Links use `App:BaseUrl`.
* New providers start from the newest notification; old notifications are never back-filled.

The **Delivery log** shows every message with the address masked, its state (Queued, Sent, Delivered, Failed,
Skipped), attempts, next retry and the failure reason.

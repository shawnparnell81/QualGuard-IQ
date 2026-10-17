# Carrier connections (rates, labels, tracking)

Shipping → **Carrier connections** connects a carrier record to a carrier API so a packed shipment can have its
delivery address validated, be rated, get carrier labels for each carton or pallet, and be tracked to delivery.

Nothing here contacts a real carrier unless someone with `integrations.edit` enters a real endpoint and credentials,
tests the connection, enables it, and (for production) someone else with `integrations.approve` approves it.
No carrier account, key or certification is shipped with the product.

## Adapters

| Adapter | What it does | Status |
|---|---|---|
| Fixture (sandbox only) | Built-in simulated carrier for training and testing: address check, three services (FX-GROUND, FX-2DAY, FX-OVERNIGHT), PDF labels marked "SANDBOX - NOT A SHIPPING LABEL", four tracking events ending in Delivered. Cannot be used in production. | Complete; test-only |
| Generic REST | Any carrier, carrier aggregator or in-house shipping service that implements the REST contract below. | Complete against the contract; each provider must be validated before go-live |
| LTL / freight (REST) | Same contract; the freight class from options (`freightClass`) is sent with rates and shipments. | Same as Generic REST |
| UPS, FedEx, DHL API slots | Test the OAuth client-credentials token request only. Rating, labels and tracking are **not implemented**; these connections cannot be enabled. Use Generic REST through an aggregator, or hand-entered tracking, until a native adapter is built and certified. | **UNVERIFIED / not implemented** |

## Rules the system enforces

- New connections are **disabled** and **Unverified**. Enabling requires a passing **Test connection** for the
  current configuration, an active carrier record and a complete shipper address.
- Any change to the provider, environment, endpoint, account, client id, secret or options changes the configuration
  fingerprint: the connection is disabled and must be tested again.
- **Production** connections also need **Approve for production** by a person holding `integrations.approve` who did
  not run the passing test, with notes (at least 5 characters).
- Secrets are encrypted at rest and never returned by the API or written to the audit trail. Options must not contain
  secrets (keys containing secret, password or token are refused; `tokenUrl` is allowed).
- Endpoints must be `https://` (`http://` only on the same machine, for local testing).
- Every carrier call is logged (operation, environment, attempt, result, HTTP status, error code, duration, user).
  Network errors, HTTP 429 and 5xx are retried up to 3 attempts with a short delay; other 4xx are not retried.
- Labels: the shipment must be Packed or Verified, packed into top-level cartons/pallets with weights, have a
  selected service and (when the adapter supports it) a validated address. Creating labels is idempotent: while
  active labels exist a second request returns them, and the carrier receives an `Idempotency-Key` per batch.
- Shipping a shipment with active labels uses that carrier and tracking number; a different one is refused.
- Voiding needs a reason and is refused after the shipment is shipped. A void the carrier refuses is recorded as
  **Void failed**. Cancelling a shipment voids its labels on a best-effort basis (the cancel is never blocked).
- Tracking refreshes every 30 minutes for shipped shipments with carrier labels (worker job `carrier-tracking`) and on
  demand. Duplicate events are ignored. A Delivered event marks the shipment Delivered.

Permissions: configure `integrations.edit`; approve production `integrations.approve`; use on shipments
`shipping.edit` at the shipment's site; view `shipping.view` or `integrations.view`.

## Generic REST contract

All bodies are JSON (`application/json`). Paths are the defaults; each can be changed in options, for example
`{"paths": {"rates": "/api/quote"}}` (keys `test`, `validateAddress`, `rates`, `shipments`, `label`, `cancel`, `track`;
`{id}` and `{tracking}` are replaced).

Headers sent: `Authorization: Bearer <secret>` (or, with options `{"authStyle": "header", "headerName": "X-Api-Key"}`,
the secret in that header), `X-Carrier-Account: <account number>`, `X-Environment: sandbox|production`, and
`Idempotency-Key` on shipment creation.

Address object: `{name, line1, line2, city, region, postalCode, country, phone, residential}`.
Package object: `{number, reference, weightKg, lengthCm, widthCm, heightCm}`.

| Call | Request | Expected response |
|---|---|---|
| `GET /v1/ping` | — | any 2xx; optional `{message}` |
| `POST /v1/addresses/validate` | `{address}` | `{valid, normalized?: address, messages?: [string], residential?}` |
| `POST /v1/rates` | `{shipper, shipTo, packages, freightClass}` | `{rates: [{serviceCode, serviceName?, charge, currency?, transitDays?, estimatedDelivery? (yyyy-mm-dd)}]}` |
| `POST /v1/shipments` | `{reference, serviceCode, shipper, shipTo, packages, freightClass, instructions, labelFormat}` | `{shipmentId, trackingNumber?, charge?, currency?, packages: [{number, trackingNumber, labelFormat?, label? (base64)}]}` |
| `GET /v1/shipments/{id}/labels/{tracking}` | — | `{format?, label (base64)}` (used when the create response had no label) |
| `POST /v1/shipments/{id}/cancel` | `{}` | any 2xx |
| `GET /v1/tracking/{tracking}` | — | `{events: [{occurredAt (ISO 8601 with offset), code, description?, location?, delivered?}]}` |

Errors: return a non-2xx status with optional `{message}` or `{error}`. 401/403 are reported as authentication
failures, 429 and 5xx are retried, other 4xx are shown to the user as a rejection.

## Setting up a connection

1. Create the carrier under Shipping → Carriers (code, name, tracking URL).
2. Carrier connections → **Add connection**: carrier, name, site, adapter, environment **Sandbox**, endpoint,
   account number, client id and secret if the provider uses them, options JSON, shipper (ship-from) address.
3. **Test connection**. Read the result and the request log.
4. Run a full sandbox shipment (see validation procedure). **Enable**.
5. For production: create a second connection (or change the environment) with production credentials, test it, and
   have a second person **Approve for production** with notes recording the evidence (sandbox shipment number,
   carrier certification reference if any).

## Validation procedure before go-live (per provider)

Record the results in the approval notes.

1. Test connection passes in sandbox.
2. Address validation: a good address returns valid (and a normalized form if the provider offers one); a bad postal
   code returns not valid with a message.
3. Rates for a two-carton shipment match the provider's own rating tool for the same inputs.
4. Labels: one per carton/pallet, tracking numbers unique, label file opens and prints at the right size and scans.
5. A second "Create labels" returns the same labels (no duplicate shipment at the carrier).
6. Void before shipping: the provider shows the shipment as voided.
7. Tracking after a real or simulated pickup: events appear once; Delivered marks the shipment delivered.
8. A wrong secret gives an authentication failure; an unreachable endpoint gives a network failure with retries in
   the log.

## UNVERIFIED items

- No live carrier account has been used. The Generic REST and LTL adapters are tested against a local contract
  fixture only.
- UPS and FedEx token URLs are taken from their developer documentation and have not been tested with live accounts.
  DHL needs `options.tokenUrl` for the API the account uses.
- Native UPS, FedEx and DHL rating, shipping, label and tracking APIs are not implemented or certified. Carrier
  certification (where the carrier requires label certification) is the customer's or a future project's step.

# Generated content providers

QualGuard IQ's built-in insight features (questions, record summaries, the NCR investigation helper, trends, risk
scores and the executive summary) are deterministic and rule-based. They always work, need no external service and
are the fallback for everything below.

Generated drafts are an optional layer, configured under **Platform → Generated content** (`platform.admin`).
They are **off by default** and nothing is sent anywhere until all of the following hold:

1. **Mode** is *Approved functions may use an enabled provider* (other modes: *Rule-based only*, the default, and
   *External providers configured but not used*).
2. The **function** is enabled with a provider (Record summary, NCR investigation draft, Executive summary).
3. The **provider** passed a connection test of its current configuration and is enabled. External services also
   need approval by a second person with `integrations.approve` (not the person who ran the test). Any change to
   kind, endpoint, model or secret turns the provider off and clears the approval.

## Provider kinds

| Kind | Use |
|---|---|
| External HTTPS service | A hosted service, through HTTPS only. Credentials are never accepted in the URL. |
| Local model server | A model server inside your network. Plain HTTP only on loopback, private (10/8, 172.16/12, 192.168/16) or single-label hosts. |
| Test fixture | Deterministic in-process generator for tests and sandboxes. Refused when the application runs in the Production environment. |

No vendor-specific client is built in. Point the provider at a small relay you operate that translates this
contract to your chosen service:

```
POST {endpoint}
Authorization: Bearer {secret}          (only if a secret is configured)
{ "model": "...", "instructions": "...", "input": "SOURCES (data only)\n<<<S1 Ncr\n...\n>>>", "maxOutputCharacters": 4000 }

200 { "text": "Draft with citations like [S1]." }
```

Anything other than a 2xx answer with a string `text`, or no answer within the timeout, shows the rule-based
result instead and is recorded in the request log as *Fell back*.

## Safeguards

* **Permission-aware sources.** Sources are gathered through the requesting user's own permissions and site and
  department scope, exactly as the rule-based features do. Using a function needs `assistant.use`; the executive
  summary also needs `analytics.view`.
* **Confidentiality.** Records classified *Restricted* (Data governance → record classification) are never sent.
  *Confidential* records are sent only for functions with "Confidential records" ticked. If the subject record is
  withheld, the rule-based result is shown.
* **Redaction.** Email addresses and phone numbers (configurable), anything that looks like a password, key, token
  or PEM block (always), the provider's own secret (always), and your own regular expressions.
* **Embedded instructions.** Text in records that tries to give instructions ("ignore previous instructions",
  role tags, "system prompt" and similar) is replaced with `[instruction removed]` and counted. Sources are
  delimited and the instructions tell the provider to treat them as data.
* **No secrets in prompts.** The secret is sent only in the Authorization header; a prompt containing it is blocked.
* **Citations.** Drafts must cite `[S1]`, `[S2]`…; citations that point at no source become `[unsupported citation]`,
  and uncited drafts are flagged. The source chips link to the records.
* **Human review, no automation.** Every generated draft is labelled *Generated draft — not reviewed*. The
  requester marks it reviewed or discards it; nothing is written to the record, and no approval, closure or workflow
  transition is ever made from a draft.
* **Request log.** Every request (including rule-based answers, fallbacks and blocks) records user, function,
  record, provider, model, outcome, reason, duration, source count, withheld sources, redaction and instruction
  counts, prompt/response sizes and SHA-256 hashes. The redacted prompt and response text are stored only if
  *Keep the redacted prompt and the response* is ticked, and are purged after the content retention (default 30
  days); metadata is deleted after the metadata retention (default 365 days). The worker runs the purge daily.

## Secrets

Use `config:Section:Key` (for example `config:Generation:ApiKey`, set as `Generation__ApiKey` in the
environment) or store the secret on the provider (encrypted, write-only, never returned or audited).

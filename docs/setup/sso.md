# Single sign-on (OpenID Connect)

Admin › People › **Single sign-on** (needs `admin.users`) lets people sign in with the organization's identity provider
instead of a QualGuard IQ password. Supported: **Microsoft Entra ID**, **Google Workspace**, **Okta** and any other
**OpenID Connect** provider that publishes a discovery document.

Nothing contacts a provider until an administrator adds one. No provider account, client or key ships with the product.

## How it works

1. The sign-in page shows a **Sign in with <name>** button for every provider that is turned on.
2. The button sends the browser to the provider with the **authorization code flow and PKCE (S256)**, a random `state`
   and a random `nonce`. They are kept in a short-lived encrypted cookie (`qms.sso`, 10 minutes, this browser only).
3. The provider sends the browser back to the **redirect URI**. QualGuard IQ checks the `state` against the cookie,
   exchanges the code (with the PKCE verifier and the client secret) and validates the ID token against the provider's
   **discovery document** and **signing keys**:
   - signature (RS256/384/512, PS256/384/512 or ES256/384/512; `none` and shared-secret algorithms are refused; keys are
     re-read once when an unknown key id appears, so key rotation needs no action),
   - issuer equals the discovery issuer, audience contains the client ID (and `azp` matches when present),
   - not expired, not before `nbf`, `iat` not in the future (2 minutes clock tolerance),
   - `nonce` equals the one sent.
4. The account is found (see *Account matching*). The browser returns to the sign-in page with a one-time ticket that
   only works in that browser for a few minutes. The page exchanges it for a session.
5. Unless the provider's MFA is trusted, the app's own two-step rules then apply exactly as after a password: an
   authenticator code (or a trusted browser), or set-up when the organization or a role requires it.

**Redirect URI** (register it at the provider): `<App:BaseUrl>/api/auth/sso/callback`, for example
`https://qms.contoso.com/api/auth/sso/callback`. The Single sign-on page shows the exact value with a copy button.

## Rules the system enforces

- A new provider is **off** and **Not tested**. **Turn on** is only possible after a **Test sign-in** with the current
  settings passed. Changing the provider type, authority, client ID, client secret or scopes turns an enabled provider
  off and requires a new test. Domain, MFA and account-creation options can change without a new test.
- The test sign-in is a real sign-in at the provider by the administrator (any account in an allowed domain). It records
  each step (discovery, code exchange, ID token, verified email, allowed domain, which account would match) on the
  provider. It does not sign anyone in to QualGuard IQ. **Check connection** reads only the discovery document and keys.
- The client secret is **encrypted** (ASP.NET Core Data Protection) and never returned by the API or written to the
  audit trail (the trail records "set (value not recorded)").
- Authority URLs must be `https://`. (`http://` is allowed only when `Sso:AllowHttp` is true, which only the
  development and test configurations set, for the local fake provider.)
- At least one **allowed email domain** per provider. A domain can require single sign-on for only one provider.
- Customer and supplier **portal accounts never use single sign-on**.

## Account matching

1. If this provider's subject (`sub`) was linked to an account before, that account is used (an email change at the
   provider does not matter).
2. Otherwise the provider's **verified email** is matched to an account's email, and the link is recorded:
   - the email must be verified: `email_verified = true`; for Entra ID `xms_edov = true` is also accepted;
     for Google the `hd` claim must equal the email's domain (a Workspace account, not a consumer account using that address);
   - the email's domain must be one of the provider's **allowed domains**;
   - an account already linked to a *different* subject at the same provider is refused (no takeover by a reused address).
3. If no account matches and **Create an account at first sign-in** is off (the default), sign-in is refused with
   "There is no account for you yet. Ask your administrator to add you."
4. If it is on, an account is created with the display name from the provider, **no password**, and the **role chosen
   by the administrator** for this provider, organization-wide. The role is required, is never assumed, and may not
   contain administration permissions. Add site-scoped roles afterwards in Admin › Users.

Disabled and locked accounts cannot sign in through a provider either.

## Domains that must use single sign-on, and break-glass accounts

- **Domains that must use single sign-on** (a subset of the allowed domains): password sign-in, API tokens
  (`POST /api/auth/token`) and password reset emails are refused for those addresses. The sign-in page then highlights
  the right **Sign in with …** button. The answer is the same whether or not an account exists.
- **Break-glass accounts** are administrators who may always sign in with their password, so someone can get in when the
  provider is down or misconfigured. A domain can only require single sign-on while at least one active break-glass
  administrator exists, and the last one cannot be removed while a domain requires it. Give break-glass accounts two-step
  verification (the page warns when one has none), keep the password sealed, and review their sign-ins in Security events.

## Two-step verification after the provider

- **Trust the provider's multi-factor sign-in: off** (default): after the provider, the app asks for its own code when
  the account has an authenticator, honours trusted browsers, and requires set-up when the policy or a role demands it.
- **On**: the provider's sign-in is final. Turn this on only when the provider enforces MFA for every user of this
  application (Entra Conditional Access, Google 2-Step Verification enforcement, an Okta authentication policy). The
  provider's `amr` values are written to the security log with each sign-in.

## Microsoft Entra ID

1. Entra admin center › **Identity › Applications › App registrations › New registration**.
   - Supported account types: **Accounts in this organizational directory only (single tenant)**.
   - Redirect URI: platform **Web**, value = the redirect URI above.
2. On **Overview** copy the **Application (client) ID** and the **Directory (tenant) ID**.
3. **Certificates & secrets › New client secret**. Copy the secret **Value** (not the Secret ID). Note its expiry date and
   enter a new secret before it expires (this needs a new test sign-in).
4. **Token configuration › Add optional claim › ID** › tick **email** and **xms_edov**. Without them Entra ID does not
   say the address is verified and sign-in is refused ("did not confirm that … is verified").
   Each user needs the **Email** (mail) attribute set.
5. **API permissions**: Microsoft Graph delegated `openid`, `profile`, `email` (grant admin consent to avoid a consent screen).
6. Optional: **Enterprise applications › (the app) › Properties › Assignment required = Yes**, then assign groups.
7. In QualGuard IQ: Provider **Microsoft Entra ID**, Authority `https://login.microsoftonline.com/<tenant id>/v2.0`,
   the client ID and secret, allowed domains (your verified domains, for example `contoso.com`).
   `common`, `organizations` and `consumers` addresses are refused: a tenant-specific address is required.
8. **Test sign-in**, then **Turn on**.

## Google Workspace

1. Google Cloud console (a project owned by your Workspace) › **APIs & Services › OAuth consent screen** (Google Auth
   platform › Branding/Audience): user type **Internal**; scopes `openid`, `email`, `profile`.
2. **Credentials › Create credentials › OAuth client ID** › **Web application** › **Authorized redirect URIs** = the
   redirect URI above. Copy the client ID and client secret.
3. In QualGuard IQ: Provider **Google Workspace**, Authority `https://accounts.google.com` (filled in), the client ID and
   secret, allowed domains = your Workspace domain(s). With one allowed domain Google's account chooser is limited to it.
4. Sign-in requires `email_verified` and an `hd` (hosted domain) claim equal to the email's domain, so personal Google
   accounts cannot use a work address.
5. MFA: Admin console › Security › Authentication › **2-Step Verification** › enforce.
6. **Test sign-in**, then **Turn on**.

## Okta

1. Admin Console › **Applications › Applications › Create App Integration** › **OIDC – OpenID Connect** › **Web Application**.
   - Grant type: **Authorization Code**. Sign-in redirect URIs: the redirect URI above.
   - Client authentication: **Client secret**; tick **Require PKCE as additional verification**.
   - Assignments: the groups that may use QualGuard IQ.
2. Copy the **Client ID** and **Client secret**.
3. The Authority is the **issuer** of the authorization server you use, exactly as Okta shows it:
   the org server `https://<org>.okta.com`, or a custom one such as `https://<org>.okta.com/oauth2/default`
   (Security › API › Authorization Servers › Issuer URI).
4. Okta sends `email_verified` with the `email` scope. MFA: Security › Authentication Policies › a rule for this app
   requiring two factors.
5. In QualGuard IQ: Provider **Okta**, the authority, client ID and secret, allowed domains. **Test sign-in**, then **Turn on**.

## Other OpenID Connect providers

Requirements: a discovery document at `<authority>/.well-known/openid-configuration` whose `issuer` equals the authority,
the authorization code flow, PKCE S256 (if the provider lists its methods), an asymmetric ID token signature, and
`email` plus `email_verified` claims. Confidential clients use `client_secret_basic` when the provider lists it,
otherwise `client_secret_post`; with no secret stored the client ID is sent alone (public client with PKCE).

## Troubleshooting

| Message on the sign-in page | Meaning |
|---|---|
| Sign-in was cancelled at your identity provider | The person cancelled, or the provider asked for consent or interaction it could not get. |
| That sign-in could not be matched to this browser | The `state` cookie was missing or different (another browser, more than 10 minutes, cookies blocked). |
| Your identity provider's answer could not be verified | Code exchange or ID token validation failed. Security events show the exact reason (`SsoSignInFailed`). |
| …did not confirm your email address | No verified email; for Entra ID add the `email` and `xms_edov` optional claims. |
| Your email domain is not allowed | The verified email's domain is not an allowed domain of that provider. |
| There is no account for you yet | No matching account and account creation at first sign-in is off. |
| …already linked to a different identity | The account was linked to another subject at this provider; remove the link by deleting and re-adding the account, or investigate. |

The provider card on the Single sign-on page shows the last test's steps and the claims received (subject shortened).

## Security log and audit trail

Security events: `SsoSignInStarted`, `SsoSignInFailed` (with the reason), `SsoAccountLinked`, `SsoAccountCreated`,
`SignIn` (detail "Single sign-on (<provider>, provider methods: …)"), `SignInBlockedSso`, `SsoProviderTested`,
`SsoProviderEnabled`, `SsoProviderDisabled`, `SsoProviderRemoved`, `SsoBreakGlassAdded`, `SsoBreakGlassRemoved`.
Audit trail: provider created/updated (before and after values; secret recorded as set, never its value), test passed or
failed, account linked, account created at first sign-in, break-glass changes.

## Configuration

| Setting | Purpose |
|---|---|
| `App:BaseUrl` | Public address of the web app; forms the redirect URI. Required in production. |
| `DataProtection:KeyPath` | Where the Data Protection keys live. The client secrets (and authenticator keys) are encrypted with them; if they are lost, enter each provider's secret again. |
| `Auth:ChallengeMinutes` | How long the one-time ticket after the provider (and each later sign-in step) stays valid (default 5). |
| `Sso:AllowHttp` | Development and tests only: allows `http://` authorities for a local provider. Never set in production. |

## Testing without a real provider

The backend tests (`tests/QualGuardIQ.Tests/SsoTests.cs`) run a fake provider (`Fakes/FakeOidcProvider.cs`) in the test
process, and the UI test (`web/e2e/e2e28-sso.spec.ts`) runs `web/e2e/fake-oidc.ts` on port 5183. Both generate their RSA
signing key when they start, so no key material is kept in the repository. They cover PKCE, state, nonce, wrong
audience, wrong issuer, bad or unknown signing key, `alg: none`, HS256, expired tokens, unverified and foreign emails,
account linking and creation, the provider's MFA trusted or not, required domains and break-glass accounts.

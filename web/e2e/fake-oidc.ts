import http from 'node:http';
import crypto from 'node:crypto';

/**
 * Local OpenID Connect provider for the UI tests. Its sign-in page asks only for an email (no password) and answers with a code; the
 * token endpoint checks the client secret, redirect URI and PKCE verifier and returns an RS256 ID token. The signing key is generated
 * when the provider starts, so no key material is kept in the repository.
 */
export const FAKE_CLIENT_ID = 'qgiq-e2e-client';
export const FAKE_CLIENT_SECRET = 'e2e-fake-provider-secret';

const b64 = (b: Buffer) => b.toString('base64url');

export async function startFakeOidc(port = Number(process.env.E2E_OIDC_PORT ?? 5183)) {
  const issuer = `http://127.0.0.1:${port}`;
  const { privateKey, publicKey } = crypto.generateKeyPairSync('rsa', { modulusLength: 2048 });
  const kid = crypto.randomBytes(8).toString('hex');
  const jwk = { ...(publicKey.export({ format: 'jwk' }) as object), kid, use: 'sig', alg: 'RS256' };
  const codes = new Map<string, { email: string; nonce: string; challenge: string; redirect: string }>();

  const idToken = (email: string, nonce: string) => {
    const now = Math.floor(Date.now() / 1000);
    const local = email.split('@')[0];
    const name = local.split(/[._-]/).map((w) => w[0].toUpperCase() + w.slice(1)).join(' ');
    const head = b64(Buffer.from(JSON.stringify({ alg: 'RS256', typ: 'JWT', kid })));
    const body = b64(Buffer.from(JSON.stringify({ iss: issuer, aud: FAKE_CLIENT_ID, sub: 'e2e-' + crypto.createHash('sha256').update(email).digest('hex').slice(0, 16),
      email, email_verified: true, name, iat: now, nbf: now, exp: now + 600, nonce, amr: ['pwd', 'mfa'] })));
    return `${head}.${body}.${b64(crypto.sign('sha256', Buffer.from(`${head}.${body}`), privateKey))}`;
  };
  const json = (res: http.ServerResponse, status: number, body: unknown) => { res.writeHead(status, { 'Content-Type': 'application/json' }); res.end(JSON.stringify(body)); };
  const read = (req: http.IncomingMessage) => new Promise<string>((ok) => { let s = ''; req.on('data', (c) => (s += c)); req.on('end', () => ok(s)); });
  const esc = (s: string) => s.replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c]!);

  const server = http.createServer(async (req, res) => {
    const url = new URL(req.url ?? '/', issuer);
    if (url.pathname === '/.well-known/openid-configuration') return json(res, 200, {
      issuer, authorization_endpoint: `${issuer}/authorize`, token_endpoint: `${issuer}/token`, jwks_uri: `${issuer}/jwks`,
      response_types_supported: ['code'], subject_types_supported: ['public'], id_token_signing_alg_values_supported: ['RS256'],
      code_challenge_methods_supported: ['S256'], token_endpoint_auth_methods_supported: ['client_secret_basic', 'client_secret_post'],
    });
    if (url.pathname === '/jwks') return json(res, 200, { keys: [jwk] });
    if (url.pathname === '/authorize' && req.method === 'GET') {
      const q = url.searchParams;
      if (q.get('client_id') !== FAKE_CLIENT_ID || q.get('code_challenge_method') !== 'S256' || !q.get('nonce')) return json(res, 400, { error: 'invalid_request' });
      const hidden = ['redirect_uri', 'state', 'nonce', 'code_challenge'].map((k) => `<input type="hidden" name="${k}" value="${esc(q.get(k) ?? '')}">`).join('');
      res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
      return res.end(`<!doctype html><html lang="en"><head><title>Test identity provider</title><style>body{font-family:system-ui,sans-serif;background:#f3f5f8;display:grid;place-items:center;height:100vh;margin:0}
form{background:#fff;padding:32px;border-radius:8px;box-shadow:0 8px 24px rgba(0,0,0,.08);display:grid;gap:12px;width:320px}input[type=email]{padding:10px;font-size:15px}button{padding:10px;font-size:15px;background:#007dc1;color:#fff;border:0;border-radius:4px}</style></head>
<body><form method="post" action="/authorize"><h1 style="font-size:20px;margin:0">Test identity provider</h1>${hidden}<label>Email <input type="email" name="email" value="${esc(q.get('login_hint') ?? '')}" required style="width:100%;box-sizing:border-box"></label><button type="submit">Continue</button></form></body></html>`);
    }
    if (url.pathname === '/authorize' && req.method === 'POST') {
      const f = new URLSearchParams(await read(req));
      const code = crypto.randomBytes(16).toString('hex');
      codes.set(code, { email: f.get('email')!, nonce: f.get('nonce')!, challenge: f.get('code_challenge')!, redirect: f.get('redirect_uri')! });
      res.writeHead(302, { Location: `${f.get('redirect_uri')}?code=${code}&state=${encodeURIComponent(f.get('state') ?? '')}` });
      return res.end();
    }
    if (url.pathname === '/token' && req.method === 'POST') {
      const f = new URLSearchParams(await read(req));
      let id = f.get('client_id'), secret = f.get('client_secret');
      const auth = req.headers.authorization ?? '';
      if (auth.startsWith('Basic ')) [id, secret] = Buffer.from(auth.slice(6), 'base64').toString().split(':').map(decodeURIComponent);
      if (id !== FAKE_CLIENT_ID || secret !== FAKE_CLIENT_SECRET) return json(res, 401, { error: 'invalid_client' });
      const c = codes.get(f.get('code') ?? ''); codes.delete(f.get('code') ?? '');
      if (!c || c.redirect !== f.get('redirect_uri')) return json(res, 400, { error: 'invalid_grant' });
      if (b64(crypto.createHash('sha256').update(f.get('code_verifier') ?? '').digest()) !== c.challenge) return json(res, 400, { error: 'invalid_grant', error_description: 'PKCE' });
      return json(res, 200, { access_token: 'unused', token_type: 'Bearer', expires_in: 3600, id_token: idToken(c.email, c.nonce) });
    }
    json(res, 404, { error: 'not_found' });
  });
  await new Promise<void>((ok) => server.listen(port, '127.0.0.1', ok));
  return { issuer, close: () => new Promise<void>((ok) => server.close(() => ok())) };
}

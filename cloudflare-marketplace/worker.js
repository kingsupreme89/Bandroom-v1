// Bandroom "The Bandroom" marketplace worker -- Sound Bank (songs) and Trophy Room (team
// background images) community uploads. Files live in an R2 bucket; each file's metadata
// (display name + school name, exactly the upload prompt spec) lives in KV so the app can list
// and search without downloading every file.
//
// POST /upload   multipart/form-data: file, type ("song"|"image"), name, school
//   -> stores the file in R2 at "<type>/<school>/<uuid>-<original filename>",
//      stores metadata in KV at "meta:<type>:<uuid>" = { name, school, key, uploadedAt, ownerToken },
//      returns { id, url, ownerToken }.
//   -> rate-limited per IP (see RATE_LIMIT_* below).
//
// GET /list?type=song|image[&school=<name>]
//   -> { items: [{ id, name, school, url, uploadedAt, likes, reports }, ...] }, newest first.
//
// GET /file/<key>
//   -> streams the raw file back out of R2 (this is how the app actually plays/displays it).
//
// DELETE /item/<type>/<id>   header: X-Owner-Token: <ownerToken from /upload>
//   -> deletes the R2 file + KV metadata, only if the token matches the one returned at upload
//      time. No accounts, so this is the only ownership check there is -- lose the token
//      (browser data cleared, different device) and the upload can't be deleted this way.
//
// POST /report/<type>/<id>
//   -> increments a report counter for that item (KV). No auth needed -- cheap first pass at
//      moderation until there's a real review queue.
//
// POST /like/<type>/<id>
//   -> increments a like counter for that item (KV), returns the new count. No de-dup (no
//      accounts to de-dup against) -- a determined user could spam likes; acceptable for a v1.
//
// GET /leaderboard?type=song|image
//   -> { schools: [{ school, count }, ...] }, sorted by count descending. Per-team upload counts.
//
// POST /auth/verify   { idToken: "<Google ID token from the desktop app's OAuth flow>" }
//   -> verifies the token's RS256 signature against Google's published JWKS, checks iss/aud/exp,
//      upserts "user:<sub>" in KV with the profile, mints a random app session token stored as
//      "session:<token>" -> sub (30-day TTL), returns { sessionToken, profile }.
//   -> Scaffolded ahead of the app actually having a real Google OAuth Client ID configured
//      (see GoogleAuthService.cs) -- safe to deploy now since nothing calls it until that's set.
//   -> This is the ONLY place a Google identity gets trusted server-side. The desktop app also
//      decodes the ID token locally for immediate display, but that's explicitly NOT
//      signature-verified there -- only this endpoint's verification result should ever be
//      trusted for anything that writes shared state (e.g. a future "link uploads to my
//      account" feature).
//
// No accounts on uploads YET -- anyone can still upload anonymously via the owner-token system
// above. /auth/verify exists so a real account system can be built on top without a second
// migration later, but nothing currently requires being signed in.

const MAX_UPLOAD_BYTES = 25 * 1024 * 1024; // 25MB -- generous for a song clip or a background image

// Simple sliding-window rate limit: at most this many uploads per IP per window. Not a
// bulletproof defense (IPs are spoofable-ish behind shared NAT/VPN and this is best-effort
// anti-abuse, not security), but it's a real backstop where there was previously zero limiting.
const RATE_LIMIT_MAX_UPLOADS = 10;
const RATE_LIMIT_WINDOW_SECONDS = 60 * 10; // 10 minutes

function cors(extra) {
  return {
    "Access-Control-Allow-Origin": "*",
    "Access-Control-Allow-Methods": "GET, POST, DELETE, OPTIONS",
    "Access-Control-Allow-Headers": "Content-Type, X-Owner-Token",
    ...extra,
  };
}

function sanitizeSegment(s) {
  return String(s ?? "").replace(/[^a-zA-Z0-9 _.-]/g, "").trim().slice(0, 80);
}

function jsonResponse(body, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { ...cors(), "Content-Type": "application/json" } });
}

// Google's OAuth "Desktop app" client type -- ID tokens it issues always carry this issuer/
// audience. Kept here (not in a KV/env binding) since it's public information, not a secret --
// same reasoning as GoogleAuthService.ClientId on the app side.
const GOOGLE_CLIENT_ID = "REPLACE_WITH_DESKTOP_OAUTH_CLIENT_ID.apps.googleusercontent.com";
const GOOGLE_JWKS_URL = "https://www.googleapis.com/oauth2/v3/certs";

function base64UrlToUint8Array(b64url) {
  const b64 = b64url.replace(/-/g, "+").replace(/_/g, "/").padEnd(b64url.length + (4 - (b64url.length % 4)) % 4, "=");
  const bin = atob(b64);
  const bytes = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
  return bytes;
}

function base64UrlToJson(b64url) {
  return JSON.parse(new TextDecoder().decode(base64UrlToUint8Array(b64url)));
}

let _jwksCache = null; // { keys, fetchedAt } -- Google's keys rotate infrequently; cache in-memory per isolate
async function getGoogleJwks() {
  if (_jwksCache && Date.now() - _jwksCache.fetchedAt < 60 * 60 * 1000) return _jwksCache.keys;
  const res = await fetch(GOOGLE_JWKS_URL);
  const data = await res.json();
  _jwksCache = { keys: data.keys, fetchedAt: Date.now() };
  return data.keys;
}

/// Verifies a Google-issued RS256 ID token's signature (via Web Crypto, no external library
/// needed -- Workers ship SubtleCrypto) plus issuer/audience/expiry. Returns the decoded payload
/// on success, or null on ANY failure -- callers must treat null as "not authenticated", never
/// fall back to trusting the unverified payload.
async function verifyGoogleIdToken(idToken) {
  const parts = idToken.split(".");
  if (parts.length !== 3) return null;
  const [headerB64, payloadB64, sigB64] = parts;

  let header, payload;
  try { header = base64UrlToJson(headerB64); payload = base64UrlToJson(payloadB64); }
  catch { return null; }

  if (payload.aud !== GOOGLE_CLIENT_ID) return null;
  if (payload.iss !== "https://accounts.google.com" && payload.iss !== "accounts.google.com") return null;
  if (typeof payload.exp !== "number" || payload.exp * 1000 < Date.now()) return null;
  if (!payload.sub) return null;

  const keys = await getGoogleJwks();
  const jwk = keys.find((k) => k.kid === header.kid && k.alg === "RS256");
  if (!jwk) return null;

  const cryptoKey = await crypto.subtle.importKey(
    "jwk", jwk, { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" }, false, ["verify"]);
  const signature = base64UrlToUint8Array(sigB64);
  const signedData = new TextEncoder().encode(`${headerB64}.${payloadB64}`);
  const valid = await crypto.subtle.verify("RSASSA-PKCS1-v1_5", cryptoKey, signature, signedData);

  return valid ? payload : null;
}

// ---- Item index (avoids KV list()) -----------------------------------------------------
// The free Workers KV plan has a hard daily cap on list() operations. /list and /leaderboard
// used to do a full paginated list() scan on every call -- with the app hitting them on every
// team-grid open, hub load, and leaderboard render, that blew through the daily cap and made
// EVERY marketplace request (including uploads, since checkRateLimit also touches KV) start
// throwing "KV list() limit exceeded for the day". Maintaining an explicit id index per type
// means /list and /leaderboard only ever do get/put, which have a much higher free-tier quota.
function indexKey(type) {
  return `index:${type}`;
}

// Raw read only -- no rebuild attempt. Returns null if the index doesn't exist yet, which
// callers must treat as "unknown", never as "empty" (see addToIndex/removeFromIndex below).
async function readIndexRaw(env, type) {
  const raw = await env.MARKETPLACE_META.get(indexKey(type));
  if (!raw) return null;
  try { return JSON.parse(raw); } catch { return null; }
}

// Full list() scan to (re)build the index from scratch. Throws if the daily list() quota is
// exhausted -- callers decide what "can't rebuild right now" means for them.
async function rebuildIndex(env, type) {
  const ids = [];
  let cursor;
  do {
    const page = await env.MARKETPLACE_META.list({ prefix: `meta:${type}:`, cursor });
    for (const k of page.keys) ids.push(k.name.slice(`meta:${type}:`.length));
    cursor = page.list_complete ? undefined : page.cursor;
  } while (cursor);
  await env.MARKETPLACE_META.put(indexKey(type), JSON.stringify(ids));
  return ids;
}

async function getIndexIds(env, type) {
  const existing = await readIndexRaw(env, type);
  return existing !== null ? existing : await rebuildIndex(env, type);
}

// IMPORTANT: if the index doesn't exist yet and a rebuild fails (quota exhausted), this must
// NOT write a fresh index containing only `id` -- that would permanently orphan every
// already-uploaded item that isn't `id` (once an index exists, it's never rebuilt again). In
// that case we leave the index absent; the item's meta record is already stored by the caller,
// so the next successful rebuild (after the quota resets) picks it up naturally alongside
// everything else.
async function addToIndex(env, type, id) {
  const existing = await readIndexRaw(env, type);
  if (existing !== null) {
    if (!existing.includes(id)) existing.push(id);
    await env.MARKETPLACE_META.put(indexKey(type), JSON.stringify(existing));
    return;
  }
  let rebuilt;
  try { rebuilt = await rebuildIndex(env, type); } catch { return; } // leave index absent, self-heals later
  if (!rebuilt.includes(id)) rebuilt.push(id);
  await env.MARKETPLACE_META.put(indexKey(type), JSON.stringify(rebuilt));
}

async function removeFromIndex(env, type, id) {
  const existing = await readIndexRaw(env, type);
  if (existing === null) return; // nothing indexed yet -- next rebuild reflects reality anyway
  const next = existing.filter((existingId) => existingId !== id);
  await env.MARKETPLACE_META.put(indexKey(type), JSON.stringify(next));
}

async function checkRateLimit(env, request) {
  const ip = request.headers.get("cf-connecting-ip") ?? "unknown";
  const key = `ratelimit:upload:${ip}`;
  const raw = await env.MARKETPLACE_META.get(key);
  const count = raw ? Number(raw) : 0;
  if (count >= RATE_LIMIT_MAX_UPLOADS) return false;
  await env.MARKETPLACE_META.put(key, String(count + 1), { expirationTtl: RATE_LIMIT_WINDOW_SECONDS });
  return true;
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    if (request.method === "OPTIONS") return new Response(null, { headers: cors() });

    if (url.pathname === "/upload" && request.method === "POST") {
      const contentLength = Number(request.headers.get("content-length") ?? "0");
      if (contentLength > MAX_UPLOAD_BYTES) {
        return new Response("file too large (25MB max)", { status: 413, headers: cors() });
      }

      const allowed = await checkRateLimit(env, request);
      if (!allowed) {
        return new Response("too many uploads -- try again in a few minutes", { status: 429, headers: cors() });
      }

      let form;
      try {
        form = await request.formData();
      } catch {
        return new Response("bad form data", { status: 400, headers: cors() });
      }

      const type = form.get("type");
      if (type !== "song" && type !== "image") {
        return new Response('type must be "song" or "image"', { status: 400, headers: cors() });
      }
      const name = sanitizeSegment(form.get("name"));
      const school = sanitizeSegment(form.get("school"));
      if (!name || !school) {
        return new Response("name and school are required", { status: 400, headers: cors() });
      }
      const file = form.get("file");
      if (!(file instanceof File)) {
        return new Response("file is required", { status: 400, headers: cors() });
      }

      const id = crypto.randomUUID();
      const ownerToken = crypto.randomUUID();
      const safeFilename = sanitizeSegment(file.name) || "upload";
      const r2Key = `${type}/${school}/${id}-${safeFilename}`;

      await env.MARKETPLACE_FILES.put(r2Key, await file.arrayBuffer(), {
        httpMetadata: { contentType: file.type || "application/octet-stream" },
      });

      const meta = {
        id, type, name, school, key: r2Key, uploadedAt: new Date().toISOString(),
        ownerToken, likes: 0, reports: 0,
      };
      await env.MARKETPLACE_META.put(`meta:${type}:${id}`, JSON.stringify(meta));
      await addToIndex(env, type, id);

      return jsonResponse({ id, url: `${url.origin}/file/${encodeURIComponent(r2Key)}`, ownerToken });
    }

    if (url.pathname === "/list" && request.method === "GET") {
      const type = url.searchParams.get("type");
      if (type !== "song" && type !== "image") {
        return new Response('type must be "song" or "image"', { status: 400, headers: cors() });
      }
      const schoolFilter = url.searchParams.get("school");

      let ids;
      try {
        ids = await getIndexIds(env, type);
      } catch {
        // Daily KV list() quota exhausted and no index exists yet to fall back on -- return an
        // empty list rather than a 500 so the UI shows "nothing here yet" instead of erroring.
        // Self-heals: once the quota resets, the next /list call rebuilds the index for good.
        return jsonResponse({ items: [] });
      }

      const items = [];
      for (const id of ids) {
        const raw = await env.MARKETPLACE_META.get(`meta:${type}:${id}`);
        if (!raw) continue;
        const meta = JSON.parse(raw);
        if (schoolFilter && meta.school.toLowerCase() !== schoolFilter.toLowerCase()) continue;
        // ownerToken is a delete credential -- never echoed back in /list, only returned once
        // at upload time, so a passive listing request can never leak another uploader's token.
        const { ownerToken, ...pub } = meta;
        items.push({ ...pub, likes: meta.likes ?? 0, reports: meta.reports ?? 0, url: `${url.origin}/file/${encodeURIComponent(meta.key)}` });
      }

      items.sort((a, b) => (a.uploadedAt < b.uploadedAt ? 1 : -1));
      return jsonResponse({ items });
    }

    if (url.pathname === "/leaderboard" && request.method === "GET") {
      const type = url.searchParams.get("type");
      if (type !== "song" && type !== "image") {
        return new Response('type must be "song" or "image"', { status: 400, headers: cors() });
      }

      let ids;
      try {
        ids = await getIndexIds(env, type);
      } catch {
        return jsonResponse({ schools: [] });
      }

      const counts = new Map();
      for (const id of ids) {
        const raw = await env.MARKETPLACE_META.get(`meta:${type}:${id}`);
        if (!raw) continue;
        const meta = JSON.parse(raw);
        counts.set(meta.school, (counts.get(meta.school) ?? 0) + 1);
      }

      const schools = [...counts.entries()]
        .map(([school, count]) => ({ school, count }))
        .sort((a, b) => b.count - a.count);
      return jsonResponse({ schools });
    }

    if (url.pathname.startsWith("/like/") && request.method === "POST") {
      const parts = url.pathname.slice("/like/".length).split("/");
      const [type, id] = parts;
      if ((type !== "song" && type !== "image") || !id) {
        return new Response("bad request", { status: 400, headers: cors() });
      }
      const metaKey = `meta:${type}:${id}`;
      const raw = await env.MARKETPLACE_META.get(metaKey);
      if (!raw) return new Response("not found", { status: 404, headers: cors() });
      const meta = JSON.parse(raw);
      meta.likes = (meta.likes ?? 0) + 1;
      await env.MARKETPLACE_META.put(metaKey, JSON.stringify(meta));
      return jsonResponse({ likes: meta.likes });
    }

    if (url.pathname.startsWith("/report/") && request.method === "POST") {
      const parts = url.pathname.slice("/report/".length).split("/");
      const [type, id] = parts;
      if ((type !== "song" && type !== "image") || !id) {
        return new Response("bad request", { status: 400, headers: cors() });
      }
      const metaKey = `meta:${type}:${id}`;
      const raw = await env.MARKETPLACE_META.get(metaKey);
      if (!raw) return new Response("not found", { status: 404, headers: cors() });
      const meta = JSON.parse(raw);
      meta.reports = (meta.reports ?? 0) + 1;
      await env.MARKETPLACE_META.put(metaKey, JSON.stringify(meta));
      return jsonResponse({ reports: meta.reports });
    }

    if (url.pathname.startsWith("/item/") && request.method === "DELETE") {
      const parts = url.pathname.slice("/item/".length).split("/");
      const [type, id] = parts;
      if ((type !== "song" && type !== "image") || !id) {
        return new Response("bad request", { status: 400, headers: cors() });
      }
      const metaKey = `meta:${type}:${id}`;
      const raw = await env.MARKETPLACE_META.get(metaKey);
      if (!raw) return new Response("not found", { status: 404, headers: cors() });
      const meta = JSON.parse(raw);

      const token = request.headers.get("x-owner-token");
      if (!token || token !== meta.ownerToken) {
        return new Response("forbidden -- owner token doesn't match", { status: 403, headers: cors() });
      }

      await env.MARKETPLACE_FILES.delete(meta.key);
      await env.MARKETPLACE_META.delete(metaKey);
      await removeFromIndex(env, type, id);
      return jsonResponse({ deleted: true });
    }

    if (url.pathname === "/auth/verify" && request.method === "POST") {
      let body;
      try { body = await request.json(); } catch { return new Response("bad request", { status: 400, headers: cors() }); }
      const idToken = body?.idToken;
      if (!idToken || typeof idToken !== "string") return new Response("bad request", { status: 400, headers: cors() });

      const claims = await verifyGoogleIdToken(idToken);
      if (!claims) return new Response("invalid token", { status: 401, headers: cors() });

      const profile = {
        sub: claims.sub,
        email: claims.email ?? "",
        name: claims.name ?? claims.email ?? "",
        picture: claims.picture ?? null,
      };
      const existingRaw = await env.MARKETPLACE_META.get(`user:${claims.sub}`);
      const existing = existingRaw ? JSON.parse(existingRaw) : null;
      await env.MARKETPLACE_META.put(`user:${claims.sub}`, JSON.stringify({
        ...profile,
        createdAt: existing?.createdAt ?? new Date().toISOString(),
        lastSignInAt: new Date().toISOString(),
      }));

      const sessionToken = crypto.randomUUID() + crypto.randomUUID();
      await env.MARKETPLACE_META.put(`session:${sessionToken}`, claims.sub, { expirationTtl: 60 * 60 * 24 * 30 });

      return jsonResponse({ sessionToken, profile });
    }

    if (url.pathname.startsWith("/file/") && request.method === "GET") {
      const key = decodeURIComponent(url.pathname.slice("/file/".length));
      const obj = await env.MARKETPLACE_FILES.get(key);
      if (!obj) return new Response("not found", { status: 404, headers: cors() });
      return new Response(obj.body, {
        headers: cors({ "Content-Type": obj.httpMetadata?.contentType ?? "application/octet-stream" }),
      });
    }

    return new Response("not found", { status: 404, headers: cors() });
  },
};

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
// GET /list?type=song|image[&school=<name>][&sort=newest|views|downloads|likes]
//   -> { items: [{ id, name, school, url, uploadedAt, likes, reports, views, downloads }, ...] },
//      sorted per `sort` (default "newest").
//
// GET /file/<key>
//   -> streams the raw file back out of R2 (this is how the app actually plays/displays it).
//
// DELETE /item/<type>/<id>   header: X-Owner-Token: <ownerToken from /upload>
//   -> deletes the R2 file + KV metadata, only if the token matches the one returned at upload
//      time. No accounts, so this is the only ownership check there is -- lose the token
//      (browser data cleared, different device) and the upload can't be deleted this way.
//   -> Admin override: header X-Admin-Token: <ADMIN_TOKEN> (Worker secret, set via
//      `wrangler secret put ADMIN_TOKEN`, never hardcoded here) bypasses the ownerToken match
//      entirely and deletes regardless of who owns the item. Only the app owner's local/dev
//      build ever has this token (see admin_token.local.txt in the C# app) -- it is
//      deliberately never shipped in the public installer.
//
// PATCH /item/<type>/<id>   header: X-Admin-Token: <ADMIN_TOKEN>  OR  X-Owner-Token: <ownerToken>
//                           body: { name?, school? }
//   -> metadata edit. Either the admin token (bypasses ownership, same as DELETE's admin
//      override) or the item's own ownerToken (same one returned at /upload time, same one
//      DELETE's owner path already checks) can rename/re-categorize -- a regular uploader can
//      now fix a typo in their own upload without deleting and re-uploading. Both paths run
//      through the same sanitizeSegment() the /upload path uses. Updates only `name`/`school` --
//      `id`/`key`/`ownerToken` and any counters (likes/reports/views/downloads) are never
//      touched by this endpoint.
//
// POST /report/<type>/<id>
//   -> increments a report counter for that item (KV). No auth needed -- cheap first pass at
//      moderation until there's a real review queue.
//
// POST /like/<type>/<id>
//   -> increments a like counter for that item (KV), returns the new count. No de-dup (no
//      accounts to de-dup against) -- a determined user could spam likes; acceptable for a v1.
//
// POST /view/<type>/<id>
//   -> increments a view counter for that item (KV), returns the new count. Fired when the
//      app opens an item's preview/detail (not on every list render) -- same no-de-dup caveat
//      as /like, same rate limit bucket as like/report.
//
// POST /download/<type>/<id>
//   -> increments a download counter for that item (KV), returns the new count. Fired when a
//      download actually completes (not merely requested) -- same pattern as /like.
//
// GET /leaderboard?type=song|image
//   -> { schools: [{ school, count }, ...] }, sorted by count descending. Per-team upload counts.
//
// POST /auth/verify   { idToken: "<Google ID token from the desktop app's OAuth flow>" }
//   -> verifies the token's RS256 signature against Google's published JWKS, checks iss/aud/exp,
//      upserts "user:<sub>" in KV with the profile, mints a random app session token stored as
//      "session:<token>" -> sub (30-day TTL), returns { sessionToken, profile }.
//   -> Rate-limited per IP like /upload (see checkRateLimit) -- live and reachable now that a
//      real Google OAuth Client ID is configured (see GoogleAuthService.cs).
//   -> This is the ONLY place a Google identity gets trusted server-side. The desktop app also
//      decodes the ID token locally for immediate display, but that's explicitly NOT
//      signature-verified there -- only this endpoint's verification result should ever be
//      trusted for anything that writes shared state (e.g. a future "link uploads to my
//      account" feature).
//
// GET/PUT /profile   header: Authorization: Bearer <sessionToken from /auth/verify>
//   -> the signed-in user's "universal profile" (favorite team + lifetime stats: games watched,
//      songs triggered, marketplace uploads/downloads). GET returns {found:false} if nothing's
//      saved yet. PUT merges favoriteTeam/stats into the existing user:<sub> record.
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
    "Access-Control-Allow-Methods": "GET, POST, DELETE, PATCH, OPTIONS",
    "Access-Control-Allow-Headers": "Content-Type, X-Owner-Token, X-Admin-Token",
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
const GOOGLE_CLIENT_ID = "4767958466-o8cc22j3kdikkfedfromva3df4llsbg8.apps.googleusercontent.com";
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

// action-scoped so /upload, /like, /report each get their own independent budget per IP.
async function checkRateLimit(env, request, action, maxCount) {
  const ip = request.headers.get("cf-connecting-ip") ?? "unknown";
  const key = `ratelimit:${action}:${ip}`;
  const raw = await env.MARKETPLACE_META.get(key);
  const count = raw ? Number(raw) : 0;
  if (count >= maxCount) return false;
  await env.MARKETPLACE_META.put(key, String(count + 1), { expirationTtl: RATE_LIMIT_WINDOW_SECONDS });
  return true;
}

// /like and /report had NO rate limiting at all (unlike /upload) -- unauthenticated, unbounded
// KV writes, which is a more realistic path to exhausting the free tier's 1,000-writes/day cap
// than upload volume ever was. Generous limit since liking/reporting is meant to be casual.
const RATE_LIMIT_MAX_LIKES_REPORTS = 60;

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    if (request.method === "OPTIONS") return new Response(null, { headers: cors() });

    if (url.pathname === "/upload" && request.method === "POST") {
      const contentLength = Number(request.headers.get("content-length") ?? "0");
      if (contentLength > MAX_UPLOAD_BYTES) {
        return new Response("file too large (25MB max)", { status: 413, headers: cors() });
      }

      const allowed = await checkRateLimit(env, request, "upload", RATE_LIMIT_MAX_UPLOADS);
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
        ownerToken, likes: 0, reports: 0, views: 0, downloads: 0,
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
      const sort = url.searchParams.get("sort") ?? "newest";

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
        items.push({
          ...pub,
          likes: meta.likes ?? 0,
          reports: meta.reports ?? 0,
          // ?? 0 -- items uploaded before this field existed have no views/downloads in KV yet.
          views: meta.views ?? 0,
          downloads: meta.downloads ?? 0,
          url: `${url.origin}/file/${encodeURIComponent(meta.key)}`,
        });
      }

      // "newest" (default) preserves the existing uploadedAt-descending behavior; the rest sort
      // by their counter descending, ties broken by newest-first so equal counts still order
      // predictably.
      if (sort === "views" || sort === "downloads" || sort === "likes") {
        items.sort((a, b) => (b[sort] - a[sort]) || (a.uploadedAt < b.uploadedAt ? 1 : -1));
      } else {
        items.sort((a, b) => (a.uploadedAt < b.uploadedAt ? 1 : -1));
      }
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
      const allowedLike = await checkRateLimit(env, request, "like", RATE_LIMIT_MAX_LIKES_REPORTS);
      if (!allowedLike) {
        return new Response("too many likes -- try again in a few minutes", { status: 429, headers: cors() });
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
      const allowedReport = await checkRateLimit(env, request, "report", RATE_LIMIT_MAX_LIKES_REPORTS);
      if (!allowedReport) {
        return new Response("too many reports -- try again in a few minutes", { status: 429, headers: cors() });
      }
      const metaKey = `meta:${type}:${id}`;
      const raw = await env.MARKETPLACE_META.get(metaKey);
      if (!raw) return new Response("not found", { status: 404, headers: cors() });
      const meta = JSON.parse(raw);
      meta.reports = (meta.reports ?? 0) + 1;
      await env.MARKETPLACE_META.put(metaKey, JSON.stringify(meta));
      return jsonResponse({ reports: meta.reports });
    }

    if (url.pathname.startsWith("/view/") && request.method === "POST") {
      const parts = url.pathname.slice("/view/".length).split("/");
      const [type, id] = parts;
      if ((type !== "song" && type !== "image") || !id) {
        return new Response("bad request", { status: 400, headers: cors() });
      }
      const allowedView = await checkRateLimit(env, request, "view", RATE_LIMIT_MAX_LIKES_REPORTS);
      if (!allowedView) {
        return new Response("too many views -- try again in a few minutes", { status: 429, headers: cors() });
      }
      const metaKey = `meta:${type}:${id}`;
      const raw = await env.MARKETPLACE_META.get(metaKey);
      if (!raw) return new Response("not found", { status: 404, headers: cors() });
      const meta = JSON.parse(raw);
      meta.views = (meta.views ?? 0) + 1;
      await env.MARKETPLACE_META.put(metaKey, JSON.stringify(meta));
      return jsonResponse({ views: meta.views });
    }

    if (url.pathname.startsWith("/download/") && request.method === "POST") {
      const parts = url.pathname.slice("/download/".length).split("/");
      const [type, id] = parts;
      if ((type !== "song" && type !== "image") || !id) {
        return new Response("bad request", { status: 400, headers: cors() });
      }
      const allowedDownload = await checkRateLimit(env, request, "download", RATE_LIMIT_MAX_LIKES_REPORTS);
      if (!allowedDownload) {
        return new Response("too many downloads -- try again in a few minutes", { status: 429, headers: cors() });
      }
      const metaKey = `meta:${type}:${id}`;
      const raw = await env.MARKETPLACE_META.get(metaKey);
      if (!raw) return new Response("not found", { status: 404, headers: cors() });
      const meta = JSON.parse(raw);
      meta.downloads = (meta.downloads ?? 0) + 1;
      await env.MARKETPLACE_META.put(metaKey, JSON.stringify(meta));
      return jsonResponse({ downloads: meta.downloads });
    }

    // GET/PUT /profile   header: Authorization: Bearer <sessionToken from /auth/verify>
    //   -> the "universal profile" (favorite team + lifetime stats) mirrored here so it follows
    //      a signed-in account across devices/reinstalls. Local storage on each device is always
    //      the real source of truth day-to-day (see ConfigStore.UserProfile) -- this is a
    //      best-effort sync target, pulled once on sign-in and pushed on every local change.
    if (url.pathname === "/profile" && (request.method === "GET" || request.method === "PUT")) {
      const authHeader = request.headers.get("authorization") ?? "";
      const sessionToken = authHeader.startsWith("Bearer ") ? authHeader.slice(7) : "";
      if (!sessionToken) return new Response("missing session token", { status: 401, headers: cors() });

      const sub = await env.MARKETPLACE_META.get(`session:${sessionToken}`);
      if (!sub) return new Response("invalid or expired session", { status: 401, headers: cors() });

      const userKey = `user:${sub}`;
      const existingRaw = await env.MARKETPLACE_META.get(userKey);
      const existing = existingRaw ? JSON.parse(existingRaw) : null;

      if (request.method === "GET") {
        if (!existing || (!existing.favoriteTeam && !existing.stats)) {
          return jsonResponse({ found: false });
        }
        return jsonResponse({
          found: true,
          favoriteTeam: existing.favoriteTeam ?? null,
          rivalTeam: existing.rivalTeam ?? null,
          bio: existing.bio ?? null,
          stats: existing.stats ?? {},
          customTeamLogos: existing.customTeamLogos ?? {},
        });
      }

      // PUT -- merge into whatever profile fields /auth/verify already wrote (email/name/
      // picture/createdAt) rather than overwriting them. Dictionary fields (eventCounts,
      // gamesWatchedByTeam) and the object cap below exist because the client already tracks an
      // unbounded set of keys (one per event/team) -- capped at 200 entries each so a corrupt or
      // malicious client can't grow this KV value without bound.
      let body;
      try { body = await request.json(); } catch { return new Response("bad request", { status: 400, headers: cors() }); }

      function capObject(obj) {
        if (!obj || typeof obj !== "object") return {};
        const entries = Object.entries(obj).slice(0, 200);
        const out = {};
        // NOT sanitizeSegment(k) -- these keys are internal event/team names like "Offense:
        // Touchdown Scored", never rendered raw as HTML or used as a filesystem/KV path segment
        // the way sanitizeSegment's other callers use it. Stripping characters (it strips ":")
        // was silently splitting one local key into two different keys once round-tripped
        // through the cloud, fragmenting "most-triggered event" stats. Just cap length.
        for (const [k, v] of entries) out[k.slice(0, 80)] = Number(v) || 0;
        return out;
      }

      const favoriteTeam = typeof body?.favoriteTeam === "string" ? sanitizeSegment(body.favoriteTeam) : (existing?.favoriteTeam ?? null);
      const rivalTeam = typeof body?.rivalTeam === "string" ? sanitizeSegment(body.rivalTeam) : (existing?.rivalTeam ?? null);
      const bio = typeof body?.bio === "string" ? body.bio.slice(0, 140) : (existing?.bio ?? null);
      const stats = body?.stats && typeof body.stats === "object" ? {
        gamesWatched: Number(body.stats.gamesWatched) || 0,
        songsTriggered: Number(body.stats.songsTriggered) || 0,
        marketplaceUploads: Number(body.stats.marketplaceUploads) || 0,
        marketplaceDownloads: Number(body.stats.marketplaceDownloads) || 0,
        favoriteTeamWins: Number(body.stats.favoriteTeamWins) || 0,
        favoriteTeamLosses: Number(body.stats.favoriteTeamLosses) || 0,
        streakCurrentDays: Number(body.stats.streakCurrentDays) || 0,
        eventCounts: capObject(body.stats.eventCounts),
        gamesWatchedByTeam: capObject(body.stats.gamesWatchedByTeam),
      } : (existing?.stats ?? {});

      // customTeamLogos: keyed by team name -> { base64Png, updatedAtUtc }, "newest wins" merge
      // happens client-side (WebBridge.MergeLatestWins) before this PUT, so the server just
      // carries the field through untouched -- same schemaless-KV-JSON treatment as stats above.
      // Capped at 50 entries (fewer than the 200 on eventCounts/gamesWatchedByTeam) since each
      // entry embeds a full base64 PNG rather than a number -- KV's ~25MB value cap is nowhere
      // close at this app's roster size, but logos are the one field here big enough to matter if
      // that ever changes (flagged in the handoff notes, not a problem yet).
      function capLogos(obj) {
        if (!obj || typeof obj !== "object") return {};
        const entries = Object.entries(obj).slice(0, 50);
        const out = {};
        for (const [k, v] of entries) {
          if (!v || typeof v !== "object") continue;
          if (typeof v.base64Png !== "string" || typeof v.updatedAtUtc !== "string") continue;
          out[k.slice(0, 80)] = { base64Png: v.base64Png, updatedAtUtc: v.updatedAtUtc };
        }
        return out;
      }
      const customTeamLogos = body?.customTeamLogos && typeof body.customTeamLogos === "object"
        ? capLogos(body.customTeamLogos)
        : (existing?.customTeamLogos ?? {});

      await env.MARKETPLACE_META.put(userKey, JSON.stringify({ ...(existing ?? {}), favoriteTeam, rivalTeam, bio, stats, customTeamLogos }));
      return jsonResponse({ favoriteTeam, rivalTeam, bio, stats, customTeamLogos });
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

      const adminToken = request.headers.get("x-admin-token");
      const isAdmin = !!env.ADMIN_TOKEN && !!adminToken && adminToken === env.ADMIN_TOKEN;

      if (!isAdmin) {
        const token = request.headers.get("x-owner-token");
        if (!token || token !== meta.ownerToken) {
          return new Response("forbidden -- owner token doesn't match", { status: 403, headers: cors() });
        }
      }

      await env.MARKETPLACE_FILES.delete(meta.key);
      await env.MARKETPLACE_META.delete(metaKey);
      await removeFromIndex(env, type, id);
      return jsonResponse({ deleted: true });
    }

    if (url.pathname.startsWith("/item/") && request.method === "PATCH") {
      // Metadata edit -- either the app owner's admin token (bypasses ownership, same as
      // DELETE's admin override), OR the uploader's own X-Owner-Token for THIS item (same
      // ownerToken minted at /upload time and already used by DELETE's owner path). No other
      // auth grants this -- an owner token only ever authorizes editing/deleting the one item
      // it was issued for.
      const parts = url.pathname.slice("/item/".length).split("/");
      const [type, id] = parts;
      if ((type !== "song" && type !== "image") || !id) {
        return new Response("bad request", { status: 400, headers: cors() });
      }
      const metaKey = `meta:${type}:${id}`;
      const raw = await env.MARKETPLACE_META.get(metaKey);
      if (!raw) return new Response("not found", { status: 404, headers: cors() });
      const meta = JSON.parse(raw);

      const adminToken = request.headers.get("x-admin-token");
      const isAdmin = !!env.ADMIN_TOKEN && !!adminToken && adminToken === env.ADMIN_TOKEN;
      if (!isAdmin) {
        const ownerToken = request.headers.get("x-owner-token");
        if (!ownerToken || ownerToken !== meta.ownerToken) {
          return new Response("forbidden -- owner or admin token required", { status: 403, headers: cors() });
        }
      }

      let body;
      try {
        body = await request.json();
      } catch {
        return new Response("bad request -- invalid JSON body", { status: 400, headers: cors() });
      }

      // Only editable metadata fields -- id/key/ownerToken/counters are never touched here.
      // Run through the SAME sanitizeSegment used at upload time -- these fields get interpolated
      // unescaped into an `alt="..."` attribute inside innerHTML on the client (app.js's item-tile
      // renderer), so anything admitting quotes/angle-brackets here would be a stored-XSS vector
      // hitting every user who lists this item, not just the admin.
      if (typeof body.name === "string" && sanitizeSegment(body.name)) meta.name = sanitizeSegment(body.name);
      if (typeof body.school === "string" && sanitizeSegment(body.school)) meta.school = sanitizeSegment(body.school);

      await env.MARKETPLACE_META.put(metaKey, JSON.stringify(meta));
      return jsonResponse({ id, name: meta.name, school: meta.school });
    }

    if (url.pathname === "/auth/verify" && request.method === "POST") {
      // Now that sign-in is actually live (real Client ID configured), this was the one
      // remaining mutating endpoint with zero rate limiting -- unbounded retries against it
      // could still mint unlimited sessions (a KV write per success) toward the free-tier daily
      // write cap, the same failure class already fixed on /list, /like, /report.
      const allowedAuth = await checkRateLimit(env, request, "authverify", RATE_LIMIT_MAX_LIKES_REPORTS);
      if (!allowedAuth) {
        return new Response("too many sign-in attempts -- try again in a few minutes", { status: 429, headers: cors() });
      }

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

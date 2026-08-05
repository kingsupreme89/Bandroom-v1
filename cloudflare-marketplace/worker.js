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
// No accounts -- anyone can upload. Deliberately small/simple for a first version.

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

      return jsonResponse({ id, url: `${url.origin}/file/${encodeURIComponent(r2Key)}`, ownerToken });
    }

    if (url.pathname === "/list" && request.method === "GET") {
      const type = url.searchParams.get("type");
      if (type !== "song" && type !== "image") {
        return new Response('type must be "song" or "image"', { status: 400, headers: cors() });
      }
      const schoolFilter = url.searchParams.get("school");

      const items = [];
      let cursor;
      do {
        const page = await env.MARKETPLACE_META.list({ prefix: `meta:${type}:`, cursor });
        for (const k of page.keys) {
          const raw = await env.MARKETPLACE_META.get(k.name);
          if (!raw) continue;
          const meta = JSON.parse(raw);
          if (schoolFilter && meta.school.toLowerCase() !== schoolFilter.toLowerCase()) continue;
          // ownerToken is a delete credential -- never echoed back in /list, only returned once
          // at upload time, so a passive listing request can never leak another uploader's token.
          const { ownerToken, ...pub } = meta;
          items.push({ ...pub, likes: meta.likes ?? 0, reports: meta.reports ?? 0, url: `${url.origin}/file/${encodeURIComponent(meta.key)}` });
        }
        cursor = page.list_complete ? undefined : page.cursor;
      } while (cursor);

      items.sort((a, b) => (a.uploadedAt < b.uploadedAt ? 1 : -1));
      return jsonResponse({ items });
    }

    if (url.pathname === "/leaderboard" && request.method === "GET") {
      const type = url.searchParams.get("type");
      if (type !== "song" && type !== "image") {
        return new Response('type must be "song" or "image"', { status: 400, headers: cors() });
      }

      const counts = new Map();
      let cursor;
      do {
        const page = await env.MARKETPLACE_META.list({ prefix: `meta:${type}:`, cursor });
        for (const k of page.keys) {
          const raw = await env.MARKETPLACE_META.get(k.name);
          if (!raw) continue;
          const meta = JSON.parse(raw);
          counts.set(meta.school, (counts.get(meta.school) ?? 0) + 1);
        }
        cursor = page.list_complete ? undefined : page.cursor;
      } while (cursor);

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
      return jsonResponse({ deleted: true });
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

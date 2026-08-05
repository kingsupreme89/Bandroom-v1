// Bandroom "The Bandroom" marketplace worker -- Sound Bank (songs) and Trophy Room (team
// background images) community uploads. Files live in an R2 bucket; each file's metadata
// (display name + school name, exactly the upload prompt spec) lives in KV so the app can list
// and search without downloading every file.
//
// POST /upload   multipart/form-data: file, type ("song"|"image"), name, school
//   -> stores the file in R2 at "<type>/<school>/<uuid>-<original filename>",
//      stores metadata in KV at "meta:<type>:<uuid>" = { name, school, key, uploadedAt },
//      returns { id, url }.
//
// GET /list?type=song|image[&school=<name>]
//   -> { items: [{ id, name, school, url, uploadedAt }, ...] }, newest first.
//
// GET /file/<key>
//   -> streams the raw file back out of R2 (this is how the app actually plays/displays it).
//
// No accounts -- anyone can upload. Deliberately small/simple for a first version; if abuse
// becomes a problem later, that's a follow-up (rate limiting, review queue, etc), not solved here.

const MAX_UPLOAD_BYTES = 25 * 1024 * 1024; // 25MB -- generous for a song clip or a background image

function cors(extra) {
  return {
    "Access-Control-Allow-Origin": "*",
    "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
    "Access-Control-Allow-Headers": "Content-Type",
    ...extra,
  };
}

function sanitizeSegment(s) {
  return String(s ?? "").replace(/[^a-zA-Z0-9 _.-]/g, "").trim().slice(0, 80);
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
      const safeFilename = sanitizeSegment(file.name) || "upload";
      const r2Key = `${type}/${school}/${id}-${safeFilename}`;

      await env.MARKETPLACE_FILES.put(r2Key, await file.arrayBuffer(), {
        httpMetadata: { contentType: file.type || "application/octet-stream" },
      });

      const meta = { id, type, name, school, key: r2Key, uploadedAt: new Date().toISOString() };
      await env.MARKETPLACE_META.put(`meta:${type}:${id}`, JSON.stringify(meta));

      return new Response(JSON.stringify({ id, url: `${url.origin}/file/${encodeURIComponent(r2Key)}` }), {
        headers: { ...cors(), "Content-Type": "application/json" },
      });
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
          items.push({ ...meta, url: `${url.origin}/file/${encodeURIComponent(meta.key)}` });
        }
        cursor = page.list_complete ? undefined : page.cursor;
      } while (cursor);

      items.sort((a, b) => (a.uploadedAt < b.uploadedAt ? 1 : -1));
      return new Response(JSON.stringify({ items }), {
        headers: { ...cors(), "Content-Type": "application/json" },
      });
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

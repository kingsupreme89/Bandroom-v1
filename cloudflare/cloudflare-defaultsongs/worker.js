// Bandroom default song pack worker -- serves the one big optional asset (2,241 files,
// ~2.8GB zipped) that got pulled out of the installer to stay under GitHub Releases' 2GB
// per-asset cap (see v1.0.48 release notes). The app downloads this once, after install,
// only if the user opts in.
//
// GET /pack.zip
//   -> streams the zip straight from R2 (obj.body is already a ReadableStream, so this
//      doesn't buffer 2.8GB in the Worker's memory). Supports Range requests so the app's
//      download can resume/retry rather than restarting from zero on a network hiccup.
//
// GET /pack-info
//   -> { size: <bytes>, etag: "<r2 etag>" }
//      Lets the app show "2.8 GB" before the user commits to downloading, and lets it
//      detect a stale cached copy later without re-downloading to check.

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    const cors = {
      "Access-Control-Allow-Origin": "*",
      "Access-Control-Allow-Methods": "GET, OPTIONS",
      "Access-Control-Allow-Headers": "Content-Type, Range",
    };

    if (request.method === "OPTIONS") {
      return new Response(null, { headers: cors });
    }

    if (url.pathname === "/pack-info" && request.method === "GET") {
      const head = await env.DEFAULT_SONGS.head("pack.zip");
      if (!head) return new Response(JSON.stringify({ error: "not uploaded yet" }), {
        status: 404, headers: { ...cors, "Content-Type": "application/json" },
      });
      return new Response(JSON.stringify({ size: head.size, etag: head.etag }), {
        headers: { ...cors, "Content-Type": "application/json" },
      });
    }

    if (url.pathname === "/pack.zip" && request.method === "GET") {
      const range = request.headers.get("Range") ?? undefined;
      const obj = await env.DEFAULT_SONGS.get("pack.zip", range ? { range: parseRange(range) } : undefined);
      if (!obj) return new Response("not found", { status: 404, headers: cors });

      const headers = {
        ...cors,
        "Content-Type": "application/zip",
        "Content-Length": String(obj.size),
        "Accept-Ranges": "bytes",
        ETag: obj.etag,
      };
      if (obj.range) {
        const start = "offset" in obj.range ? obj.range.offset : 0;
        const end = start + ("length" in obj.range ? obj.range.length : obj.size) - 1;
        headers["Content-Range"] = `bytes ${start}-${end}/${obj.size}`;
        return new Response(obj.body, { status: 206, headers });
      }
      return new Response(obj.body, { headers });
    }

    return new Response("not found", { status: 404, headers: cors });
  },
};

// Minimal single-range "bytes=start-end" / "bytes=start-" parser -- the app's downloader only
// ever sends simple single-range requests (resume-from-offset), never multi-range.
function parseRange(header) {
  const m = /^bytes=(\d+)-(\d*)$/.exec(header);
  if (!m) return undefined;
  const offset = parseInt(m[1], 10);
  if (m[2]) return { offset, length: parseInt(m[2], 10) - offset + 1 };
  return { offset };
}

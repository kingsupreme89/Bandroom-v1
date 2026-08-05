// Bandroom live user-count worker.
//
// POST /heartbeat  { id: "<anonymous per-install guid>" }
//   -> stores KV key "u:<id>" with a 120s TTL. Each running Bandroom pings this
//      every ~60s, so the key naturally expires ~60s after the app closes/crashes
//      without needing an explicit "goodbye" call.
//
// GET /count
//   -> { count: <number of live keys under "u:"> }
//
// No IP, hostname, or any identifying info is stored -- just a random GUID the
// app generates once and keeps locally, and a timestamp KV manages for us via TTL.

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    // Same-origin-ish CORS: the app calls this from a WebView2 page served off a
    // virtual https://appassets host, so allow any origin -- there's no cookie/auth
    // to leak, just a counter.
    const cors = {
      "Access-Control-Allow-Origin": "*",
      "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
      "Access-Control-Allow-Headers": "Content-Type",
    };

    if (request.method === "OPTIONS") {
      return new Response(null, { headers: cors });
    }

    if (url.pathname === "/heartbeat" && request.method === "POST") {
      let body;
      try {
        body = await request.json();
      } catch {
        return new Response("bad json", { status: 400, headers: cors });
      }
      const id = String(body?.id ?? "").slice(0, 64);
      if (!/^[a-zA-Z0-9-]{8,64}$/.test(id)) {
        return new Response("bad id", { status: 400, headers: cors });
      }
      await env.USERCOUNT.put(`u:${id}`, "1", { expirationTtl: 120 });
      return new Response("ok", { headers: cors });
    }

    if (url.pathname === "/count" && request.method === "GET") {
      // The free Workers KV plan has a hard daily cap on list() operations. This used to run a
      // full list() scan on every /count call -- with clients polling every ~20s, that alone
      // was enough to exhaust the daily cap and 500 the whole worker (same failure mode found
      // in the marketplace worker). Cache the computed count in KV for 15s so list() runs at
      // most once per 15s total, no matter how many clients are polling.
      const cacheRaw = await env.USERCOUNT.get("count_cache");
      if (cacheRaw) {
        try {
          const cached = JSON.parse(cacheRaw);
          if (Date.now() - cached.computedAt < 15000) {
            return new Response(JSON.stringify({ count: cached.count }), {
              headers: { ...cors, "Content-Type": "application/json" },
            });
          }
        } catch { /* fall through to recompute */ }
      }

      let count = 0;
      try {
        let cursor;
        do {
          const page = await env.USERCOUNT.list({ prefix: "u:", cursor });
          count += page.keys.length;
          cursor = page.list_complete ? undefined : page.cursor;
        } while (cursor);
      } catch {
        // Quota exhausted and no fresh cache to fall back on -- serve the last known count
        // (even if stale) rather than a 500, so the UI doesn't break.
        if (cacheRaw) {
          try { return new Response(JSON.stringify({ count: JSON.parse(cacheRaw).count }), {
            headers: { ...cors, "Content-Type": "application/json" },
          }); } catch { /* fall through */ }
        }
        return new Response(JSON.stringify({ count: 0 }), {
          headers: { ...cors, "Content-Type": "application/json" },
        });
      }

      await env.USERCOUNT.put("count_cache", JSON.stringify({ count, computedAt: Date.now() }), {
        expirationTtl: 120,
      });

      return new Response(JSON.stringify({ count }), {
        headers: { ...cors, "Content-Type": "application/json" },
      });
    }

    return new Response("not found", { status: 404, headers: cors });
  },
};

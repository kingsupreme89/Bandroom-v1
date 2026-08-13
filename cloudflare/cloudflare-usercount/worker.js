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
//
// GET /downloads
//   -> { count: <all-time worldwide download count> }
//      Sums assets[].download_count across every GitHub release of GITHUB_REPO -- GitHub already
//      tracks the real, exact per-asset download count, so this is a true total, not a proxy like
//      counting first app launches would be. Cached in KV for 5 minutes to stay well under
//      GitHub's 60 req/hr unauthenticated rate limit (shared by every Worker isolate hitting it).
//
// GET /discord/messages?after=<messageId>
//   -> { messages: [{ id, author, avatarUrl, content, timestampUtc }] }
//      Relays the owner's Discord channel so it can be shown as a read-only chat feed inside
//      Bandroom without the app holding open its own Discord gateway connection. Requires
//      env.DISCORD_BOT_TOKEN (secret, `wrangler secret put DISCORD_BOT_TOKEN`) and
//      env.DISCORD_CHANNEL_ID (var or secret) to be configured -- if either is missing this
//      returns { messages: [] } rather than an error, so the app just shows its "not connected"
//      empty state instead of erroring. `after` is optional; when present only messages newer
//      than that Discord message id are returned (still relies on the shared 3s cache below, so
//      it's a "give me what's fresh" hint, not a guarantee of a fully precise incremental fetch).
//      Response messages are sorted oldest-to-newest (Discord's own API returns newest-first) and
//      stripped down to only what the UI needs -- no raw Discord payload (embeds, mentions,
//      attachments, etc.) ever reaches the client.
//
//      Rate-limit note: multiple Bandroom instances polling this endpoint would otherwise each
//      hit Discord's REST API directly. Instead this caches the last fetch from Discord for 3s
//      per Worker isolate (a plain in-memory module-level variable, not KV -- simplest thing that
//      works for a first pass; if this worker ever scales across many isolates a KV- or
//      Durable-Object-backed cache would coalesce bursts more reliably, but at Bandroom's
//      current scale a single isolate handling all traffic is the common case).

// Per-isolate cache for the Discord relay -- see the doc comment above /discord/messages.
// Deliberately module-level (survives across requests handled by the same warm isolate, reset
// on cold start) rather than KV, per the "simplest first pass" note above.
let _discordCache = { fetchedAt: 0, messages: [] };

// GitHub repo the installer is published from -- GitHub already tracks a real, exact download
// count per release asset (assets[].download_count), so this is the true all-time worldwide
// number, not a proxy like first-launch would be. Summed across every release/asset.
const GITHUB_REPO = "kingsupreme89/Bandroom-v1";

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

    if (url.pathname === "/downloads" && request.method === "GET") {
      // Cache in KV for 5 minutes -- GitHub's unauthenticated API is rate-limited to 60
      // req/hr per source IP, and every Worker isolate shares this one, so polling it
      // straight from every client's ticker refresh would blow through that fast.
      const cacheRaw = await env.USERCOUNT.get("downloads_cache_v2");
      if (cacheRaw) {
        try {
          const cached = JSON.parse(cacheRaw);
          if (Date.now() - cached.computedAt < 300000) {
            return new Response(JSON.stringify({ count: cached.count }), {
              headers: { ...cors, "Content-Type": "application/json" },
            });
          }
        } catch { /* fall through to recompute */ }
      }

      let count = 0;
      try {
        // Paginate. The GitHub Releases API defaults to 30 releases per page, so a single
        // unpaginated fetch silently drops every release older than the 30 newest -- which
        // under-counts the all-time total (the counter showed 339 instead of the real 520,
        // with exactly the 181 downloads from releases v1.0.9..v1.0.48 landing on page 2).
        // Walk pages of 100 until a short page signals the end of history. At Bandroom's
        // current ~76 releases this is a single request, still far under GitHub's 60/hr limit.
        const releases = [];
        for (let page = 1; page <= 20; page++) {
          const res = await fetch(
            `https://api.github.com/repos/${GITHUB_REPO}/releases?per_page=100&page=${page}`,
            { headers: { "User-Agent": "bandroom-usercount-worker", Accept: "application/vnd.github+json" } },
          );
          if (!res.ok) {
            // Page 1 failed (rate-limit / GitHub hiccup) -> serve the cached total rather than 0.
            if (page === 1 && cacheRaw) {
              return new Response(JSON.stringify({ count: JSON.parse(cacheRaw).count }), {
                headers: { ...cors, "Content-Type": "application/json" },
              });
            }
            break; // a later page failing just means we keep what we already summed
          }
          const batch = await res.json();
          releases.push(...batch);
          if (batch.length < 100) break;
        }

        for (const rel of releases) {
          for (const asset of rel.assets ?? []) {
            // Only count the actual installer asset. Squirrel.Windows (the auto-update
            // mechanism) also publishes "RELEASES" and "*.nupkg" assets per release, and the
            // app itself fetches those automatically on every update check -- counting them
            // would report auto-update traffic as if it were human downloads (in practice,
            // >80% of the raw asset total is this noise, not real installs).
            if (/setup.*\.exe$/i.test(asset.name ?? "")) {
              count += asset.download_count ?? 0;
            }
          }
        }
      } catch {
        if (cacheRaw) {
          try { return new Response(JSON.stringify({ count: JSON.parse(cacheRaw).count }), {
            headers: { ...cors, "Content-Type": "application/json" },
          }); } catch { /* fall through */ }
        }
      }

      await env.USERCOUNT.put("downloads_cache_v2", JSON.stringify({ count, computedAt: Date.now() }), {
        expirationTtl: 3600,
      });

      return new Response(JSON.stringify({ count }), {
        headers: { ...cors, "Content-Type": "application/json" },
      });
    }

    if (url.pathname === "/discord/messages" && request.method === "GET") {
      const token = env.DISCORD_BOT_TOKEN;
      const channelId = env.DISCORD_CHANNEL_ID;
      if (!token || !channelId) {
        // Owner hasn't run the wrangler secret/var setup yet -- empty list, not an error, so the
        // app renders its quiet "Discord feed not connected" state instead of retry-spamming.
        return new Response(JSON.stringify({ messages: [] }), {
          headers: { ...cors, "Content-Type": "application/json" },
        });
      }

      const now = Date.now();
      if (now - _discordCache.fetchedAt >= 3000) {
        try {
          const discordRes = await fetch(
            `https://discord.com/api/v10/channels/${channelId}/messages?limit=50`,
            { headers: { Authorization: `Bot ${token}` } }
          );
          if (discordRes.ok) {
            const raw = await discordRes.json();
            const mapped = raw
              .map((m) => ({
                id: m.id,
                author: m.author?.username ?? "Unknown",
                avatarUrl: m.author?.avatar
                  ? `https://cdn.discordapp.com/avatars/${m.author.id}/${m.author.avatar}.png?size=64`
                  : null,
                content: String(m.content ?? "").slice(0, 2000),
                timestampUtc: m.timestamp,
              }))
              .reverse(); // Discord returns newest-first; the client wants oldest-to-newest.
            _discordCache = { fetchedAt: now, messages: mapped };
          }
          // On a non-ok response (bad token, missing permissions, rate limited) fall through and
          // serve whatever's already cached rather than erroring the whole endpoint.
        } catch {
          // Network hiccup talking to Discord -- same fallback, serve the stale cache below.
        }
      }

      let messages = _discordCache.messages;
      const after = url.searchParams.get("after");
      if (after) {
        const idx = messages.findIndex((m) => m.id === after);
        if (idx >= 0) messages = messages.slice(idx + 1);
      }

      return new Response(JSON.stringify({ messages }), {
        headers: { ...cors, "Content-Type": "application/json" },
      });
    }

    return new Response("not found", { status: 404, headers: cors });
  },
};

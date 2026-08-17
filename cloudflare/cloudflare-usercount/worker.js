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
      // Cache in KV for 5 minutes -- GraphQL is authenticated (5000 req/hr) so this is generous
      // headroom, not a rate-limit necessity like the old REST approach, but no reason to hit
      // GitHub on every single ticker refresh either.
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

      const cachedCount = (() => {
        try { return JSON.parse(cacheRaw ?? "null")?.count ?? null; } catch { return null; }
      })();

      let count = 0;
      let gotRealData = false;
      // FIXED 2026-08-16: GitHub's REST list-releases endpoint (`GET /repos/.../releases`) was
      // reliably returning HTTP 200 with a genuinely empty `[]` body for this repo, confirmed live
      // both unauthenticated AND with a valid token -- not a rate-limit/transient hiccup, GitHub's
      // REST list endpoint itself was broken for this repo. `res.ok` being true meant no error path
      // ever caught it, so this got computed as 0 and CACHED as the real download count for up to
      // an hour, silently breaking the ticker for everyone. GitHub's GraphQL API returns the exact
      // same data correctly (confirmed live: 88 releases, real per-asset counts) -- switched the
      // whole computation to GraphQL, which requires an auth token even for public repos (REST
      // didn't). Needs `wrangler secret put GITHUB_TOKEN` -- a token with just public read access
      // is enough (no write scopes needed).
      if (env.GITHUB_TOKEN) {
        try {
          let cursor = null;
          const releases = [];
          for (let page = 0; page < 20; page++) {
            const [repoOwner, repoName] = GITHUB_REPO.split("/");
            const query = `query($cursor: String) { repository(owner: "${repoOwner}", name: "${repoName}") {
              releases(first: 100, after: $cursor) {
                pageInfo { hasNextPage endCursor }
                nodes { releaseAssets(first: 20) { nodes { name downloadCount } } }
              }
            } }`;
            const res = await fetch("https://api.github.com/graphql", {
              method: "POST",
              headers: {
                Authorization: `Bearer ${env.GITHUB_TOKEN}`,
                "Content-Type": "application/json",
                "User-Agent": "bandroom-usercount-worker",
              },
              body: JSON.stringify({ query, variables: { cursor } }),
            });
            if (!res.ok) { console.error("GraphQL HTTP error", res.status, await res.text()); break; }
            const json = await res.json();
            if (json.errors) console.error("GraphQL errors", JSON.stringify(json.errors));
            const rel = json?.data?.repository?.releases;
            if (!rel) break;
            releases.push(...rel.nodes);
            if (!rel.pageInfo.hasNextPage) break;
            cursor = rel.pageInfo.endCursor;
          }

          if (releases.length > 0) {
            gotRealData = true;
            for (const rel of releases) {
              for (const asset of rel.releaseAssets?.nodes ?? []) {
                // Only count the actual installer asset. Squirrel.Windows (the auto-update
                // mechanism) also publishes "RELEASES" and "*.nupkg" assets per release, and the
                // app itself fetches those automatically on every update check -- counting them
                // would report auto-update traffic as if it were human downloads (in practice,
                // >80% of the raw asset total is this noise, not real installs).
                if (/setup.*\.exe$/i.test(asset.name ?? "")) {
                  count += asset.downloadCount ?? 0;
                }
              }
            }
          }
        } catch (err) { console.error("GraphQL fetch threw", err && err.message); /* gotRealData stays false -- falls through to the cache-preservation guard below */ }
      } else {
        console.error("GITHUB_TOKEN not set on env");
      }

      // Never trust a 0/empty result over an existing known-good higher cache -- total download
      // counts only go up in practice, and this endpoint has already been burned once by trusting
      // a spuriously-empty upstream result (see the long comment above).
      const finalCount = gotRealData ? count : (cachedCount ?? 0);

      await env.USERCOUNT.put("downloads_cache_v2", JSON.stringify({ count: finalCount, computedAt: Date.now() }), {
        expirationTtl: 3600,
      });

      return new Response(JSON.stringify({ count: finalCount }), {
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

# Deploying the Bandroom user-count worker

One-time setup (~5 minutes), needs Node.js installed.

```bash
cd cloudflare-usercount
npx wrangler login              # opens a browser, log into Cloudflare (free account is fine)
npx wrangler kv namespace create USERCOUNT
```

That last command prints something like:

```
{ binding = "USERCOUNT", id = "abcd1234..." }
```

Copy that `id` into `wrangler.toml`, replacing `REPLACE_WITH_KV_NAMESPACE_ID`. Then:

```bash
npx wrangler deploy
```

It'll print your live URL, something like:

```
https://bandroom-usercount.<your-subdomain>.workers.dev
```

Send me that URL and I'll wire it into the app (heartbeat sender + the header ticker).

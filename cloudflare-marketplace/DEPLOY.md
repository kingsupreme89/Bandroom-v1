# Deploying the Bandroom marketplace worker

Same account as `cloudflare-usercount` -- if you've already done `wrangler login` for that one,
you don't need to log in again. One-time setup:

```bash
cd cloudflare-marketplace
npx wrangler r2 bucket create bandroom-marketplace-files
npx wrangler kv namespace create MARKETPLACE_META
```

The KV command prints something like:

```
{ binding = "MARKETPLACE_META", id = "abcd1234..." }
```

Copy that `id` into `wrangler.toml`, replacing `REPLACE_WITH_KV_NAMESPACE_ID`. Then:

```bash
npx wrangler deploy
```

It'll print your live URL, something like:

```
https://bandroom-marketplace.<your-subdomain>.workers.dev
```

Send me that URL and I'll wire it into the app (upload flow + Sound Bank/Trophy Room grids).

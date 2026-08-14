# Deploying the Bandroom marketplace worker

The KV namespace id and R2 bucket name are **already set** in `wrangler.toml` — do not create or
edit them again. Just deploy (needs Node.js + a one-time `wrangler login`).

```bash
cd cloudflare-marketplace
npx wrangler login               # once, opens a browser to authorize
npx wrangler secret put ADMIN_TOKEN        # once, paste the admin token (see below)
npx wrangler deploy
```

It prints a live URL like:

```
https://bandroom-marketplace.<your-subdomain>.workers.dev
```

## One required secret: ADMIN_TOKEN
`/item` DELETE/PATCH supports an admin override that bypasses upload ownership. The worker reads
it from `env.ADMIN_TOKEN`. Set it with:

```bash
npx wrangler secret put ADMIN_TOKEN
# paste the SAME value stored in the app's admin_token.local.txt
```

(If it's not set, admin-token operations simply fail closed at 403; regular uploader owner-token
operations still work.)

## Verify after deploy
```
# empty-but-valid list (may be [] on a fresh account)
curl "https://bandroom-marketplace.<subdomain>.workers.dev/list?type=song"
# -> { "items": [] }
```

## Full endpoint + secret reference
See `cloudflare/SECRETS_CHECKLIST.md`.
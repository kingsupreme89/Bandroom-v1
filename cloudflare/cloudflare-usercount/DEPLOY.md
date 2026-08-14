# Deploying the Bandroom user-count worker

The `USERCOUNT` KV namespace id is **already set** in `wrangler.toml` — you do not need to create
or edit anything. Just deploy (needs Node.js + a one-time `wrangler login`).

```bash
cd cloudflare-usercount
npx wrangler login               # once, opens a browser to authorize
npx wrangler deploy
```

It prints a live URL like:

```
https://bandroom-usercount.<your-subdomain>.workers.dev
```

## Optional: Discord relay
`/discord/messages` needs two env values (see `cloudflare/SECRETS_CHECKLIST.md`):

```bash
npx wrangler secret put DISCORD_BOT_TOKEN    # paste the bot token when prompted
npx wrangler var put DISCORD_CHANNEL_ID channel_id_here
npx wrangler deploy
```

If you skip these, `/discord/messages` returns `{ "messages": [] }` and the app shows its quiet
"not connected" state. The **download counter** (`/downloads`) and live **user count** (`/count`)
need no secrets and no further setup.

## Verify after deploy
```
curl https://bandroom-usercount.<subdomain>.workers.dev/downloads
# -> { "count": <number> }
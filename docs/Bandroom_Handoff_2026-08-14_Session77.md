# Bandroom Handoff — August 14, 2026 — Session 77

Same idea as always: what happened, explained plain.

## Fixed: "No Song Assigned" False Alarm on Pregame Take the Field

Owner report: the event log kept saying `Pregame Take the Field (Home BG) -- no song assigned,
nothing played` even though a song WAS assigned (screenshot showed `sec_lsu_21_2pt__norm_Song`
assigned to that exact event with working Assign/Edit and Assign PA buttons).

Root cause, traced with a research subagent then fixed directly: `"Other: Pregame Take the Field"`
fires before possession has ever been read for the game (it happens during the pregame walkout,
before any snap), so it lands in `OnEngineEventsDetected`'s `_possession == null` branch
(`WebMainForm.cs`). That branch hardcoded routing every `"Other:*"` event to `FireEventForSide(
"home", ...)`. If the owner assigned the song while on the **Away** tab, it lives in `_awayConfig`,
but playback only ever looked in `_homeConfig` -- found nothing, logged "unassigned," even though
the away-side song was correctly assigned the whole time.

Fix (`WebMainForm.cs` ~line 3337-3350): the possession-unknown branch now fires `"Other:*"` events
for **both** `home` and `away`, each resolved against its own config, instead of hardcoding home
only. This is the actually-correct behavior, not just a patch -- both bands physically take the
field pregame, so both sides' assigned songs should play (or each independently log "unassigned" if
that side genuinely has nothing set).

Verified: `dotnet build BandAudioHook.csproj -c Debug` -- clean, 0 warnings/errors. Launched
`Bandroom.exe` from `bin/Debug/net10.0-windows10.0.19041.0/` for the owner to live-test assigning a
song under the Away tab and confirming it fires instead of logging "no song assigned."

## Build & Run Status

- `dotnet build BandAudioHook.csproj -c Debug` -- clean, 0 warnings/errors.
- App launched live for owner testing; not yet confirmed fixed in a real game by the owner as of
  this handoff.

## Git

Not yet committed -- `WebMainForm.cs` has the fix as an uncommitted local change. No release
triggered this session (no "ppup").

## Options Discussed, Not Started

- Owner hasn't yet confirmed the fix resolves the issue live in-game.
- Session 76's open items (Mac audio engine, Sparkle auto-update, icon-crop batch pass) weren't
  touched this session -- still open, see that handoff for detail.

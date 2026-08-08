# Bandroom Handoff — August 8, 2026 (Session 8)

Picks up right after Session 7's handoff. This was a long, live session run mid-game with the
owner testing in real time. Covers: real bug fixes, the clipping island UI, a cover-flow
matchup picker, and a new (not-yet-deployed) profile-sharing marketplace feature.

**⚠️ Not deployed yet:** `cloudflare/cloudflare-marketplace/worker.js` has a real code change
(new "profile" item type) that needs `wrangler deploy` before profile sharing works against the
live worker. See §5.

**⏸️ Paused, waiting on owner:** a file-deletion request (§6) — do NOT delete anything there
without fresh explicit confirmation.

---

## 1. Real bugs found and fixed (all committed to `master`, pushed)

- **`DefenseHelper.cs`**: the ordinary "held them on 3rd, forced 4th down" case (no loss of
  yards, no turnover) had **no evaluator at all**. Only the rare same-snap-turnover variant
  (`BigEventHelper`) and the stuffed-for-a-loss variant fired `"Defense: Third Down"`/`"(Loss)"`.
  Every team's default song pack already has a `"Defense: Third Down"` song mapped for exactly
  the common case — it was silently unreachable. Added the missing branch.
- **`wirePreviewBar()` was defined but never called anywhere.** Play/Pause, Stop, and
  click-to-seek on every song preview (Sound Bank/Trophy Room/My Downloads) have been dead
  since this code was written. Now wired in on startup.
- **Two separate audio pathways didn't stop each other.** JS `<audio>` (marketplace/downloads
  previews) and native `AudioPlayer.Play` (Sound Bank/assign-island previews) are independent
  outputs — pressing Play on one didn't stop the other, so they could overlap. Each preview
  entry point now stops the other pathway first.
- **`openSaveProfileDialog` was defined twice** in `app.js`. JS hoisting means the second
  definition silently won everywhere, including the Save rail button's own click handler — and
  that second version just called `.click()` on the same button, re-firing its own handler,
  calling `openSaveProfileDialog()` again. **Every Save attempt was infinite recursion.**
  Removed the dead duplicate.
- **`RELEASES` manifest shipped with a UTF-8 BOM**, corrupting the first entry's SHA1 hash when
  Squirrel's `GithubSource` client parsed it — `CheckForUpdate()` silently found zero valid
  entries and reported "already on latest" even when a real update existed. Bit v1.0.50 live;
  fixed by re-uploading a stripped `RELEASES` and patching `release.ps1` to strip the BOM on
  every future release automatically.
- **Three dialogs (Set Matchup, Save Profile, Marketplace Upload) had markup + JS wiring but
  zero CSS anywhere** — unhiding them just dropped an unstyled `<div>` into normal document
  flow. Styled to match the existing `#team-picker-overlay` pattern. Also: the matchup picker's
  team grid reused the `.team-picker-grid` *class* but the 4-col grid CSS only targeted it by
  *ID*, so tiles rendered as one giant unsized swatch.
- **`ConfigStore.cs:689`**: `down:4th`'s default trigger had a leftover personal dev-machine
  absolute path (`dies irie 0.wav`) instead of `""` like every other entry — showed as a fake
  "assigned" song that silently failed to play for any real user. Fixed.
- Stale "coming soon" copy for Trophy Room backgrounds — that feature (Set as Background on any
  image tile) already works and has for a while. Copy corrected.

## 2. Clipping island — new persistent UI

A static glass panel ("Clip Preview") now lives permanently below the Offense/Defense/
Situations grid on every tab, same width as the grid above it, pulsing LED outline in the
active team's colors (see [[project_bandroom_theme]] memory — this glass look is now the
standing app theme for all future redesigns).

- Houses the (now-actually-wired) shared song-preview bar, relocated out of its old
  fixed-position floating strip.
- **Assign mode**: "Assign / Edit" and "Assign PA" on any event card now open inline in this
  island — search, select, Assign Selected, Browse for file, Trim, Clear Assignment — instead
  of a separate native popup stealing focus mid-game. Backed by new `WebBridge`/`WebMainForm`
  methods: `GetTrackLibrary`, `PreviewLocalFile`, `AssignTrackFile`, `ClearTrackAssignment`,
  `BrowseForAudioFile`, `OpenTrimmer`. The native `AssignTrackForm`/`OpenAssignTrack` path still
  exists in the C# but is no longer called from this UI flow — safe to remove later once this
  is confirmed solid, not removed yet in case of regressions.
- `#center-column` layout converted to real flexbox (`flex-direction: column`, situations panel
  `flex: 1 1 auto`) instead of a magic-number `max-height: calc(100vh - 260px)`, so adding/
  removing panels here never needs rebalancing pixel offsets to keep scrolling correct.

## 3. Cover-flow matchup picker

Set Matchup's Away/Home team lists replaced with a CFB27-style cover-flow carousel (center
tile large/sharp, up to two neighbors each side scaled down + tilted + faded, arrows or
click-a-tile to cycle) — reference was a screenshot of the actual CFB27 team-select screen.
Browsing IS picking (matches the reference's live-cycling behavior); GAMETIME is still the real
commit point.

## 4. Save Profile flow

Saving now shows a real glass confirmation dialog (not just a toast) explaining what saving
actually did, with a real "Export & Share with a Friend" action (wired to the existing Export
flow). Deliberately does NOT have a fake "upload the whole profile to the marketplace" button —
that wasn't a real feature until §5 below.

## 5. NEW, undeployed: "Share Profile" / "Load Profile from Others"

Owner wants a pill that auto-loads other people's team-assignment profiles. Built the real
version, not a stub:

- `cloudflare/cloudflare-marketplace/worker.js`: added a 4th `VALID_TYPES` entry, `"profile"`,
  alongside song/image/pa. Reuses all existing upload/list/download plumbing — just JSON bytes
  instead of audio/image bytes.
- Uploaded profile JSON contains **trigger + event name + filename only, never a full local
  path** (paths are machine-specific, meaningless on someone else's PC — same constraint the
  existing Export/Import already lives with).
- Applying a downloaded profile (`ApplyMarketplaceProfile` in `WebBridge.cs`) matches by
  **filename** against the applier's own `Songs` library and reports exactly what auto-assigned
  vs what didn't (`{applied, total, unmatched}`) — real filename-based auto-assign for the
  common "everyone's using the default pack" case, honestly scoped to what's actually possible
  (it can't summon audio files that don't exist on this machine).
- UI: "Share Profile" / "Load Profile from Others" pills in the left Profiles panel, new
  `#load-profile-overlay` glass list dialog.

**Blocking issue: the worker change is not deployed.** `ShareCurrentProfileToMarketplace` will
currently 400 with `"type must be song/image/pa"` against the live worker until
`cloudflare/cloudflare-marketplace/worker.js` is deployed (`wrangler deploy` from that
directory, or the owner's "lehgo" trigger word if they mean this worker specifically — confirm
scope, since "lehgo" historically means *both* the marketplace and usercount workers together).

## 6. PAUSED — do not act without fresh confirmation

**43 team profiles** (Georgia, Clemson, LSU, Alabama, Auburn, and 38 others) currently have
triggers pointing at 8 unnamed placeholder files in `Songs/uploaded/` (`3 0.wav`, `AUDIO
2/3/4.mp3`, `BG_CROWD_CHANT+BAND_NWA 0.wav`, `BLUE MOON BAND FULL.mp3`, `DIES IRIE 0.wav`, `UH
OH 0.wav`) — almost certainly from an earlier "Apply to All Teams" pass that copied a test file
everywhere. Owner asked to delete these ("delete all files dont follow filestyem") but dismissed
the follow-up question about how to handle the now-orphaned assignments before answering.
**Nothing was deleted.** Confirm with the owner exactly which files and whether to clear the
affected trigger assignments to "Unassigned" before touching this — the blast radius is much
bigger than a simple file cleanup (43 profiles, dozens of songs.json/situation entries).

## 7. Other open items from this session, not started

- **Global font-readability pass**: owner flagged small text (10–11px) throughout as hard to
  read. Deliberately not done blind — touches dozens of rules across a huge stylesheet; needs a
  scoped pass with visual verification, not a mechanical find-replace.
- **Filename-based auto-indexing / bulk reindex**: owner wants something like Cline's
  `scripts/intake_engine.py` (title cleaning, team/trigger detection, confidence scoring) wired
  into the app for auto-tagging uploads and populating team profiles from filenames like
  `clem4thdown`. Not started — the profile-sharing filename-match in §5 is a narrower, real
  step in this direction, not the full engine.
- **DL button → auto-extract → auto-index pipeline** for the default song pack: owner wants the
  "Download Base Sound Pack" button (currently opens a Google Drive link, see Session 7 §3) to
  auto-extract into the Bandroom folder and auto-index using whatever indexing logic exists.
  Not started.
- **Full HUD redesign brief** (dual-team split view, drag-and-drop library drawer, OCR activity
  feed, etc.) — owner explicitly said this comes after everything else above. Not started.
- **Google auth sign-in** — owner mentioned this in passing with no clear ask attached. Surface
  with the owner next session to find out what specifically they want (something to do with
  tying marketplace uploads/profile-shares to a signed-in account, per a passing comment during
  the profile-sharing work in §5?).
- **1st-down live-detection flakiness**: owner reported this repeatedly ("idk why that down is
  so hard to get"). Traced the code path — `FirstDownHelper`/`PlayDelta.WasFirstDown`/the sticky
  `_lastKnownDown` OCR fix are all intact and already patched from prior sessions, and
  `EventRouter` runs every evaluator independently (nothing silently blocks/overrides
  `FirstDownHelper`). Couldn't diagnose further without a live OCR log or screenshot of a missed
  1st down — this is most likely a broadcast-skin/crop-calibration issue, same category as the
  still-pending Remote Play scorebug preset from Session 7, not a routing bug.
- **Which events still show "unconfirmed"**: `ConfirmedTriggers` in `WebBridge.cs` only lists 7
  (touchdown, turnover, PAT good, legacy 1st/2nd/3rd/4th down). Everything else — kickoff, TFL,
  start-of-4th-quarter, all 5 timeout variants, field goal made/missed, safety, victory in hand,
  iced game, and every Defense-stop variant including the one just fixed in §1 — shows amber
  until the owner confirms it live and it gets added to that set.

---

## What "next session" should do, in order

1. Get owner's go-ahead, then `wrangler deploy` the marketplace worker (§5) so profile sharing
   actually works — confirm whether "lehgo" covers just this worker or both as usual.
2. Get explicit direction on the 8 placeholder files across 43 profiles (§6) before touching
   anything.
3. Watch a live game with the owner if possible to get a real screenshot/log of a missed 1st
   down — that's the only way to make further progress on that specific complaint.
4. Scope and start the filename-based auto-indexing engine for real (§7) — the profile-sharing
   filename-match this session is a working proof of concept for the mechanism.
5. Font-readability pass, DL-button auto-extract pipeline, full HUD redesign — in that order,
   per owner's own stated priority from this session.

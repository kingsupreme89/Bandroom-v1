# Bandroom Handoff — Session 43 (2026-08-11)

Owner ask, verbatim scope: "we need to get mac app functional up to this build" (i.e. bring
`src/Bandroom.Mac` up to feature parity with the Windows app as of Session 42), then "can you wire
all that up" to keep going after the first pass. This session did not touch Windows-side files
(`WebBridge.cs`/`WebMainForm.cs`/`ConfigStore.cs`) except to read them as the porting reference,
and did not touch `wwwroot/app.js`/`index.html` except the one bridge-detection line described
below. **Windows-hosted development the whole session — nothing here has been run on an actual
Mac.** Everything below is "builds clean and mirrors the Windows logic," never "confirmed live."

## 1. The Mac app didn't build at session start

`dotnet build src/Bandroom.Mac/Bandroom.Mac.csproj` failed with 10 errors. Root cause: several
root-level shared `.cs` files linked into the Mac project referenced Windows-only types that
weren't themselves linked in (`CloudDatabaseService`, `IntakeEngine`, `AudioTrackMetadata`, NAudio's
`AudioCache`), plus one real bug (`Action<double,string>` delegate arity mismatch in
`MainWindow.axaml.cs`). Fixed:
- Linked `CloudDatabaseService.cs`/`IntakeEngine.cs` into `Bandroom.Mac.csproj` (both are pure
  managed, no Windows deps, safe to link as-is).
- Added Mac-safe stubs in `PlatformStubs.Mac.cs` for `AudioCache` (real one is
  `AudioEngine.cs`, NAudio/Windows-only — Invalidate() only, all that's called) and
  `AudioTrackMetadata`/`AudioTrackMetadataStore` (duplicated the pure-data record + Load/Save
  JSON sidecar logic; `AnalyzeAudioFile` is a stub returning zeros/nulls since real duration/
  loudness analysis needs NAudio's `AudioFileReader`).
- Fixed the delegate signature bug in `ImportDefaultSongPackZipFromWeb`.

## 2. The real blocker: no working JS↔C# bridge at all

The Mac app has no embedded webview. `MainWindow.axaml.cs`'s `OnOpened` starts a plain
`HttpListener` on port 18765 serving `wwwroot/` and shells out to `/usr/bin/open` to launch the
**system default browser** pointed at it. `wwwroot/app.js` gates every bridge call on
`window.chrome?.webview?.hostObjects?.bandroom` (WebView2-only) — in a plain browser that's always
`null`, so **every single `bridge.X()` call across the whole app was silently dead** before this
session, regardless of what methods existed on `MacWebBridge`.

Fixed by adding a same-origin JSON-RPC transport, no changes needed to any of the ~180 existing
`bridge.X()` call sites in app.js:
- `ServeBridgeCall` (`MainWindow.axaml.cs`): any `POST /bridge/{MethodName}` is reflection-dispatched
  to the matching public instance method on `MacWebBridge`, JSON body deserialized positionally into
  the method's parameters, awaited if it returns a `Task`, result JSON-serialized back. Runs on its
  own `Task.Run` per request (matters below).
- `ServeFile` now injects `<script>window.__BANDROOM_HTTP_BRIDGE__ = true;</script>` into the
  `<head>` of any `.html` it serves.
- `wwwroot/app.js`'s one changed line: `const bridge = window.chrome?.webview?.hostObjects?.bandroom
  ?? (window.__BANDROOM_HTTP_BRIDGE__ ? _makeHttpRpcBridge() : null);` — `_makeHttpRpcBridge()` is a
  `Proxy` that does a **synchronous** XHR POST to `/bridge/{method}` and JSON-parses the response,
  matching both the `await bridge.X()` and bare `bridge.X()` call patterns already in the file.
  Windows' WebView2 path is untouched — confirmed `BandAudioHook.csproj` still builds clean.
- Also added `/overlay/chat` (→ `wwwroot/overlay-chat.html`) and `/overlay/chat/data` (→
  `{"messages":[]}`) routes to `MainWindow.axaml.cs`, mirroring Session 42's new
  `LocalOverlayServer.cs` on the Windows side — without this, Mac's `GetOverlayChatUrl()` would
  have pointed at a 404.

## 3. Full bridge method parity port

With the transport working, went through `WebBridge.cs`/`WebMainForm.cs` method-by-method and
ported everything reasonable into `MacWebBridge.cs`/`MainWindow.axaml.cs`. (A background agent did
a first pass on part of this — it hit its session token limit partway through and left a
non-compiling intermediate state, some methods on `MacWebBridge` with no matching `MainWindow`
counterpart yet; finished by hand from there, verifying the whole diff against `WebBridge.cs`'s
full method list afterward.)

**Fully ported, real functionality:**
- Track metadata drawer (`GetTrackMetadata`/`SaveTrackMetadata`/`AnalyzeTrackMetadata`)
- Public profile toggle (`TogglePublicProfile`)
- Big Game conditional slots (`AssignBigGameTrackFile`/`ClearBigGameTrackAssignment`/
  `GetBigGameSettings`/`SaveBigGameSettings`)
- Help & Guide Event Log (`GetEventActivityLog`/`ExportEventActivityLog` — `EventActivityLog.cs`
  turned out to already be cross-platform-safe, just needed linking into the csproj)
- Supabase settings (`GetSupabaseSettings`/`SaveSupabaseSettings`)
- Default songs folder relocate (`RelocateDefaultSongsFolder` — real Avalonia
  `StorageProvider.OpenFolderPickerAsync` folder picker, see item 4)
- Soundboard slot playback (`PlaySoundboardSlot`)
- Whistle browse/replace (`BrowseAndSetLeadInWhistle`, real file picker + copy) and per-event
  lead-in toggle (`SetEventPlayLeadInWhistle`)
- Crowd bus clip browse (`BrowseAndSetCrowdBusClip`, real file picker + copy)
- Profile management (`DuplicateProfile`, `GetTeamsNeedingDefaultProfile`,
  `ApplyDefaultProfileForTeam[Overwrite]`, `ApplyConferencePackForTeam`,
  `ApplyConferencePackSelections`, `PreviewConferencePackForTeam`)
- `AddCustomTeam` (already just called shared `TeamColors.AddCustomTeam`)
- Default/conference song pack browsing (`BrowseForSongPackFolder`, `ImportDefaultSongPackFolder`,
  `GetDefaultSongsFolderPath`, `GetDefaultPackSongsForTeam`, `GetDefaultPackTeams`,
  `GetConferencePackSongsForTeam`) — including real ID3 title-tag reads via **TagLibSharp**, added
  as a new package reference to `Bandroom.Mac.csproj` (pure managed, genuinely cross-platform,
  unlike NAudio)
- Batch song add (`AddSongsBatch`, real multi-select file picker)
- Whistle/library volume (`GetWhistleVolume`/`SetWhistleVolume`)
- `BrowseForAudioFile` (was previously a hardcoded `return null` stub — now a real
  `StorageProvider` file picker + `ConfigStore.ImportIntoSongsLibrary`)

**Config-only / no-op, documented in code comments:** the EQ/DSP toggle cluster (`GetEqPreset`/
`SetEqPreset`, transient shaper, stereo widener, ducking, no-effects-bypass, controller rumble,
sub-bass level, crowd-bus enable). `AudioPlayer.Mac.cs`'s backend is `afplay` (a dumb whole-file
player, no live audio graph) — there is no real-time effects chain to hook these into on Mac at
all. These persist state via the same static fields Windows uses (or new Mac-only stub services
`CrowdBusService`/`ControllerRumbleService` in `PlatformStubs.Mac.cs` for the two that have no
existing Mac-side field) so the Settings UI round-trips correctly across relaunch, but none of them
touch actual playback. Same precedent as Windows' old "Compact Mode" no-op control.

**Deliberately not faked — honest errors instead of pretend functionality:**
- `SaveTrimAsLeadInWhistle`/`SaveTrim` (actual audio trimming) — Windows' version uses NAudio's
  `OffsetSampleProvider` + `AudioNormalizer`, both NAudio/Windows-only. Returns a clear "not
  supported on the Mac app yet" JSON error rather than a fake success. `PrepareTrimForWhistle`/
  `PrepareTrim` (the copy-into-scratch-folder + duration-probe half) DO work — duration just comes
  back as 0 via the `AudioTrackMetadataStore` stub from item 1.
- `GetSystemVolumeInfo` — Windows reads WASAPI directly; no macOS equivalent wired up (would need
  CoreAudio/AVFoundation native interop). Returns `{"known":false,...}` honestly.
- `ScanDynastySave` — no real CFB27 dynasty-save parser exists on **either** platform yet (was
  already a `Task.FromResult<string?>(null)` stub on Windows); mirrored as-is.

**Deliberately skipped, not ported:** `FireTestEventRouted`/`FireTestEventPair` — the Ctrl+Shift+T
dev test-hooks on Windows that exercise `ResolveEventRouting`, the Big Game routing-tier engine
(home-only-always / un-gated Offense / gated Defense tiers). That's a real, separate feature the
Mac side doesn't have any equivalent of yet (Mac's `FireEventForSide` has a simpler signature, no
`volumeMultiplier`/routing-tier concept) — porting it wasn't in scope for "get the app working,"
flagging for a future session if the Mac app needs the same Big Game gating logic for real games.

## 4. New Avalonia file/folder picker plumbing

Added two reusable async helpers in `MainWindow.axaml.cs` (`BrowseForFolderFromWeb`,
`BrowseForAudioFileNativeAsync`) using Avalonia 12's `StorageProvider.OpenFolderPickerAsync`/
`OpenFilePickerAsync`, replacing the previous hardcoded-`null` stub for `BrowseForAudioFileFromWeb`.
Since `ServeBridgeCall` already dispatches every RPC call on its own background `Task.Run` thread
(never the UI thread), synchronous bridge methods that need a picker block via
`Dispatcher.UIThread.InvokeAsync(...).GetAwaiter().GetResult()` safely — confirmed this is safe by
checking the calling context in `StartWebServer` before relying on it.

## Verified this session
- `dotnet build src/Bandroom.Mac/Bandroom.Mac.csproj -c Debug` and `-c Release`: 0 errors (5
  pre-existing warnings, unrelated to this session's changes).
- `dotnet build BandAudioHook.csproj -c Debug` (Windows): still 0 errors/warnings — confirmed the
  shared `wwwroot/app.js` bridge-detection change doesn't affect the WebView2 path.
- Diffed every `public` method name in `WebBridge.cs` against `MacWebBridge.cs` after the port;
  only the two deliberately-skipped `FireTestEvent*` methods remain missing.

## Not yet confirmed — real next steps
1. **Nothing in this session has been run on an actual Mac, or even in a real browser.** Everything
   is build-clean and logic-traced against the Windows source only. First real next step for
   whoever picks this up on a Mac: launch the app, confirm the system browser opens to
   `localhost:18765`, and click through Settings/Profile/Assignment/Band Director to see whether
   the RPC bridge actually round-trips in practice (JSON casing edge cases, `Proxy`/XHR quirks,
   `StorageProvider` picker behavior on real macOS, etc. — none of this can be verified from
   Windows).
2. **Nothing from this session is committed to git yet** — there is no git repo initialized in this
   working tree at all (per this session's own tool constraints); whoever picks this up should
   `git init`/commit deliberately rather than assume prior sessions' "commit before ppup" workflow
   applies unchanged.
3. EQ/DSP controls are config-only on Mac (item 3) — if real-time audio effects matter for Mac
   users, `AudioPlayer.Mac.cs`'s `afplay` backend would need replacing with something that exposes a
   live mixing/effects graph (e.g. `AVAudioEngine` via a native interop layer), a substantial
   separate project.
4. Real audio trimming and system-volume readout remain Windows-only (item 3) — flagged with honest
   in-app errors rather than silently missing, but still unimplemented.
5. `FireTestEventRouted`/`FireTestEventPair` (item 3) — only matters if/when Mac needs the Big Game
   routing-tier gating for live games, not just basic event firing.
6. Carried forward from Session 40: Sound Bank still has no team-color theming (unrelated to this
   session, not touched).

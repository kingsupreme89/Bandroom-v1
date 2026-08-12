// Bridge to the C# host. On Windows this is WebMainForm.cs via
// CoreWebView2.AddHostObjectToScript("bandroom", ...). The Mac app has no embedded webview (it
// opens this page in the system browser over its own HttpListener), so `window.chrome.webview`
// never exists there -- instead MainWindow.axaml.cs stamps `window.__BANDROOM_HTTP_BRIDGE__` onto
// the page when it serves index.html, and in that case we use a synchronous-XHR Proxy that POSTs
// each call to /bridge/{MethodName} (see MainWindow.axaml.cs's ServeBridgeCall) and parses the
// JSON response -- same call shape (`bridge.Method(args)`, awaitable) as the WebView2 path, so no
// other call site in this file needs to know which transport is in use. Falls back to static
// placeholder data only when neither transport is present (e.g. a plain browser preview opened
// with no host app running at all) so the layout is still inspectable.
function _makeHttpRpcBridge() {
  return new Proxy({}, {
    get(_target, methodName) {
      if (typeof methodName !== "string") return undefined;
      return function (...args) {
        const xhr = new XMLHttpRequest();
        xhr.open("POST", `/bridge/${methodName}`, false); // synchronous: matches both `await bridge.X()` and non-awaited `bridge.X()` call sites
        xhr.setRequestHeader("Content-Type", "application/json");
        try {
          xhr.send(JSON.stringify(args));
        } catch (err) {
          console.error(`[mac-bridge] ${methodName} request failed`, err);
          return null;
        }
        if (xhr.status < 200 || xhr.status >= 300) {
          console.error(`[mac-bridge] ${methodName} returned ${xhr.status}`, xhr.responseText);
          return null;
        }
        if (!xhr.responseText) return null;
        try {
          return JSON.parse(xhr.responseText);
        } catch {
          return xhr.responseText;
        }
      };
    },
  });
}
const bridge =
  window.chrome?.webview?.hostObjects?.bandroom ??
  (window.__BANDROOM_HTTP_BRIDGE__ ? _makeHttpRpcBridge() : null);

// The Bandroom community marketplace worker (cloudflare-marketplace/worker.js) -- R2 for files,
// KV for name/school metadata. See that file for the exact /upload, /list, /file contract.
const MARKETPLACE_URL = "https://bandroom-marketplace.bandroom.workers.dev";

// Admin marketplace override (owner-only) -- set once in init() from bridge.IsAdminMode().
// Stays false for every real end-user build (see WebBridge.cs's AdminTokenPath comment).
let _isAdminMode = false;

// ---- Global JS crash guard -------------------------------------------------------------
// A JS exception thrown inside one render function (e.g. buildItemTile) used to be able to
// take out the whole page silently -- WebView2's console shows it, but nothing in-app signals
// that anything failed, so the user is left staring at a half-broken screen with no clue.
// These two handlers catch (1) synchronous throws that escape all the way to the top
// (window.onerror) and (2) rejected Promises nobody ever attached a .catch to
// (unhandledrejection) -- the two ways a JS error can go fully unhandled in a browser context.
// Deliberately loud (console.error, same as every other catch block in this file) rather than
// swallowed -- the goal is "don't let the UI die", not "hide that something broke" during dev.
// showToast is a function declaration further down this same file; declarations are hoisted,
// so calling it from here (registered at load time, long before it fires) is safe.
let _lastJsErrorToastAt = 0;
function _notifyJsError(label, detail) {
  console.error(`[global-error-guard] ${label}`, detail);
  try {
    // Rate-limited to one toast every 4s -- a render loop that keeps throwing (e.g. once per
    // animation frame) would otherwise carpet-bomb the screen with toasts, which is its own
    // kind of "the UI is unusable" failure mode.
    const now = Date.now();
    if (now - _lastJsErrorToastAt > 4000) {
      _lastJsErrorToastAt = now;
      showToast("Something went wrong rendering part of the UI.");
    }
  } catch (toastErr) {
    // showToast itself must never be able to turn error-reporting into a second error.
    console.error("[global-error-guard] showToast failed while reporting an error", toastErr);
  }
}
window.addEventListener("error", (e) => {
  _notifyJsError("Uncaught error", e.error ?? e.message ?? e);
});
window.addEventListener("unhandledrejection", (e) => {
  _notifyJsError("Unhandled promise rejection", e.reason ?? e);
});

// The usercount worker (cloudflare-usercount/worker.js) also carries the Discord chat relay --
// GET /discord/messages?after=<id> -- since it already has the lightweight per-isolate caching
// pattern this needed and didn't require standing up a whole new worker for one endpoint.
const USERCOUNT_URL = "https://bandroom-usercount.bandroom.workers.dev";

const categoryColors = {
  Offense: "#2f6f78",
  Defense: "#7a3a3a",
  Situations: "#5c4fa0",
};

// Universal UI click tick -- one delegate covers every button/tile in the app (including ones
// rendered dynamically later, like situation rows) instead of wiring a sound call into every
// individual click handler above. Capture phase so it fires before the element's own handler
// runs, matching the instant "physical press" feel of the CSS :active flash it accompanies.
document.addEventListener("click", (e) => {
  if (e.target.closest("button, .team-swatch, .category-row")) bridge?.PlayClickSound();
}, true);

/// Hover magnify: only the exact tile under the cursor scales up (2x), no neighbor falloff --
/// simpler/cleaner than a full dock-wave sweep, and cheaper (one tile touched per event instead
/// of recomputing distance for every tile in the grid on every mousemove). Bound once per grid
/// container at init since the containers themselves (#team-grid, #team-picker-grid, etc.) are
/// static in the DOM even though their .team-swatch children get torn down/rebuilt on re-render.
function enableDockMagnify(gridEl) {
  if (!gridEl) return;
  let current = null;
  const setScale = (tile, scale) => {
    tile.style.transform = scale > 1.01 ? `scale(${scale})` : "";
    tile.style.zIndex = scale > 1.01 ? "5" : "";
  };
  gridEl.addEventListener("mouseover", (e) => {
    const tile = e.target.closest(".team-swatch");
    if (!tile || tile === current) return;
    if (current) setScale(current, 1);
    current = tile;
    setScale(tile, 2);
  });
  gridEl.addEventListener("mouseleave", () => {
    if (current) setScale(current, 1);
    current = null;
  });
  // The magnify scale is set as an inline style, which beats the stylesheet's
  // .team-swatch:active press-down rule (inline always wins over a class selector) -- so
  // without this, clicking a magnified tile silently ate the "physical press" feedback.
  gridEl.addEventListener("mousedown", (e) => {
    const tile = e.target.closest(".team-swatch");
    if (tile === current) setScale(tile, 1.92);
  });
  gridEl.addEventListener("mouseup", (e) => {
    const tile = e.target.closest(".team-swatch");
    if (tile === current) setScale(tile, 2);
  });
}
for (const id of ["team-grid", "team-picker-grid", "matchup-away-grid", "matchup-home-grid", "onboarding-grid", "bandroom-team-grid"]) {
  enableDockMagnify(document.getElementById(id));
}

let state = {
  teams: [],
  categories: [],
  savedProfiles: [],
  activeTeam: "General",
  watching: "off", // off | waiting | watching
  matchupHome: null,
  matchupAway: null,
  matchupLocked: false,
  currentSituationsCategory: null,
};

async function init() {
  // wireControls() attaches every click handler in the app (rail buttons, header
  // controls, etc). It used to run only after a chain of sequential awaits below --
  // if ANY of those threw (e.g. a bridge call failing), wireControls() never ran and
  // the whole UI looked dead (no version, no working buttons, nothing). Run it FIRST
  // so a data-fetch failure can only blank out its own piece of the UI, never the
  // controls themselves.
  wireControls();

  if (bridge) {
    try {
      state.teams = JSON.parse(await bridge.GetTeams());
    } catch (err) { console.error("GetTeams failed", err); }
    try {
      state.categories = JSON.parse(await bridge.GetCategories());
    } catch (err) { console.error("GetCategories failed", err); }
    try {
      state.activeTeam = await bridge.GetActiveTeam();
    } catch (err) { console.error("GetActiveTeam failed", err); }
    try {
      document.getElementById("app-version").textContent = "v" + await bridge.GetAppVersion();
    } catch (err) {
      console.error("GetAppVersion failed", err);
      document.getElementById("app-version").textContent = "";
    }
    try {
      state.savedProfiles = JSON.parse(await bridge.GetSavedProfiles());
    } catch (err) { console.error("GetSavedProfiles failed", err); }
    try {
      const userProfile = JSON.parse(await bridge.GetUserProfile());
      state.toastsEnabled = userProfile.toastsEnabled !== false;
      updateFavoriteTeamJumpButton(userProfile.favoriteTeam);
      // Auto-load the user's favorite team on launch if nothing was already active (e.g. first
      // run, or GetActiveTeam came back empty) -- previously favoriteTeam only drove the jump
      // star/profile label and never actually selected anything on startup.
      if (!state.activeTeam && userProfile.favoriteTeam) state.activeTeam = userProfile.favoriteTeam;
    } catch (err) { console.error("GetUserProfile (startup) failed", err); }
    try {
      // Admin marketplace override (owner-only) -- see WebBridge.cs's IsAdminMode. Cached once
      // at startup since tile rendering is synchronous and this never changes mid-session; false
      // for every real end-user install (no admin_token.local.txt ships in the installer).
      _isAdminMode = await bridge.IsAdminMode();
    } catch (err) { console.error("IsAdminMode failed", err); }
    try {
      await refreshLeadInWhistleSection();
    } catch (err) { console.error("GetLeadInWhistleAvailable/Enabled failed", err); }
    try {
      await refreshVolumeSliders();
    } catch (err) { console.error("refreshVolumeSliders failed", err); }
    try {
      await refreshBigGameSection();
    } catch (err) { console.error("refreshBigGameSection failed", err); }
    try {
      await refreshSoundBoothSection();
    } catch (err) { console.error("refreshSoundBoothSection failed", err); }
  } else {
    state.teams = [{ name: "General", primary: "#22d3ee", secondary: "#22d3ee" }];
    state.categories = [
      { name: "Offense", assigned: 4, total: 20 },
      { name: "Defense", assigned: 1, total: 20 },
      { name: "Situations", assigned: 3, total: 7 },
    ];
  }
  renderTeamGrid();
  renderCategories();
  setActiveTeam(state.activeTeam, /*fromInit*/ true);
  updateProfileStatus();
  await loadMatchup();
  maybeShowOnboarding();
  pollUserCount();
  loadChangelog();
  pollTickerActivity();
  initSuggestedPanel();
}

// ---- "Suggested for You" panel (Mixer sidebar) -------------------------------------------
// Top-downloaded songs/images/profiles across the whole marketplace (not scoped to the active
// team), pulled via the same /list?sort=downloads the hub grids already use. Shows a small
// rotating batch instead of a static top-N so the panel doesn't go stale between sessions --
// cycles to the next batch every 5 minutes on a plain setInterval (no server push needed, this
// is just paging through an already-fetched, already-sorted list).
let _suggestedPool = [];
let _suggestedRotationIndex = 0;
const SUGGESTED_BATCH_SIZE = 4;
const SUGGESTED_ROTATE_MS = 5 * 60 * 1000;

async function initSuggestedPanel() {
  await refreshSuggestedPool();
  renderSuggestedBatch();
  setInterval(async () => {
    // Re-pull periodically too (every rotation), not just page through one stale snapshot --
    // otherwise a brand-new top downloaded item never shows up until a full app restart.
    await refreshSuggestedPool();
    _suggestedRotationIndex += SUGGESTED_BATCH_SIZE;
    if (_suggestedRotationIndex >= _suggestedPool.length) _suggestedRotationIndex = 0;
    renderSuggestedBatch();
  }, SUGGESTED_ROTATE_MS);
}

async function refreshSuggestedPool() {
  try {
    const [songs, images, profiles] = await Promise.all([
      fetchUploadList("song", null, "downloads"),
      fetchUploadList("image", null, "downloads"),
      fetchUploadList("profile", null, "downloads"),
    ]);
    _suggestedPool = [...songs, ...images, ...profiles]
      .filter((it) => (it.downloads ?? 0) > 0)
      .sort((a, b) => (b.downloads ?? 0) - (a.downloads ?? 0));
  } catch (err) {
    console.error("refreshSuggestedPool failed", err);
    _suggestedPool = [];
  }
}

function renderSuggestedBatch() {
  const list = document.getElementById("suggested-list");
  if (!list) return;
  if (!_suggestedPool.length) {
    list.innerHTML = `<div class="suggested-empty">Nothing downloaded yet -- check back soon.</div>`;
    return;
  }
  if (_suggestedRotationIndex >= _suggestedPool.length) _suggestedRotationIndex = 0;
  const batch = _suggestedPool.slice(_suggestedRotationIndex, _suggestedRotationIndex + SUGGESTED_BATCH_SIZE);
  list.innerHTML = "";
  for (const item of batch) {
    const row = document.createElement("div");
    row.className = "suggested-row";
    const icon = item.type === "image" ? "\u{1F5BC}" : item.type === "profile" ? "\u{1F464}" : "\u{1F3B5}";
    const nameEl = document.createElement("div");
    nameEl.className = "suggested-row-name";
    nameEl.textContent = item.name;
    const metaEl = document.createElement("div");
    metaEl.className = "suggested-row-meta";
    metaEl.textContent = item.school;
    const body = document.createElement("div");
    body.className = "suggested-row-body";
    body.append(nameEl, metaEl);
    const typeEl = document.createElement("span");
    typeEl.className = "suggested-row-type";
    typeEl.textContent = icon;
    const dlEl = document.createElement("span");
    dlEl.className = "suggested-row-dl";
    dlEl.textContent = `\u{2B07} ${(item.downloads ?? 0).toLocaleString()}`;
    row.append(typeEl, body, dlEl);

    // Task queue item 8 (Session 11): this row previously had no per-item transport at all --
    // just a type icon/name/school/download-count, and clicking anywhere on the row navigated
    // away to that item's team album. Same .bandroom-item-action button pattern already used on
    // the marketplace hub's Popular Songs row tiles (buildItemTile), and the same click-guard
    // that lets those buttons coexist with the row's own click-to-navigate handler.
    // NOTE: intentionally NOT the .bandroom-item-actions class buildItemTile's rows use -- that
    // one overlays a square thumbnail, which would badly misplace itself on this row's
    // horizontal list-item layout. Same
    // .bandroom-item-action BUTTON styling is reused (the actual icon-button look), just laid out
    // inline via .suggested-row-actions instead of as an overlay.
    const actions = document.createElement("div");
    actions.className = "suggested-row-actions";
    if (item.type === "song") {
      const playBtn = document.createElement("button");
      playBtn.className = "bandroom-item-action";
      playBtn.title = "Play";
      playBtn.textContent = "▶";
      playBtn.addEventListener("click", (e) => { e.stopPropagation(); previewSong(item); });
      actions.appendChild(playBtn);

      const stopBtn = document.createElement("button");
      stopBtn.className = "bandroom-item-action";
      stopBtn.title = "Stop";
      stopBtn.textContent = "⏹";
      stopBtn.addEventListener("click", (e) => { e.stopPropagation(); stopPreview(); });
      actions.appendChild(stopBtn);
    }
    // Download only applies to song/image types -- the worker's DownloadAsync (server-side) only
    // handles those two, a "profile" item (shared song-assignment maps, not a file) has no
    // downloadable file at all, so no button for that type.
    if (item.type === "song" || item.type === "image") {
      const dlBtn = document.createElement("button");
      dlBtn.className = "bandroom-item-action";
      dlBtn.title = "Download to My Downloads";
      dlBtn.textContent = "⬇";
      dlBtn.addEventListener("click", async (e) => {
        e.stopPropagation();
        dlBtn.disabled = true;
        dlBtn.textContent = "...";
        const ok = bridge ? await downloadMarketplaceItem(item) : false;
        showToast(ok ? `Downloaded "${item.name}"!` : "Couldn't download that.");
        dlBtn.disabled = false;
        dlBtn.textContent = "⬇";
      });
      actions.appendChild(dlBtn);
    }
    if (actions.childElementCount > 0) row.appendChild(actions);

    row.title = `${item.name} — ${item.school}`;
    row.addEventListener("click", (e) => {
      if (e.target.closest(".bandroom-item-action")) return; // button clicks handle themselves
      openTeamAlbum(item.school);
    });
    list.appendChild(row);
  }
}

// Populates the bottom ticker with real recent marketplace uploads (see fetchRecentUploads),
// replacing the static "No uploads yet" placeholder once real data exists. Polls independently
// of pollUserCount -- separate concerns, separate elements (see the pollUserCount fix above).
async function pollTickerActivity() {
  try {
    const [items, downloads, online] = await Promise.all([
      fetchRecentUploads(8),
      fetchWorldwideDownloadCount(),
      bridge ? bridge.GetActiveUserCount() : Promise.resolve(-1),
    ]);
    const el = document.getElementById("ticker-text");
    const parts = [];
    if (online >= 0) parts.push(`🎺 ${online} band member${online === 1 ? "" : "s"} online now`);
    if (downloads != null) parts.push(`🌎 ${downloads.toLocaleString()} downloads worldwide`);
    parts.push(
      ...items.map((it) => `${it.name} (${it.type === "song" ? "song" : "background"}) uploaded by ${it.school}`)
    );
    // Standing credit -- always included in the ticker rotation, not tied to live upload data.
    parts.push(
      "Special Thanks To: CubensisMonster (School Band Rooms) & WashedOutConsultant (Base Sound Pack), For Their Contributions! This Would Be The Same Without You!"
    );
    if (parts.length > 0) el.textContent = parts.join("      •      ");
  } catch (err) {
    console.error("pollTickerActivity failed", err);
  }
  setTimeout(pollTickerActivity, 60000);
}

// All-time worldwide installer download count, from GitHub's own per-asset download counters
// (the real number, not a proxy). Cached ~5min server-side by the worker itself; this just adds
// a thin client-side cache on top so re-polling every 60s doesn't hit the worker needlessly.
let _downloadCountCache = { value: null, fetchedAt: 0 };
async function fetchWorldwideDownloadCount() {
  if (Date.now() - _downloadCountCache.fetchedAt < 60000) return _downloadCountCache.value;
  try {
    const res = await fetch(`${USERCOUNT_URL}/downloads`);
    if (!res.ok) return _downloadCountCache.value;
    const data = await res.json();
    _downloadCountCache = { value: data.count ?? null, fetchedAt: Date.now() };
    return _downloadCountCache.value;
  } catch (err) {
    console.error("fetchWorldwideDownloadCount failed", err);
    return _downloadCountCache.value;
  }
}

// Was previously (wrongly) writing the online count into #ticker-text -- that element is the
// upload-activity ticker (see the marketplace section below), not the presence indicator. The
// presence-dot's tooltip is the actual online-count display and was never being updated at all
// (permanently stuck on "Connecting..."); fixed to update that instead.
async function pollUserCount() {
  const dot = document.getElementById("presence-dot");
  const count = bridge ? await bridge.GetActiveUserCount() : -1;
  dot.title = count < 0 ? "Connecting…" : `${count} band member${count === 1 ? "" : "s"} online`;
  setTimeout(pollUserCount, 30000);
}

/// Shared fill for any team tile/badge: shows the real logo when TeamLogos\ has one for this
/// team, otherwise falls back to the color-gradient + initials monogram. The gradient is always
/// set (even with a logo) so it still shows through logos that have transparent backgrounds.
// BUG FIX 2026-08-09: the matchup coverflow (renderMatchupCoverflow) tears down and rebuilds its
// 5 tiles on every single arrow-click/search keystroke, and positions them via CSS transform
// classes (cf-l2/cf-l1/cf-center/cf-r1/cf-r2). native `loading="lazy"` combined with that churn
// meant an <img> could get destroyed and replaced by the next render before the browser ever
// resolved its intersection check, so the logo never painted -- looked like "matchup logos don't
// work" even though the exact same helper works fine for grids that render once and stay put.
// `eager=true` (used by the matchup coverflow specifically) skips lazy-loading for that case;
// every other caller (100+ team grids/pickers) keeps the lazy default.
function fillTeamSwatch(el, t, eager = false) {
  el.style.background = `linear-gradient(135deg, ${t.primary}, ${t.secondary})`;
  el.style.setProperty("--tile-color", t.primary); // press glow + dock-hover ring use the team's own color
  if (t.logoUrl) {
    const loadingAttr = eager ? "" : ' loading="lazy"';
    el.innerHTML = `<img src="${t.logoUrl}" alt="${t.name}" class="team-logo-img" draggable="false"${loadingAttr} decoding="async">`;
  } else {
    el.textContent = t.initials ?? "";
  }
}

// Forces every tile to a real square by measuring its rendered width and setting height to
// match, instead of trusting CSS aspect-ratio + grid stretch -- two rounds of CSS-only fixes
// (align-content, align-items) didn't resolve reports of squashed/non-square tiles in the team
// picker and matchup grids, so this sidesteps the CSS grid sizing behavior entirely rather than
// guessing at a third one. Re-measures on window resize since those dialogs are responsive-width.
function squareUpTiles(gridEl) {
  if (!gridEl) return;
  requestAnimationFrame(() => {
    const first = gridEl.querySelector(".team-swatch");
    if (!first) return;
    const w = first.getBoundingClientRect().width;
    if (w < 1) return;
    for (const t of gridEl.querySelectorAll(".team-swatch")) t.style.height = `${w}px`;
  });
}
window.addEventListener("resize", () => {
  for (const id of ["team-grid", "team-picker-grid", "matchup-away-grid", "matchup-home-grid", "onboarding-grid", "bandroom-team-grid"])
    squareUpTiles(document.getElementById(id));
});

/// REVERTED -- CSS `zoom` scaling on window resize broke click hit-testing across the app
/// (confirmed live: matchup screen team tiles stopped being clickable at all). Chromium's
/// `zoom` property visually rescales content but pointer-event coordinates don't reliably
/// remap in every WebView2 runtime version, especially stacked with the per-tile inline
/// `transform: scale()` from enableDockMagnify -- the combination is a known source of
/// click-target misalignment. Correctness beats the resize-scaling cosmetic, so this is
/// disabled until a hit-test-safe approach (e.g. rem-based sizing recalculated on resize,
/// with no `zoom`/`transform` involved) replaces it.

function renderTeamGrid() {
  const grid = document.getElementById("team-grid");
  grid.innerHTML = "";
  for (const t of state.teams) {
    const sw = document.createElement("div");
    const configured = state.savedProfiles.includes(t.name);
    sw.className = "team-swatch" + (t.name === state.activeTeam ? " active" : "") + (configured ? " configured" : "");
    sw.title = t.name + (configured ? " ✓" : "");
    fillTeamSwatch(sw, t);
    sw.addEventListener("click", () => selectTeam(t.name));
    grid.appendChild(sw);
  }
  squareUpTiles(grid);
}

async function updateProfileStatus() {
  const el = document.getElementById("profile-status");
  if (!el) return;
  const configured = state.savedProfiles.includes(state.activeTeam);
  const total = state.savedProfiles.length;
  if (!configured) {
    el.innerHTML = `<span class="profile-unsaved">No tracks assigned yet for ${state.activeTeam}</span>`;
    return;
  }
  let savedAt = "";
  try {
    const t = await bridge?.GetProfileSavedAt(state.activeTeam);
    if (t) savedAt = ` at ${t}`;
  } catch (err) { console.error("GetProfileSavedAt failed", err); }
  el.innerHTML = `<span class="profile-saved">&#10003; ${state.activeTeam} saved${savedAt} &mdash; ${total} team${total !== 1 ? "s" : ""} configured</span>`;
}

function renderCategories() {
  const list = document.getElementById("category-list");
  list.innerHTML = "";
  const totalAssigned = state.categories.reduce((n, c) => n + c.assigned, 0);
  const totalAll = state.categories.reduce((n, c) => n + c.total, 0);
  const all = [{ name: "All", assigned: totalAssigned, total: totalAll }, ...state.categories];
  for (const c of all) {
    const row = document.createElement("div");
    row.className = "category-row" + (c.name === state.currentSituationsCategory ? " selected" : "");
    row.innerHTML = `
      <span class="category-dot" style="background:${categoryColors[c.name] ?? "#8b95a1"}"></span>
      <span class="category-text">
        <span class="category-name">${c.name}</span>
        <span class="category-count">${c.assigned}/${c.total}</span>
      </span>`;
    row.addEventListener("click", () => openSituations(c.name));
    list.appendChild(row);
  }
}

async function openSituations(category) {
  const panel = document.getElementById("situations-panel");
  const list = document.getElementById("situations-list");
  document.getElementById("situations-title").textContent = category === "All" ? "All Situations" : category;
  panel.hidden = false;
  // Remember which category is showing so selectTeam() can re-pull the newly active team's
  // OWN assignments into this same panel -- without this, switching Away/Home while the
  // panel is open left it showing whichever team's data happened to be fetched last (looked
  // like both sides shared identical assignments, since the DOM was simply never refreshed).
  state.currentSituationsCategory = category;
  // Mark which tab is active -- previously nothing did this, so switching Offense/Defense/etc.
  // tabs gave no visual sign of which one was actually open (see .category-row.selected in
  // style.css). renderCategories() only runs on data refresh, not on every tab click, so this
  // needs its own toggle here rather than relying on the next re-render to catch up.
  document.querySelectorAll("#category-list .category-row").forEach((row) => {
    const name = row.querySelector(".category-name")?.textContent;
    row.classList.toggle("selected", name === category);
  });

  // openCardPopover reparents the Share To popover straight onto <body> (see its own doc
  // comment -- escapes the card's glass/backdrop-filter stacking context). Rebuilding `list` below
  // discards the ROW that used to own each popover, but the popover itself now lives outside that
  // subtree, so it'd otherwise orphan on body forever across every single refresh. Sweep them here.
  document.querySelectorAll("body > .situation-share-popover").forEach((p) => p.remove());

  const events = bridge ? JSON.parse(await bridge.GetEventsForCategory(category)) : [];
  list.innerHTML = "";
  for (const ev of events) {
    const row = document.createElement("div");
    // "Island" tile instead of a full-width list row: LED dot color says the status at a
    // glance (assigned+confirmed = green pulse, assigned but unconfirmed = amber pulse,
    // nothing assigned yet = dim/no pulse) without needing to read the badge text.
    const ledClass = !ev.fileName ? "situation-led-off" : ev.confirmed ? "situation-led-green" : "situation-led-amber";
    row.className = "situation-row" + (ev.confirmed ? "" : " situation-unconfirmed");
    row.dataset.trigger = ev.trigger; // punch-list item 6: lets scrollToSituationRow() find this card after a trim-save
    row.innerHTML = `
      <span class="situation-text">
        <div class="situation-name"><span class="situation-led ${ledClass}"></span><span class="situation-name-text">${friendlyEventName(ev.eventName)}</span></div>
        <div class="situation-file">${ev.fileName ? ev.fileName : "Unassigned"}</div>
        <div class="situation-file situation-file-pa">PA: ${ev.paFileName ? ev.paFileName : "none"}</div>
      </span>
      <span class="situation-actions" style="position: relative;">
        <button class="situation-btn" data-act="assign">Assign / Edit</button>
        <button class="situation-btn situation-btn-pa" data-act="assign-pa" title="Assign a PA Announcer clip that plays alongside the main song for this situation">Assign PA</button>
        <span class="situation-transport" style="position: relative;">
          <button class="bandroom-item-action" data-act="open-clipper" title="Open the Clipper to pick/trim a song for this event">&#9986;</button>
          <button class="bandroom-item-action" data-act="share-to" title="Assign this same song to another event too" ${ev.fileName ? "" : "disabled"}>&#8599;</button>
          <button class="bandroom-item-action" data-act="preview" title="Play" ${ev.fileName ? "" : "disabled"}>&#9654;</button>
          <button class="bandroom-item-action" data-act="stop" title="Stop">&#9209;</button>
          <button class="bandroom-item-action" data-act="volume" title="Adjust this event's own volume">&#128266;</button>
          <button class="bandroom-item-action situation-whistle-toggle${ev.playLeadInWhistle === false ? "" : " active"}" data-act="whistle" title="${ev.playLeadInWhistle === false ? "Lead-in whistle off for this song -- click to turn it back on" : "Lead-in whistle on for this song (when the global toggle is on) -- click to skip it for just this one"}">&#128239;</button>
          <button class="bandroom-item-action situation-whistle-toggle${ev.altWhistleSet ? " active" : ""}" data-act="alt-whistle" title="${ev.altWhistleSet ? "This event has its own alternate whistle clip -- click to clear it and go back to the global whistle" : "Pick an alternate whistle clip just for this event, instead of the global one"}">&#127895;</button>
          <button class="bandroom-item-action situation-whistle-toggle${ev.speed2x ? " active" : ""}" data-act="speed2x" title="${ev.speed2x ? "Playing at 1.09x speed -- click to go back to normal speed" : "Play this event's song at 1.09x speed (in-game and preview)"}">&#9193;</button>
          <button class="bandroom-item-action" data-act="track-info" title="Track Info" ${ev.fileName ? "" : "disabled"}>&#8505;</button>
          <div class="situation-share-popover glass" hidden>
            <div class="situation-copy-title">Share this song to&hellip;</div>
            <div class="situation-copy-list"></div>
            <button class="situation-copy-close" title="Close">&times;</button>
          </div>
        </span>
        <div class="situation-volume-popover" hidden>
          <input type="range" min="0" max="100" value="100" class="slider situation-volume-slider" />
          <span class="situation-volume-value">100%</span>
          <button class="situation-volume-close" title="Close">&times;</button>
        </div>
      </span>`;
    row.querySelector('[data-act="assign"]').addEventListener("click", () => openClipperAssign(ev.trigger, ev.eventName, false, ev.fileName));
    row.querySelector('[data-act="assign-pa"]').addEventListener("click", () => openClipperAssign(ev.trigger, ev.eventName, true, ev.paFileName));
    // Same jump as "Assign / Edit" above, just as a compact icon in the transport strip --
    // owner asked for the same-style icon button (like the ↗ Share to... icon) as a quicker way
    // into the Clipper without hunting for the pill row on narrower cards.
    row.querySelector('[data-act="open-clipper"]').addEventListener("click", () => openClipperAssign(ev.trigger, ev.eventName, false, ev.fileName));
    wireSituationShareToPopover(row, ev, events);
    row.querySelector('[data-act="preview"]').addEventListener("click", () => { _previewAudio?.pause(); bridge?.PreviewEvent(ev.trigger); });
    row.querySelector('[data-act="stop"]').addEventListener("click", () => bridge?.StopPreview());
    row.querySelector('[data-act="track-info"]').addEventListener("click", () => openTrackInfoDrawer(ev.trigger, ev.fileName));
    row.querySelector('[data-act="whistle"]').addEventListener("click", (e) => {
      const btn = e.currentTarget;
      const nowOn = !btn.classList.contains("active");
      btn.classList.toggle("active", nowOn);
      btn.title = nowOn
        ? "Lead-in whistle on for this song (when the global toggle is on) -- click to skip it for just this one"
        : "Lead-in whistle off for this song -- click to turn it back on";
      bridge?.SetEventPlayLeadInWhistle(ev.trigger, nowOn);
    });
    // Per-event alternate whistle -- clicking when unset opens Clipper Island in "alt-whistle"
    // mode (pick a library song, Trim... it, Set as Alt Whistle -- same flow the global whistle
    // button already used, previously this was the only whistle control still using a bare native
    // OpenFileDialog via BrowseAndSetEventAltWhistle). Clicking again while set just clears it
    // back to the global whistle rather than trying to cram a second "replace" affordance into one
    // icon button.
    row.querySelector('[data-act="alt-whistle"]').addEventListener("click", async (e) => {
      e.stopPropagation();
      if (ev.altWhistleSet) {
        await bridge?.ClearEventAltWhistle(ev.trigger);
        showToast("Cleared -- this event uses the global whistle again.");
        await openSituations(state.currentSituationsCategory);
      } else {
        await openClipperAssignForAltWhistle(ev.trigger, ev.eventName);
      }
    });
    row.querySelector('[data-act="speed2x"]').addEventListener("click", (e) => {
      const btn = e.currentTarget;
      const nowOn = !btn.classList.contains("active");
      btn.classList.toggle("active", nowOn);
      btn.title = nowOn
        ? "Playing at 1.09x speed -- click to go back to normal speed"
        : "Play this event's song at 1.09x speed (in-game and preview)";
      bridge?.SetEventPlaybackSpeed2x(ev.trigger, nowOn);
    });
    wireSituationVolumePopover(row, ev.trigger);
    list.appendChild(row);
  }
}

/// Shared open/close for the card-anchored popovers (.situation-share-popover and the volume
/// popover) -- FIXED 2026-08-11: these used to be position:absolute nested inside the card, which
/// a glass/backdrop-filter card traps in its own stacking context (z-index can't escape it), so
/// the popover rendered clipped/overlapping a NEIGHBORING card instead of floating on top (see the
/// owner's screenshot). Reparenting to document.body + position:fixed with JS-computed coordinates
/// escapes that entirely. Also now slides out from the right edge of the anchor button (owner
/// request) instead of popping up above it, via the .slide-open class (see style.css transition).
function openCardPopover(anchorBtn, popover) {
  document.body.appendChild(popover);
  popover.hidden = false;
  popover.classList.remove("slide-open");
  const rect = anchorBtn.getBoundingClientRect();
  const popW = 280;
  let left = rect.right + 10;
  if (left + popW > window.innerWidth - 8) left = rect.left - popW - 10; // no room on the right -- open to the left instead
  if (left < 8) left = 8;
  popover.style.left = `${left}px`;
  popover.style.top = `${rect.top}px`;
  requestAnimationFrame(() => {
    const popH = popover.offsetHeight;
    if (rect.top + popH > window.innerHeight - 8) {
      popover.style.top = `${Math.max(8, window.innerHeight - 8 - popH)}px`;
    }
    popover.classList.add("slide-open");
  });
}
function closeCardPopover(popover) {
  popover.classList.remove("slide-open");
  setTimeout(() => { popover.hidden = true; }, 180);
}
/// Lets the owner drag a popover to a spot of their choosing (owner request) by its title bar --
/// switches it to explicit left/top tracking the cursor, same fixed-position element either way.
function makePopoverDraggable(popover, handle) {
  let dragging = false, offsetX = 0, offsetY = 0;
  handle.addEventListener("mousedown", (e) => {
    dragging = true;
    const rect = popover.getBoundingClientRect();
    offsetX = e.clientX - rect.left;
    offsetY = e.clientY - rect.top;
    e.preventDefault();
  });
  document.addEventListener("mousemove", (e) => {
    if (!dragging) return;
    popover.style.left = `${e.clientX - offsetX}px`;
    popover.style.top = `${e.clientY - offsetY}px`;
  });
  document.addEventListener("mouseup", () => { dragging = false; });
}

/// "Share to..." button -- pushes THIS row's already-assigned song
/// (source) onto another event on the same team (target) instead of pulling one in. Same
/// candidate list (this card's category, same team, no extra bridge call) but unfiltered by
/// whether the target already has something assigned -- sharing INTO an unassigned event is the
/// whole point (fast bulk-assignment), and sharing into an already-assigned one is allowed too
/// (just overwrites the target, same as Copy From does in the other direction). Button itself is
/// disabled via the `disabled` attribute set at render time when this row has nothing to share.
function wireSituationShareToPopover(row, ev, events) {
  const btn = row.querySelector('[data-act="share-to"]');
  const popover = row.querySelector(".situation-share-popover");
  const listEl = popover.querySelector(".situation-copy-list");
  const closeBtn = popover.querySelector(".situation-copy-close");

  const closePopover = () => closeCardPopover(popover);
  makePopoverDraggable(popover, popover.querySelector(".situation-copy-title"));

  btn.addEventListener("click", (e) => {
    e.stopPropagation();
    if (!popover.hidden) { closePopover(); return; }
    document.querySelectorAll(".situation-share-popover").forEach((p) => { p.hidden = true; p.classList.remove("slide-open"); });
    const candidates = events.filter((other) => other.trigger !== ev.trigger);
    listEl.innerHTML = candidates.length
      ? ""
      : `<div class="situation-copy-empty">No other events on this team yet.</div>`;
    for (const other of candidates) {
      const item = document.createElement("button");
      item.className = "situation-copy-option";
      item.innerHTML = `<span class="situation-copy-option-name">${friendlyEventName(other.eventName)}</span><span class="situation-copy-option-file">${other.fileName ? "overwrites " + other.fileName : "unassigned"}</span>`;
      item.addEventListener("click", async (ce) => {
        ce.stopPropagation();
        const ok = bridge ? await bridge.CopyEventAssignment(ev.trigger, other.trigger) : false;
        closePopover();
        if (ok) {
          showToast(`Shared "${friendlyEventName(ev.eventName)}"'s song to "${friendlyEventName(other.eventName)}".`);
          await openSituations(state.currentSituationsCategory);
        } else {
          showToast("Couldn't share that assignment.");
        }
      });
      listEl.appendChild(item);
    }
    openCardPopover(btn, popover);
  });
  closeBtn.addEventListener("click", (e) => { e.stopPropagation(); closePopover(); });
}

/// Volume button on an event card pops out a small slider (+ close/X) instead of a permanent
/// on-card control -- owner explicitly asked for this same "click to pop out, X to close"
/// pattern here as the PA volume model. Only one popover open at a time (closes any other card's
/// popover first) so they don't pile up across a long situations list.
function wireSituationVolumePopover(row, trigger) {
  const btn = row.querySelector('[data-act="volume"]');
  const popover = row.querySelector(".situation-volume-popover");
  const slider = row.querySelector(".situation-volume-slider");
  const valueLabel = row.querySelector(".situation-volume-value");
  const closeBtn = row.querySelector(".situation-volume-close");

  const closePopover = () => { popover.hidden = true; };

  btn.addEventListener("click", async (e) => {
    e.stopPropagation();
    if (!popover.hidden) { closePopover(); return; }
    document.querySelectorAll(".situation-volume-popover").forEach((p) => { p.hidden = true; });
    const current = bridge ? await bridge.GetEventVolume(trigger) : 100;
    slider.value = current;
    valueLabel.textContent = `${current}%`;
    popover.hidden = false;
  });
  closeBtn.addEventListener("click", (e) => { e.stopPropagation(); closePopover(); });
  slider.addEventListener("input", (e) => {
    valueLabel.textContent = `${e.target.value}%`;
    bridge?.SetEventVolume(trigger, Number(e.target.value));
  });
  slider.addEventListener("click", (e) => e.stopPropagation());
}

async function selectTeam(name) {
  if (name === state.activeTeam) return;
  state.activeTeam = name;
  _clipperAssignLibrary = null; // team-scoped default/conference pack songs are merged in per-team, see openClipperAssign
  if (bridge) await bridge.SelectTeam(name);
  setActiveTeam(name);
  renderTeamGrid();
  // ROOT CAUSE FIX (Bug 1a): bridge.SelectTeam swaps the backend's in-memory profile to the
  // newly active team correctly, but the situations panel (if left open while flipping
  // Away/Home) was never told to re-fetch -- it just kept displaying whatever GetEventsForCategory
  // result was already sitting in the DOM from before the switch, which made both sides look
  // identical. Re-run openSituations for whichever category is currently showing so it re-pulls
  // straight from the now-active team's real, freshly-loaded profile.
  if (state.currentSituationsCategory && !document.getElementById("situations-panel").hidden) {
    await openSituations(state.currentSituationsCategory);
  }
  await refreshCategories();
}

/// Best-effort abbreviation/name match for the quick-load-by-abbreviation input on the assign
/// page: exact initials match wins, then initials-starts-with, then name-starts-with, then name
/// contains -- in that priority order, first hit wins. Returns null if the query is empty or
/// nothing matches at all (caller shows "no match" instead of guessing).
function findTeamByAbbreviation(query) {
  const q = (query || "").trim().toLowerCase();
  if (!q) return null;
  const teams = (state.teams || []).filter((t) => t.name !== "General");
  return (
    teams.find((t) => (t.initials || "").toLowerCase() === q) ||
    teams.find((t) => (t.initials || "").toLowerCase().startsWith(q)) ||
    teams.find((t) => t.name.toLowerCase().startsWith(q)) ||
    teams.find((t) => t.name.toLowerCase().includes(q)) ||
    null
  );
}

/// Quick-load-by-abbreviation control on the assign page's matchup side bar: type a team's
/// abbreviation/initials or partial name, see a live best-match hint, confirm before it actually
/// switches the active profile (reuses the exact same selectTeam() the sidebar tile grid uses).
function setupQuickLoadProfile() {
  const input = document.getElementById("quick-load-input");
  const hint = document.getElementById("quick-load-hint");
  const loadBtn = document.getElementById("btn-quick-load");
  const confirmOverlay = document.getElementById("quick-load-confirm-overlay");
  const confirmText = document.getElementById("quick-load-confirm-text");
  const yesBtn = document.getElementById("btn-quick-load-yes");
  const noBtn = document.getElementById("btn-quick-load-no");
  if (!input || !loadBtn) return;
  document.getElementById("btn-quick-load-close").addEventListener("click", () => noBtn.click());

  const updateHint = () => {
    const match = findTeamByAbbreviation(input.value);
    hint.textContent = match ? `→ ${match.name}` : (input.value.trim() ? "No match" : "");
  };
  input.addEventListener("input", updateHint);

  const tryConfirm = () => {
    const match = findTeamByAbbreviation(input.value);
    if (!match) { showToast("No team matches that yet -- keep typing."); return; }
    confirmText.textContent = `Is "${match.name}" the team you found -- the right team?`;
    confirmOverlay.hidden = false;
    yesBtn.onclick = () => {
      confirmOverlay.hidden = true;
      selectTeam(match.name);
      input.value = "";
      hint.textContent = "";
    };
    noBtn.onclick = () => {
      confirmOverlay.hidden = true;
      input.focus();
    };
  };
  loadBtn.addEventListener("click", tryConfirm);
  input.addEventListener("keydown", (e) => { if (e.key === "Enter") tryConfirm(); });
  // No backdrop-click-to-close -- Yes/No/X only, matching every other popup in this app.
}

/// Picks the most legible ink color for text sitting on a `bg` swatch, preferring the team's
/// OTHER color (primary on secondary, or vice versa) over plain black/white when it actually
/// reads well -- so a pill reads as "team colors" rather than a generic dark-on-light chip.
/// Falls back to black/white by relative luminance when the other team color doesn't contrast
/// enough (e.g. two similarly-dark or similarly-light team colors).
function pickContrastInk(bg, altColor) {
  const luminance = (hex) => {
    const m = /^#?([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i.exec(hex || "");
    if (!m) return null;
    const [r, g, b] = [m[1], m[2], m[3]].map((h) => {
      const c = parseInt(h, 16) / 255;
      return c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4);
    });
    return 0.2126 * r + 0.7152 * g + 0.0722 * b;
  };
  const bgLum = luminance(bg);
  if (bgLum == null) return "#06222a";
  const contrastRatio = (l1, l2) => (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05);
  const altLum = luminance(altColor);
  if (altLum != null) {
    const altRatio = contrastRatio(bgLum, altLum);
    if (altRatio >= 3.5) return altColor; // legible enough to read as "the other team color"
  }
  const blackRatio = contrastRatio(bgLum, 0);
  const whiteRatio = contrastRatio(bgLum, 1);
  return whiteRatio > blackRatio ? "#ffffff" : "#06222a";
}

// A handful of teams (Appalachian State, Army, ...) have literal black as their primary -- fine
// as a jersey color, unreadable as a glow/accent color. Shared by every --team-primary consumer
// (setActiveTeam, previewTeamGlow, applySchoolGlow) so there's one fallback rule, not one per call site.
function isNearBlack(hex) {
  const h = (hex || "").replace("#", "");
  if (h.length !== 6) return false;
  const r = parseInt(h.slice(0, 2), 16), g = parseInt(h.slice(2, 4), 16), b = parseInt(h.slice(4, 6), 16);
  return r < 20 && g < 20 && b < 20;
}

// Shared perceived-brightness check (same WCAG relative-luminance formula pickContrastInk uses
// locally) -- used by applyTeamGlowVars to pick whichever of primary/secondary is the lighter
// color for --team-glow. Design rule: LED/glow pulses must always read as light-on-dark, never
// use the team's dark color for a glow (illegible against the dark glass background).
function relativeLuminance(hex) {
  const m = /^#?([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i.exec(hex || "");
  if (!m) return 0;
  const [r, g, b] = [m[1], m[2], m[3]].map((h) => {
    const c = parseInt(h, 16) / 255;
    return c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4);
  });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

/// Sets the four --team-* CSS custom properties (primary/secondary + their contrast inks) that
/// drive the app-wide background tint and glow pulse. Split out of setActiveTeam so a coverflow
/// can call it live while just browsing (previewTeamGlow below) without triggering
/// setActiveTeam's other side effects (header text, background image swap, bridge calls).
function applyTeamGlowVars(team) {
  const rawSecondary = team?.secondary ?? "#22d3ee";
  const rawPrimary = team?.primary ?? "#0f766e";
  const primary = isNearBlack(rawPrimary) ? rawSecondary : rawPrimary;
  // --team-secondary drives nearly every glow/border/badge/text-accent color in the theme (see
  // the color-mix(...var(--team-secondary)...) rules throughout style.css), not just decorative
  // glow -- so a team whose authentic secondary color is literal black (several FCS schools:
  // North Dakota, Illinois State, Youngstown State, Wofford, Stony Brook, ...) made the ENTIRE
  // accent system go invisible-on-dark for that team: black glow borders, black badge text, etc.
  // Same isNearBlack fallback --team-primary already gets, applied here too -- falls back to
  // primary (if that one isn't ALSO near-black) or the app's default accent as a last resort.
  const secondary = isNearBlack(rawSecondary) ? (isNearBlack(primary) ? "#22d3ee" : primary) : rawSecondary;
  document.documentElement.style.setProperty("--team-secondary", secondary);
  document.documentElement.style.setProperty("--team-primary", primary);
  document.documentElement.style.setProperty("--team-secondary-ink", pickContrastInk(secondary, primary));
  document.documentElement.style.setProperty("--team-primary-ink", pickContrastInk(primary, secondary));
  // Design rule (2026-08-09): every LED/glow pulse uses whichever of primary/secondary is
  // lighter -- never the dark one, regardless of which is "the" primary color.
  const glow = relativeLuminance(primary) >= relativeLuminance(secondary) ? primary : secondary;
  document.documentElement.style.setProperty("--team-glow", glow);
}

/// Live-updates the background tint/glow to whichever team is centered in a coverflow or
/// hovered in a team-picker grid, WITHOUT changing state.activeTeam -- browsing is not picking.
/// Callers must call restoreActiveTeamGlow() when the picker closes without confirming, so the
/// real active team's glow comes back instead of staying stuck on whatever was last previewed.
function previewTeamGlow(teamNameOrObj) {
  const team = typeof teamNameOrObj === "string"
    ? state.teams?.find((t) => t.name === teamNameOrObj)
    : teamNameOrObj;
  if (team) applyTeamGlowVars(team);
}

function restoreActiveTeamGlow() {
  const team = state.teams?.find((t) => t.name === state.activeTeam);
  applyTeamGlowVars(team);
}

function setActiveTeam(name, fromInit = false) {
  document.getElementById("team-name").textContent = name;
  applyBackground(name);
  const team = state.teams.find((t) => t.name === name);
  applyTeamGlowVars(team);
  updateProfileStatus();
  updateHeaderTeamBadge(team);
  updateMatchupSideBar();
  // Switching teams always resets the backend's active preset back to "Plain" (SelectTeamFromWeb),
  // so the pill bar needs to reflect that too rather than keeping the previous team's selection.
  state.activeTeamPreset = "";
  updateTeamPresetBar();
}

/// Shows a one-click Away/Home toggle above the situations list once a matchup is set, so it's
/// obvious which team's profile you're currently assigning songs to (they're two separate
/// profiles -- e.g. Alabama's Touchdown cue is independent from Arkansas's Touchdown cue -- and
/// this is the fast way to flip between editing them instead of hunting for the team grid).
function updateMatchupSideBar() {
  const bar = document.getElementById("matchup-side-bar");
  if (!bar) return;
  if (!state.matchupHome || !state.matchupAway) {
    bar.hidden = true;
    return;
  }
  bar.hidden = false;
  const awayBtn = document.getElementById("btn-side-away");
  const homeBtn = document.getElementById("btn-side-home");
  // FIXED: used to set awayBtn/homeBtn.textContent directly, which wipes out the logo/arrow
  // markup added for Game Day mode's VS-styled header (textContent replaces ALL children with
  // plain text) -- now populates the named inner elements instead.
  const awayTeam = state.teams.find((t) => t.name === state.matchupAway);
  const homeTeam = state.teams.find((t) => t.name === state.matchupHome);
  document.getElementById("matchup-side-away-name").textContent = `Away: ${state.matchupAway}`;
  document.getElementById("matchup-side-home-name").textContent = `Home: ${state.matchupHome}`;
  const awayLogo = document.getElementById("matchup-side-away-logo");
  const homeLogo = document.getElementById("matchup-side-home-logo");
  if (awayTeam?.logoUrl) { awayLogo.src = awayTeam.logoUrl; awayLogo.hidden = false; } else { awayLogo.hidden = true; }
  if (homeTeam?.logoUrl) { homeLogo.src = homeTeam.logoUrl; homeLogo.hidden = false; } else { homeLogo.hidden = true; }
  awayBtn.classList.toggle("active", state.activeTeam === state.matchupAway);
  homeBtn.classList.toggle("active", state.activeTeam === state.matchupHome);
  awayBtn.style.setProperty("--side-glow", awayTeam?.secondary || awayTeam?.primary || "");
  homeBtn.style.setProperty("--side-glow", homeTeam?.secondary || homeTeam?.primary || "");
}

// ---- Team song-assignment presets: Home / Away / Big Game (owner request 2026-08-11) -----------
// Three switchable alternate profiles per team, e.g. so a team's "Big Game" songs can differ from
// their everyday set. Bridge: SwitchTeamPreset/CopyTeamPreset/GetTeamPresetStatus (WebBridge.cs ->
// WebMainForm's *FromWeb versions), which reuse the existing named-profile save/load machinery --
// a preset is just an extra profile named "<team> · <preset>". Gameplay auto-picks the right one
// at GAMETIME (see WebMainForm.GameplayProfileKey); this bar is only about EDITING them.
const TEAM_PRESET_IDS = ["", "Home", "Away", "BigGame"];

function presetPillId(preset) {
  return `btn-preset-${preset === "BigGame" ? "biggame" : preset === "" ? "plain" : preset.toLowerCase()}`;
}

function updateTeamPresetBar() {
  const bar = document.getElementById("team-preset-bar");
  if (!bar) return;
  if (!state.activeTeam) { bar.hidden = true; return; }
  bar.hidden = false;
  for (const preset of TEAM_PRESET_IDS) {
    const btn = document.getElementById(presetPillId(preset));
    if (btn) btn.classList.toggle("active", (state.activeTeamPreset || "") === preset);
  }
  refreshTeamPresetStatus();
}

async function refreshTeamPresetStatus() {
  if (!bridge) return;
  let status = {};
  try { status = JSON.parse(await bridge.GetTeamPresetStatus()); }
  catch (err) { console.error("GetTeamPresetStatus failed", err); }
  for (const preset of ["Home", "Away", "BigGame"]) {
    document.getElementById(presetPillId(preset))?.classList.toggle("configured", !!status[preset]);
  }
  const labels = { "": "Plain", Home: "Home", Away: "Away", BigGame: "Big Game" };
  for (const selectId of ["team-preset-copy-from", "team-preset-copy-to"]) {
    const sel = document.getElementById(selectId);
    if (!sel) continue;
    const prevValue = sel.value;
    sel.innerHTML = "";
    for (const preset of TEAM_PRESET_IDS) {
      const configured = preset === "" || !!status[preset];
      const opt = document.createElement("option");
      opt.value = preset;
      opt.textContent = labels[preset] + (configured ? "" : " (empty)");
      sel.appendChild(opt);
    }
    // Default the "from" side to whatever's currently being edited, "to" side to the next one
    // over -- just a sane starting point, the user can always repick either dropdown.
    sel.value = prevValue || (selectId === "team-preset-copy-from" ? (state.activeTeamPreset || "") : "Home");
  }
}

async function switchTeamPreset(preset) {
  if (!bridge || !state.activeTeam) return;
  let applied = "";
  try { applied = await bridge.SwitchTeamPreset(preset || ""); }
  catch (err) { console.error("SwitchTeamPreset failed", err); return; }
  state.activeTeamPreset = applied || "";
  updateTeamPresetBar();
  // Same re-fetch selectTeam() does after a profile swap -- _config changed server-side, the
  // song-assignment list on screen needs to re-pull from it.
  if (state.currentSituationsCategory && !document.getElementById("situations-panel").hidden) {
    await openSituations(state.currentSituationsCategory);
  }
  await refreshCategories();
}

function wireTeamPresetBar() {
  document.querySelectorAll(".pill-preset").forEach((btn) => {
    btn.addEventListener("click", () => switchTeamPreset(btn.dataset.preset || ""));
  });
  document.getElementById("btn-preset-copy")?.addEventListener("click", async () => {
    if (!bridge) return;
    const from = document.getElementById("team-preset-copy-from").value;
    const to = document.getElementById("team-preset-copy-to").value;
    const labels = { "": "Plain", Home: "Home", Away: "Away", BigGame: "Big Game" };
    if (from === to) { showToast("Pick two different presets to copy between."); return; }
    let ok = false;
    try { ok = await bridge.CopyTeamPreset(from, to); }
    catch (err) { console.error("CopyTeamPreset failed", err); showToast("Copy failed."); return; }
    if (!ok) { showToast(`"${labels[from]}" preset hasn't been saved yet -- nothing to copy.`); return; }
    showToast(`Copied ${labels[from]} → ${labels[to]}.`);
    if ((state.activeTeamPreset || "") === to && state.currentSituationsCategory && !document.getElementById("situations-panel").hidden) {
      await openSituations(state.currentSituationsCategory);
    }
    await refreshTeamPresetStatus();
  });
}

/// Events-page Auto-Assign (matchup-side-bar): overwrites EVERY slot for state.activeTeam with
/// the default pack, after an explicit overwrite confirm -- previously this only filled empty
/// slots silently; the owner wants a real "replace what I've got" action instead, gated behind a
/// confirm since it's destructive to any hand-tuned assignments. If the default pack isn't
/// downloaded/imported at all, this shows the SAME #songpack-prompt-overlay every other "need the
/// pack" entry point uses (Download/Skip -> Locate & Import zip or folder) rather than a dead-end
/// error, then re-runs itself once the pack finishes importing.
async function handleAutoAssignClick() {
  if (!state.activeTeam || !bridge) return;
  let hasPack = false;
  try { hasPack = await bridge.HasDefaultSongPack(); } catch (err) { console.error("HasDefaultSongPack failed", err); }
  if (!hasPack) {
    document.getElementById("songpack-prompt-overlay").hidden = false;
    window.addEventListener("bandroom:songpackready", () => handleAutoAssignClick(), { once: true });
    return;
  }
  const team = state.activeTeam;
  document.getElementById("auto-assign-confirm-text").textContent =
    `Quick-overwrite replaces every assigned song for ${team} with the default pack (anything customized is lost). ` +
    `Guided Assign instead walks through each event one at a time so you can confirm or pick from candidates yourself.`;
  document.getElementById("auto-assign-confirm-overlay").hidden = false;
  const cancelBtn = document.getElementById("btn-auto-assign-cancel");
  const yesBtn = document.getElementById("btn-auto-assign-confirm-yes");
  const guidedBtn = document.getElementById("btn-auto-assign-guided");
  await new Promise((resolve) => {
    const cleanup = () => {
      cancelBtn.removeEventListener("click", onCancel);
      yesBtn.removeEventListener("click", onYes);
      guidedBtn.removeEventListener("click", onGuided);
      document.getElementById("auto-assign-confirm-overlay").hidden = true;
    };
    const onCancel = () => { cleanup(); resolve(); };
    const onYes = async () => {
      cleanup();
      await runAutoAssignOverwrite(team);
      resolve();
    };
    const onGuided = async () => {
      cleanup();
      await startAutoAssignWizard(team);
      resolve();
    };
    cancelBtn.addEventListener("click", onCancel);
    yesBtn.addEventListener("click", onYes);
    guidedBtn.addEventListener("click", onGuided);
  });
}

/// Reusable "keyword overlap" matcher shared by the guided wizard: strips filler words out of an
/// event's display name and scores library entries by how many of the remaining significant words
/// appear in the entry's filename. Not fuzzy/AI matching -- just enough to auto-pick an obvious
/// single hit and narrow the list when there's more than one plausible candidate, same spirit as
/// the substring search box already used everywhere else in this file.
const AUTO_ASSIGN_STOPWORDS = new Set([
  "the", "and", "for", "with", "from", "your", "team", "event", "situation", "pa", "announcer",
]);
function matchCandidatesForEvent(eventName, library) {
  const words = friendlyEventName(eventName)
    .toLowerCase()
    .split(/[^a-z0-9]+/)
    .filter((w) => w.length >= 3 && !AUTO_ASSIGN_STOPWORDS.has(w));
  if (!words.length) return [];
  return library
    .map((item) => {
      const name = (item.name || "").toLowerCase();
      const score = words.reduce((n, w) => n + (name.includes(w) ? 1 : 0), 0);
      return { item, score };
    })
    .filter((s) => s.score > 0)
    .sort((a, b) => b.score - a.score)
    .map((s) => s.item);
}

/// Guided Auto-Assign wizard: unlike runAutoAssignOverwrite (one confirm, replaces every slot from
/// the default pack silently), this walks every event for `team` one at a time, reusing the same
/// #clipper-assign panel every "Assign / Edit" button already opens -- so the user always sees the
/// real search/play/browse UI, just pre-seeded with a best-guess match. Library = local Songs
/// library (GetTrackLibrary, already includes past marketplace downloads) merged with this team's
/// default-pack songs (GetDefaultPackSongsForTeam) -- searching "local and market at once" per the
/// owner's request, without needing a live remote catalog call per event.
let _autoAssignWizard = null;

async function startAutoAssignWizard(team) {
  showToast(`Scanning ${team}'s events...`);
  let queue = [];
  try {
    for (const cat of state.categories) {
      if (cat.name === "All") continue;
      const evs = JSON.parse(await bridge.GetEventsForCategory(cat.name));
      for (const ev of evs) queue.push(ev);
    }
  } catch (err) {
    console.error("GetEventsForCategory (wizard) failed", err);
  }
  if (!queue.length) {
    showToast(`No events found for ${team}.`);
    return;
  }

  let library = [];
  try {
    const [localJson, packJson, conferenceJson] = await Promise.all([
      bridge.GetTrackLibrary(),
      bridge.GetDefaultPackSongsForTeam(team),
      bridge.GetConferencePackSongsForTeam(team),
    ]);
    const local = JSON.parse(localJson) || [];
    const pack = (JSON.parse(packJson) || []).map((s) => ({ ...s, source: s.source || "local" }));
    // Conference-wide cues go last -- same team-specific-beats-generic priority as the backend's
    // "run team pack first, conference pack only backfills" order, just reflected here so a
    // team's own song wins ties in matchCandidatesForEvent when both exist for the same event.
    const conference = (JSON.parse(conferenceJson) || []).map((s) => ({ ...s, source: s.source || "local" }));
    const seenPaths = new Set(local.map((it) => it.path));
    const packAndConference = [...pack, ...conference].filter((it) => !seenPaths.has(it.path));
    for (const it of packAndConference) seenPaths.add(it.path);
    library = [...local, ...packAndConference];
  } catch (err) {
    console.error("auto-assign wizard library load failed", err);
  }

  _autoAssignWizard = { team, queue, index: 0, library, assigned: 0, skipped: 0, cancelled: false, log: [] };
  document.getElementById("auto-assign-wizard-bar").hidden = false;
  await advanceAutoAssignWizard();
}

async function advanceAutoAssignWizard() {
  const wiz = _autoAssignWizard;
  if (!wiz || wiz.cancelled) return;
  if (wiz.index >= wiz.queue.length) {
    finishAutoAssignWizard(false);
    return;
  }
  const ev = wiz.queue[wiz.index];
  document.getElementById("auto-assign-wizard-progress").textContent =
    `Guided Auto-Assign -- ${wiz.team}: event ${wiz.index + 1} of ${wiz.queue.length} (${friendlyEventName(ev.eventName)})`;

  if (ev.fileName) {
    document.getElementById("auto-assign-confirm-text").textContent =
      `"${friendlyEventName(ev.eventName)}" already has "${ev.fileName}" assigned. Overwrite it?`;
    const overlay = document.getElementById("auto-assign-confirm-overlay");
    const cancelBtn = document.getElementById("btn-auto-assign-cancel");
    const yesBtn = document.getElementById("btn-auto-assign-confirm-yes");
    const guidedBtn = document.getElementById("btn-auto-assign-guided");
    guidedBtn.hidden = true;
    overlay.hidden = false;
    const proceed = await new Promise((resolve) => {
      const cleanup = () => {
        cancelBtn.removeEventListener("click", onSkip);
        yesBtn.removeEventListener("click", onYes);
        overlay.hidden = true;
        guidedBtn.hidden = false;
      };
      const onSkip = () => { cleanup(); resolve(false); };
      const onYes = () => { cleanup(); resolve(true); };
      cancelBtn.addEventListener("click", onSkip);
      yesBtn.addEventListener("click", onYes);
    });
    if (!_autoAssignWizard || _autoAssignWizard.cancelled) return;
    if (!proceed) {
      wiz.skipped++;
      wiz.index++;
      await advanceAutoAssignWizard();
      return;
    }
  }

  await openWizardEventPicker(ev);
}

/// Opens the shared clipper-assign panel for one wizard event, pre-seeding it with the best
/// keyword match (see matchCandidatesForEvent): a single strong match is auto-selected so the
/// user just has to hit "Assign Selected" to confirm it; multiple matches narrow the visible list
/// via the existing search box instead of forcing a pick; zero matches leaves the full library
/// visible so the user can search manually or hit Skip.
async function openWizardEventPicker(ev) {
  const wiz = _autoAssignWizard;
  if (!wiz) return;
  _clipperAssignLibrary = wiz.library;
  await openClipperAssign(ev.trigger, ev.eventName, false, ev.fileName);
  const candidates = matchCandidatesForEvent(ev.eventName, wiz.library);
  const searchInput = document.getElementById("clipper-assign-search");
  if (candidates.length === 1) {
    searchInput.value = "";
    renderClipperAssignList("");
    const list = document.getElementById("clipper-assign-list");
    const row = [...list.querySelectorAll(".clipper-assign-row")].find((r) => r.title === candidates[0].path);
    row?.click();
  } else if (candidates.length > 1) {
    const words = friendlyEventName(ev.eventName).toLowerCase().split(/[^a-z0-9]+/).filter((w) => w.length >= 3 && !AUTO_ASSIGN_STOPWORDS.has(w));
    searchInput.value = words[0] || "";
    renderClipperAssignList(searchInput.value);
  } else {
    searchInput.value = "";
    renderClipperAssignList("");
  }
}

/// Called instead of a bare closeClipperAssign() by the panel's Assign/Browse/Clear buttons
/// whenever the wizard is the one driving the panel, so choosing a song there advances to the
/// next event instead of just closing. Falls through to a normal close otherwise.
async function afterClipperAssignAction(trigger, assignedThisEvent, songName) {
  const wiz = _autoAssignWizard;
  if (wiz && !wiz.cancelled && wiz.queue[wiz.index]?.trigger === trigger) {
    const ev = wiz.queue[wiz.index];
    if (assignedThisEvent) wiz.assigned++; else wiz.skipped++;
    wiz.log.push({ eventName: friendlyEventName(ev.eventName), songName: assignedThisEvent ? (songName || null) : null, skipped: !assignedThisEvent });
    wiz.index++;
    closeClipperAssign();
    await advanceAutoAssignWizard();
  } else {
    showToast(assignedThisEvent ? `Assigned "${songName || "clip"}".` : "Assignment cleared.");
    flashPanel(document.getElementById("clipper-island"));
    closeClipperAssign();
  }
}

function finishAutoAssignWizard(cancelledEarly) {
  const wiz = _autoAssignWizard;
  document.getElementById("auto-assign-wizard-bar").hidden = true;
  document.getElementById("auto-assign-confirm-overlay").hidden = true;
  _autoAssignWizard = null;
  if (!wiz) return;
  refreshCategories();
  if (state.currentSituationsCategory) openSituations(state.currentSituationsCategory);
  showAutoAssignSummary(wiz, cancelledEarly);
}

/// Popup (not just a toast) confirming exactly what the guided wizard changed for `team` --
/// which event got which song, and which were skipped -- so it's clear at a glance instead of
/// having to re-open every event to check.
function showAutoAssignSummary(wiz, cancelledEarly) {
  const overlay = document.getElementById("auto-assign-summary-overlay");
  const title = document.getElementById("auto-assign-summary-title");
  const list = document.getElementById("auto-assign-summary-list");
  if (!overlay || !title || !list) {
    showToast(cancelledEarly
      ? `Guided Assign cancelled -- ${wiz.assigned} event${wiz.assigned === 1 ? "" : "s"} assigned before stopping.`
      : `Guided Assign complete for ${wiz.team}: ${wiz.assigned} assigned, ${wiz.skipped} skipped.`);
    return;
  }
  title.textContent = cancelledEarly
    ? `Guided Assign Cancelled -- ${wiz.team}`
    : `Guided Assign Complete -- ${wiz.team}`;
  list.innerHTML = "";
  if (!wiz.log.length) {
    list.innerHTML = `<div class="clipper-assign-row" style="cursor:default;">Nothing was changed.</div>`;
  } else {
    for (const entry of wiz.log) {
      const row = document.createElement("div");
      row.className = "auto-assign-summary-row" + (entry.skipped ? " skipped" : "");
      row.innerHTML = `
        <span class="auto-assign-summary-row-event">${entry.eventName}</span>
        <span class="auto-assign-summary-row-song">${entry.skipped ? "Skipped" : entry.songName}</span>`;
      list.appendChild(row);
    }
  }
  overlay.hidden = false;
}

async function runAutoAssignOverwrite(team) {
  const btn = document.getElementById("btn-auto-assign");
  btn.disabled = true;
  const prevLabel = btn.textContent;
  btn.textContent = "Assigning...";
  try {
    const filled = await bridge.ApplyDefaultProfileForTeamOverwrite(team);
    showToast(filled > 0
      ? `Auto-assigned ${filled} slot${filled === 1 ? "" : "s"} for ${team} from the default pack.`
      : `No default-pack songs found for ${team}.`);
    if (filled > 0 && !document.getElementById("situations-panel").hidden && state.currentSituationsCategory)
      await openSituations(state.currentSituationsCategory);
  } catch (err) {
    console.error("ApplyDefaultProfileForTeamOverwrite failed", err);
    showToast("Couldn't auto-assign -- try again.");
  } finally {
    btn.disabled = false;
    btn.textContent = prevLabel;
  }
}

function updateHeaderTeamBadge(team) {
  const badge = document.getElementById("header-team-badge");
  if (!badge) return;
  if (team) {
    fillTeamSwatch(badge, team);
    badge.title = `Editing ${team.name}'s sound profile -- click to switch (use Set Matchup for home/away)`;
  } else {
    badge.style.background = "rgba(255,255,255,0.08)";
    badge.textContent = "?";
    badge.title = "Click to pick a team";
  }
}

async function applyBackground(name) {
  const url = bridge ? await bridge.GetTeamBackgroundUrl(name) : null;
  const el = document.getElementById("backdrop");
  el.style.backgroundImage = url ? `url("${url}")` : "none";
}

// ---- Band Room viewer (Situations panel "Enter Band Room" pill) -----------------------------
// Fullscreen gallery of the active team's background images. Gathers the gallery from the
// team's Sound Bank image uploads (fetchUploadList("image", team)) plus whatever's currently
// live via GetTeamBackgroundUrl (in case that one isn't itself in the upload list, e.g. a
// bundled default-pack background). Two stacked layers (#bandroom-viewer-layer-a/-b) crossfade
// via opacity so prev/next actually animates instead of hard-cutting -- background-image itself
// isn't a real animatable CSS property despite some other spots in this app declaring a
// transition on it.
let _bvImages = [];
let _bvIndex = 0;
let _bvActiveLayer = "a";

async function openBandroomViewer() {
  const team = state.teams?.find((t) => t.name === state.activeTeam);
  if (!team) { showToast("Pick a team first."); return; }

  const nameEl = document.getElementById("bandroom-viewer-team-name");
  nameEl.textContent = team.name;
  applySchoolGlow(nameEl, team.name);

  const [items, activeUrl] = await Promise.all([
    fetchUploadList("image", team.name, null),
    bridge ? bridge.GetTeamBackgroundUrl(team.name) : null,
  ]);
  _bvImages = (items || []).map((i) => i.url).filter(Boolean);
  if (activeUrl && !_bvImages.includes(activeUrl)) _bvImages.unshift(activeUrl);
  if (!_bvImages.length) {
    showToast(`${team.name} has no backgrounds yet -- add one from the Sound Bank.`);
    return;
  }
  _bvIndex = Math.max(0, activeUrl ? _bvImages.indexOf(activeUrl) : 0);

  const overlay = document.getElementById("bandroom-viewer-overlay");
  overlay.hidden = false;
  requestAnimationFrame(() => overlay.classList.add("bandroom-viewer-visible"));
  setBandroomViewerImage(_bvImages[_bvIndex], true);
  updateBandroomViewerCounter();
}

function setBandroomViewerImage(url, first) {
  const layerA = document.getElementById("bandroom-viewer-layer-a");
  const layerB = document.getElementById("bandroom-viewer-layer-b");
  const showing = _bvActiveLayer === "a" ? layerA : layerB;
  const hidden = _bvActiveLayer === "a" ? layerB : layerA;
  hidden.style.backgroundImage = `url("${sanitizeHTML(url)}")`;
  if (first) {
    showing.style.backgroundImage = `url("${sanitizeHTML(url)}")`;
    showing.style.opacity = "1";
    hidden.style.opacity = "0";
    return;
  }
  void hidden.offsetWidth; // force reflow so the opacity change below actually transitions
  hidden.style.opacity = "1";
  showing.style.opacity = "0";
  _bvActiveLayer = _bvActiveLayer === "a" ? "b" : "a";
}

function shiftBandroomViewer(dir) {
  if (!_bvImages.length) return;
  _bvIndex = ((_bvIndex + dir) % _bvImages.length + _bvImages.length) % _bvImages.length;
  setBandroomViewerImage(_bvImages[_bvIndex]);
  updateBandroomViewerCounter();
}

function updateBandroomViewerCounter() {
  document.getElementById("bandroom-viewer-counter").textContent =
    _bvImages.length > 1 ? `${_bvIndex + 1} / ${_bvImages.length}` : "";
}

function closeBandroomViewer() {
  const overlay = document.getElementById("bandroom-viewer-overlay");
  overlay.classList.remove("bandroom-viewer-visible");
  setTimeout(() => { overlay.hidden = true; }, 300);
}

function setupBandroomViewer() {
  document.getElementById("btn-enter-bandroom-viewer").addEventListener("click", openBandroomViewer);
  document.getElementById("btn-close-bandroom-viewer").addEventListener("click", closeBandroomViewer);
  document.getElementById("btn-bandroom-viewer-prev").addEventListener("click", () => shiftBandroomViewer(-1));
  document.getElementById("btn-bandroom-viewer-next").addEventListener("click", () => shiftBandroomViewer(1));
  document.addEventListener("keydown", (e) => {
    const overlay = document.getElementById("bandroom-viewer-overlay");
    if (overlay.hidden) return;
    if (e.key === "Escape") closeBandroomViewer();
    else if (e.key === "ArrowLeft") shiftBandroomViewer(-1);
    else if (e.key === "ArrowRight") shiftBandroomViewer(1);
  });
}

async function refreshCategories() {
  if (!bridge) return;
  state.categories = JSON.parse(await bridge.GetCategories());
  renderCategories();
}

function setWatching(mode) {
  // Stop Watching is the one explicit "this game is over" signal (see WebMainForm._matchupLocked)
  // -- unlocks the matchup and swaps the VS backdrop back to normal for the next game.
  if (mode === "off" && state.matchupLocked) {
    state.matchupLocked = false;
    revertVsBackdrop();
    exitGameDayMode();
    closeBandroomViewer();
    updateMatchupLabel();
  }
  // Toast only on the WATCHING -> WAITING transition (the game window disappeared mid-session,
  // e.g. alt-tabbed out, game closed) -- not on every "waiting" state, which is also the normal
  // startup state before the game window is even found the first time and would be pure noise.
  if (state.watching === "watching" && mode === "waiting")
    showToast("Lost the game window -- Bandroom is waiting for it to come back.");
  state.watching = mode;
  const status = document.getElementById("watch-status");
  const label = document.getElementById("watch-label");
  const stopBtn = document.getElementById("btn-stop-watching");
  const wasLive = status.classList.contains("pill-watching") || status.classList.contains("pill-waiting");
  status.classList.remove("pill-off", "pill-waiting", "pill-watching", "watch-live-flash", "watch-live-glow");
  // "LIVE" covers both watching (window found) and waiting (locked in, window not found yet) --
  // once the matchup is locked, this pill stays lit until Stop Watching is pressed, it never
  // reverts to "Not watching" just because the game window blinked out mid-session (see the
  // toast a few lines up, same "waiting" transition).
  if (mode === "watching" || mode === "waiting") {
    status.classList.add(mode === "watching" ? "pill-watching" : "pill-waiting");
    label.textContent = "LIVE";
    // Flash twice on the OFF -> LIVE transition only, then settle into a steady glow -- re-adding
    // the flash class every tick (e.g. watching <-> waiting toggling) would restart the flash
    // instead of just holding the glow.
    if (!wasLive) {
      status.classList.add("watch-live-flash");
      status.addEventListener("animationend", function onFlashEnd(e) {
        if (e.animationName !== "watch-live-flash") return;
        status.removeEventListener("animationend", onFlashEnd);
        status.classList.remove("watch-live-flash");
        status.classList.add("watch-live-glow");
      });
    } else {
      status.classList.add("watch-live-glow");
    }
  } else {
    status.classList.add("pill-off");
    label.textContent = "Not watching";
  }
  if (stopBtn) stopBtn.hidden = mode === "off";
}

// ---- Track Info drawer (see AudioTrackMetadata.cs / WebBridge.GetTrackMetadata et al) ----
let _trackInfoTrigger = null;
// durationSeconds/integratedLufs(Approx)/truePeakDbtp aren't editable in the form -- kept here so
// Save doesn't silently drop whatever GetTrackMetadata/AnalyzeTrackMetadata last computed for
// this file.
let _trackInfoComputed = { durationSeconds: null, integratedLufs: null, truePeakDbtp: null, integratedLufsApprox: null };

function fillTrackInfoForm(meta) {
  _trackInfoComputed = {
    durationSeconds: meta?.durationSeconds ?? null,
    integratedLufs: meta?.integratedLufs ?? null,
    truePeakDbtp: meta?.truePeakDbtp ?? null,
    integratedLufsApprox: meta?.integratedLufsApprox ?? null,
  };
  document.getElementById("ti-title").value = meta?.standardTitle ?? "";
  document.getElementById("ti-artist").value = meta?.standardArtist ?? "";
  document.getElementById("ti-school").value = meta?.schoolAbbreviation ?? "";
  document.getElementById("ti-energy").value = meta?.energyLevel ?? "";
  document.getElementById("ti-instrumentation").value = meta?.prominentInstrumentation ?? "";
  document.getElementById("ti-trim").value = meta?.recommendedTrim ?? "";
  document.getElementById("ti-trigger-event").value = meta?.primaryGameTriggerEvent ?? "";
  document.getElementById("ti-category").value = meta?.marketplaceCategory ?? "";
  document.getElementById("ti-reverb-preset").value = meta?.recommendedReverbPreset ?? "";
  document.getElementById("ti-fingerprint").value = meta?.acousticFingerprint ?? "";
  document.getElementById("ti-duration").textContent = meta?.durationSeconds
    ? `Duration: ${meta.durationSeconds.toFixed(1)}s` : "";
  // Real K-weighted LUFS wins when present; fall back to the old RMS approximation for sidecars
  // written before LoudnessAnalyzer existed (see AudioTrackMetadata.IntegratedLufsApprox doc).
  document.getElementById("ti-lufs").textContent = meta?.integratedLufs != null
    ? `Loudness: ${meta.integratedLufs.toFixed(1)} LUFS`
    : meta?.integratedLufsApprox != null ? `Loudness (approx): ${meta.integratedLufsApprox.toFixed(1)} dBFS` : "";
  document.getElementById("ti-truepeak").textContent = meta?.truePeakDbtp != null
    ? `True Peak: ${meta.truePeakDbtp.toFixed(1)} dBTP` : "";
}

async function openTrackInfoDrawer(trigger, fileName) {
  _trackInfoTrigger = trigger;
  document.getElementById("track-info-overlay").hidden = false;
  document.getElementById("track-info-filename").textContent = fileName || "";
  let meta = null;
  try { meta = JSON.parse(await bridge.GetTrackMetadata(trigger)); } catch (err) { console.error("GetTrackMetadata failed", err); }
  document.getElementById("track-info-empty").hidden = !!meta;
  fillTrackInfoForm(meta);
}
document.getElementById("btn-close-track-info")?.addEventListener("click", () => {
  document.getElementById("track-info-overlay").hidden = true;
  _trackInfoTrigger = null;
});
document.getElementById("btn-track-info-suggest")?.addEventListener("click", async () => {
  if (!_trackInfoTrigger || !bridge) return;
  try {
    const result = JSON.parse(await bridge.AnalyzeTrackMetadata(_trackInfoTrigger));
    if (result.success) {
      document.getElementById("track-info-empty").hidden = true;
      fillTrackInfoForm(result.metadata);
    } else {
      showToast(result.error || "Couldn't analyze this file.");
    }
  } catch (err) { console.error("AnalyzeTrackMetadata failed", err); }
});
document.getElementById("btn-track-info-save")?.addEventListener("click", async () => {
  if (!_trackInfoTrigger || !bridge) return;
  const metadata = {
    standardTitle: document.getElementById("ti-title").value.trim() || null,
    standardArtist: document.getElementById("ti-artist").value.trim() || null,
    schoolAbbreviation: document.getElementById("ti-school").value.trim() || null,
    energyLevel: document.getElementById("ti-energy").value || null,
    prominentInstrumentation: document.getElementById("ti-instrumentation").value.trim() || null,
    recommendedTrim: document.getElementById("ti-trim").value.trim() || null,
    primaryGameTriggerEvent: document.getElementById("ti-trigger-event").value.trim() || null,
    marketplaceCategory: document.getElementById("ti-category").value.trim() || null,
    recommendedReverbPreset: document.getElementById("ti-reverb-preset").value || null,
    acousticFingerprint: document.getElementById("ti-fingerprint").value.trim() || null,
    durationSeconds: _trackInfoComputed.durationSeconds,
    integratedLufs: _trackInfoComputed.integratedLufs,
    truePeakDbtp: _trackInfoComputed.truePeakDbtp,
    integratedLufsApprox: _trackInfoComputed.integratedLufsApprox,
  };
  try {
    const result = JSON.parse(await bridge.SaveTrackMetadata(_trackInfoTrigger, JSON.stringify(metadata)));
    if (result.success) {
      showToast(result.fileName ? `Track info saved. Renamed to "${result.fileName}".` : "Track info saved.");
      document.getElementById("track-info-overlay").hidden = true;
    } else {
      showToast(result.error || "Couldn't save track info.");
    }
  } catch (err) { console.error("SaveTrackMetadata failed", err); }
});

// ---- Profile / Google sign-in (scaffolded -- see GoogleAuthService.ClientId for setup status) ----
async function openProfile() {
  document.getElementById("profile-overlay").hidden = false;
  await refreshProfileView();
}
function closeProfile() {
  document.getElementById("profile-overlay").hidden = true;
}

// Settings tab (merged into the themed Profile overlay -- replaces the old native
// SettingsForm.cs, opened via the header gear icon). See switchProfileTab/wireProfileSettingsTab.
let _profileSettingsLoaded = false;

function switchProfileTab(tab) {
  document.querySelectorAll(".profile-rail-tab").forEach((btn) => {
    const active = btn.dataset.profileTab === tab;
    btn.classList.toggle("active", active);
    btn.setAttribute("aria-selected", active ? "true" : "false");
  });
  document.querySelectorAll(".profile-tab-panel").forEach((panel) => {
    panel.hidden = panel.dataset.profilePanel !== tab;
  });
  if (tab === "settings" && !_profileSettingsLoaded) {
    _profileSettingsLoaded = true;
    refreshProfileSettingsTab();
  }
}

const REVERB_PRESET_OPTIONS = [
  { value: "off", label: "Off" },
  { value: "stadium", label: "Stadium" },
  { value: "nightgame", label: "Night Game" },
  { value: "nightgameprimetime", label: "Prime Time" },
];

async function refreshProfileSettingsTab() {
  if (!bridge) return;
  try {
    const [timingJson, volume, reverb, scorebugJson, alwaysOnTop] = await Promise.all([
      bridge.GetPlaybackTimingSettings(),
      bridge.GetVolume(),
      bridge.GetReverb(),
      bridge.GetScorebugPresets(),
      bridge.GetAlwaysOnTop(),
    ]);
    const timing = JSON.parse(timingJson);
    document.getElementById("settings-pre-roll").value = timing.PreRollSeconds;
    document.getElementById("settings-fade-start").value = timing.FadeStartSeconds;
    document.getElementById("settings-fade-duration").value = timing.FadeOutDuration;
    document.getElementById("settings-cooldown").value = timing.CooldownSeconds;

    document.getElementById("settings-volume").value = volume;
    document.getElementById("settings-volume-value").textContent = `${volume}%`;

    const reverbSelect = document.getElementById("settings-reverb");
    reverbSelect.innerHTML = "";
    for (const opt of REVERB_PRESET_OPTIONS) reverbSelect.appendChild(new Option(opt.label, opt.value, false, opt.value === reverb));

    const scorebug = JSON.parse(scorebugJson);
    const scorebugSelect = document.getElementById("settings-scorebug");
    scorebugSelect.innerHTML = "";
    for (const name of scorebug.names || []) scorebugSelect.appendChild(new Option(name, name, false, name === scorebug.active));

    document.getElementById("settings-always-on-top").checked = !!alwaysOnTop;
  } catch (err) { console.error("refreshProfileSettingsTab failed", err); }
}

function wireProfileSettingsTab() {
  document.querySelectorAll(".profile-rail-tab").forEach((btn) => {
    btn.addEventListener("click", () => switchProfileTab(btn.dataset.profileTab));
  });
  document.getElementById("btn-close-profile-settings").addEventListener("click", closeProfile);

  document.getElementById("btn-apply-timing-settings").addEventListener("click", async () => {
    const settings = {
      PreRollSeconds: Number(document.getElementById("settings-pre-roll").value) || 0,
      FadeStartSeconds: Number(document.getElementById("settings-fade-start").value) || 0,
      FadeOutDuration: Number(document.getElementById("settings-fade-duration").value) || 0,
      CooldownSeconds: Number(document.getElementById("settings-cooldown").value) || 0,
    };
    try {
      await bridge.SavePlaybackTimingSettings(JSON.stringify(settings));
      showToast("Timing settings applied");
    } catch (err) { console.error("SavePlaybackTimingSettings failed", err); }
  });

  document.getElementById("settings-volume").addEventListener("input", (e) => {
    bridge?.SetVolume(Number(e.target.value));
    document.getElementById("settings-volume-value").textContent = `${e.target.value}%`;
  });
  document.getElementById("settings-reverb").addEventListener("change", (e) => bridge?.SetReverb(e.target.value));
  document.getElementById("settings-scorebug").addEventListener("change", (e) => bridge?.SetScorebugPreset(e.target.value));
  document.getElementById("settings-always-on-top").addEventListener("change", (e) => bridge?.SetAlwaysOnTop(e.target.checked));

  document.getElementById("btn-settings-stop-playback").addEventListener("click", () => bridge?.StopPlayback());
  document.getElementById("btn-settings-open-songs-folder").addEventListener("click", () => bridge?.OpenSongsFolder());
  document.getElementById("btn-settings-clear-all").addEventListener("click", () => {
    if (confirm("Clear every saved song assignment for every team? This can't be undone.")) bridge?.ClearAllAssignments();
  });
  document.getElementById("btn-settings-reset-team").addEventListener("click", () => {
    if (confirm("Clear every saved assignment for the currently selected team? This can't be undone.")) bridge?.ResetTeamProfile();
  });
}
async function refreshProfileView() {
  if (!bridge) return;
  let user;
  try {
    user = JSON.parse(await bridge.GetCurrentUser());
  } catch (err) {
    console.error("GetCurrentUser failed", err);
    user = { signedIn: false };
  }
  document.getElementById("profile-signed-out").hidden = user.signedIn;
  document.getElementById("profile-signed-in").hidden = !user.signedIn;
  document.getElementById("profile-name").textContent = user.signedIn ? (user.name ?? "") : "Not signed in";
  document.getElementById("profile-email").textContent = user.signedIn ? (user.email ?? "") : "";
  const memberSince = document.getElementById("profile-member-since");
  memberSince.textContent = user.signedIn && user.signedInAt
    ? `Signed in on this device since ${new Date(user.signedInAt).toLocaleDateString()}` : "";
  // Google's picture is only used as a fallback -- a local custom avatar (works signed-out too,
  // see UploadAvatar) always wins if one's been set.
  state.googleAvatarUrl = user.signedIn ? (user.picture ?? null) : null;
  await refreshUniversalProfileView();
}

function populateTeamSelect(select, includeBlank) {
  if (select.options.length !== state.teams.length + (includeBlank ? 1 : 0)) {
    select.innerHTML = "";
    if (includeBlank) {
      const blank = document.createElement("option");
      blank.value = "";
      blank.textContent = "Choose a team...";
      select.appendChild(blank);
    }
    for (const team of state.teams) {
      const opt = document.createElement("option");
      opt.value = team.name;
      opt.textContent = team.name;
      select.appendChild(opt);
    }
  }
}

function updateFavoriteTeamJumpButton(favoriteTeam) {
  const btn = document.getElementById("btn-jump-favorite-team");
  btn.hidden = !favoriteTeam;
  btn.dataset.team = favoriteTeam ?? "";
}

// ---- Universal profile: favorite team + lifetime stats (works fully signed-out; syncs to the
// cloud automatically once signed in -- see WebBridge.GetUserProfile/SetFavoriteTeam). ----
async function refreshUniversalProfileView() {
  // Rebuild whenever the option count doesn't match state.teams (+1 for the blank placeholder on
  // favorite) -- NOT just "if empty". state.teams starts as [] and fills in asynchronously after
  // GetTeams() resolves; if the profile overlay is opened before that finishes, a naive
  // "only populate once" guard would permanently lock the dropdown empty for the whole session,
  // since it'd never get a second chance to see the real list.
  populateTeamSelect(document.getElementById("profile-rival-team"), true);

  let profile;
  try {
    profile = JSON.parse(await bridge.GetUserProfile());
  } catch (err) {
    console.error("GetUserProfile failed", err);
    return;
  }

  state.toastsEnabled = profile.toastsEnabled !== false;
  updateFavoriteTeamJumpButton(profile.favoriteTeam);

  document.getElementById("profile-favorite-team-label").textContent = profile.favoriteTeam || "None selected";
  document.getElementById("profile-rival-team").value = profile.rivalTeam ?? "";
  document.getElementById("profile-bio-input").value = profile.bio ?? "";
  document.getElementById("profile-toasts-toggle").checked = state.toastsEnabled;

  document.getElementById("profile-stat-games").textContent = profile.gamesWatched ?? 0;
  document.getElementById("profile-stat-songs").textContent = profile.songsTriggered ?? 0;
  document.getElementById("profile-stat-uploads").textContent = profile.marketplaceUploads ?? 0;
  document.getElementById("profile-stat-downloads").textContent = profile.marketplaceDownloads ?? 0;
  document.getElementById("profile-stat-streak").textContent = profile.streakCurrentDays ?? 0;
  document.getElementById("profile-level-num").textContent = profile.level ?? 1;

  const mostTriggered = document.getElementById("profile-most-triggered");
  mostTriggered.textContent = profile.mostTriggeredEvent
    ? `Most-triggered event: ${profile.mostTriggeredEvent} (${profile.mostTriggeredCount}x)` : "";

  document.getElementById("profile-record-text").textContent =
    `${profile.favoriteTeamWins ?? 0}-${profile.favoriteTeamLosses ?? 0}`;

  // Avatar: local custom upload wins over the Google picture, which wins over showing nothing
  // (falls back to the header's team-badge look via CSS when hidden).
  const avatarImg = document.getElementById("profile-avatar");
  const avatarUrl = profile.avatarUrl ?? state.googleAvatarUrl;
  if (avatarUrl) { avatarImg.src = avatarUrl; avatarImg.hidden = false; } else { avatarImg.hidden = true; }

  renderProfileAchievements(profile.achievements ?? []);
  renderProfileByTeamList(profile.gamesWatchedByTeam ?? {});
  await renderProfileMyUploads();
  await renderProfileActivityFeed();

  state.profileGoogleUserId = profile.googleUserId ?? null;
  const publicToggle = document.getElementById("profile-public-toggle");
  publicToggle.checked = !!profile.isPublicProfile;
  document.getElementById("profile-share-actions").hidden = !profile.isPublicProfile;
  document.getElementById("profile-share-panel").hidden = true;
  loadPlayersLeaderboard();
}

// ---- Public Profile: opt-in toggle + share link/QR ------------------------------------
// Distinct from #btn-share-profile (a per-TEAM song-assignment share, see shareCurrentProfile
// above) -- this shares the player's own identity/stats page, gated behind Google sign-in since
// the public URL is keyed by the stable GoogleUserId (see WebBridge.TogglePublicProfile).
async function onProfilePublicToggleChanged(e) {
  const checked = e.target.checked;
  let result;
  try {
    result = JSON.parse(await bridge.TogglePublicProfile(checked));
  } catch (err) {
    console.error("TogglePublicProfile failed", err);
    result = { ok: false, error: "Couldn't reach the server -- try again." };
  }
  if (!result.ok) {
    e.target.checked = !checked; // revert the checkbox
    showToast(result.error || "Couldn't update your public profile setting.");
    return;
  }
  document.getElementById("profile-share-actions").hidden = !result.isPublicProfile;
  if (!result.isPublicProfile) document.getElementById("profile-share-panel").hidden = true;
  showToast(result.isPublicProfile ? "Your profile is now public." : "Your profile is now private.");
}

function shareMyProfile() {
  if (!state.profileGoogleUserId) {
    showToast("Sign in with Google first.");
    return;
  }
  const url = `https://bandroom.app/profile/${encodeURIComponent(state.profileGoogleUserId)}`;
  document.getElementById("profile-share-link").textContent = url;
  document.getElementById("profile-share-panel").hidden = false;
  generateQRCode(url, "profile-share-qr");
}

async function openPublicProfile(sub) {
  const overlay = document.getElementById("public-profile-overlay");
  const notFound = document.getElementById("public-profile-notfound");
  const statsGrid = document.getElementById("public-profile-stats-grid");
  overlay.hidden = false;
  notFound.hidden = true;
  statsGrid.hidden = false;
  try {
    const res = await fetch(`${MARKETPLACE_URL}/profile/${encodeURIComponent(sub)}`);
    if (!res.ok) throw new Error(`not found: ${res.status}`);
    const p = await res.json();

    document.getElementById("public-profile-name").textContent = p.name || "Bandroom Player";
    document.getElementById("public-profile-team").textContent = p.favoriteTeam || "No favorite team set";
    const avatar = document.getElementById("public-profile-avatar");
    if (p.picture) { avatar.src = p.picture; avatar.hidden = false; } else { avatar.hidden = true; }
    document.getElementById("public-profile-stat-games").textContent = p.stats?.gamesWatched ?? 0;
    document.getElementById("public-profile-stat-songs").textContent = p.stats?.songsTriggered ?? 0;
    document.getElementById("public-profile-stat-streak").textContent = p.stats?.streakCurrentDays ?? 0;
  } catch (err) {
    console.error("openPublicProfile failed", err);
    statsGrid.hidden = true;
    notFound.hidden = false;
  }
}

async function loadPlayersLeaderboard() {
  const container = document.getElementById("profile-leaderboard-table");
  if (!container) return;
  try {
    const res = await fetch(`${MARKETPLACE_URL}/leaderboard/users?metric=games&limit=10`);
    const data = res.ok ? await res.json() : { entries: [] };
    renderLeaderboardTable(container, data.entries ?? [], "users");
  } catch (err) {
    console.error("loadPlayersLeaderboard failed", err);
    renderLeaderboardTable(container, [], "users");
  }
}

// Real activity feed -- reuses the same EventActivityLog buffer that powers the Help & Guide
// Event Log tab (see WebBridge.GetEventActivityLog), shown newest-first, capped to the most
// recent 15 entries so the profile panel doesn't grow unbounded.
async function renderProfileActivityFeed() {
  const el = document.getElementById("profile-activity-feed");
  if (!el || !bridge) return;
  let entries;
  try {
    entries = JSON.parse(await bridge.GetEventActivityLog());
  } catch (err) {
    console.error("GetEventActivityLog failed", err);
    return;
  }
  const recent = entries.slice(-15).reverse();
  el.innerHTML = recent.length
    ? recent.map((e) => {
        const [time, ...rest] = e.text.split(" -- ");
        return `<div class="profile-activity-item"><span class="profile-activity-time">${time}</span><span>${rest.join(" -- ")}</span></div>`;
      }).join("")
    : `<div class="profile-activity-item">No activity yet -- fire a cue to see it here.</div>`;
}

function renderProfileAchievements(achievements) {
  const el = document.getElementById("profile-achievements");
  el.innerHTML = "";
  for (const a of achievements) {
    const badge = document.createElement("span");
    badge.className = "achievement-badge" + (a.unlocked ? " unlocked" : "");
    badge.textContent = a.label;
    badge.title = a.unlocked ? "Unlocked!" : "Not yet unlocked";
    el.appendChild(badge);
  }
}

function renderProfileByTeamList(byTeam) {
  const el = document.getElementById("profile-by-team-list");
  const top5 = Object.entries(byTeam).sort((a, b) => b[1] - a[1]).slice(0, 5);
  el.innerHTML = top5.length
    ? top5.map(([team, count]) => `<div class="profile-by-team-row"><span>${team}</span><span>${count}</span></div>`).join("")
    : `<div class="profile-by-team-row-empty">No games watched yet.</div>`;
}

// "My Uploads" + "Likes Received" + "Top Uploader" all derive from data already on hand
// client-side (the local ownerToken tracking used for delete eligibility, plus the same /list
// and /leaderboard calls the marketplace tabs already make) -- no new server endpoint needed.
// Known limitation: uploads aren't tied to a signed-in account server-side yet (see worker.js),
// so this only ever reflects uploads made from THIS device/browser profile, not "everything this
// Google account has ever uploaded anywhere".
// Reopening Profile fast enough to fire a second renderProfileMyUploads before the first's
// fetches resolve could otherwise let the OLDER call's slower response land last and overwrite
// the correct render with stale data -- this token makes every call check "am I still the most
// recent call" before touching the DOM.
let _profileMyUploadsRenderToken = 0;

async function renderProfileMyUploads() {
  const myToken = ++_profileMyUploadsRenderToken;
  const mine = loadMyUploads();
  const ids = Object.keys(mine);
  const listEl = document.getElementById("profile-my-uploads-list");
  const likesEl = document.getElementById("profile-stat-likes");
  const badgeEl = document.getElementById("profile-top-uploader-badge");

  if (ids.length === 0) {
    listEl.innerHTML = `<div class="profile-by-team-row-empty">No uploads from this device yet.</div>`;
    likesEl.textContent = "0";
    badgeEl.hidden = true;
    return;
  }

  try {
    const [songs, images, songBoard, imageBoard] = await Promise.all([
      fetchUploadList("song"), fetchUploadList("image"),
      fetch(`${MARKETPLACE_URL}/leaderboard?type=song`).then((r) => (r.ok ? r.json() : { schools: [] })),
      fetch(`${MARKETPLACE_URL}/leaderboard?type=image`).then((r) => (r.ok ? r.json() : { schools: [] })),
    ]);
    if (myToken !== _profileMyUploadsRenderToken) return; // a newer call already started -- don't clobber it
    const allItems = [...songs, ...images];
    const myItems = allItems.filter((item) => ids.includes(item.id));

    listEl.innerHTML = myItems.length
      ? myItems.map((item) => `<div class="profile-by-team-row"><span>${item.school} — ${item.name}</span><span>${item.likes ?? 0}♥</span></div>`).join("")
      : `<div class="profile-by-team-row-empty">Uploads from this device aren't showing yet -- they may still be indexing.</div>`;

    const totalLikes = myItems.reduce((sum, item) => sum + (item.likes ?? 0), 0);
    likesEl.textContent = totalLikes;

    const mySchools = new Set(myItems.map((item) => item.school));
    const allBoard = [...songBoard.schools, ...imageBoard.schools];
    const maxCount = allBoard.length ? Math.max(...allBoard.map((s) => s.count)) : 0;
    const topSchool = maxCount > 0 ? allBoard.find((s) => mySchools.has(s.school) && s.count === maxCount) : null;
    if (topSchool) {
      badgeEl.hidden = false;
      badgeEl.querySelector("span").textContent = topSchool.school;
    } else {
      badgeEl.hidden = true;
    }
  } catch (err) {
    if (myToken !== _profileMyUploadsRenderToken) return;
    console.error("renderProfileMyUploads failed", err);
    listEl.innerHTML = `<div class="profile-by-team-row-empty">Couldn't load upload details right now.</div>`;
  }
}

// ---- Help & Guide dashboard (task queue item 2, Session 11) --------------------------------
// ~40 real, verified tips (deliberately NOT reusing TIPS_DATABASE further below, which mixes in
// features that don't exist in Bandroom -- "Dynasty mode", "Recruiting tracker", "Bowl
// projections", etc -- this list only covers things actually in this codebase) plus a full
// ELI7 install/feature/FAQ guide, opened from the new sidebar Help pill.
const HELP_TIPS = [
  "New: the 50 most popular FCS schools now ship built-in, right alongside every FBS team -- check the Team picker.",
  "Press Ctrl+K anytime to open the command palette and jump straight to any screen.",
  "Click a team's tile in the Team panel to make it the active team -- its color becomes the whole app's glow color.",
  "Set Matchup lets you pick a Home and Away team so Bandroom can auto-switch which team's songs play.",
  "The little star button in the header jumps straight to your favorite team.",
  "You can search for a team by typing in any team-picker search box instead of scrolling.",
  "The Bandroom marketplace lets you download songs and background images other users have shared.",
  "You can upload your own songs to a team's Sound Bank from that team's album view.",
  "Every upload gets a Like and a Dislike button -- your feedback helps good uploads rise to the top.",
  "The Popular Songs shelf in the marketplace hub is ranked by downloads + likes combined.",
  "The Top Team Background Uploads shelf shows real backgrounds from the default pack.",
  "Downloaded songs and images show up in My Downloads, not directly in your Songs library.",
  "You can Share Profile to send your whole team's song setup to other Bandroom users.",
  "Load Profile from Others applies someone else's shared song setup, matched by filename to your own library.",
  "The Assign / Edit button on any situation opens the Clipper -- Bandroom's built-in song picker.",
  "The Clipper's song list is grouped by where each file came from: Marketplace Downloads, Trimmed Clips, Your Imports, and Imported Files.",
  "Use the team sidebar inside the Clipper to filter the song list down to one team's songs.",
  "Trim lets you cut a song down to just the part you want, and normalizes the volume automatically.",
  "The Default Song Pack is a one-time optional download that fills every team with real songs.",
  "Importing the Default Song Pack never overwrites a song you already picked yourself.",
  "You can move the Default Song Pack to a different folder or drive from the command palette.",
  "Team Backgrounds are the big photo behind the whole app -- pick one per team.",
  "You can set a custom background for any team from the Sound Bank or from My Downloads.",
  "Set Matchup uses a cover-flow carousel, just like picking your favorite team -- swipe or click the arrows.",
  "Your Favorite Team is set from your Profile, using the same cover-flow picker as Set Matchup.",
  "PA Announcer clips play alongside your regular songs to make big moments feel like a real broadcast.",
  "The Lead-In Whistle plays a short whistle sound right before some songs kick in.",
  "You can turn the Lead-In Whistle on or off without needing to delete the clip itself.",
  "The header's Bandroom title glows in your active team's main color.",
  "If a team's main color is too close to black, Bandroom automatically uses their second color instead so it's still visible.",
  "The bottom clipper island is where you preview and control whatever song is currently playing.",
  "You can Export your team's whole profile to a file and Import it back later, or on another PC.",
  "Apply to All Teams copies your current team's song setup to every other team at once.",
  "Streamer Mode hides your personal info so it's safe to have on screen while broadcasting.",
  "The Discord panel lets you chat without leaving Bandroom.",
  "Command Palette entries like Reset Team Profile are there for one-off actions you don't need a button for all the time.",
  "You can preview any marketplace song before deciding to download it.",
  "The Profile dashboard tracks stats like games watched, songs triggered, and your win/loss record.",
  "Achievements unlock automatically as you use Bandroom more -- no need to do anything extra.",
  "The Settings panel (gear icon) has audio timing controls like fade-out and re-fire cooldown.",
  "You can pick which Scorebug preset Bandroom watches for, to match how your game is set up on screen.",
];

let _helpGuideRendered = false;
function initHelpGuide() {
  const overlay = document.getElementById("help-guide-overlay");
  const pill = document.getElementById("btn-help-pill");
  if (!overlay || !pill) return;

  pill.addEventListener("click", () => {
    overlay.hidden = false;
    if (!_helpGuideRendered) {
      _helpGuideRendered = true;
      renderHelpTips();
      document.getElementById("help-guide-full").innerHTML = HELP_GUIDE_HTML;
    }
  });
  document.getElementById("btn-close-help-guide").addEventListener("click", () => {
    overlay.hidden = true;
    stopEventLogPolling();
  });

  document.querySelectorAll(".help-guide-tab").forEach((tab) => {
    tab.addEventListener("click", () => {
      document.querySelectorAll(".help-guide-tab").forEach((t) => t.classList.remove("active"));
      tab.classList.add("active");
      document.getElementById("help-guide-tips").hidden = tab.dataset.tab !== "tips";
      document.getElementById("help-guide-full").hidden = tab.dataset.tab !== "guide";
      document.getElementById("help-guide-eventlog").hidden = tab.dataset.tab !== "eventlog";
      if (tab.dataset.tab === "eventlog") startEventLogPolling();
      else stopEventLogPolling();
    });
  });

  const shareGuideBtn = document.getElementById("btn-share-guide");
  if (shareGuideBtn) shareGuideBtn.addEventListener("click", openProfileShareGuide);

  const exportLogBtn = document.getElementById("btn-export-event-log");
  if (exportLogBtn) exportLogBtn.addEventListener("click", exportEventActivityLog);

  const logSearchInput = document.getElementById("event-log-search");
  if (logSearchInput) logSearchInput.addEventListener("input", renderEventLogList);

  const simpleToggle = document.getElementById("event-log-simple-toggle");
  if (simpleToggle) simpleToggle.addEventListener("change", renderEventLogList);
}

// Reduces a full log line down to just "Event (Team): song.mp3" -- every real fire's message is
// built by RecordFireResult (WebMainForm.cs) as "<time> -- <Event> (<Team>) -- played '<file>'",
// so this is a pure string trim, not new data. Returns null for anything that isn't an actual
// fire (skipped/blocked/duplicate/near-miss entries all lack " -- played '"), which is what lets
// the simple-mode filter below drop them instead of just reformatting them.
function simplifyLogLine(text) {
  const m = /^.*? -- (.+?) -- played '(.+)'$/.exec(text || "");
  return m ? `${m[1]}: ${m[2]}` : null;
}

// Live "why didn't my song play" feed inside Help & Guide's Event Log tab. Polls only while
// that tab is actually visible -- started when the tab is clicked, stopped when the overlay
// closes or another tab takes over, so this never leaves an interval ticking in the background.
let _eventLogPollHandle = null;
function startEventLogPolling() {
  stopEventLogPolling();
  refreshEventActivityLog();
  _eventLogPollHandle = setInterval(refreshEventActivityLog, 2000);
}
function stopEventLogPolling() {
  if (_eventLogPollHandle) {
    clearInterval(_eventLogPollHandle);
    _eventLogPollHandle = null;
  }
}
// Punch-list item 7: search/filter box above the Event Log list. The full buffer is already
// fetched every 2s poll -- _eventLogEntries holds the latest raw fetch so the search box can
// re-filter instantly on every keystroke without waiting on the next poll, and so a poll landing
// mid-search still respects whatever's currently typed.
let _eventLogEntries = [];
function renderEventLogList() {
  const list = document.getElementById("event-log-list");
  if (!list) return;
  const query = (document.getElementById("event-log-search")?.value || "").trim().toLowerCase();
  const simpleMode = document.getElementById("event-log-simple-toggle")?.checked ?? false;

  let rows = _eventLogEntries.map((e) => ({ raw: e.text, simple: simplifyLogLine(e.text) }));
  if (simpleMode) rows = rows.filter((r) => r.simple !== null);
  if (query) rows = rows.filter((r) => (r.raw || "").toLowerCase().includes(query));

  if (_eventLogEntries.length === 0) {
    list.innerHTML = `<div class="event-log-empty">Nothing logged yet -- this fills in as Bandroom plays or skips cues during a game.</div>`;
    return;
  }
  if (rows.length === 0) {
    list.innerHTML = query
      ? `<div class="event-log-empty">No log entries match "${sanitizeHTML(query)}".</div>`
      : `<div class="event-log-empty">Nothing's actually played yet -- uncheck "Only show what actually played" to see skipped/blocked events too.</div>`;
    return;
  }
  // Newest first, so the most recent "why didn't that play" answer is right at the top.
  list.innerHTML = rows.slice().reverse().map((r) =>
    `<div class="event-log-row">${sanitizeHTML(simpleMode ? r.simple : r.raw)}</div>`
  ).join("");
}
async function refreshEventActivityLog() {
  const list = document.getElementById("event-log-list");
  if (!list || !bridge) return;
  try {
    _eventLogEntries = JSON.parse(await bridge.GetEventActivityLog()) || [];
  } catch (err) {
    console.error("GetEventActivityLog failed", err);
    return;
  }
  renderEventLogList();
}
async function exportEventActivityLog() {
  if (!bridge) return;
  try {
    const path = await bridge.ExportEventActivityLog();
    if (path) showToast(`Saved to: ${path}`);
    else showToast("Couldn't save the log file -- try again.");
  } catch (err) {
    console.error("ExportEventActivityLog failed", err);
    showToast("Couldn't save the log file -- try again.");
  }
}

// Opens the Help & Guide overlay straight to the "Share Profile / Load Profile from Others"
// section (rather than making the user hunt through the Full Guide tab) -- the pill lives right
// next to Save since that's where users go looking for "how do I share my setup with someone".
function openProfileShareGuide() {
  const overlay = document.getElementById("help-guide-overlay");
  if (!overlay) return;
  overlay.hidden = false;
  if (!_helpGuideRendered) {
    _helpGuideRendered = true;
    renderHelpTips();
    document.getElementById("help-guide-full").innerHTML = HELP_GUIDE_HTML;
  }
  document.querySelectorAll(".help-guide-tab").forEach((t) => t.classList.remove("active"));
  document.querySelector('.help-guide-tab[data-tab="guide"]')?.classList.add("active");
  document.getElementById("help-guide-tips").hidden = true;
  document.getElementById("help-guide-full").hidden = false;
  document.getElementById("help-guide-eventlog").hidden = true;
  stopEventLogPolling();
  requestAnimationFrame(() => {
    document.getElementById("help-section-profile-sharing")?.scrollIntoView({ block: "start" });
  });
}

function renderHelpTips() {
  const el = document.getElementById("help-guide-tips");
  el.innerHTML = HELP_TIPS.map((tip, i) =>
    `<div class="help-tip-row"><span class="help-tip-num">${i + 1}.</span><span>${sanitizeHTML(tip)}</span></div>`
  ).join("");
}

// Written so a 7-year-old could follow it: short sentences, explain WHY not just WHAT, no jargon
// without immediately explaining it. Covers every real feature verified as of Session 11 --
// marketplace, clipper/assign, default song pack + relocation, team backgrounds, matchup/
// coverflow picking, PA announcer, lead-in whistle, and a FAQ section for common confusion
// points. Static HTML string (not built from smaller pieces) so it's easy to read/edit as one
// document -- update this whenever a real feature changes, it's the definitive "how Bandroom
// works" reference the owner asked for.
const HELP_GUIDE_HTML = `
<div class="help-guide-section">
  <h3>What is Bandroom?</h3>
  <p>Bandroom listens to your college football game and plays songs and sounds automatically,
  like a real band would. When your team scores a touchdown, Bandroom notices and plays your
  touchdown song. You don't have to click anything during the game -- Bandroom does it for you.</p>
</div>
<div class="help-guide-section">
  <h3>Getting started (first launch)</h3>
  <ul>
    <li>The very first time you open Bandroom, it asks you to pick your favorite team. You'll see
    team logos slide by like a carousel -- click the arrows or click a logo to move it to the
    middle, then press <strong>Confirm Team</strong>.</li>
    <li>Right after that, Bandroom points out <strong>The Bandroom</strong> button -- that's the
    community marketplace where other people's songs and pictures live.</li>
    <li>You don't need to do anything else to start playing -- Bandroom already has some default
    songs it can use, and you can download a much bigger free pack (see "Default Song Pack" below).</li>
  </ul>
</div>
<div class="help-guide-section">
  <h3>Picking teams and setting a matchup</h3>
  <ul>
    <li><strong>Active team</strong>: click any team in the left-side Team panel. Whichever team
    you click becomes the "active" team, and the whole app changes color to match that team.</li>
    <li><strong>Set Matchup</strong>: use this before you start watching a game. It lets you pick a
    Home team and an Away team. Once both are picked, Bandroom automatically knows which team's
    songs to play depending on who has the ball.</li>
    <li>Both the favorite-team picker and Set Matchup use the same sliding carousel of team logos
    -- it's the same picker everywhere, so once you've learned it once, you know it everywhere.</li>
  </ul>
</div>
<div class="help-guide-section">
  <h3>Don't see your team? Add it yourself (TeamBuilder)</h3>
  <p>If your school isn't in the list, you can add it in about 30 seconds:</p>
  <ol>
    <li>Open the full Team picker and click <strong>Add School</strong>.</li>
    <li>Type your school's name.</li>
    <li>Pick a <strong>primary color</strong> and a <strong>secondary color</strong> -- these are
    just the two main colors of your team, so Bandroom can color the app to match, like it does
    for every other school.</li>
    <li>Click the button to save it. Your new school shows up right away in the Team picker, no
    restart needed.</li>
    <li>Last step -- give it a logo: find your new school's tile in the Team picker and click the
    small pencil icon on it. That opens the logo tool where you can upload and crop a picture to
    use as its logo.</li>
  </ol>
  <p><strong>Good to know:</strong> a school you add this way is just a name, colors, and a logo
  -- it won't automatically detect scores or plays for a game engine that doesn't know that school
  (no real-world team roster data). You still assign its songs the normal way, and it works with
  Set Matchup and everything else just like any other team.</p>
</div>
<div class="help-guide-section" id="help-section-profile-sharing">
  <h3>The Sound Bank and The Bandroom marketplace</h3>
  <p>Every team has its own <strong>Sound Bank</strong> -- a folder of that team's songs and
  background pictures. <strong>The Bandroom</strong> is the shared marketplace where every
  Bandroom user in the world can upload and download from each other's Sound Banks.</p>
  <ul>
    <li><strong>Downloading</strong>: open a team's Sound Bank, find a song or picture you like,
    and press the download button. It gets saved to <strong>My Downloads</strong> on your
    computer -- it doesn't automatically become one of your assigned songs, you still have to
    assign it (see "Assigning songs" below).</li>
    <li><strong>Uploading</strong>: open a team's Sound Bank and press "+ Upload". Pick a song or
    picture from your computer, give it a clear name (like "UGA 3rd Down Stop" -- team name +
    what situation it's for), and Bandroom uploads it for everyone to use. Songs get automatically
    trimmed and evened out in volume so they sound consistent with everyone else's uploads.</li>
    <li><strong>Like / Dislike</strong>: every upload has a heart (like) and a thumbs-down
    (dislike) button. This is just feedback -- it doesn't delete anything, it just helps good
    uploads stand out.</li>
    <li><strong>Popular Songs shelf</strong>: on the marketplace's front page, songs are ranked by
    how many downloads and likes they've gotten, so the best stuff floats to the top.</li>
    <li><strong>Share Profile / Load Profile from Others</strong>: this is different from
    uploading a single song -- it shares your WHOLE team's setup (which song plays for which
    situation) so someone else can copy it in one click, instead of assigning 30+ songs by hand.</li>
  </ul>
</div>
<div class="help-guide-section">
  <h3>Assigning songs (the Clipper)</h3>
  <p>Every game situation (like "Touchdown" or "3rd Down Stop") needs a song assigned to it before
  Bandroom can play it. Click <strong>Assign / Edit</strong> on any situation to open the
  <strong>Clipper</strong> -- Bandroom's built-in song picker.</p>
  <ul>
    <li>The song list is grouped into sections so you can tell where each song came from:
    <strong>Marketplace Downloads</strong> (songs you got from other users), <strong>Trimmed
    Clips</strong> (songs you cut down yourself), <strong>Your Imports</strong> (songs from the
    "import my own song" flow), and <strong>Imported Files</strong> (anything you dragged in or
    browsed for directly).</li>
    <li>Use the <strong>team sidebar</strong> next to the song list to narrow the list down to one
    team's songs, instead of scrolling through everything.</li>
    <li>Click a song, then press <strong>Assign Selected</strong> to lock it in for that
    situation.</li>
    <li><strong>Trim</strong> opens a tool that lets you cut a long song down to just the exciting
    part, and automatically makes the volume consistent so nothing is way louder or quieter than
    everything else.</li>
  </ul>
</div>
<div class="help-guide-section">
  <h3>Default Song Pack</h3>
  <p>Instead of finding and assigning every single song yourself, Bandroom offers a big,
  free, one-time download called the <strong>Default Song Pack</strong> -- thousands of songs
  already sorted by team and situation. When you import it, Bandroom automatically fills in any
  situation you HAVEN'T already picked a song for -- it will never replace a song you chose
  yourself.</p>
  <ul>
    <li>Because the pack is huge (a few gigabytes), you download it from a link that opens in your
    web browser, then come back to Bandroom and press <strong>Locate &amp; Import</strong> to
    point Bandroom at the file you downloaded.</li>
    <li>You can see exactly where the pack is saved on your computer, or move it to a different
    folder or drive (handy if your main drive is small), from the command palette (press Ctrl+K
    and search "song pack").</li>
  </ul>
</div>
<div class="help-guide-section">
  <h3>Team Backgrounds</h3>
  <p>The big picture behind the whole app is called a <strong>Team Background</strong>. Every team
  can have its own. You can pick one from a team's Sound Bank, download one someone else uploaded
  and set it from My Downloads, or upload your own custom picture.</p>
</div>
<div class="help-guide-section">
  <h3>PA Announcer and Lead-In Whistle</h3>
  <ul>
    <li><strong>PA Announcer</strong> clips are short voice clips (like a real stadium announcer)
    that can play alongside your regular songs, to make big plays feel like a real broadcast.</li>
    <li><strong>Lead-In Whistle</strong> is a short referee-whistle sound that can play right
    before certain songs start, like the real whistle that starts a play. You can turn it on or
    off, or replace the whistle clip with your own sound.</li>
  </ul>
</div>
<div class="help-guide-section">
  <h3>Settings</h3>
  <p>Click the gear icon to open Settings. This is where you control things that apply to the
  whole app, not just one team: how loud everything plays, how much time passes before a song
  fades out, reverb (makes songs sound like they're echoing in a real stadium), and which
  <strong>Scorebug preset</strong> Bandroom should look for on your screen (this tells Bandroom
  where the score/clock/down-and-distance numbers are, so it can read them correctly).</p>
</div>
<div class="help-guide-section">
  <h3>Frequently Asked Questions</h3>
  <ul>
    <li><strong>Nothing plays when I score -- why?</strong> Make sure that situation actually has
    a song assigned (open the Clipper and check), and that you've set a matchup so Bandroom knows
    which team is which.</li>
    <li><strong>Why don't I see a song I just uploaded?</strong> Uploads sometimes take a few
    seconds to show up everywhere after uploading -- try refreshing the list. It's already
    uploaded successfully, it just takes a moment to spread everywhere.</li>
    <li><strong>Did downloading the Default Song Pack overwrite my own songs?</strong> No --
    importing the pack only fills in situations you haven't assigned a song to yet. Anything you
    picked yourself is always kept.</li>
    <li><strong>Can I use my own music?</strong> Yes -- drag and drop a song file onto Bandroom, or
    use the import/browse buttons in the Clipper, and it'll be added to your own library.</li>
    <li><strong>Is any of this required?</strong> No -- everything in this guide (marketplace,
    default pack, PA announcer, whistle, backgrounds) is optional. Bandroom works with just you
    picking your own songs by hand if that's all you want.</li>
  </ul>
</div>
<div class="help-guide-section">
  <h3>No sound is playing? Try this, in order</h3>
  <p>Go down this list one step at a time -- most "no sound" problems are one of these five things.</p>
  <ol>
    <li><strong>Turn the volume up.</strong> Open the gear icon (Settings) and check the big
    <strong>VOLUME</strong> slider isn't all the way down or at 0%. Also check your computer's own
    volume (the speaker icon on your taskbar) isn't muted.</li>
    <li><strong>Make sure a song is actually assigned.</strong> A situation with no song picked
    for it will always stay silent -- that's not a bug, it's just empty. Click <strong>Assign /
    Edit</strong> on that card and check a song is listed there. If it says "Unassigned" or
    "none", pick one, or use the <strong>Default Song Pack</strong> to auto-fill everything at
    once.</li>
    <li><strong>Make sure you've set a matchup.</strong> Click <strong>Set Matchup</strong> and
    pick your Home and Away teams before kickoff. Without this, Bandroom doesn't know which
    team's songs belong to which side, so it may play nothing (or the wrong team's song).</li>
    <li><strong>Make sure Bandroom is actually watching the game.</strong> Look at the top of the
    screen -- it should say <strong>Watching</strong>, not <strong>Not watching</strong>. If it
    says "Not watching," click it (or use Set Matchup again) to start watching before you kick
    off.</li>
    <li><strong>Still nothing? Try a full restart.</strong> Close Bandroom completely, close the
    game too, then reopen Bandroom first and reopen the game after. This clears up almost every
    remaining case, especially right after updating to a new version.</li>
  </ol>
  <p>If you tried all five and it's still silent, please tell us in Discord exactly which step
  didn't work -- that's the fastest way for us to fix it for everyone.</p>
</div>
<div class="help-guide-section">
  <h3>An update broke something -- how do I go back to an older version?</h3>
  <p>Sometimes a new update has a bug we haven't caught yet. You can always go back to a version
  that worked for you while we fix it. Here's how, step by step:</p>
  <ol>
    <li>Close Bandroom completely if it's open.</li>
    <li>Open your web browser and go to
    <a href="https://github.com/kingsupreme89/Bandroom-v1/releases" target="_blank" rel="noopener">github.com/kingsupreme89/Bandroom-v1/releases</a>
    -- this is the page with every version of Bandroom we've ever released.</li>
    <li>Find the version you want (for example, an older one that you know worked, like
    <strong>v1.0.52</strong>). Click on it to open that version's page.</li>
    <li>Click the download link/file for that version (near the bottom of the page, under
    "Assets") and save it to your computer.</li>
    <li>Run the file you downloaded and install it like normal -- it will replace the newer
    version. Your songs, teams, and settings all stay exactly as they were; installing an older
    version does not delete anything you've set up.</li>
    <li>Bandroom may try to auto-update itself back to the newest version the next time you open
    it. If you want to stay on the older version for now, just click "Not Now" / skip if it asks
    about updating.</li>
  </ol>
  <p>Please also tell us in Discord which version broke and what stopped working -- that's the
  only way we know to fix it before turning updates back on for you.</p>
</div>
`;

/// The sidebar volume sliders (home/away/pa/whistle/master) only ever wrote to the bridge on
/// "input" -- nothing ever read the persisted value back on startup, so every launch showed the
/// HTML's default 100%/72% regardless of what was actually saved/playing. Sound Booth's own knobs
/// (SOUNDBOOTH_KNOBS) already had a working get()/set() pair for the same underlying values; this
/// just hydrates the sidebar sliders from the same bridge getters at startup.
async function refreshVolumeSliders() {
  if (!bridge) return;
  const map = [
    ["slider-volume", "volume-value", bridge.GetVolume],
    ["slider-home-volume", "home-volume-value", bridge.GetHomeVolume],
    ["slider-away-volume", "away-volume-value", bridge.GetAwayVolume],
    ["slider-pa-volume", "pa-volume-value", bridge.GetPaVolume],
    ["slider-whistle-volume", "whistle-volume-value", bridge.GetWhistleVolume],
    ["slider-sensitivity", "sensitivity-value", bridge.GetFadeDelay],
    ["slider-sb-fade-delay", "sb-fade-delay-value", bridge.GetFadeDelay],
  ];
  for (const [sliderId, labelId, getter] of map) {
    try {
      const v = await getter.call(bridge);
      if (typeof v !== "number") continue;
      const slider = document.getElementById(sliderId);
      const label = document.getElementById(labelId);
      if (slider) slider.value = v;
      if (label) label.textContent = String(v);
    } catch (err) { console.error(`refreshVolumeSliders: ${sliderId} failed`, err); }
  }

}

// Sound Booth's knobs and the sidebar sliders both drive the same underlying bridge values
// through two entirely separate widgets -- without this, moving one left the other stale until
// its own next open/rebind. Keyed off SOUNDBOOTH_KNOBS' param names (master-volume/home-volume/
// away-volume/pa-volume/whistle-volume), same ids used by SB_KNOB_PARAMS.
function syncSoundBoothKnobDisplay(paramKey) {
  if (_sbHeroKnob?.getParamKey() === paramKey) _sbHeroKnob.rebind(paramKey);
  if (_sbContextKnob?.getParamKey() === paramKey) _sbContextKnob.rebind(paramKey);
}

/// Fade Delay has three separate controls all driving the same SetFadeDelay bridge value: the
/// sidebar's Fire Sensitivity slider (slider-sensitivity), the Sound Booth Mixer tab's own Fade
/// slider (slider-sb-fade-delay), and the Fade knob pill. `skipId` is whichever one the user is
/// actively dragging, so its own input isn't fought mid-drag by a redundant .value write.
function syncFadeDelaySlider(value, skipId) {
  for (const [sliderId, valueId] of [
    ["slider-sensitivity", "sensitivity-value"],
    ["slider-sb-fade-delay", "sb-fade-delay-value"],
  ]) {
    if (sliderId === skipId) continue;
    document.getElementById(sliderId).value = value;
    document.getElementById(valueId).textContent = value;
  }
}

/// Task queue item 5 (Session 11) -- the panel section is now always visible (see index.html's
/// comment on #leadin-whistle-section), but the enable/disable toggle row only makes sense once a
/// clip actually exists; the hint text swaps to reflect which state we're in. Pulled out of
/// initClipperIsland's inline try/catch into its own function so the upload button's success
/// handler can re-run it without duplicating this logic.
async function refreshLeadInWhistleSection() {
  const whistleAvailable = await bridge.GetLeadInWhistleAvailable();
  const hintText = whistleAvailable
    ? "A short sound that plays right before every triggered clip."
    : "A short sound that plays right before every triggered clip -- like a referee's whistle starting a play. No whistle set yet.";
  const enabled = whistleAvailable ? await bridge.GetLeadInWhistleEnabled() : null;

  // #leadin-whistle-section (old adjust-panel sidebar) and the Sound Booth Mixer tab's
  // sb-prefixed copy (see index.html comment on soundbooth-whistle-fade-section) both mirror the
  // same bridge state -- Game Day mode hides the former, so the latter needs to stay in sync too.
  for (const [rowId, hintId, toggleId] of [
    ["leadin-whistle-toggle-row", "leadin-whistle-hint", "toggle-leadin-whistle"],
    ["sb-leadin-whistle-toggle-row", "sb-leadin-whistle-hint", "toggle-sb-leadin-whistle"],
  ]) {
    document.getElementById(rowId).hidden = !whistleAvailable;
    document.getElementById(hintId).textContent = hintText;
    if (whistleAvailable) document.getElementById(toggleId).checked = enabled;
  }
}

// THE SOUND BOOTH -- Sound Booth overhaul dashboard. Plain-language (i) explanations live here
// (not on the server) since they're static copy, not app state; SB_INFO_TEXT keys match each
// control's data-info attribute in index.html.
const SB_INFO_TEXT = {
  "eq-marchingband": "This cleans up your marching band recordings so they sound less muddy -- it cuts out some rumble, tames boomy tuba/bass drum, and brings out the trumpets and snare a little more. \"Megaphone\" instead makes anything sound like it's blasting through an old stadium PA speaker, on purpose.",
  "transient-shaper": "Makes drum and cymbal hits punch harder without turning up the whole song -- like giving the snare a little extra crack right when it hits.",
  "stereo-widener": "Takes a recording that sounds narrow or one-note (like it's coming from one spot) and spreads it out so it sounds bigger and fuller through two speakers.",
  "ducking": "When something big happens like a touchdown, this quietly turns the music down for a second so the crowd sound and announcer can be heard clearly, then brings the music back up on its own.",
  "controller-rumble": "If you have an Xbox or PlayStation-style controller plugged in, this gives it a light buzz when the game is close and the clock is running out -- the last 2 minutes of the 4th quarter or overtime, with the score within a touchdown either way. Needs a controller connected to do anything.",
  "sub-bass": "Adds a low rumbly 'thump' under the sound on big tackle-for-loss plays -- like feeling a hit in your chest, not just hearing it. Off by default since it's a newer effect; try Subtle first.",
  "crowd-bus": "Plays a looping crowd-noise sound in the background that gets louder automatically when the game is close, it's the 4th quarter, or time is running out -- and stays quieter the rest of the time. You have to pick your own crowd-noise sound file first (Set Crowd Clip button) since Bandroom doesn't come with one built in.",

  // Reverb -- each preset is a fixed (room size, damping, wet mix, stereo width) recipe, not a
  // user-adjustable knob. Numbers match ReverbPresets.Get in ReverbProvider.cs so this stays
  // truthful if the recipe ever gets retuned. Dome/Rain removed 2026-08-11 (owner call, both
  // read as washy/muddy) alongside a general tightening pass on the remaining three.
  "reverb": "Off: dry, no room sound added.\n\nStadium: a tight, open-air tail -- some top end soaked up along the way, like a real crowd absorbing sound.\n\nNight Game: tighter and warmer than Stadium, more top end damped down -- cooler night air.\n\nPrime Time: Night Game's warmth with a wider stereo image -- the big-game-under-the-lights version.",

  // Sub-Bass -- how much low-end "thump" gets layered under big tackle-for-loss hits.
  "sub-bass": "Off: no added low-end thump.\n\nSubtle: a light thump under big hits -- felt more than heard.\n\nStadium: a noticeably bigger thump -- reads as \"that was a real hit\" without overpowering the song.\n\nEarthquake: the heaviest setting, strong enough to rattle a subwoofer. Can overpower quieter songs -- try Stadium first if unsure.",

  // Knob params -- what each continuous control actually changes.
  "knob-master-volume": "The overall volume for every triggered song and event, unless a matchup is set (then Home/Away volume take over for in-game cues instead).",
  "knob-home-volume": "Volume for the home team's own event cues once a matchup is set -- independent of Away, so one side can be louder or quieter than the other.",
  "knob-away-volume": "Volume for the away team's event cues once a matchup is set -- independent of Home.",
  "knob-pa-volume": "Volume for the separate PA Announcer layer, which plays alongside (not instead of) the main song for the same event.",
  "knob-fade-delay": "How many seconds a triggered clip plays at full volume before it starts fading out. There's no fade-in -- clips always start at full volume immediately; this only controls when the fade-OUT ramp begins.",
  "knob-whistle-clip": "The Whistle pill above adjusts this clip's volume, but the clip itself -- and whether it plays at all -- is set here: pick or replace the sound file, and toggle it on/off.",
};

// BUG FIX: this popover is a single shared element (one #soundbooth-info-popover for every (i)
// button in the modal, see index.html), but it only ever got `hidden = false` -- CSS gave it a
// fixed top/right position relative to #sound-booth-overlay (the fixed, full-viewport backdrop,
// the nearest positioned ancestor since #sound-booth itself never set `position`), so every (i)
// button across every tab popped the exact same box in the exact same spot instead of anchoring
// near whichever row you actually clicked -- reads as a stray box overlapping unrelated content.
// Now positions itself next to the clicked button, clamped to stay inside the panel.
function refreshSoundBoothInfoPopover(key, anchorBtn) {
  const popover = document.getElementById("soundbooth-info-popover");
  const text = document.getElementById("soundbooth-info-text");
  text.textContent = SB_INFO_TEXT[key] || "";
  popover.hidden = false;

  const panel = document.getElementById("sound-booth");
  if (anchorBtn && panel) {
    const panelRect = panel.getBoundingClientRect();
    const btnRect = anchorBtn.getBoundingClientRect();
    const popW = popover.offsetWidth || 240;
    let left = btnRect.left - panelRect.left + panel.scrollLeft - popW + btnRect.width;
    left = Math.max(8, Math.min(left, panel.clientWidth - popW - 8));
    let top = btnRect.bottom - panelRect.top + panel.scrollTop + 6;
    popover.style.left = `${left}px`;
    popover.style.top = `${top}px`;
    popover.style.right = "auto";
  }
}

async function refreshSoundBoothSection() {
  if (!bridge) return;
  try {
    const reverb = await bridge.GetReverb();
    document.querySelectorAll("#reverb-tiles .reverb-tile").forEach((t) => {
      t.classList.toggle("active", t.dataset.reverb === reverb);
    });
  } catch (err) { console.error("GetReverb failed", err); }
  try {
    const eqPreset = await bridge.GetEqPreset();
    document.querySelectorAll("#soundbooth-eq-tiles .sb-tile").forEach((t) => {
      t.classList.toggle("active", t.dataset.eq === eqPreset);
    });
  } catch (err) { console.error("GetEqPreset failed", err); }
  try {
    document.getElementById("toggle-transient-shaper").checked = await bridge.GetTransientShaperEnabled();
  } catch (err) { console.error("GetTransientShaperEnabled failed", err); }
  try {
    document.getElementById("toggle-stereo-widener").checked = await bridge.GetStereoWidenerEnabled();
  } catch (err) { console.error("GetStereoWidenerEnabled failed", err); }
  try {
    document.getElementById("toggle-ducking").checked = await bridge.GetDuckingEnabled();
  } catch (err) { console.error("GetDuckingEnabled failed", err); }
  try {
    document.getElementById("toggle-controller-rumble").checked = await bridge.GetControllerRumbleEnabled();
  } catch (err) { console.error("GetControllerRumbleEnabled failed", err); }
  try {
    const subBassLevel = await bridge.GetSubBassLevel();
    document.querySelectorAll("#soundbooth-subbass-tiles .sb-tile").forEach((t) => {
      t.classList.toggle("active", t.dataset.subbass === subBassLevel);
    });
  } catch (err) { console.error("GetSubBassLevel failed", err); }
  try {
    const bypassed = await bridge.GetNoEffectsBypass();
    document.getElementById("btn-soundbooth-no-effects").classList.toggle("active", bypassed);
  } catch (err) { console.error("GetNoEffectsBypass failed", err); }
  try {
    await refreshCrowdBusSection();
  } catch (err) { console.error("refreshCrowdBusSection failed", err); }
}

async function refreshCrowdBusSection() {
  if (!bridge) return;
  const clipAvailable = await bridge.GetCrowdBusClipAvailable();
  document.getElementById("crowdbus-label").textContent = clipAvailable
    ? "Crowd Gets Louder in Close Games"
    : "Crowd Gets Louder in Close Games (needs a clip first)";
  document.getElementById("toggle-crowd-bus").checked = clipAvailable && await bridge.GetCrowdBusEnabled();
  document.getElementById("toggle-crowd-bus").disabled = !clipAvailable;
}

// Big Game panel (Adjust sidebar) -- REDEFINED 2026-08-10: was an editable auto-detect volume
// rule ("quarter 4, score within 8"); now a manual per-matchup flag for "both bands are
// physically here" (see ConfigStore.BigGameSettings's doc comment).
//
// BUG FIX: this used to be TWO independently-settable states -- the real gating flag
// (toggle-big-game-enabled/SaveBigGameSettings, affects actual away-volume/routing behavior)
// and a separate, purely-cosmetic "banner" flag (toggle-big-game-banner/_bigGameBannerEnabled,
// only controlled whether the Gameday logo glows). They could silently disagree: unchecking one
// control left the other's UI (and the logo) looking unchanged, so "did this actually turn off"
// was never obvious. All three controls (sidebar checkbox, sidebar banner checkbox, matchup
// pill) now always agree -- toggling ANY of them flips the real flag, the logo glow, and the
// other two controls together via applyBigGameEnabled below.
let _bigGameBannerEnabled = false;

async function refreshBigGameSection() {
  if (!bridge) return;
  let enabled = false;
  try {
    const s = JSON.parse(await bridge.GetBigGameSettings());
    enabled = s.Enabled === true;
  } catch (err) { console.error("GetBigGameSettings failed", err); }
  applyBigGameEnabled(enabled, { save: false });
}

function updateMatchupBigGameBadge() {
  const badge = document.getElementById("matchup-vs-badge");
  if (badge) badge.classList.toggle("big-game-active", _bigGameBannerEnabled);
}

/// Single source of truth for all three Big Game controls + the Gameday logo glow. `save: true`
/// (used by every user-driven toggle) also persists the real flag immediately -- no separate
/// "Save Big Game Setting" step needed anymore for the checkbox to take effect, that button now
/// just exists for anyone used to clicking it (re-saves whatever's already showing as checked).
function applyBigGameEnabled(isBigGame, { save }) {
  _bigGameBannerEnabled = isBigGame;
  try { localStorage.setItem("bandroom-biggame-banner", isBigGame ? "true" : "false"); } catch (_) {}
  document.getElementById("toggle-big-game-enabled").checked = isBigGame;
  document.getElementById("toggle-big-game-banner").checked = isBigGame;
  const matchupToggle = document.getElementById("toggle-matchup-big-game");
  if (matchupToggle) matchupToggle.checked = isBigGame;
  updateMatchupBigGameBadge();
  if (save) {
    bridge?.SaveBigGameSettings(isBigGame).catch((err) => {
      console.error("SaveBigGameSettings failed", err);
      showToast("Couldn't save Big Game setting -- try again.");
    });
  }
}

function wireBigGameSection() {
  document.getElementById("toggle-big-game-banner").addEventListener("change", (e) => {
    applyBigGameEnabled(e.target.checked, { save: true });
  });
  document.getElementById("toggle-big-game-enabled").addEventListener("change", (e) => {
    applyBigGameEnabled(e.target.checked, { save: true });
  });
  document.getElementById("btn-big-game-save").addEventListener("click", async () => {
    const isBigGame = document.getElementById("toggle-big-game-enabled").checked;
    try {
      await bridge?.SaveBigGameSettings(isBigGame);
      showToast("Big Game setting saved.");
    } catch (err) {
      console.error("SaveBigGameSettings failed", err);
      showToast("Couldn't save Big Game setting -- try again.");
    }
  });

  // Matchup-screen Big Game pill -- same ConfigStore.BigGameSettings flag as the sidebar
  // checkbox above, just reachable without leaving the "pick your two teams" screen right before
  // GAMETIME, since that's the moment you actually know whether both bands showed up.
  document.getElementById("toggle-matchup-big-game").addEventListener("change", (e) => {
    applyBigGameEnabled(e.target.checked, { save: true });
  });
}

function wireControls() {
  wireLogoCropTool();
  wireBgCropTool();
  initHelpGuide();
  wireBigGameSection();
  wireBandDirector();
  wireProfileSettingsTab();
  document.getElementById("btn-profile").addEventListener("click", openProfile);
  document.getElementById("btn-settings").addEventListener("click", async () => {
    await openProfile();
    switchProfileTab("settings");
  });
  document.getElementById("btn-close-profile").addEventListener("click", closeProfile);
  document.getElementById("btn-close-profile-top").addEventListener("click", closeProfile);
  document.getElementById("btn-close-public-profile").addEventListener("click", () => {
    document.getElementById("public-profile-overlay").hidden = true;
  });
  document.getElementById("btn-google-signin").addEventListener("click", async () => {
    const btn = document.getElementById("btn-google-signin");
    btn.disabled = true;
    btn.textContent = "Waiting for browser sign-in...";
    try {
      const result = JSON.parse(await bridge.SignInWithGoogle());
      if (result.signedIn) {
        showToast(`Signed in as ${result.name}.`);
        // logosUpdated is empty (not just absent) on this device's first-ever sync, even if the
        // pulled profile carries many pre-existing custom logos -- see WebBridge.ApplyPulledLogos.
        // Batched into one toast rather than one per team so a multi-logo pull doesn't spam.
        if (result.logosUpdated && result.logosUpdated.length > 0) {
          showToast(`Logo updated for ${result.logosUpdated.join(", ")}.`);
          await refreshTeamsAfterLogoChange();
        }
        await refreshProfileView();
      } else {
        showToast(result.error ?? "Sign-in isn't set up yet -- needs a Google OAuth Client ID configured first.");
      }
    } catch (err) {
      console.error("SignInWithGoogle failed", err);
      showToast("Sign-in failed -- try again.");
    } finally {
      btn.disabled = false;
      btn.textContent = "Sign in with Google";
    }
  });
  document.getElementById("btn-google-signout").addEventListener("click", async () => {
    try { await bridge.SignOutOfGoogle(); } catch (err) { console.error("SignOutOfGoogle failed", err); }
    showToast("Signed out.");
    await refreshProfileView();
  });
  document.getElementById("btn-profile-favorite-team").addEventListener("click", openFavoriteTeamCoverflow);
  document.getElementById("profile-rival-team").addEventListener("change", async (e) => {
    try {
      await bridge.SetRivalTeam(e.target.value);
      showToast(e.target.value ? `Rival team set to ${e.target.value}.` : "Rival team cleared.");
    } catch (err) {
      console.error("SetRivalTeam failed", err);
      showToast("Couldn't save rival team -- try again.");
    }
  });
  document.getElementById("profile-bio-input").addEventListener("change", async (e) => {
    try { await bridge.SetBio(e.target.value); } catch (err) { console.error("SetBio failed", err); }
  });
  document.getElementById("profile-toasts-toggle").addEventListener("change", async (e) => {
    state.toastsEnabled = e.target.checked; // set locally first so this exact toggle-off doesn't toast itself
    try { await bridge.SetToastsEnabled(e.target.checked); } catch (err) { console.error("SetToastsEnabled failed", err); }
    showToast(e.target.checked ? "Toasts enabled." : "Toasts disabled.");
  });
  document.getElementById("btn-record-win").addEventListener("click", async () => {
    try {
      await bridge.RecordFavoriteTeamResult(true);
      showToast("Logged a win!");
      await refreshUniversalProfileView();
    } catch (err) { console.error("RecordFavoriteTeamResult(win) failed", err); }
  });
  document.getElementById("btn-record-loss").addEventListener("click", async () => {
    try {
      await bridge.RecordFavoriteTeamResult(false);
      showToast("Logged a loss.");
      await refreshUniversalProfileView();
    } catch (err) { console.error("RecordFavoriteTeamResult(loss) failed", err); }
  });
  document.getElementById("btn-export-user-profile").addEventListener("click", () => bridge.ExportUserProfile());
  document.getElementById("btn-import-user-profile").addEventListener("click", () => bridge.ImportUserProfile());
  document.getElementById("profile-public-toggle").addEventListener("change", onProfilePublicToggleChanged);
  document.getElementById("btn-share-my-profile").addEventListener("click", shareMyProfile);
  window.addEventListener("bandroom:profileimported", async () => {
    showToast("Profile imported.");
    await refreshUniversalProfileView();
  });
  document.getElementById("btn-reset-user-profile-stats").addEventListener("click", async () => {
    if (!confirm("Reset all lifetime stats (games watched, songs triggered, streak, record)? Your favorite team, rival, bio, and avatar are kept.")) return;
    try {
      await bridge.ResetUserProfileStats();
      showToast("Stats reset.");
      await refreshUniversalProfileView();
    } catch (err) { console.error("ResetUserProfileStats failed", err); }
  });
  document.getElementById("btn-profile-avatar-upload").addEventListener("click", () => {
    document.getElementById("profile-avatar-file-input").click();
  });
  document.getElementById("profile-avatar-file-input").addEventListener("change", async (e) => {
    const file = e.target.files[0];
    e.target.value = ""; // allow re-selecting the same file next time
    if (!file) return;
    try {
      const compressed = await compressImageFile(file);
      const buf = await compressed.arrayBuffer();
      const base64 = btoa(new Uint8Array(buf).reduce((s, b) => s + String.fromCharCode(b), ""));
      const ok = await bridge.UploadAvatar(base64);
      if (ok) { showToast("Avatar updated."); await refreshProfileView(); }
      else showToast("Couldn't save avatar -- try a different image.");
    } catch (err) {
      console.error("Avatar upload failed", err);
      showToast("Couldn't process that image.");
    }
  });
  document.getElementById("btn-jump-favorite-team").addEventListener("click", async (e) => {
    const team = e.currentTarget.dataset.team;
    if (team) await selectTeam(team);
  });
  document.getElementById("btn-stop-watching").addEventListener("click", async () => {
    try {
      const next = await bridge?.ToggleWatching();
      setWatching(next ?? "off");
    } catch (err) {
      console.error("ToggleWatching failed", err);
      showToast("Couldn't stop watching -- try again.");
    }
  });

  document.getElementById("btn-minimize").addEventListener("click", () => bridge?.MinimizeWindow());
  document.getElementById("btn-maximize").addEventListener("click", () => bridge?.MaximizeWindow());
  document.getElementById("btn-close").addEventListener("click", () => bridge?.CloseWindow());

  document.getElementById("btn-copy-all").addEventListener("click", () => {
    if (!confirm(`Copy ${state.activeTeam}'s current song setup to every other team? This overwrites each team's own assignments.`)) return;
    bridge?.CopyCurrentToAllTeams();
  });
  document.getElementById("btn-export-profile").addEventListener("click", () => bridge?.ExportProfile());
  document.getElementById("btn-import-profile").addEventListener("click", openImportTargetTeamDialog);
  document.getElementById("btn-add-school").addEventListener("click", openAddSchoolDialog);
  document.getElementById("btn-delete-profile").addEventListener("click", () => {
    if (!confirm(`Delete ${state.activeTeam}'s entire song setup and reset it back to defaults? This can't be undone.`)) return;
    bridge?.DeleteCurrentProfile();
  });
  document.getElementById("btn-share-profile").addEventListener("click", shareCurrentProfile);
  document.getElementById("btn-load-profile").addEventListener("click", openLoadProfileDialog);
  document.getElementById("btn-close-load-profile").addEventListener("click", closeLoadProfileDialog);
  document.getElementById("load-profile-overlay").addEventListener("click", (e) => {
    if (e.target.id === "load-profile-overlay") closeLoadProfileDialog();
  });

  // Drag the borderless window by pulling on the header center region -- but not when the
  // mousedown started on a real control inside it (e.g. "Set Matchup"), since native drag
  // capture swallows the click before it ever reaches the button.
  document.getElementById("drag-handle").addEventListener("mousedown", (e) => {
    if (e.button === 0 && !e.target.closest("button")) bridge?.BeginDrag();
  });
  document.getElementById("btn-update").addEventListener("click", () => bridge?.ShowUpdate());
  document.getElementById("btn-bandroom-cloud").addEventListener("click", openBandroomMarketplace);
  document.getElementById("btn-sound-bank").addEventListener("click", () => openTeamAlbum(state.activeTeam));
  document.getElementById("btn-my-downloads").addEventListener("click", openMyDownloads);
  document.getElementById("btn-close-my-downloads").addEventListener("click", closeMyDownloads);
  document.getElementById("btn-back-my-downloads").addEventListener("click", backFromMyDownloads);
  document.getElementById("btn-open-soundbooth").addEventListener("click", openSoundBooth);
  document.getElementById("btn-close-soundbooth").addEventListener("click", closeSoundBooth);
  // Reachable straight from the Assignment screen (owner request) -- Sound Booth opens as an
  // overlay on top of #clipper-assign rather than replacing it, so closing it drops you right
  // back into the assignment you were on.
  document.getElementById("btn-clipper-assign-soundbooth")?.addEventListener("click", openSoundBooth);
  document.getElementById("gameday-reopen-soundbooth")?.addEventListener("click", openSoundBooth);
  initSoundBoothRack();
  document.getElementById("btn-discord-chat").addEventListener("click", openDiscordChat);
  document.getElementById("btn-close-discord-chat").addEventListener("click", closeDiscordChat);
  document.getElementById("btn-import-local-song")?.addEventListener("click", importLocalSong);
  document.getElementById("btn-close-bandroom").addEventListener("click", closeBandroomMarketplace);
  document.getElementById("bandroom-overlay").addEventListener("click", (e) => {
    if (e.target.id === "bandroom-overlay") closeBandroomMarketplace();
  });
  // Debounced (200ms) via setupSearchDebounce()'s filterBandroomTeams -- filters the grid
  // already rendered by openBandroomMarketplace's renderBandroomTeamGrid("") instead of an
  // instant full-rebuild on every keystroke.

  document.getElementById("btn-close-bandroom-album").addEventListener("click", closeTeamAlbum);
  document.getElementById("bandroom-album-overlay").addEventListener("click", (e) => {
    if (e.target.id === "bandroom-album-overlay") closeTeamAlbum();
  });
  // Owner report: team logos in the market/Sound Bank had no way back to team select -- clicking
  // this logo now returns to wherever the album was opened from (see backFromTeamAlbum).
  document.getElementById("bandroom-album-icon").addEventListener("click", backFromTeamAlbum);
  // Owner report: "Bandroom market has no back button or forwards" -- explicit chevron button
  // alongside the logo-click shortcut above (some users won't realize the logo itself is
  // clickable), plus a Forward button on the hub that reappears once you've gone back, to jump
  // straight into the album you just left instead of re-searching for the same team.
  document.getElementById("btn-back-bandroom-album").addEventListener("click", backFromTeamAlbum);
  document.getElementById("btn-forward-bandroom-album").addEventListener("click", () => {
    if (_lastAlbumTeam) openTeamAlbum(_lastAlbumTeam);
  });
  document.getElementById("bandroom-album-search").addEventListener("input", onAlbumSearchInput);
  document.getElementById("btn-bandroom-album-download-all").addEventListener("click", downloadAlbumAll);
  // Direct entry point for the pack importer -- previously Ctrl+K-only (and the "already have
  // the zip" overlay had no CSS to actually display when opened that way, see style.css). Skips
  // straight to "Ready to Import?" since someone already digging in Sound Bank has files, not a
  // fresh-download need.
  document.getElementById("btn-bandroom-album-import-pack").addEventListener("click", () => {
    document.getElementById("songpack-import-overlay").hidden = false;
  });
  document.getElementById("btn-reset").addEventListener("click", () => bridge?.ResetTeamProfile());

  document.getElementById("bandroom-upload-file-input").addEventListener("change", onUploadFileChosen);
  document.getElementById("btn-bandroom-upload-cancel").addEventListener("click", closeUploadDialog);
  document.getElementById("btn-bandroom-upload-close").addEventListener("click", closeUploadDialog);
  document.getElementById("bandroom-upload-overlay").addEventListener("click", (e) => {
    if (e.target.id === "bandroom-upload-overlay") closeUploadDialog();
  });
  document.getElementById("btn-bandroom-upload-confirm").addEventListener("click", confirmUpload);
  document.getElementById("bandroom-upload-name").addEventListener("keydown", (e) => {
    if (e.key === "Enter") confirmUpload();
  });

  document.getElementById("slider-volume").addEventListener("input", (e) => {
    document.getElementById("volume-value").textContent = e.target.value;
    bridge?.SetVolume(Number(e.target.value));
    // Marketplace/song preview plays through a separate JS <audio> pathway (see previewSong) --
    // the master volume slider only reached the native player before this, so preview playback
    // was always stuck at full volume regardless of what the pill showed.
    if (_previewAudio) _previewAudio.volume = Number(e.target.value) / 100;
    if (_trimAudio) _trimAudio.volume = Number(e.target.value) / 100;
    syncSoundBoothKnobDisplay("master-volume", Number(e.target.value));
  });
  document.getElementById("slider-home-volume").addEventListener("input", (e) => {
    document.getElementById("home-volume-value").textContent = e.target.value;
    bridge?.SetHomeVolume(Number(e.target.value));
    syncSoundBoothKnobDisplay("home-volume", Number(e.target.value));
  });
  document.getElementById("slider-away-volume").addEventListener("input", (e) => {
    document.getElementById("away-volume-value").textContent = e.target.value;
    bridge?.SetAwayVolume(Number(e.target.value));
    syncSoundBoothKnobDisplay("away-volume", Number(e.target.value));
  });
  document.getElementById("slider-pa-volume").addEventListener("input", (e) => {
    document.getElementById("pa-volume-value").textContent = e.target.value;
    bridge?.SetPaVolume(Number(e.target.value));
    syncSoundBoothKnobDisplay("pa-volume", Number(e.target.value));
  });
  document.getElementById("slider-whistle-volume").addEventListener("input", (e) => {
    document.getElementById("whistle-volume-value").textContent = e.target.value;
    bridge?.SetWhistleVolume(Number(e.target.value));
    syncSoundBoothKnobDisplay("whistle-volume", Number(e.target.value));
  });
  document.getElementById("toggle-leadin-whistle").addEventListener("change", (e) => {
    bridge?.SetLeadInWhistleEnabled(e.target.checked);
  });
  // Task queue item 5 (Session 11) -- direct path for the lead-in whistle clip, instead of the
  // only way to set one being buried in the trimmer's "Set as Lead-In Whistle" button. Owner
  // feedback (round after that): a bare native file picker here was confusing -- it didn't show
  // you had to already have a song loaded, and gave no confirmation once you'd picked one. Now
  // opens Clipper Island in "whistle" mode instead: pick a track from the library (or Browse for
  // file... inside the island for a from-disk pick, same as before), Trim... it, then Set as
  // Lead-In Whistle -- with a toast + panel glow on save (see btn-trim-whistle above).
  document.getElementById("btn-leadin-whistle-upload").addEventListener("click", () => {
    openClipperAssignForWhistle();
  });
  // Sound Booth Mixer tab's own copy of the whistle controls (see index.html comment on
  // soundbooth-whistle-fade-section) -- same bridge calls as the pair above, just reachable while
  // docked in Game Day mode where #adjust-panel is hidden.
  document.getElementById("toggle-sb-leadin-whistle").addEventListener("change", (e) => {
    bridge?.SetLeadInWhistleEnabled(e.target.checked);
  });
  document.getElementById("btn-sb-leadin-whistle-upload").addEventListener("click", () => {
    openClipperAssignForWhistle();
  });
  document.getElementById("slider-sensitivity").addEventListener("input", (e) => {
    document.getElementById("sensitivity-value").textContent = e.target.value;
    bridge?.SetFadeDelay(Number(e.target.value));
    syncSoundBoothKnobDisplay("fade-delay", Number(e.target.value));
    syncFadeDelaySlider(e.target.value, "slider-sensitivity");
  });
  // Sound Booth Mixer tab's own copy of the Fade slider (see index.html comment on the second
  // soundbooth-whistle-fade-section) -- same SetFadeDelay bridge call and same underlying value as
  // the Fade knob pill above it and the sidebar's Fire Sensitivity slider, kept in sync three ways.
  document.getElementById("slider-sb-fade-delay").addEventListener("input", (e) => {
    document.getElementById("sb-fade-delay-value").textContent = e.target.value;
    bridge?.SetFadeDelay(Number(e.target.value));
    syncSoundBoothKnobDisplay("fade-delay", Number(e.target.value));
    syncFadeDelaySlider(e.target.value, "slider-sb-fade-delay");
  });
  document.querySelectorAll(".reverb-tile").forEach((tile) => {
    tile.addEventListener("click", () => {
      document.querySelectorAll(".reverb-tile").forEach((t) => t.classList.remove("active"));
      tile.classList.add("active");
      bridge?.SetReverb(tile.dataset.reverb);
    });
  });

  // The Sound Booth
  document.querySelectorAll("#soundbooth-eq-tiles .sb-tile").forEach((tile) => {
    tile.addEventListener("click", () => {
      document.querySelectorAll("#soundbooth-eq-tiles .sb-tile").forEach((t) => t.classList.remove("active"));
      tile.classList.add("active");
      bridge?.SetEqPreset(tile.dataset.eq);
    });
  });
  document.getElementById("toggle-transient-shaper").addEventListener("change", (e) => {
    bridge?.SetTransientShaperEnabled(e.target.checked);
  });
  document.getElementById("toggle-stereo-widener").addEventListener("change", (e) => {
    bridge?.SetStereoWidenerEnabled(e.target.checked);
  });
  document.getElementById("toggle-ducking").addEventListener("change", (e) => {
    bridge?.SetDuckingEnabled(e.target.checked);
  });
  document.getElementById("toggle-controller-rumble").addEventListener("change", (e) => {
    bridge?.SetControllerRumbleEnabled(e.target.checked);
  });
  document.querySelectorAll("#soundbooth-subbass-tiles .sb-tile").forEach((tile) => {
    tile.addEventListener("click", () => {
      document.querySelectorAll("#soundbooth-subbass-tiles .sb-tile").forEach((t) => t.classList.remove("active"));
      tile.classList.add("active");
      bridge?.SetSubBassLevel(tile.dataset.subbass);
    });
  });
  document.getElementById("toggle-crowd-bus").addEventListener("change", (e) => {
    bridge?.SetCrowdBusEnabled(e.target.checked);
  });
  document.getElementById("btn-crowdbus-upload").addEventListener("click", async () => {
    const btn = document.getElementById("btn-crowdbus-upload");
    btn.disabled = true;
    try {
      const ok = bridge ? await bridge.BrowseAndSetCrowdBusClip() : false;
      if (ok) {
        showToast("Crowd ambience clip set.");
        await refreshCrowdBusSection();
      }
    } catch (err) {
      console.error("BrowseAndSetCrowdBusClip failed", err);
      showToast("Couldn't set that crowd clip -- try again.");
    }
    btn.disabled = false;
  });
  document.getElementById("btn-soundbooth-no-effects").addEventListener("click", async () => {
    const btn = document.getElementById("btn-soundbooth-no-effects");
    const next = !btn.classList.contains("active");
    btn.classList.toggle("active", next);
    btn.textContent = next ? "Effects Off -- Click to Restore" : "No Effects -- Hear It Raw";
    await bridge?.SetNoEffectsBypass(next);
  });
  document.querySelectorAll(".sb-info").forEach((btn) => {
    btn.addEventListener("click", (e) => {
      e.preventDefault();
      e.stopPropagation();
      refreshSoundBoothInfoPopover(btn.dataset.info, btn);
    });
  });
  document.getElementById("soundbooth-info-close").addEventListener("click", () => {
    document.getElementById("soundbooth-info-popover").hidden = true;
  });

  document.querySelectorAll("[data-action]").forEach((item) => {
    item.addEventListener("click", () => runRailAction(item.dataset.action));
  });

  document.getElementById("btn-close-situations").addEventListener("click", () => {
    document.getElementById("situations-panel").hidden = true;
    state.currentSituationsCategory = null;
  });
  setupBandroomViewer();
  // These two dialogs attach their real Cancel/Skip handlers dynamically each time they're
  // opened (see handleAutoAssignClick/showDefaultProfilePrompt) since the outcome is awaited via
  // a per-open Promise -- the X buttons just proxy a click onto whichever Cancel/Skip button is
  // live right now rather than duplicating that per-open wiring.
  document.getElementById("btn-auto-assign-close").addEventListener("click", () =>
    document.getElementById("btn-auto-assign-cancel").click());
  document.getElementById("btn-default-profile-close").addEventListener("click", () =>
    document.getElementById("btn-default-profile-skip").click());

  window.addEventListener("bandroom:refresh", refreshCategories);
  // Fired by WebMainForm.SyncPublicTeamLogosAsync when the startup public-logo sync actually
  // changed something -- reuses the same repaint path a local logo save already uses so a
  // publicly-pushed logo shows up live without needing a restart.
  window.addEventListener("bandroom:publiclogosupdated", refreshTeamsAfterLogoChange);
  window.addEventListener("bandroom:watchstate", (e) => setWatching(e.detail));
  // Names exactly which trigger OCR just read and played a sound for -- lets a user verify live
  // that Bandroom read the right thing off the scoreboard, without checking logs.
  window.addEventListener("bandroom:triggerfired", (e) => showToast(`Trigger fired: ${e.detail}`));
  // Right Ctrl "band cutoff" -- global hotkey, fires even when Bandroom isn't focused (see
  // KeyboardHook.Cutoff / WebMainForm.OnCutoff). Just a confirmation toast; the actual
  // AudioPlayer.StopAll() already happened on the C# side before this event is even dispatched.
  window.addEventListener("bandroom:cutoff", () => showToast("Cutoff — all audio stopped."));
  window.addEventListener("bandroom:profileschanged", async () => {
    if (bridge) state.savedProfiles = JSON.parse(await bridge.GetSavedProfiles());
    renderTeamGrid();
    updateProfileStatus();
  });
  window.addEventListener("bandroom:updateavailable", () => {
    const btn = document.getElementById("btn-update");
    btn.classList.remove("dim", "downgraded");
    btn.textContent = "↑ Update";
    btn.title = "A new version is available -- click to update.";
    // Passive toast in addition to the button changing -- the button alone is easy to miss if
    // it's not the thing you're looking at when a new version lands in the background.
    showToast("A new Bandroom update is available -- click \"↑ Update\" in the header to grab it.");
  });
  // Fires when this install is OLDER than a version this machine has run before -- almost
  // always means an old cached Setup.exe got run by mistake. Louder than the normal update
  // button since this is a "you're missing stuff you've already seen" situation, not
  // a routine "new version exists" one.
  window.addEventListener("bandroom:downgraded", () => {
    const btn = document.getElementById("btn-update");
    btn.classList.remove("dim");
    btn.classList.add("downgraded");
    btn.textContent = "↑ Fix Version";
    btn.title = "This looks like an older build than one you've already run -- click to update to the latest.";
    showToast("This is an older Bandroom build than one you've run before -- click \"Fix Version\" in the header to update.");
  });

  // Update download/install progress -- see WebMainForm.ShowUpdateDialogFromWeb. Replaces the
  // old silent-download-then-instant-relaunch flow with visible progress and a confirm step.
  const updateOverlay = document.getElementById("update-overlay");
  const updateHeader = document.getElementById("update-header");
  const updateFill = document.getElementById("update-progress-fill");
  const updateSub = document.getElementById("update-sub");
  const updateActions = document.getElementById("update-actions");
  window.addEventListener("bandroom:updatedownloading", () => {
    updateHeader.textContent = "Downloading update…";
    updateSub.textContent = "Hang tight, this only takes a moment.";
    updateFill.style.width = "0%";
    updateActions.hidden = true;
    updateOverlay.hidden = false;
  });
  window.addEventListener("bandroom:updateprogress", (e) => {
    updateFill.style.width = `${Math.max(0, Math.min(100, e.detail))}%`;
  });
  window.addEventListener("bandroom:updateready", () => {
    updateHeader.textContent = "Update ready";
    updateFill.style.width = "100%";
    updateSub.textContent = "Restart Bandroom to finish installing.";
    updateActions.hidden = false;
  });
  window.addEventListener("bandroom:updatefailed", () => {
    updateOverlay.hidden = true;
  });
  document.getElementById("btn-update-restart").addEventListener("click", () => bridge?.RestartForUpdate());

  initDefaultSongPackPrompt();
  wirePreviewBar();
  initClipperAssign();
  wireInlineTrimmer();

  document.getElementById("header-team-badge").addEventListener("click", openTeamPicker);
  // Files dropped anywhere on the window get copied into Songs\ (normalized name) by the
  // native DragDrop handler in WebMainForm.cs; re-render so newly imported tracks show up
  // in any open Assign dialog / situation list right away.
  window.addEventListener("bandroom:songsimported", async (e) => {
    showToast(`Imported ${e.detail} song${e.detail === 1 ? "" : "s"} to your Sound Bank`);
  });

  document.getElementById("btn-close-picker").addEventListener("click", closeTeamPicker);
  document.getElementById("team-picker-overlay").addEventListener("click", (e) => {
    if (e.target.id === "team-picker-overlay") closeTeamPicker();
  });
  // Debounced (200ms) via setupSearchDebounce() -- re-renders the coverflow filtered to the
  // search text instead of an instant full-rebuild on every keystroke.
  document.getElementById("team-picker-search").addEventListener("keydown", (e) => {
    if (e.key === "Enter") { selectTeam(_teamPickerPicked); closeTeamPicker(); }
  });

  document.getElementById("btn-close-import-target-team").addEventListener("click", closeImportTargetTeamDialog);
  document.getElementById("import-target-team-overlay").addEventListener("click", (e) => {
    if (e.target.id === "import-target-team-overlay") closeImportTargetTeamDialog();
  });
  document.getElementById("import-target-team-search").addEventListener("input", (e) => renderImportTargetTeamGrid(e.target.value));

  document.getElementById("btn-close-add-school").addEventListener("click", closeAddSchoolDialog);
  document.getElementById("add-school-overlay").addEventListener("click", (e) => {
    if (e.target.id === "add-school-overlay") closeAddSchoolDialog();
  });
  document.getElementById("btn-add-school-confirm").addEventListener("click", submitAddSchool);
  document.getElementById("add-school-name").addEventListener("keydown", (e) => {
    if (e.key === "Enter") submitAddSchool();
  });

  document.getElementById("btn-matchup").addEventListener("click", openMatchupDialog);
  document.getElementById("btn-close-matchup").addEventListener("click", closeMatchupDialog);
  document.getElementById("btn-matchup-cancel").addEventListener("click", closeMatchupDialog);
  document.getElementById("btn-matchup-confirm").addEventListener("click", confirmMatchup);
  document.getElementById("matchup-overlay").addEventListener("click", (e) => {
    if (e.target.id === "matchup-overlay") closeMatchupDialog();
  });
  document.getElementById("matchup-home-search").addEventListener("input", (e) => {
    renderMatchupCoverflow("home", e.target.value);
    renderMatchupSideGrid("home", e.target.value);
  });
  document.getElementById("matchup-away-search").addEventListener("input", (e) => {
    renderMatchupCoverflow("away", e.target.value);
    renderMatchupSideGrid("away", e.target.value);
  });
  document.querySelectorAll(".coverflow-arrow").forEach((btn) => {
    btn.addEventListener("click", () => shiftCoverflow(btn.dataset.side, parseInt(btn.dataset.dir, 10)));
  });
  wireCoverflowWheel(document.getElementById("matchup-home-track"), (dir) => shiftCoverflow("home", dir));
  wireCoverflowWheel(document.getElementById("matchup-away-track"), (dir) => shiftCoverflow("away", dir));
  wireCoverflowWheel(document.getElementById("team-picker-track"), (dir) => shiftTeamPicker(dir));
  // Owner request 2026-08-11: switching sides via these arrows should feel like walking into
  // that team's own fullscreen band room, not just a quiet data refresh underneath the still-open
  // cards. Reuses the existing Band Room viewer (openBandroomViewer -- the same fullscreen photo
  // modal the "Enter Band Room" pill already opens) rather than building a second gallery.
  document.getElementById("btn-side-away").addEventListener("click", async () => { await selectTeam(state.matchupAway); await openBandroomViewer(); });
  document.getElementById("btn-side-home").addEventListener("click", async () => { await selectTeam(state.matchupHome); await openBandroomViewer(); });
  document.getElementById("btn-auto-assign").addEventListener("click", handleAutoAssignClick);
  document.getElementById("btn-auto-assign-header")?.addEventListener("click", handleAutoAssignClick);
  setupQuickLoadProfile();
  wireTeamPresetBar();
  document.getElementById("btn-close-auto-assign-summary")?.addEventListener("click", () => {
    document.getElementById("auto-assign-summary-overlay").hidden = true;
  });
  document.getElementById("btn-auto-assign-summary-done")?.addEventListener("click", () => {
    document.getElementById("auto-assign-summary-overlay").hidden = true;
  });

  document.getElementById("btn-save-profile-cancel").addEventListener("click", closeSaveProfileDialog);
  document.getElementById("btn-save-profile-close").addEventListener("click", closeSaveProfileDialog);
  document.getElementById("btn-save-profile-confirm").addEventListener("click", confirmSaveProfile);
  document.getElementById("save-profile-overlay").addEventListener("click", (e) => {
    if (e.target.id === "save-profile-overlay") closeSaveProfileDialog();
  });
  document.getElementById("save-profile-name").addEventListener("input", updateSaveProfileSubtext);
  document.getElementById("save-profile-name").addEventListener("keydown", (e) => {
    if (e.key === "Enter") confirmSaveProfile();
  });

  // Owner request: the native "How to Use Bandroom" popup (WinForms ShortcutsForm) is gone --
  // it was a plain unthemed dialog duplicating what the in-app Help & Guide overlay already
  // covers, more thoroughly, in the app's own theme. Both Help entry points now open that same
  // overlay (see btn-help-pill's click handler in initHelpGuide).
  document.getElementById("btn-help").addEventListener("click", () => {
    document.getElementById("btn-help-pill")?.click();
  });

  document.addEventListener("keydown", (e) => {
    // Creator-only batch logo/icon import (item 20) -- deliberately NOT a menu item or button
    // anywhere: this is a maintainer tool for bulk-prepping team art, not something an end user
    // should ever stumble into. A four-key chord is about as low-risk of an accidental trigger
    // as a keyboard shortcut gets, while still needing zero setup (no --dev launch flag to
    // remember, no hidden menu to find) for the one person who actually uses it.
    if (e.ctrlKey && e.altKey && e.shiftKey && e.key.toUpperCase() === "L") {
      e.preventDefault();
      openBatchLogoImportTool();
      return;
    }
    if (e.key !== "Escape") return;
    if (!document.getElementById("team-picker-overlay").hidden) closeTeamPicker();
    if (!document.getElementById("save-profile-overlay").hidden) closeSaveProfileDialog();
    if (!document.getElementById("matchup-overlay").hidden) closeMatchupDialog();
    if (!document.getElementById("import-target-team-overlay").hidden) closeImportTargetTeamDialog();
    if (!document.getElementById("add-school-overlay").hidden) closeAddSchoolDialog();
    if (!document.getElementById("favorite-team-overlay").hidden) { document.getElementById("favorite-team-overlay").hidden = true; restoreActiveTeamGlow(); }
    // Album closes first if both happen to be open (it renders on top of the team-grid overlay).
    if (!document.getElementById("bandroom-upload-overlay").hidden) closeUploadDialog();
    else if (!document.getElementById("bandroom-album-overlay").hidden) closeTeamAlbum();
    else if (!document.getElementById("bandroom-overlay").hidden) closeBandroomMarketplace();
    else if (!document.getElementById("my-downloads-overlay").hidden) closeMyDownloads();
    else if (!document.getElementById("sound-booth-overlay").hidden) closeSoundBooth();
    else if (!document.getElementById("logo-crop-overlay").hidden) closeLogoCropTool();
    else if (!document.getElementById("profile-overlay").hidden) closeProfile();
    else if (!document.getElementById("discord-chat-overlay").hidden) closeDiscordChat();
  });

  // Owner request: Tab previews the highlighted song on the Assignment screen instead of tabbing
  // focus off into the page -- same action as clicking btn-clipper-assign-play. Only intercepts
  // Tab while #clipper-assign is actually open and the trimmer isn't (Tab there would fight with
  // the zoom/handle controls), and never while a text input/textarea has focus so normal
  // browser-native field-to-field tabbing (e.g. clipper-assign-search) still works.
  document.addEventListener("keydown", (e) => {
    if (e.key !== "Tab") return;
    const assignPanel = document.getElementById("clipper-assign");
    if (!assignPanel || assignPanel.hidden) return;
    if (_trimTrigger || _trimForWhistle) return;
    const active = document.activeElement;
    if (active && (active.tagName === "INPUT" || active.tagName === "TEXTAREA")) return;
    e.preventDefault();
    document.getElementById("btn-clipper-assign-play")?.click();
  });
}

/// Release notes written as filler by release.ps1's default -Notes param when a release ships
/// with no real bullet points -- never counts as a "feature" or gets shown as one.
const CHANGELOG_FILLER_PATTERN = /full changelog/i;

/// Loaded once on startup into the always-visible "What's New" section of the Adjust panel
/// (not behind a button -- a button meant nobody ever saw it). Flattens real feature bullets
/// across releases (newest first) and caps at 10 so the panel doesn't grow unbounded; the
/// "See full changelog on GitHub" link only appears once at least 10 real bullets have actually
/// been shown, never as a stand-in for a release that shipped with no real notes.
async function loadChangelog() {
  const list = document.getElementById("changelog-list");
  if (!list) return;
  list.innerHTML = `<div class="changelog-empty">Loading...</div>`;

  const entries = bridge ? JSON.parse(await bridge.GetChangelog()) : [];
  const usable = entries
    .map((e) => ({ ...e, notes: e.notes.filter((n) => !CHANGELOG_FILLER_PATTERN.test(n)) }))
    .filter((e) => e.notes.length > 0);

  if (usable.length === 0) {
    list.innerHTML = `<div class="changelog-empty">Couldn't load release notes right now.</div>`;
    return;
  }

  list.innerHTML = "";
  let shownBullets = 0;
  for (const e of usable) {
    if (shownBullets >= 10) break;
    const row = document.createElement("div");
    row.className = "changelog-entry";
    const notes = e.notes.map((n) => `<li>${n}</li>`).join("");
    row.innerHTML = `
      <div class="changelog-entry-header">
        <span class="changelog-version">${e.title}</span>
        <span class="changelog-date">${e.publishedAt}</span>
        ${e.prerelease ? `<span class="changelog-prerelease">Beta</span>` : ""}
      </div>
      <ul class="changelog-notes">${notes}</ul>`;
    list.appendChild(row);
    shownBullets += e.notes.length;
  }

  if (shownBullets >= 10) {
    const link = document.createElement("a");
    link.className = "changelog-full-link";
    link.href = "https://github.com/kingsupreme89/Bandroom-v1/releases";
    link.target = "_blank";
    link.rel = "noopener";
    link.textContent = "See the full changelog on GitHub →";
    list.appendChild(link);
  }
}

// "Choose a Team" picker (owner request: same large iTunes CoverFlow the favorite-team picker
// uses, replacing the old 4-col grid). Side tiles browse (re-center, same as
// favorite/onboarding), the CENTER tile is the one that actually selects+closes -- matches the
// old grid's "click = pick" immediacy while still letting you browse past a team without
// committing to it, which a plain grid's every-tile-is-a-button never allowed.
let _teamPickerPicked = null;
let _teamPickerWired = false;

function openTeamPicker() {
  document.getElementById("team-picker-overlay").hidden = false;
  const search = document.getElementById("team-picker-search");
  search.value = "";
  _teamPickerPicked = state.activeTeam || null;
  renderTeamPickerCoverflow("");
  search.focus();

  if (!_teamPickerWired) {
    _teamPickerWired = true;
    document.querySelectorAll("#team-picker .coverflow-arrow").forEach((btn) => {
      btn.addEventListener("click", () => shiftTeamPicker(parseInt(btn.dataset.dir, 10)));
    });
  }
}

function closeTeamPicker() {
  document.getElementById("team-picker-overlay").hidden = true;
  restoreActiveTeamGlow();
}

function shiftTeamPicker(dir) {
  const filter = document.getElementById("team-picker-search")?.value || "";
  const teams = matchupCoverflowTeams(filter);
  if (!teams.length) return;
  let idx = teams.findIndex((t) => t.name === _teamPickerPicked);
  if (idx === -1) idx = 0;
  idx = ((idx + dir) % teams.length + teams.length) % teams.length;
  _teamPickerPicked = teams[idx].name;
  renderTeamPickerCoverflow(filter);
}

function renderTeamPickerCoverflow(filter) {
  const track = document.getElementById("team-picker-track");
  const nameEl = document.getElementById("team-picker-name");
  if (!track || !nameEl) return;
  const teams = matchupCoverflowTeams(filter);
  track.innerHTML = "";
  if (!teams.length) {
    nameEl.textContent = "No teams found";
    return;
  }

  let centerIdx = teams.findIndex((t) => t.name === _teamPickerPicked);
  if (centerIdx === -1) centerIdx = 0;

  const positions = [[-2, "cf-l2"], [-1, "cf-l1"], [0, "cf-center"], [1, "cf-r1"], [2, "cf-r2"]];
  for (const [offset, cls] of positions) {
    const idx = ((centerIdx + offset) % teams.length + teams.length) % teams.length;
    const t = teams[idx];
    const tile = document.createElement("div");
    tile.className = "team-swatch " + cls;
    tile.title = t.name;
    fillTeamSwatch(tile, t, true);
    if (cls === "cf-center") {
      tile.addEventListener("click", () => { selectTeam(t.name); closeTeamPicker(); });
      if (t.name !== "General") {
        const editBtn = document.createElement("button");
        editBtn.className = "team-swatch-edit-logo";
        editBtn.title = `Set a custom logo for ${t.name}`;
        editBtn.textContent = "✎";
        editBtn.addEventListener("click", (e) => { e.stopPropagation(); openLogoCropTool(t.name); });
        tile.appendChild(editBtn);

        const bgBtn = document.createElement("button");
        bgBtn.className = "team-swatch-edit-bg";
        bgBtn.title = `Set a custom background for ${t.name}`;
        bgBtn.textContent = "\u{1F5BC}";
        bgBtn.addEventListener("click", (e) => { e.stopPropagation(); openBackgroundCropTool(t.name); });
        tile.appendChild(bgBtn);
      }
    } else {
      tile.addEventListener("click", () => { _teamPickerPicked = t.name; renderTeamPickerCoverflow(filter); });
    }
    track.appendChild(tile);
  }

  _teamPickerPicked = teams[centerIdx].name;
  nameEl.textContent = _teamPickerPicked;
  previewTeamGlow(teams[centerIdx]);
}

/// Import now asks WHICH team the profile file is for instead of assuming state.activeTeam -- a
/// profile file someone hands you is very often meant for a different school than whatever
/// happens to be selected right now. Picking a team here closes the dialog and immediately hands
/// off to the native file picker (WebBridge.ImportProfile -> WebMainForm.ImportProfileFromWeb)
/// for that explicit team. Distinct from openLoadProfileDialog/closeLoadProfileDialog below,
/// which is the unrelated "Load Profile from Others" marketplace feature.
function openImportTargetTeamDialog() {
  document.getElementById("import-target-team-overlay").hidden = false;
  const search = document.getElementById("import-target-team-search");
  search.value = "";
  renderImportTargetTeamGrid("");
  search.focus();
}

function closeImportTargetTeamDialog() {
  document.getElementById("import-target-team-overlay").hidden = true;
}

function renderImportTargetTeamGrid(filter) {
  renderTeamGridInto("import-target-team-grid", filter, (name) => {
    closeImportTargetTeamDialog();
    bridge?.ImportProfile(name);
  });
}

/// TeamBuilder "Add School" v2 -- name + primary/secondary color + optional mascot. The mascot is
/// an OCR-matching alias only (GameWatcher.HomeTeamMascot/AwayTeamMascot): the game's penalty
/// banner sometimes shows the mascot instead of the school name, so a custom school with no
/// mascot set just won't match penalty-attribution text, same as v1. The 50 most popular FCS
/// programs now ship as real roster entries (TeamColors.cs FcsTeams) and already show up in the
/// main Team picker, so this dialog stays a fully custom entry point for anything not in either
/// list. Logo isn't set here -- once added, the new school shows up in the full Team picker like
/// any other, where the existing per-tile pencil icon (openLogoCropTool) already works for it.
/// In-app replacement for window.prompt() when editing an upload's name/school (used by both the
/// per-owner edit pencil and the admin-only edit tool). Resolves { name, school } on Save, or
/// null if closed/cancelled -- same contract the two prompt() calls it replaces had (null = bail).
function editUploadDialog(initialName, initialSchool, title) {
  return new Promise((resolve) => {
    const overlay = document.getElementById("edit-upload-overlay");
    const nameInput = document.getElementById("edit-upload-name");
    const schoolInput = document.getElementById("edit-upload-school");
    const err = document.getElementById("edit-upload-error");
    const confirmBtn = document.getElementById("btn-edit-upload-confirm");
    const closeBtn = document.getElementById("btn-close-edit-upload");

    document.getElementById("edit-upload-title").textContent = title || "Edit Upload";
    nameInput.value = initialName ?? "";
    schoolInput.value = initialSchool ?? "";
    err.hidden = true;
    err.textContent = "";
    overlay.hidden = false;
    nameInput.focus();

    let done = false;
    const finish = (result) => {
      if (done) return;
      done = true;
      overlay.hidden = true;
      confirmBtn.onclick = null;
      closeBtn.onclick = null;
      overlay.onclick = null;
      resolve(result);
    };
    confirmBtn.onclick = () => {
      const name = nameInput.value.trim();
      if (!name) {
        err.textContent = "A name is required.";
        err.hidden = false;
        return;
      }
      finish({ name, school: schoolInput.value.trim() });
    };
    closeBtn.onclick = () => finish(null);
    // No backdrop-click-to-close: this dialog holds typed input (name/school), and a stray
    // click outside it while typing silently discarded whatever was entered. Close/Cancel only.
  });
}

function openAddSchoolDialog() {
  document.getElementById("add-school-name").value = "";
  document.getElementById("add-school-mascot").value = "";
  document.getElementById("add-school-primary").value = "#22d3ee";
  document.getElementById("add-school-secondary").value = "#ffffff";
  const err = document.getElementById("add-school-error");
  err.hidden = true;
  err.textContent = "";
  document.getElementById("add-school-overlay").hidden = false;
  document.getElementById("add-school-name").focus();
}

function closeAddSchoolDialog() {
  document.getElementById("add-school-overlay").hidden = true;
}

async function submitAddSchool() {
  const err = document.getElementById("add-school-error");
  err.hidden = true;
  err.textContent = "";
  if (!bridge) return;

  // Bug blocker: double-click or a slow bridge call could otherwise fire AddCustomTeam twice
  // for one click, and WebBridge's own duplicate-name guard would then surface the SECOND call
  // as a confusing "already exists" error for a school the user just added themselves in this
  // same action. Disabling the button for the duration of the one in-flight call closes that.
  const confirmBtn = document.getElementById("btn-add-school-confirm");
  if (confirmBtn.disabled) return;

  const name = document.getElementById("add-school-name").value.trim();
  const mascot = document.getElementById("add-school-mascot").value.trim();
  const primary = document.getElementById("add-school-primary").value;
  const secondary = document.getElementById("add-school-secondary").value;
  if (!name) {
    err.textContent = "A school name is required.";
    err.hidden = false;
    return;
  }

  confirmBtn.disabled = true;
  try {
    const result = JSON.parse(await bridge.AddCustomTeam(name, primary, secondary, mascot));
    if (!result.success) {
      err.textContent = result.error || "Couldn't add that school.";
      err.hidden = false;
      return;
    }
    closeAddSchoolDialog();
    showToast(`Added ${result.team.name}. Set a logo for it from the full Team picker.`);
    await refreshTeamsAfterLogoChange();
    renderTeamGrid();
  } catch (err2) {
    console.error("AddCustomTeam failed", err2);
    err.textContent = "Couldn't add that school -- try again.";
    err.hidden = false;
  } finally {
    confirmBtn.disabled = false;
  }
}

function renderTeamGridInto(gridId, filter, onPick, showEditLogo = false) {
  const grid = document.getElementById(gridId);
  grid.innerHTML = "";
  const q = filter.trim().toLowerCase();
  for (const t of state.teams) {
    if (q && !t.name.toLowerCase().includes(q)) continue;
    const sw = document.createElement("div");
    sw.className = "team-swatch" + (t.name === state.activeTeam ? " active" : "");
    sw.title = t.name;
    fillTeamSwatch(sw, t);
    sw.addEventListener("click", () => onPick(t.name));
    if (showEditLogo && t.name !== "General") {
      const editBtn = document.createElement("button");
      editBtn.className = "team-swatch-edit-logo";
      editBtn.title = `Set a custom logo for ${t.name}`;
      editBtn.textContent = "✎"; // pencil
      editBtn.addEventListener("click", (e) => {
        e.stopPropagation();
        openLogoCropTool(t.name);
      });
      sw.appendChild(editBtn);

      // Same idea, different target -- the full-screen backdrop image behind this team's
      // screens, not the badge-shaped logo. Separate crop tool (openBackgroundCropTool) with a
      // 16:9 output instead of a square.
      const bgBtn = document.createElement("button");
      bgBtn.className = "team-swatch-edit-bg";
      bgBtn.title = `Set a custom background for ${t.name}`;
      bgBtn.textContent = "\u{1F5BC}"; // framed picture
      bgBtn.addEventListener("click", (e) => {
        e.stopPropagation();
        openBackgroundCropTool(t.name);
      });
      sw.appendChild(bgBtn);
    }
    grid.appendChild(sw);
  }
  squareUpTiles(grid);
}

// Every marketplace entry point below is wrapped defensively: state.teams may not have loaded
// yet, a team's data can be malformed, or a DOM node can be missing mid-transition -- none of
// that should throw past this boundary and leave an overlay stuck half-open/unclickable. On any
// failure we log for diagnosis, force every marketplace overlay closed (a known-good state), and
// toast the user instead of silently freezing.
function marketplaceGuard(fn, label) {
  try {
    fn();
  } catch (err) {
    console.error(`Marketplace error (${label})`, err);
    try {
      document.getElementById("bandroom-overlay").hidden = true;
      document.getElementById("bandroom-album-overlay").hidden = true;
    } catch { /* DOM itself is gone -- nothing more we can do */ }
    albumTeam = null;
    showToast("The Bandroom hit a snag -- closed it. Try again.");
  }
}

// ---- Marketplace data layer -------------------------------------------------------------
// Thin wrappers over the cloudflare-marketplace worker's GET /list. Every call is defensive --
// a network hiccup returns an empty list instead of throwing, since marketplaceGuard's job is
// to keep the UI alive, not to surface raw fetch errors to the user.
async function fetchUploadList(type, school, sort) {
  const result = await fetchUploadListDetailed(type, school, sort);
  return result.items;
}

// Same as fetchUploadList, but also reports whether the fetch itself failed -- callers that need
// to tell "the worker said this team really has zero uploads" apart from "the fetch failed" (no
// false empty states) should use this instead.
async function fetchUploadListDetailed(type, school, sort) {
  try {
    const qs = new URLSearchParams({ type });
    if (school) qs.set("school", school);
    if (sort) qs.set("sort", sort);
    const res = await fetch(`${MARKETPLACE_URL}/list?${qs}`);
    if (!res.ok) return { items: [], error: true };
    const data = await res.json();
    return { items: Array.isArray(data.items) ? data.items : [], error: false };
  } catch (err) {
    console.error(`fetchUploadList(${type}) failed`, err);
    return { items: [], error: true };
  }
}

// Which hub sort is currently selected -- "newest" (default), "views", "downloads", "likes".
// Module-level so it survives re-renders (e.g. re-opening the hub keeps the last choice).
let _hubSort = "newest";

async function fetchRecentUploads(limit, sort) {
  const [songs, images] = await Promise.all([
    fetchUploadList("song", null, sort),
    fetchUploadList("image", null, sort),
  ]);
  const combined = [...songs, ...images];
  // The worker sorts each type independently by the counter -- merging two independently-sorted
  // lists needs a re-sort by the same key, same as the "newest" merge already did.
  if (sort === "views" || sort === "downloads" || sort === "likes") {
    combined.sort((a, b) => (b[sort] ?? 0) - (a[sort] ?? 0));
  } else {
    combined.sort((a, b) => (a.uploadedAt < b.uploadedAt ? 1 : -1));
  }
  return combined.slice(0, limit);
}

// ---- "My uploads" tracking (item 5) ----------------------------------------------------
// No accounts exist, so ownership is tracked purely client-side: the worker hands back a
// one-time ownerToken at upload time (see worker.js POST /upload), which we stash in
// localStorage keyed by item id. Only tiles whose id shows up here get a Delete button --
// this browser/app-instance is the only place that ever sees its own token.
const MY_UPLOADS_KEY = "bandroom:myUploads";

function loadMyUploads() {
  try {
    const raw = localStorage.getItem(MY_UPLOADS_KEY);
    const parsed = raw ? JSON.parse(raw) : {};
    return parsed && typeof parsed === "object" ? parsed : {};
  } catch (err) {
    console.error("loadMyUploads failed", err);
    return {};
  }
}

function recordMyUpload(type, uploadResult) {
  if (!uploadResult?.id || !uploadResult?.ownerToken) return;
  try {
    const mine = loadMyUploads();
    mine[uploadResult.id] = { type, ownerToken: uploadResult.ownerToken, at: Date.now() };
    localStorage.setItem(MY_UPLOADS_KEY, JSON.stringify(mine));
  } catch (err) {
    console.error("recordMyUpload failed", err);
  }
}

function myUploadToken(id) {
  return loadMyUploads()[id]?.ownerToken ?? null;
}

function forgetMyUpload(id) {
  try {
    const mine = loadMyUploads();
    delete mine[id];
    localStorage.setItem(MY_UPLOADS_KEY, JSON.stringify(mine));
  } catch (err) {
    console.error("forgetMyUpload failed", err);
  }
}

async function deleteUploadedItem(item) {
  const token = myUploadToken(item.id);
  if (!token) return false;
  try {
    const res = await fetch(`${MARKETPLACE_URL}/item/${item.type}/${encodeURIComponent(item.id)}`, {
      method: "DELETE",
      headers: { "X-Owner-Token": token },
    });
    if (!res.ok) throw new Error(`delete failed: ${res.status}`);
    forgetMyUpload(item.id);
    return true;
  } catch (err) {
    console.error("deleteUploadedItem failed", err);
    return false;
  }
}

// Owner-scoped rename/re-categorize, for a regular uploader fixing a typo in their own upload --
// same worker.js PATCH /item/<type>/<id> the admin edit uses, just authenticated with the
// uploader's own ownerToken (X-Owner-Token) instead of X-Admin-Token. Server-side sanitization
// (sanitizeSegment) is identical either way, so this can't reopen the stored-XSS class of bug the
// admin path already had fixed. Returns the server's { id, name, school } on success, or null.
async function editUploadedItem(item, newName, newSchool) {
  const token = myUploadToken(item.id);
  if (!token) return null;
  try {
    const res = await fetch(`${MARKETPLACE_URL}/item/${item.type}/${encodeURIComponent(item.id)}`, {
      method: "PATCH",
      headers: { "X-Owner-Token": token, "Content-Type": "application/json" },
      body: JSON.stringify({ name: newName, school: newSchool }),
    });
    if (!res.ok) return null;
    return await res.json();
  } catch (err) {
    console.error("editUploadedItem failed", err);
    return null;
  }
}

async function downloadMarketplaceItem(item) {
  try {
    const raw = await bridge.DownloadMarketplaceItem(item.type, item.name, item.school, item.url);
    const result = JSON.parse(raw);
    if (result.success) {
      try { await bridge.RecordMarketplaceDownload(); } catch (err) { console.error("RecordMarketplaceDownload failed", err); }
      // Server-side download counter, for the "Most Downloaded" sort -- fired only once the
      // download actually completed, same as the local RecordMarketplaceDownload call above.
      recordItemDownload(item);
    }
    return !!result.success;
  } catch (err) {
    console.error("downloadMarketplaceItem failed", err);
    return false;
  }
}

async function reportUploadedItem(item) {
  try {
    const res = await fetch(`${MARKETPLACE_URL}/report/${item.type}/${encodeURIComponent(item.id)}`, { method: "POST" });
    return res.ok;
  } catch (err) {
    console.error("reportUploadedItem failed", err);
    return false;
  }
}

// Fire-and-forget increments -- same shape as likeUploadedItem, but callers don't need the
// updated count back (views/downloads aren't shown live on a tile the way likes are), so these
// don't block on the response the way likeUploadedItem's caller does.
async function recordItemView(item) {
  try {
    await fetch(`${MARKETPLACE_URL}/view/${item.type}/${encodeURIComponent(item.id)}`, { method: "POST" });
  } catch (err) {
    console.error("recordItemView failed", err);
  }
}

async function recordItemDownload(item) {
  try {
    await fetch(`${MARKETPLACE_URL}/download/${item.type}/${encodeURIComponent(item.id)}`, { method: "POST" });
  } catch (err) {
    console.error("recordItemDownload failed", err);
  }
}

async function likeUploadedItem(item) {
  try {
    const res = await fetch(`${MARKETPLACE_URL}/like/${item.type}/${encodeURIComponent(item.id)}`, { method: "POST" });
    if (!res.ok) return null;
    const data = await res.json();
    return typeof data.likes === "number" ? data.likes : null;
  } catch (err) {
    console.error("likeUploadedItem failed", err);
    return null;
  }
}

// Symmetric with likeUploadedItem -- separate /dislike endpoint + counter on the worker
// (cloudflare/cloudflare-marketplace/worker.js), same fire-and-report shape.
async function dislikeUploadedItem(item) {
  try {
    const res = await fetch(`${MARKETPLACE_URL}/dislike/${item.type}/${encodeURIComponent(item.id)}`, { method: "POST" });
    if (!res.ok) return null;
    const data = await res.json();
    return typeof data.dislikes === "number" ? data.dislikes : null;
  } catch (err) {
    console.error("dislikeUploadedItem failed", err);
    return null;
  }
}

let _hubSortListenerBound = false;

function openBandroomMarketplace() {
  marketplaceGuard(() => {
    document.getElementById("bandroom-overlay").hidden = false;
    const search = document.getElementById("bandroom-search");
    search.value = "";
    renderBandroomTeamGrid("");
    const sortSelect = document.getElementById("bandroom-hub-sort");
    if (sortSelect) {
      sortSelect.value = _hubSort;
      // Bound once, not per-open -- re-adding the same listener on every open would stack up
      // duplicate handlers (same pattern as other one-time listener guards in this file).
      if (!_hubSortListenerBound) {
        _hubSortListenerBound = true;
        sortSelect.addEventListener("change", () => {
          _hubSort = sortSelect.value;
          renderBandroomHub();
        });
      }
    }
    renderBandroomHubHeroRow();
    renderPopularSongsShelf();
    renderTopTeamBackgroundsShelf();
    search.focus();
  }, "openBandroomMarketplace");
}

function openMyDownloads() {
  marketplaceGuard(() => {
    document.getElementById("bandroom-overlay").hidden = true;
    document.getElementById("bandroom-album-overlay").hidden = true;
    document.getElementById("my-downloads-overlay").hidden = false;
    initMyDownloadsToolbar();
    loadMyDownloads();
  }, "openMyDownloads");
}

function closeMyDownloads() {
  document.getElementById("my-downloads-overlay").hidden = true;
  _previewAudio?.pause();
}

// Band Director streamer dashboard (Phase 1 -- see BANDROOM_STREAMER_MASTER_PROMPT.md SYSTEM 2).
// Everything Twitch/YouTube-specific is static mock data; Master Volume and the Quick Trigger
// slot mapping are real (same bridge plumbing as elsewhere in the app).
const MOCK_BD_CHAT_COMMANDS = [
  { cmd: "td", when: "3m ago" },
  { cmd: "kickoff", when: "just now" },
  { cmd: "hype", when: "5m ago" },
];
const MOCK_BD_LIVE_LOG = [
  { text: "TD: Fight Song (12s)" },
  { text: "3rd: Hype Sting (8s)" },
  { text: "DEF: Stop Chant (6s)" },
];
const MOCK_BD_QUEUE = [
  { title: "Kickoff Song", source: "waiting" },
  { title: "TD Song", source: "viewer" },
  { title: "Boos", source: null },
];
const MOCK_BD_POLL = {
  question: "Pick 3rd Down",
  options: [{ label: "Neck", votes: 12 }, { label: "Hype", votes: 8 }, { label: "Chant", votes: 5 }],
};

let _bdQuickTriggerMap = null;

function openBandDirector() {
  document.getElementById("band-director-overlay").hidden = false;
  renderBandDirectorMockData();
  refreshBandDirectorVolume();
}

function closeBandDirector() {
  document.getElementById("band-director-overlay").hidden = true;
}

function renderBandDirectorMockData() {
  document.getElementById("bd-chat-commands-list").innerHTML = MOCK_BD_CHAT_COMMANDS
    .map((c) => `<div class="bd-list-row"><span>!${c.cmd}</span><span class="bd-muted">${c.when}</span></div>`)
    .join("");
  document.getElementById("bd-live-log-list").innerHTML = MOCK_BD_LIVE_LOG
    .map((l) => `<div class="bd-list-row">${l.text}</div>`)
    .join("");
  document.getElementById("bd-queue-list").innerHTML = MOCK_BD_QUEUE
    .map((q, i) => `<div class="bd-list-row">${i === 0 ? "▶" : `${i + 1}.`} ${q.title}${q.source ? ` (${q.source})` : ""}</div>`)
    .join("");
  const totalVotes = MOCK_BD_POLL.options.reduce((sum, o) => sum + o.votes, 0);
  document.getElementById("bd-polls-panel").innerHTML =
    `<div class="bd-panel-title" style="margin-bottom:8px;">"${MOCK_BD_POLL.question}"</div>` +
    MOCK_BD_POLL.options
      .map((o) => `<div class="bd-list-row"><span>${o.label}</span><span class="bd-muted">${o.votes} (${totalVotes ? Math.round((o.votes / totalVotes) * 100) : 0}%)</span></div>`)
      .join("");
}

async function refreshBandDirectorVolume() {
  try {
    const v = await bridge.GetVolume();
    document.getElementById("bd-master-volume").value = v;
    document.getElementById("bd-master-volume-value").textContent = `${v}%`;
  } catch (err) { console.error("refreshBandDirectorVolume failed", err); }
}

async function loadBandDirectorQuickTriggerMap() {
  try {
    const settings = JSON.parse(await bridge.GetBandDirectorDashboardSettings());
    _bdQuickTriggerMap = settings.QuickTriggerMap || {};
  } catch (err) {
    console.error("loadBandDirectorQuickTriggerMap failed", err);
    _bdQuickTriggerMap = {};
  }
  return _bdQuickTriggerMap;
}

async function onBandDirectorQuickTriggerClick(slot) {
  const map = _bdQuickTriggerMap || (await loadBandDirectorQuickTriggerMap());
  const eventKey = map[slot];
  if (!eventKey) {
    showToast(`Slot ${slot} has no song assigned -- set it in ⚙ Settings`);
    return;
  }
  bridge?.FireTestEvent("Home", eventKey);
}

async function openBandDirectorSettings() {
  document.getElementById("band-director-settings-overlay").hidden = false;
  try {
    const [settings, eventsJson] = await Promise.all([
      loadBandDirectorQuickTriggerMap(),
      bridge.GetEventsForCategory(null),
    ]);
    const eventNames = [...new Set(JSON.parse(eventsJson).map((e) => e.eventName))].sort();
    const container = document.getElementById("bd-settings-slots");
    container.innerHTML = "";
    for (let i = 1; i <= 8; i++) {
      const slot = String(i);
      const current = settings[slot] || "";
      const row = document.createElement("div");
      row.className = "bd-settings-row";
      const label = document.createElement("label");
      label.textContent = `Slot ${slot}`;
      const select = document.createElement("select");
      select.dataset.slot = slot;
      select.appendChild(new Option("-- Unassigned --", ""));
      for (const name of eventNames) select.appendChild(new Option(name, name, false, name === current));
      row.appendChild(label);
      row.appendChild(select);
      container.appendChild(row);
    }
  } catch (err) { console.error("openBandDirectorSettings failed", err); }
}

function closeBandDirectorSettings() {
  document.getElementById("band-director-settings-overlay").hidden = true;
}

async function saveBandDirectorSettings() {
  const map = {};
  document.querySelectorAll("#bd-settings-slots select").forEach((sel) => { map[sel.dataset.slot] = sel.value; });
  try {
    await bridge.SaveBandDirectorDashboardSettings(JSON.stringify(map));
    _bdQuickTriggerMap = map;
    closeBandDirectorSettings();
    showToast("Band Director quick triggers saved");
  } catch (err) { console.error("saveBandDirectorSettings failed", err); }
}

function wireBandDirector() {
  document.getElementById("btn-band-director").addEventListener("click", openBandDirector);
  document.getElementById("btn-close-band-director").addEventListener("click", closeBandDirector);
  document.getElementById("btn-band-director-settings").addEventListener("click", openBandDirectorSettings);
  document.getElementById("btn-close-band-director-settings").addEventListener("click", closeBandDirectorSettings);
  document.getElementById("btn-cancel-band-director-settings").addEventListener("click", closeBandDirectorSettings);
  document.getElementById("btn-save-band-director-settings").addEventListener("click", saveBandDirectorSettings);
  document.getElementById("bd-master-volume").addEventListener("input", (e) => {
    bridge?.SetVolume(Number(e.target.value));
    document.getElementById("bd-master-volume-value").textContent = `${e.target.value}%`;
  });
  document.querySelectorAll(".bd-trigger-btn").forEach((btn) => {
    btn.addEventListener("click", () => onBandDirectorQuickTriggerClick(btn.dataset.triggerSlot));
  });
  document.getElementById("btn-bd-copy-overlay-url").addEventListener("click", async () => {
    try {
      const url = await bridge.GetOverlayChatUrl();
      await navigator.clipboard.writeText(url);
      showToast("Overlay URL copied -- add it as an OBS Browser Source");
    } catch (err) {
      console.error("GetOverlayChatUrl/copy failed", err);
      showToast("Couldn't copy the overlay URL");
    }
  });
  document.getElementById("btn-bd-edit-overlay").addEventListener("click", () => showToast("Overlay editor -- coming soon"));
  document.getElementById("btn-bd-generate-guest-code").addEventListener("click", () => showToast("Guest DJ -- coming soon"));
  wireBandDirectorTabs();
}

// Setup / Live tabs -- scoped to #band-director so this never collides with Sound Booth's own
// .soundbooth-tab/.soundbooth-tab-panel reuse (see the #sound-booth-scoped fix above this
// function's sibling wiring block for the bug that pattern already caused once).
function wireBandDirectorTabs() {
  document.querySelectorAll("#band-director [data-bd-tab]").forEach((tab) => {
    tab.addEventListener("click", () => {
      document.querySelectorAll("#band-director [data-bd-tab]").forEach((t) => {
        t.classList.remove("active");
        t.setAttribute("aria-selected", "false");
      });
      tab.classList.add("active");
      tab.setAttribute("aria-selected", "true");
      const target = tab.dataset.bdTab;
      document.querySelectorAll("#band-director [data-bd-panel]").forEach((panel) => {
        panel.hidden = panel.dataset.bdPanel !== target;
      });
    });
  });
}

function openSoundBooth() {
  document.getElementById("sound-booth-overlay").hidden = false;
  refreshSoundBoothSection();
  refreshSoundBoothKnobs();
  setSoundBoothPreviewButtonsIdle();
  startSoundBoothMeters();
}

function closeSoundBooth() {
  document.getElementById("sound-booth-overlay").hidden = true;
  document.getElementById("soundbooth-info-popover").hidden = true;
  stopSoundBoothMeters();
}

// ================================================================
// SOUND BOOTH PLUGIN RACK (Session 32) -- rotary knob component, live IN/OUT
// meters, tab strip, and Preview audition, restyled from an owner-supplied
// plugin-UI reference but wired only to real AudioPlayer params. See the
// Session 32 handoff doc for what's real vs. decorative in the reference.
// ================================================================

// Binding table: which bridge getter/setter/range each knob "param" maps to.
// The hero knob is always "master-volume"; the context knob rebinds to
// whichever of these the active pill selects.
const SB_KNOB_PARAMS = {
  "master-volume": { min: 0, max: 100, get: () => bridge.GetVolume(), set: (v) => bridge.SetVolume(v), label: "Master", unit: "%" },
  "home-volume": { min: 0, max: 100, get: () => bridge.GetHomeVolume(), set: (v) => bridge.SetHomeVolume(v), label: "Home", unit: "%" },
  "away-volume": { min: 0, max: 100, get: () => bridge.GetAwayVolume(), set: (v) => bridge.SetAwayVolume(v), label: "Away", unit: "%" },
  "pa-volume": { min: 0, max: 100, get: () => bridge.GetPaVolume(), set: (v) => bridge.SetPaVolume(v), label: "PA", unit: "%" },
  "whistle-volume": { min: 0, max: 100, get: () => bridge.GetWhistleVolume(), set: (v) => bridge.SetWhistleVolume(v), label: "Whistle", unit: "%" },
  "fade-delay": { min: 0, max: 30, get: () => bridge.GetFadeDelay(), set: (v) => bridge.SetFadeDelay(v), label: "Fade", unit: "s" },
};

// -135deg to +135deg sweep, matching the reference's ~270 degree arc.
const SB_KNOB_MIN_ANGLE = -135;
const SB_KNOB_MAX_ANGLE = 135;

function sbKnobAngleForValue(value, min, max) {
  const t = max === min ? 0 : (value - min) / (max - min);
  return SB_KNOB_MIN_ANGLE + t * (SB_KNOB_MAX_ANGLE - SB_KNOB_MIN_ANGLE);
}

function sbKnobArcPath(angleDeg) {
  // Arc drawn from SB_KNOB_MIN_ANGLE up to the current angle, radius 42 centered at (50,50).
  const r = 42;
  const toXY = (deg) => {
    const rad = ((deg - 90) * Math.PI) / 180;
    return [50 + r * Math.cos(rad), 50 + r * Math.sin(rad)];
  };
  const [x1, y1] = toXY(SB_KNOB_MIN_ANGLE);
  const [x2, y2] = toXY(angleDeg);
  const largeArc = angleDeg - SB_KNOB_MIN_ANGLE > 180 ? 1 : 0;
  return `M ${x1.toFixed(2)} ${y1.toFixed(2)} A ${r} ${r} 0 ${largeArc} 1 ${x2.toFixed(2)} ${y2.toFixed(2)}`;
}

function sbKnobRender(el, value, min, max, unit) {
  const angle = sbKnobAngleForValue(value, min, max);
  el.querySelector(".sb-knob-arc").setAttribute("d", sbKnobArcPath(angle));
  el.querySelector(".sb-knob-indicator").style.transform = `rotate(${angle}deg)`;
  el.querySelector(".sb-knob-value").textContent = Math.round(value);
  if (unit !== undefined) el.querySelector(".sb-knob-unit").textContent = unit;
  el.setAttribute("aria-valuenow", String(Math.round(value)));
}

// Turns a static .sb-knob element into a working drag/keyboard control bound to `paramKey`
// (an SB_KNOB_PARAMS key). Returns a rebind(paramKey) function so context knobs can swap params.
function initSoundBoothKnob(el, initialParamKey) {
  let paramKey = initialParamKey;
  let cfg = SB_KNOB_PARAMS[paramKey];
  let value = cfg.min;
  // BUG FIX: previously the arc/indicator stayed at their raw HTML `d=""`/unrotated state until
  // the first async rebind() resolved -- if that rebind ever failed silently (or just hadn't
  // finished yet), the knob showed as an unrendered blob instead of a valid (if default-valued)
  // dial. Render immediately with the param's own min so there's always a real, visible knob.
  sbKnobRender(el, value, cfg.min, cfg.max, cfg.unit);

  const commit = debounce((v) => { try { cfg.set(v); } catch (err) { console.error("sb-knob set failed", err); } }, 70);

  function setValue(v, { commitNow } = {}) {
    value = Math.min(cfg.max, Math.max(cfg.min, v));
    sbKnobRender(el, value, cfg.min, cfg.max);
    if (paramKey === "fade-delay") syncFadeDelaySlider(Math.round(value));
    if (commitNow) { try { cfg.set(value); } catch (err) { console.error("sb-knob set failed", err); } }
    else commit(value);
  }

  async function rebind(newParamKey) {
    paramKey = newParamKey;
    cfg = SB_KNOB_PARAMS[paramKey];
    el.setAttribute("aria-label", cfg.label);
    el.setAttribute("aria-valuemax", String(cfg.max));
    const label = el.parentElement?.querySelector(".sb-knob-label") || el.querySelector(".sb-knob-label");
    if (label) label.textContent = cfg.label;
    const infoBtn = el.querySelector(".sb-knob-info");
    if (infoBtn) infoBtn.dataset.info = `knob-${paramKey}`;
    try {
      value = cfg.min;
      const v = await cfg.get();
      value = typeof v === "number" ? v : cfg.min;
    } catch (err) { console.error("sb-knob get failed", err); }
    sbKnobRender(el, value, cfg.min, cfg.max, cfg.unit);
    if (paramKey === "fade-delay") syncFadeDelaySlider(Math.round(value));
  }

  let dragStartY = 0, dragStartValue = 0;
  el.addEventListener("pointerdown", (e) => {
    el.setPointerCapture(e.pointerId);
    el.classList.add("dragging");
    dragStartY = e.clientY;
    dragStartValue = value;
  });
  el.addEventListener("pointermove", (e) => {
    if (!el.classList.contains("dragging")) return;
    const deltaY = dragStartY - e.clientY; // drag up = increase
    const range = cfg.max - cfg.min;
    const next = dragStartValue + (deltaY / 160) * range;
    setValue(next);
  });
  const endDrag = (e) => {
    if (!el.classList.contains("dragging")) return;
    el.classList.remove("dragging");
    setValue(value, { commitNow: true });
  };
  el.addEventListener("pointerup", endDrag);
  el.addEventListener("pointercancel", endDrag);
  el.addEventListener("keydown", (e) => {
    const step = e.key === "PageUp" || e.key === "PageDown" ? (cfg.max - cfg.min) / 10 : 1;
    if (e.key === "ArrowUp" || e.key === "ArrowRight" || e.key === "PageUp") { setValue(value + step, { commitNow: true }); e.preventDefault(); }
    else if (e.key === "ArrowDown" || e.key === "ArrowLeft" || e.key === "PageDown") { setValue(value - step, { commitNow: true }); e.preventDefault(); }
    else if (e.key === "Home") { setValue(cfg.min, { commitNow: true }); e.preventDefault(); }
    else if (e.key === "End") { setValue(cfg.max, { commitNow: true }); e.preventDefault(); }
  });

  return { rebind, getParamKey: () => paramKey };
}

let _sbHeroKnob = null;
let _sbContextKnob = null;

function initSoundBoothRack() {
  const contextEl = document.getElementById("sb-knob-context");
  try {
    const heroEl = document.getElementById("sb-knob-master");
    if (!heroEl || !contextEl) { console.error("initSoundBoothRack: knob elements missing from DOM"); return; }
    _sbHeroKnob = initSoundBoothKnob(heroEl, "master-volume");
    _sbContextKnob = initSoundBoothKnob(contextEl, "home-volume");
  } catch (err) {
    // Whatever broke the knobs before this fix likely threw somewhere in here -- log it instead
    // of letting it die silently (F12 DevTools is suppressed app-wide, see
    // AreBrowserAcceleratorKeysEnabled in WebMainForm.cs, so this console.error is the only trace).
    console.error("initSoundBoothRack failed", err);
    return;
  }

  document.querySelectorAll("#sb-knob-context-pills .sb-tile").forEach((pill) => {
    pill.addEventListener("click", async () => {
      if (pill.classList.contains("active")) return;
      document.querySelectorAll("#sb-knob-context-pills .sb-tile").forEach((p) => p.classList.remove("active"));
      pill.classList.add("active");
      contextEl.classList.add("rebinding");
      await _sbContextKnob.rebind(pill.dataset.param);
      contextEl.classList.remove("rebinding");
    });
  });

  // Scoped to #sound-booth -- .soundbooth-tab/.soundbooth-tab-panel are also reused (for their
  // visual style only) by the Band Director dashboard's Setup/Live tabs (wireBandDirectorTabs),
  // which use their own data-bd-tab/data-bd-panel attributes. An unscoped query here used to
  // match those too and, since they lack dataset.sbTab/sbPanel, unhide every panel in both
  // overlays on any click -- same class-reuse trap as the soundboard-btn bug.
  document.querySelectorAll("#sound-booth .soundbooth-tab").forEach((tab) => {
    tab.addEventListener("click", () => {
      document.querySelectorAll("#sound-booth .soundbooth-tab").forEach((t) => {
        t.classList.remove("active");
        t.setAttribute("aria-selected", "false");
      });
      tab.classList.add("active");
      tab.setAttribute("aria-selected", "true");
      const target = tab.dataset.sbTab;
      document.querySelectorAll("#sound-booth .soundbooth-tab-panel").forEach((panel) => {
        panel.hidden = panel.dataset.sbPanel !== target;
      });
      document.getElementById("soundbooth-info-popover").hidden = true;
    });
  });

  document.querySelectorAll(".soundbooth-preview-btn").forEach((btn) => {
    btn.addEventListener("click", async () => {
      const playing = btn.classList.contains("previewing");
      if (playing) {
        bridge?.StopPreview();
        setSoundBoothPreviewButtonsIdle();
        return;
      }
      setSoundBoothPreviewButtonsIdle();
      btn.classList.add("previewing");
      btn.textContent = _clipperAssignSelectedName ? `⏹ Stop (${_clipperAssignSelectedName})` : "⏹ Stop";
      try {
        // Prefer whatever song is highlighted on the Assignment screen (same selection
        // btn-clipper-assign-play uses) so people can hear their own clip run through the
        // reverb/EQ/effects rack instead of only the canned "score" test cue -- falls back to
        // the fixed test event when Sound Booth is opened with nothing selected.
        if (_clipperAssignSelectedPath) await bridge?.PreviewLocalFile(_clipperAssignSelectedPath);
        else await bridge?.PreviewEvent(btn.dataset.previewTrigger || "score");
      } catch (err) { console.error("Sound Booth preview failed", err); }
    });
  });
}

function setSoundBoothPreviewButtonsIdle() {
  document.querySelectorAll(".soundbooth-preview-btn").forEach((b) => {
    b.classList.remove("previewing");
    b.textContent = _clipperAssignSelectedName ? `▶ Preview (${_clipperAssignSelectedName})` : "▶ Preview";
  });
}

async function refreshSoundBoothKnobs() {
  if (!bridge || !_sbHeroKnob || !_sbContextKnob) return;
  try { await _sbHeroKnob.rebind("master-volume"); } catch (err) { console.error("hero knob refresh failed", err); }
  try { await _sbContextKnob.rebind(_sbContextKnob.getParamKey()); } catch (err) { console.error("context knob refresh failed", err); }
}

let _sbMeterPollHandle = null;

function startSoundBoothMeters() {
  stopSoundBoothMeters();
  const inEl = document.getElementById("sb-meter-in")?.querySelector(".sb-meter-fill");
  const outEl = document.getElementById("sb-meter-out")?.querySelector(".sb-meter-fill");
  if (!inEl || !outEl || !bridge) return;
  const poll = async () => {
    try {
      const raw = await bridge.GetCurrentLevels();
      const lv = JSON.parse(raw);
      inEl.style.height = `${Math.round(Math.min(1, lv.in) * 100)}%`;
      outEl.style.height = `${Math.round(Math.min(1, lv.out) * 100)}%`;
      document.getElementById("sb-meter-in")?.classList.remove("meter-idle");
      document.getElementById("sb-meter-out")?.classList.remove("meter-idle");
    } catch (err) {
      // GetCurrentLevels missing/failing (e.g. an older build without the metering bridge
      // method) -- fall back to a dimmed, static "no live signal" state instead of erroring.
      document.getElementById("sb-meter-in")?.classList.add("meter-idle");
      document.getElementById("sb-meter-out")?.classList.add("meter-idle");
      inEl.style.height = "4%";
      outEl.style.height = "4%";
    }
  };
  poll();
  _sbMeterPollHandle = setInterval(poll, 100);
}

function stopSoundBoothMeters() {
  if (_sbMeterPollHandle) {
    clearInterval(_sbMeterPollHandle);
    _sbMeterPollHandle = null;
  }
}

// Owner report: My Downloads had no way back to The Bandroom except closing everything and
// reopening -- same "easy way back to things" gap as the team-album logo (see backFromTeamAlbum).
function backFromMyDownloads() {
  closeMyDownloads();
  openBandroomMarketplace();
}

// ---- My Downloads: search / filter / sort / group toolbar --------------------------------
// Redesigned (owner-supplied reference: a table/list music-library layout, adapted to this app's
// own tile/pill/glass conventions rather than copied verbatim) from a plain unsorted grid into a
// searchable, filterable, sortable, optionally school-grouped list -- the same shape as every
// other "browse a lot of items" surface in the app (Bandroom hub, team album) already has, which
// My Downloads was oddly missing.
let _myDownloadsItems = [];
let _myDownloadsFilter = "all";
let _myDownloadsSort = "newest";
let _myDownloadsGroupBySchool = false;
let _myDownloadsToolbarBound = false;

function initMyDownloadsToolbar() {
  if (_myDownloadsToolbarBound) return;
  _myDownloadsToolbarBound = true;

  document.getElementById("my-downloads-search").addEventListener("input", () => renderMyDownloadsList());

  document.getElementById("my-downloads-filters").addEventListener("click", (e) => {
    const btn = e.target.closest(".my-downloads-filter");
    if (!btn) return;
    document.querySelectorAll(".my-downloads-filter").forEach((b) => b.classList.remove("active"));
    btn.classList.add("active");
    _myDownloadsFilter = btn.dataset.filter;
    renderMyDownloadsList();
  });

  document.getElementById("my-downloads-sort").addEventListener("change", (e) => {
    _myDownloadsSort = e.target.value;
    renderMyDownloadsList();
  });

  document.getElementById("my-downloads-group-by-school").addEventListener("change", (e) => {
    _myDownloadsGroupBySchool = e.target.checked;
    renderMyDownloadsList();
  });
}

async function loadMyDownloads() {
  const list = document.getElementById("my-downloads-list");
  list.innerHTML = `<div class="bandroom-empty-state">Loading...</div>`;
  try {
    _myDownloadsItems = JSON.parse(await bridge.GetMyDownloads());
  } catch (err) {
    console.error("GetMyDownloads failed", err);
    _myDownloadsItems = [];
  }
  if (document.getElementById("my-downloads-overlay").hidden) return; // closed while awaiting
  renderMyDownloadsList();
}

// Glanceable count-card hero row above the filter pills (Music Library UX Brief v2 §4) -- same
// shared .bandroom-hero-row/.bandroom-hero-card component the hub and album header use. Clicking
// a card just re-drives the existing pill filter (clicks the matching .my-downloads-filter
// button) instead of duplicating the filter-apply logic here.
function renderMyDownloadsHeroRow() {
  const row = document.getElementById("my-downloads-hero-row");
  if (!row) return;
  const total = _myDownloadsItems.length;
  const songs = _myDownloadsItems.filter((i) => i.type === "song").length;
  const images = _myDownloadsItems.filter((i) => i.type === "image").length;
  const missing = _myDownloadsItems.filter((i) => i.fileExists === false).length;
  const cards = [
    { filter: "all", icon: "\u{1F4E5}", label: "All Downloads", count: total },
    { filter: "song", icon: "\u{1F3B5}", label: "Songs", count: songs },
    { filter: "image", icon: "\u{1F5BC}", label: "Backgrounds", count: images },
    { filter: "missing", icon: "⚠️", label: "Missing Files", count: missing },
  ];
  row.innerHTML = "";
  for (const card of cards) {
    const el = document.createElement("div");
    el.className = "bandroom-hero-card" + (_myDownloadsFilter === card.filter ? " active" : "");
    el.innerHTML = `
      <div class="bandroom-hero-card-icon">${card.icon}</div>
      <div>
        <div class="bandroom-hero-card-label">${card.label}</div>
        <div class="bandroom-hero-card-count">${card.count} item${card.count === 1 ? "" : "s"}</div>
      </div>`;
    el.addEventListener("click", () => {
      document.querySelector(`.my-downloads-filter[data-filter="${card.filter}"]`)?.click();
    });
    row.appendChild(el);
  }
}

function renderMyDownloadsList() {
  const list = document.getElementById("my-downloads-list");
  const countEl = document.getElementById("my-downloads-count");
  countEl.textContent = _myDownloadsItems.length > 0
    ? `${_myDownloadsItems.length} download${_myDownloadsItems.length === 1 ? "" : "s"}`
    : "";
  renderMyDownloadsHeroRow();

  const q = (document.getElementById("my-downloads-search").value || "").trim().toLowerCase();
  let items = _myDownloadsItems.filter((item) => {
    if (_myDownloadsFilter === "song" && item.type !== "song") return false;
    if (_myDownloadsFilter === "image" && item.type !== "image") return false;
    if (_myDownloadsFilter === "local" && item.source !== "local") return false;
    if (_myDownloadsFilter === "missing" && item.fileExists !== false) return false;
    if (!q) return true;
    return item.name?.toLowerCase().includes(q) || item.school?.toLowerCase().includes(q);
  });

  items = items.slice().sort((a, b) => {
    if (_myDownloadsSort === "name") return (a.name || "").localeCompare(b.name || "");
    if (_myDownloadsSort === "school") return (a.school || "Your library").localeCompare(b.school || "Your library");
    return new Date(b.downloadedAt) - new Date(a.downloadedAt); // newest
  });

  list.innerHTML = "";
  if (_myDownloadsItems.length === 0) {
    list.innerHTML = `<div class="bandroom-empty-state">Nothing downloaded yet -- open a team's Sound Bank and hit the ⬇ button on anything you like.</div>`;
    return;
  }
  if (items.length === 0) {
    list.innerHTML = `<div class="bandroom-empty-state">Nothing matches that search/filter.</div>`;
    return;
  }

  if (_myDownloadsGroupBySchool) {
    const groups = new Map();
    for (const item of items) {
      const key = item.source === "local" ? "Your library" : (item.school || "Unknown");
      if (!groups.has(key)) groups.set(key, []);
      groups.get(key).push(item);
    }
    for (const [school, groupItems] of groups) {
      const header = document.createElement("div");
      header.className = "my-downloads-group-header";
      header.textContent = `${school} (${groupItems.length})`;
      list.appendChild(header);
      for (const item of groupItems) list.appendChild(buildMyDownloadRow(item));
    }
  } else {
    for (const item of items) list.appendChild(buildMyDownloadRow(item));
  }
}

// Discord Chat panel -- read-only relay feed, polled from the usercount worker while the panel
// is open (see USERCOUNT_URL above and cloudflare-usercount/worker.js's /discord/messages doc
// comment for the full contract). _discordLastId tracks the newest message id we've rendered so
// each poll only asks for what's new; _discordPollTimer is cleared on close so polling truly
// stops rather than just going unused in the background.
let _discordLastId = null;
let _discordPollTimer = null;

function openDiscordChat() {
  document.getElementById("discord-chat-overlay").hidden = false;
  document.getElementById("btn-discord-chat").classList.add("pill-active");
  pollDiscordChat();
}

function closeDiscordChat() {
  document.getElementById("discord-chat-overlay").hidden = true;
  document.getElementById("btn-discord-chat").classList.remove("pill-active");
  if (_discordPollTimer) {
    clearTimeout(_discordPollTimer);
    _discordPollTimer = null;
  }
}

async function pollDiscordChat() {
  const overlay = document.getElementById("discord-chat-overlay");
  if (overlay.hidden) return; // panel closed while a previous poll was in flight

  const list = document.getElementById("discord-chat-messages");
  try {
    const qs = _discordLastId ? `?after=${encodeURIComponent(_discordLastId)}` : "";
    const res = await fetch(`${USERCOUNT_URL}/discord/messages${qs}`);
    const data = res.ok ? await res.json() : { messages: [] };
    const messages = data.messages ?? [];

    if (overlay.hidden) return; // closed while awaiting the fetch

    if (_discordLastId === null && messages.length === 0 && list.children.length === 0) {
      // First load, nothing came back at all -- most likely the owner hasn't set
      // DISCORD_BOT_TOKEN/DISCORD_CHANNEL_ID on the worker yet. Quiet empty state, no error toast.
      list.innerHTML = `<div class="bandroom-empty-state">Discord feed not connected.</div>`;
    } else if (messages.length > 0) {
      if (list.querySelector(".bandroom-empty-state")) list.innerHTML = "";
      const wasScrolledToBottom = list.scrollHeight - list.scrollTop - list.clientHeight < 24;
      for (const m of messages) list.appendChild(buildDiscordMessageRow(m));
      _discordLastId = messages[messages.length - 1].id;
      if (wasScrolledToBottom) list.scrollTop = list.scrollHeight;
    }
  } catch (err) {
    console.error("pollDiscordChat failed", err);
    // Transient network error -- leave whatever's already rendered alone and just retry on the
    // next tick rather than replacing it with an error state.
  }

  if (!overlay.hidden) _discordPollTimer = setTimeout(pollDiscordChat, 4500);
}

function buildDiscordMessageRow(m) {
  const row = document.createElement("div");
  row.className = "discord-message-row";
  const avatar = m.avatarUrl
    ? `<img src="${m.avatarUrl}" alt="" class="discord-avatar">`
    : `<div class="discord-avatar discord-avatar-fallback">${(m.author || "?")[0].toUpperCase()}</div>`;
  row.innerHTML = `
    ${avatar}
    <div class="discord-message-body">
      <div class="discord-message-meta">
        <span class="discord-message-author">${m.author}</span>
        <span class="discord-message-time">${discordRelativeTime(m.timestampUtc)}</span>
      </div>
      <div class="discord-message-text"></div>
    </div>`;
  // Set as textContent (not innerHTML) so message content can never inject markup.
  row.querySelector(".discord-message-text").textContent = m.content;
  return row;
}

function discordRelativeTime(timestampUtc) {
  const seconds = Math.max(0, Math.floor((Date.now() - new Date(timestampUtc).getTime()) / 1000));
  if (seconds < 60) return "just now";
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  return `${Math.floor(hours / 24)}d ago`;
}

/// End-user "import my own song" pipeline (item 21) -- runs the whole native flow (choose file,
/// name track, trim/normalize via TrimmerForm) synchronously on the C# side; this just kicks it
/// off and refreshes the grid on success. All three of those steps are modal WinForms dialogs,
/// so ImportLocalSong doesn't return until the user finishes or cancels.
async function importLocalSong() {
  const btn = document.getElementById("btn-import-local-song");
  if (!bridge || !btn) return;
  btn.disabled = true;
  try {
    const raw = await bridge.ImportLocalSong();
    const result = JSON.parse(raw);
    if (result.success) {
      showToast(`Imported "${result.name}" -- it's ready to assign to any trigger.`);
      loadMyDownloads();
    }
    // cancelled: true just means the user backed out of one of the dialogs -- not an error,
    // nothing to show.
  } catch (err) {
    console.error("ImportLocalSong failed", err);
    showToast("Couldn't import that song -- try again.");
  } finally {
    btn.disabled = false;
  }
}

// Nexus-Mods-style card, same DOM shape as buildItemTile's marketplace-card branch (see its
// comment for the XSS-sanitization reasoning -- name/school/fileUrl here trace back to whatever
// was typed when the item was uploaded/shared, same as any other marketplace-sourced string).
function buildMyDownloadRow(item) {
  const tile = document.createElement("div");
  tile.className = "marketplace-card my-downloads-card";
  const thumb = document.createElement("div");
  thumb.className = "marketplace-card-thumb";
  if (item.type === "image") {
    thumb.innerHTML = `<img src="${sanitizeHTML(item.fileUrl)}" alt="${sanitizeHTML(item.name)}" loading="lazy">`;
  } else {
    thumb.innerHTML = item.schoolLogoUrl
      ? `<img src="${sanitizeHTML(item.schoolLogoUrl)}" alt="${sanitizeHTML(item.school)}" loading="lazy">`
      : `<span>\u{1F3B5}</span>`;
  }
  thumb.innerHTML += `<span class="card-type-badge">${item.type === "image" ? "Background" : "Song"}</span>`;

  // Self-healing "My Downloads" (Music Library UX Brief v2 §2.3): the manifest can drift from
  // disk (file deleted outside the app), and GetMyDownloads now reports fileExists per entry
  // instead of silently rendering a dead file as if it were fine. A missing file stays visible
  // (so the user can see and remove it, per brief §4 -- "flagged, not silently absent") but is
  // marked and can't be previewed, since the underlying file is gone.
  if (item.fileExists === false) {
    tile.classList.add("bandroom-item-missing");
    const missingBadge = document.createElement("span");
    missingBadge.className = "card-type-badge my-downloads-missing-badge";
    missingBadge.textContent = "\u{26A0}\u{FE0F} Missing";
    missingBadge.title = "This file is no longer on disk -- remove it below.";
    thumb.appendChild(missingBadge);
  }

  const body = document.createElement("div");
  body.className = "marketplace-card-body";
  const title = document.createElement("div");
  title.className = "marketplace-card-title";
  title.textContent = item.name;
  const schoolRow = document.createElement("div");
  schoolRow.className = "marketplace-card-school";
  // Locally-imported tracks (item 21) have no school -- label them instead of showing a blank line.
  if (item.source === "local") {
    schoolRow.textContent = "Your library";
  } else {
    const team = state.teams?.find((t) => t.name === item.school);
    const dot = document.createElement("span");
    dot.className = "marketplace-card-school-dot";
    dot.style.background = team?.primary ?? "var(--text-muted)";
    schoolRow.append(dot, document.createTextNode(item.school));
    applySchoolGlow(schoolRow, item.school);
  }
  const dateEl = document.createElement("div");
  dateEl.className = "marketplace-card-uploader";
  try {
    dateEl.textContent = `Downloaded ${new Date(item.downloadedAt).toLocaleDateString(undefined, { month: "short", day: "numeric" })}`;
  } catch { dateEl.textContent = ""; }
  body.append(title, schoolRow, dateEl);
  tile.append(thumb, body);
  tile.title = item.source === "local" ? item.name : `${item.school} — ${item.name}`;

  tile.addEventListener("click", (e) => {
    if (e.target.closest(".bandroom-item-action")) return;
    if (item.fileExists === false) { showToast("That file is missing from disk -- remove it and re-download if needed."); return; }
    if (item.type === "song") previewSong({ url: item.fileUrl });
  });

  // Not reusing .bandroom-item-actions here -- that class is an absolute-positioned hover-reveal
  // overlay meant for hub tiles (see .bandroom-item-tile:hover), which would misplace these
  // buttons inside a marketplace-card-body's normal flow. This card can have up to 3 actions
  // (Share/Set Background/Remove) vs. marketplace's fixed 2 (Preview/Get), so it needs its own
  // always-visible wrapping row instead of the 2-button .marketplace-card-actions flex.
  const actions = document.createElement("div");
  actions.className = "my-downloads-card-actions";

  // Share to Marketplace (item 21) -- ONLY for tracks that came through the local-import
  // pipeline (item.source === "local") and haven't already been shared. Explicit opt-in per
  // track, never automatic -- nothing here fires without this click.
  if (item.source === "local" && item.canShare) {
    const shareBtn = document.createElement("button");
    shareBtn.className = "bandroom-item-action";
    shareBtn.title = "Share this track to the marketplace";
    shareBtn.textContent = "\u{1F4E4} Share";
    shareBtn.addEventListener("click", async (e) => {
      e.stopPropagation();
      const typed = window.prompt(`Share "${item.name}" to the marketplace -- which team is it for? (exact team name, e.g. "Georgia")`);
      if (!typed || !typed.trim()) return;
      const match = state.teams.find((t) => t.name.toLowerCase() === typed.trim().toLowerCase());
      if (!match) {
        showToast(`"${typed.trim()}" isn't a team in your roster -- use the exact team name so it shows up in that team's Sound Bank.`);
        return;
      }
      const school = match.name;
      shareBtn.disabled = true;
      shareBtn.textContent = "Sharing...";
      try {
        const raw = bridge ? await bridge.ShareLocalTrackToMarketplace(item.id, school) : null;
        const result = raw ? JSON.parse(raw) : null;
        if (result?.success) {
          showToast(`Shared "${item.name}" to ${school.trim()}'s Sound Bank!`);
          shareBtn.remove();
        } else {
          showToast(result?.error ?? "Couldn't share that -- try again.");
          shareBtn.disabled = false;
          shareBtn.textContent = "\u{1F4E4} Share";
        }
      } catch (err) {
        console.error("ShareLocalTrackToMarketplace failed", err);
        showToast("Couldn't share that -- try again.");
        shareBtn.disabled = false;
        shareBtn.textContent = "\u{1F4E4} Share";
      }
    });
    actions.appendChild(shareBtn);
  } else if (item.source === "local" && item.shared) {
    const sharedLabel = document.createElement("span");
    sharedLabel.className = "bandroom-item-action bandroom-item-action-static";
    sharedLabel.textContent = "\u{2705} Shared";
    actions.appendChild(sharedLabel);
  }

  // Set as Background -- image downloads used to need a trip back into that team's Trophy Room
  // tab to do this; now that Trophy Room is folded into My Downloads, this needs to live here
  // too (mirrors the same action in buildItemTile's album view).
  if (item.type === "image" && item.school) {
    const bgBtn = document.createElement("button");
    bgBtn.className = "bandroom-item-action";
    bgBtn.title = `Set as ${item.school}'s background`;
    bgBtn.textContent = "\u{1F5BC} Set as Background";
    bgBtn.addEventListener("click", async (e) => {
      e.stopPropagation();
      bgBtn.disabled = true;
      bgBtn.textContent = "Saving...";
      // This is an already-downloaded local file (item.fileUrl is a WebView2-only virtual-host
      // address, not a real network URL) -- SetTeamBackgroundFromDownload copies the local file
      // directly instead of trying to re-fetch it over HTTP like the album/hub version does.
      const ok = bridge ? await bridge.SetTeamBackgroundFromDownload(item.id) : false;
      if (ok) {
        showToast(`Set as ${item.school}'s background!`);
        if (state.activeTeam === item.school) applyBackground(item.school);
      } else {
        showToast("Couldn't set that background -- try again.");
      }
      bgBtn.disabled = false;
      bgBtn.textContent = "\u{1F5BC} Set as Background";
    });
    actions.appendChild(bgBtn);
  }

  const removeBtn = document.createElement("button");
  removeBtn.className = "bandroom-item-action bandroom-item-action-danger";
  removeBtn.title = "Remove from My Downloads";
  removeBtn.textContent = "\u{1F5D1}";
  removeBtn.addEventListener("click", async (e) => {
    e.stopPropagation();
    removeBtn.disabled = true;
    const ok = bridge ? await bridge.RemoveMyDownload(item.id) : false;
    if (ok) {
      showToast(`Removed "${item.name}".`);
      tile.remove();
      _myDownloadsItems = _myDownloadsItems.filter((i) => i.id !== item.id);
      const countEl = document.getElementById("my-downloads-count");
      if (countEl) countEl.textContent = _myDownloadsItems.length > 0
        ? `${_myDownloadsItems.length} download${_myDownloadsItems.length === 1 ? "" : "s"}`
        : "";
    } else { showToast("Couldn't remove that -- try again."); removeBtn.disabled = false; }
  });
  actions.appendChild(removeBtn);
  body.appendChild(actions);
  return tile;
}

// ---- Popular Songs + Top Team Background lists (Music Library UX Brief v2 §4) --------
// Row-list tables (same .bandroom-album-grid-list shape as the per-team album/My Downloads
// lists), ranked by downloads+likes for songs, seeded from the existing local TeamBackgrounds
// pack for backgrounds. Previously two horizontal auto-rotating shelves -- converted to lists so
// all three library surfaces (hub/album/downloads) read as one design system; the rotation timer
// this used to need is gone along with the shelf layout.

/// Popular Songs list -- ranked by downloads+likes combined (falls back to whatever single
/// sort the hub's dropdown has selected when that's not the default "newest"/combined view).
async function renderPopularSongsShelf() {
  const el = document.getElementById("bandroom-popular-shelf");
  if (!el) return;
  el.innerHTML = `<div class="bandroom-empty-state">Loading...</div>`;
  try {
    const songs = await fetchUploadList("song", null, null);
    if (document.getElementById("bandroom-overlay").hidden) return;
    let ranked = songs;
    if (_hubSort === "views" || _hubSort === "downloads" || _hubSort === "likes") {
      ranked = [...songs].sort((a, b) => (b[_hubSort] ?? 0) - (a[_hubSort] ?? 0));
    } else {
      // Default ranking: downloads + likes combined, per the owner's "ranked by downloads+likes"
      // spec, ties broken newest-first.
      ranked = [...songs].sort((a, b) => {
        const scoreDiff = ((b.downloads ?? 0) + (b.likes ?? 0)) - ((a.downloads ?? 0) + (a.likes ?? 0));
        return scoreDiff !== 0 ? scoreDiff : (a.uploadedAt < b.uploadedAt ? 1 : -1);
      });
    }
    ranked = ranked.slice(0, 20);
    el.innerHTML = "";
    if (ranked.length === 0) {
      el.innerHTML = `<div class="bandroom-empty-state">No songs uploaded yet -- open any team's Sound Bank and be the first!</div>`;
      return;
    }
    // Row-list table (Music Library UX Brief v2 §4), same .marketplace-card row branch the
    // per-team album/My Downloads lists use -- was a "jump straight to that team's album" hub
    // tile before this pass (buildItemTile(item, true)); the row branch's own click still
    // previews the song (its actions already cover Preview/Get), so the school name is wired
    // separately to keep the "jump to that team's Sound Bank" shortcut the hub had.
    for (const item of ranked) {
      const row = buildItemTile(item);
      const schoolRow = row.querySelector(".marketplace-card-school");
      if (schoolRow) {
        schoolRow.title = `Open ${item.school}'s Sound Bank`;
        schoolRow.addEventListener("click", (e) => {
          e.stopPropagation();
          openTeamAlbum(item.school);
        });
      }
      el.appendChild(row);
    }
  } catch (err) {
    console.error("renderPopularSongsShelf failed", err);
    el.innerHTML = `<div class="bandroom-empty-state">Couldn't load popular songs right now.</div>`;
  }
}

/// Top Team Background Uploads list -- seeded from the existing local TeamBackgrounds pack
/// (bridge.GetTeamBackgroundUrl, same source used everywhere else backgrounds are shown) rather
/// than a live marketplace image feed, per the owner's "seeded from the existing pack for now"
/// scoping. Clicking a row jumps into that team's Sound Bank, same as every other hub item.
/// Row-list table (Music Library UX Brief v2 §4) instead of the old horizontal shelf, matching
/// the album/downloads list shape -- built by hand (not buildItemTile) since these items only
/// carry a team + url, not the full marketplace item shape buildItemTile expects.
async function renderTopTeamBackgroundsShelf() {
  const el = document.getElementById("bandroom-backgrounds-shelf");
  if (!el) return;
  el.innerHTML = `<div class="bandroom-empty-state">Loading...</div>`;
  if (!bridge || !Array.isArray(state.teams)) {
    el.innerHTML = `<div class="bandroom-empty-state">Team backgrounds aren't available right now.</div>`;
    return;
  }
  try {
    const results = await Promise.all(
      state.teams.map(async (team) => {
        const url = await bridge.GetTeamBackgroundUrl(team.name).catch(() => null);
        return url ? { team, url } : null;
      })
    );
    if (document.getElementById("bandroom-overlay").hidden) return;
    const withBackgrounds = results.filter(Boolean).slice(0, 20);
    el.innerHTML = "";
    if (withBackgrounds.length === 0) {
      el.innerHTML = `<div class="bandroom-empty-state">No team backgrounds available yet.</div>`;
      return;
    }
    for (const { team, url } of withBackgrounds) {
      const row = document.createElement("div");
      row.className = "marketplace-card";
      row.title = `${team.name} background`;
      const thumb = document.createElement("div");
      thumb.className = "marketplace-card-thumb";
      thumb.innerHTML = `<img src="${sanitizeHTML(url)}" alt="${sanitizeHTML(team.name)}" loading="lazy">`;
      const body = document.createElement("div");
      body.className = "marketplace-card-body";
      const title = document.createElement("div");
      title.className = "marketplace-card-title";
      title.textContent = team.name;
      const schoolRow = document.createElement("div");
      schoolRow.className = "marketplace-card-school";
      schoolRow.textContent = "Team Background";
      body.append(title, schoolRow);
      const actions = document.createElement("div");
      actions.className = "marketplace-card-actions";
      const openBtn = document.createElement("button");
      openBtn.className = "btn-ghost";
      openBtn.textContent = "Open Sound Bank";
      openBtn.addEventListener("click", (e) => { e.stopPropagation(); openTeamAlbum(team.name); });
      actions.appendChild(openBtn);
      body.appendChild(actions);
      row.append(thumb, body);
      row.addEventListener("click", () => openTeamAlbum(team.name));
      el.appendChild(row);
    }
  } catch (err) {
    console.error("renderTopTeamBackgroundsShelf failed", err);
    el.innerHTML = `<div class="bandroom-empty-state">Couldn't load team backgrounds right now.</div>`;
  }
}

// Shortcut hero row at the top of the hub's main pane (Music Library UX Brief v2 §4) -- reuses
// the existing #bandroom-hub-sort ranking modes (_hubSort) as clickable cards instead of adding
// a second, separate sort mechanism. Clicking a card sets _hubSort and re-renders the shelves,
// same effect as picking that option from the dropdown.
function renderBandroomHubHeroRow() {
  const row = document.getElementById("bandroom-hub-hero-row");
  if (!row) return;
  const cards = [
    { sort: "newest", icon: "\u{1F195}", label: "Newest Uploads", caption: "Freshest first" },
    { sort: "downloads", icon: "\u{2B07}\u{FE0F}", label: "Most Downloaded", caption: "Crowd favorites" },
    { sort: "likes", icon: "\u{2764}\u{FE0F}", label: "Most Liked", caption: "Top rated" },
    { sort: "views", icon: "\u{1F441}\u{FE0F}", label: "Most Viewed", caption: "Trending" },
  ];
  row.innerHTML = "";
  for (const card of cards) {
    const el = document.createElement("div");
    el.className = "bandroom-hero-card" + (_hubSort === card.sort ? " active" : "");
    el.innerHTML = `
      <div class="bandroom-hero-card-icon">${card.icon}</div>
      <div>
        <div class="bandroom-hero-card-label">${card.label}</div>
        <div class="bandroom-hero-card-count">${card.caption}</div>
      </div>`;
    el.addEventListener("click", () => {
      _hubSort = card.sort;
      const sortSelect = document.getElementById("bandroom-hub-sort");
      if (sortSelect) sortSelect.value = card.sort;
      renderBandroomHub();
    });
    row.appendChild(el);
  }
}

// Kept as the shared refresh entry point other call sites (edit/delete handlers, sort-change
// listener) already use -- now refreshes both shelves instead of the old single grid.
async function renderBandroomHub() {
  renderBandroomHubHeroRow();
  await Promise.all([renderPopularSongsShelf(), renderTopTeamBackgroundsShelf()]);
}

// Song tiles use the uploading team's logo instead of a generic note icon, so every song tile
// in a given team's Sound Bank -- and in My Downloads -- looks uniform and immediately tells you
// whose song it is, the same way image tiles already show the real uploaded picture.
function teamLogoUrl(schoolName) {
  const team = state.teams?.find((t) => t.name === schoolName);
  return team?.logoUrl ?? null;
}

/// Sets the school-name text to glow in that team's color (--school-glow, consumed by
/// .bandroom-item-school[data-glow]/.marketplace-card-school[data-glow] in style.css). Per-item,
/// not the active team's --team-primary, since a tile grid can mix schools. Same near-black
/// fallback isNearBlack() (shared with setActiveTeam/previewTeamGlow) uses -- a few teams' primary
/// is literal black, which glows invisibly, so fall back to secondary for those.
function applySchoolGlow(el, schoolName) {
  const team = state.teams?.find((t) => t.name === schoolName);
  if (!team) return;
  const fallback = isNearBlack(team.secondary) ? "#22d3ee" : team.secondary;
  const glow = isNearBlack(team.primary) ? (fallback ?? team.primary) : team.primary;
  if (!glow) return;
  el.style.setProperty("--school-glow", glow);
  el.setAttribute("data-glow", "");
}

// inHub param removed 2026-08-09: the hub's Popular Songs/Top Team Backgrounds sections used to
// call this with inHub=true to get a compact .bandroom-item-tile (see Session 22's row-list
// conversion, which switched both hub call sites to the row/.marketplace-card branch below); no
// call site passes true anymore, so the old tile-branch was dead code and has been removed rather
// than left unreachable. If a compact tile view is ever wanted again, restore from git history
// instead of resurrecting a parameter no current caller uses.
function buildItemTile(item) {
  const tile = document.createElement("div");
  tile.className = "marketplace-card";
  const thumb = document.createElement("div");
  thumb.className = "marketplace-card-thumb";
  // item.name/item.school/item.url come from other users' marketplace uploads (worker.js
  // stores whatever string was posted for "name"/"school") -- innerHTML with those interpolated
  // raw is a real stored-XSS vector, sanitizeHTML() actually needs to run here rather than just
  // exist unused elsewhere in the file.
  if (item.type === "image") {
    thumb.innerHTML = `<img src="${sanitizeHTML(item.url)}" alt="${sanitizeHTML(item.name)}" loading="lazy">`;
    thumb.innerHTML += '<span class="card-type-badge">IMAGE</span>';
  } else {
    const logo = teamLogoUrl(item.school);
    thumb.innerHTML = logo
      ? `<img src="${sanitizeHTML(logo)}" alt="${sanitizeHTML(item.school)}" loading="lazy">`
      : `<span>\u{1F3B5}</span>`;
    thumb.innerHTML += '<span class="card-type-badge">SONG</span>';
  }

  {
    const body = document.createElement("div");
    body.className = "marketplace-card-body";
    const title = document.createElement("div");
    title.className = "marketplace-card-title";
    title.textContent = item.name;
    const schoolRow = document.createElement("div");
    schoolRow.className = "marketplace-card-school";
    const team = state.teams?.find((t) => t.name === item.school);
    const dot = document.createElement("span");
    dot.className = "marketplace-card-school-dot";
    dot.style.background = team?.primary ?? "var(--text-muted)";
    schoolRow.append(dot, document.createTextNode(item.school));
    applySchoolGlow(schoolRow, item.school);
    const meta = document.createElement("div");
    meta.className = "marketplace-card-meta";
    meta.innerHTML = `<span>\u{2B07} ${(item.downloads ?? 0).toLocaleString()}</span><span>\u{2661} ${(item.likes ?? 0).toLocaleString()}</span>`;
    const uploader = document.createElement("div");
    uploader.className = "marketplace-card-uploader";
    const ago = item.uploadedAt ? relativeTime(item.uploadedAt) : "";
    uploader.textContent = `Uploaded by ${item.uploadedBy ?? "anonymous"}${ago ? " \u00B7 " + ago : ""}`;
    body.append(title, schoolRow, meta, uploader);

    const actions = document.createElement("div");
    actions.className = "marketplace-card-actions";
    const previewBtn = document.createElement("button");
    previewBtn.className = "btn-ghost";
    previewBtn.textContent = "\u{25B6} Preview";
    previewBtn.addEventListener("click", (e) => { e.stopPropagation(); previewSong(item); });
    actions.appendChild(previewBtn);
    const dlBtn = document.createElement("button");
    dlBtn.className = "btn-ghost";
    dlBtn.textContent = "\u{2B07} Get";
    dlBtn.addEventListener("click", async (e) => {
      e.stopPropagation(); dlBtn.disabled = true; dlBtn.textContent = "...";
      const ok = bridge ? await downloadMarketplaceItem(item) : false;
      showToast(ok ? `Downloaded "${item.name}"!` : "Couldn't download that.");
      dlBtn.disabled = false; dlBtn.textContent = "\u{2B07} Get";
    });
    actions.appendChild(dlBtn);
    body.appendChild(actions);
    tile.append(thumb, body);
  }
  tile.title = `${item.name} \u2014 ${item.school}`;
  tile.addEventListener("click", (e) => {
    if (e.target.closest(".bandroom-item-action")) return; // hover-button clicks handle themselves
    if (item.type === "song") previewSong(item);
  });

  // Hover action row. Like/Report are always available; Set as Background only for Trophy Room
  // images; Delete only shows on tiles this browser itself uploaded (item 5).
  {
    const actions = document.createElement("div");
    actions.className = "bandroom-item-actions";

    const likeBtn = document.createElement("button");
    likeBtn.className = "bandroom-item-action";
    likeBtn.title = "Like this upload";
    likeBtn.textContent = `♡ ${item.likes ?? 0}`;
    likeBtn.addEventListener("click", async (e) => {
      e.stopPropagation();
      likeBtn.disabled = true;
      const newCount = await likeUploadedItem(item);
      if (newCount != null) { likeBtn.textContent = `♥ ${newCount}`; }
      else { likeBtn.disabled = false; showToast("Couldn't like that right now."); }
    });
    actions.appendChild(likeBtn);

    const dislikeBtn = document.createElement("button");
    dislikeBtn.className = "bandroom-item-action";
    dislikeBtn.title = "Dislike this upload";
    dislikeBtn.textContent = `\u{1F44E} ${item.dislikes ?? 0}`;
    dislikeBtn.addEventListener("click", async (e) => {
      e.stopPropagation();
      dislikeBtn.disabled = true;
      const newCount = await dislikeUploadedItem(item);
      if (newCount != null) { dislikeBtn.textContent = `\u{1F44E} ${newCount}`; }
      else { dislikeBtn.disabled = false; showToast("Couldn't register that right now."); }
    });
    actions.appendChild(dislikeBtn);

    const dlBtn = document.createElement("button");
    dlBtn.className = "bandroom-item-action";
    dlBtn.title = "Download to My Downloads";
    dlBtn.textContent = "\u{2B07}"; // down arrow
    dlBtn.addEventListener("click", async (e) => {
      e.stopPropagation();
      dlBtn.disabled = true;
      dlBtn.textContent = "...";
      const ok = bridge ? await downloadMarketplaceItem(item) : false;
      showToast(ok
        ? `Downloaded "${item.name}" -- see it in My Downloads.`
        : "Couldn't download that -- try again.");
      dlBtn.disabled = false;
      dlBtn.textContent = "\u{2B07}";
    });
    actions.appendChild(dlBtn);

    if (item.type === "image") {
      const bgBtn = document.createElement("button");
      bgBtn.className = "bandroom-item-action";
      bgBtn.title = `Set as ${item.school}'s background`;
      bgBtn.textContent = "\u{1F5BC} Set as Background";
      bgBtn.addEventListener("click", async (e) => {
        e.stopPropagation();
        bgBtn.disabled = true;
        bgBtn.textContent = "Saving...";
        const ok = bridge ? await bridge.DownloadAndSetTeamBackground(item.school, item.url) : false;
        if (ok) {
          showToast(`Set as ${item.school}'s background!`);
          if (state.activeTeam === item.school) applyBackground(item.school);
        } else {
          showToast("Couldn't set that background -- try again.");
        }
        bgBtn.disabled = false;
        bgBtn.textContent = "\u{1F5BC} Set as Background";
      });
      actions.appendChild(bgBtn);
    }

    const reportBtn = document.createElement("button");
    reportBtn.className = "bandroom-item-action";
    reportBtn.title = "Report this upload";
    reportBtn.textContent = "\u{1F6A9}";
    reportBtn.addEventListener("click", async (e) => {
      e.stopPropagation();
      reportBtn.disabled = true;
      const ok = await reportUploadedItem(item);
      showToast(ok ? "Reported -- thanks for flagging it." : "Couldn't report that right now.");
      if (!ok) reportBtn.disabled = false;
    });
    actions.appendChild(reportBtn);

    if (myUploadToken(item.id)) {
      const editBtn = document.createElement("button");
      editBtn.className = "bandroom-item-action";
      editBtn.title = "Edit your upload's name/school";
      editBtn.textContent = "\u{270F}\u{FE0F}";
      editBtn.addEventListener("click", async (e) => {
        e.stopPropagation();
        const edited = await editUploadDialog(item.name, item.school, "Edit Upload");
        if (edited === null) return;
        const newName = edited.name;
        const typedSchool = edited.school;
        // ROOT CAUSE FIX (Music Library UX Brief v2 §1/§2.2 team-key mismatch): this used to send
        // whatever free text was typed here straight to the worker's PATCH /item with zero
        // validation against the actual team roster -- unlike the "Share to Marketplace" flow
        // (buildMyDownloadRow's shareBtn handler), which already resolves the typed name against
        // state.teams before using it. A typo/abbreviation/stray-whitespace school here (e.g.
        // "UGA" or "Georgia " instead of "Georgia") would still PATCH successfully, silently
        // detaching the item from its team: fetchUploadList("school","Georgia") does an exact
        // (case-insensitive) match against the worker's stored meta.school, so a mistyped school
        // here makes the item permanently invisible in that team's Sound Bank/Trophy Room even
        // though it's still sitting in the worker's index -- the exact "modal opens, list is
        // empty despite real uploads" symptom reported for Georgia. Resolving against the
        // canonical roster here closes that off the same way the Share flow already does.
        const matchedTeam = state.teams.find((t) => t.name.toLowerCase() === typedSchool.trim().toLowerCase());
        if (!matchedTeam) {
          showToast(`"${typedSchool.trim()}" isn't a team in your roster -- use the exact team name (e.g. "Georgia").`);
          return;
        }
        const newSchool = matchedTeam.name;
        editBtn.disabled = true;
        const result = await editUploadedItem(item, newName, newSchool);
        if (result) {
          showToast(`Updated "${result.name}".`);
          item.name = result.name;
          item.school = result.school;
          renderBandroomHub();
        } else {
          showToast("Couldn't update that -- try again.");
        }
        editBtn.disabled = false;
      });
      actions.appendChild(editBtn);

      const delBtn = document.createElement("button");
      delBtn.className = "bandroom-item-action bandroom-item-action-danger";
      delBtn.title = "Delete your upload";
      delBtn.textContent = "\u{1F5D1}";
      delBtn.addEventListener("click", async (e) => {
        e.stopPropagation();
        delBtn.disabled = true;
        const ok = await deleteUploadedItem(item);
        if (ok) {
          showToast(`Deleted "${item.name}".`);
          tile.remove();
        } else {
          showToast("Couldn't delete that -- try again.");
          delBtn.disabled = false;
        }
      });
      actions.appendChild(delBtn);
    }

    // Admin-only controls (bypass ownerToken via X-Admin-Token) -- only ever rendered when
    // _isAdminMode is true, which only happens on the app owner's own dev build (see
    // WebBridge.cs's IsAdminMode/AdminTokenPath). Visually distinct "ADMIN" tag so it's never
    // confused with the regular per-owner "delete your own upload" control above.
    if (_isAdminMode) {
      const adminEditBtn = document.createElement("button");
      adminEditBtn.className = "bandroom-item-action bandroom-item-action-admin";
      adminEditBtn.title = "Admin: Edit";
      // Icon-only (no "ADMIN" text label) -- with like/download/report/delete/admin-edit/
      // admin-delete all sharing one small tile's hover row, the old text pills were wide
      // enough to force multiple wrapped rows that overflowed the thumb and overlapped the
      // tile's name/school text below (the "garbled stacked badges" bug). Gold border/color
      // still marks these as admin-only, same as before.
      adminEditBtn.textContent = "\u{1F6E0}";
      adminEditBtn.addEventListener("click", async (e) => {
        e.stopPropagation();
        const edited = await editUploadDialog(item.name, item.school, "Admin: Edit Upload");
        if (edited === null) return;
        const newName = edited.name;
        const typedSchool = edited.school;
        // Same team-key validation as the per-owner edit above (root cause fix, Music Library UX
        // Brief v2 §1/§2.2) -- the admin path had the identical unvalidated free-text hole.
        const matchedAdminTeam = state.teams.find((t) => t.name.toLowerCase() === typedSchool.trim().toLowerCase());
        if (!matchedAdminTeam) {
          showToast(`"${typedSchool.trim()}" isn't a team in your roster -- use the exact team name (e.g. "Georgia").`);
          return;
        }
        const newSchool = matchedAdminTeam.name;
        adminEditBtn.disabled = true;
        try {
          const raw = await bridge.AdminEditMarketplaceItem(item.type, item.id, newName, newSchool);
          const result = JSON.parse(raw);
          showToast(result.success ? `Admin-edited "${newName}".` : "Admin edit failed -- try again.");
          if (result.success) renderBandroomHub();
        } catch (err) {
          console.error("AdminEditMarketplaceItem failed", err);
          showToast("Admin edit failed -- try again.");
        }
        adminEditBtn.disabled = false;
      });
      actions.appendChild(adminEditBtn);

      const adminDelBtn = document.createElement("button");
      adminDelBtn.className = "bandroom-item-action bandroom-item-action-admin bandroom-item-action-danger";
      adminDelBtn.title = "Admin: Delete";
      adminDelBtn.textContent = "\u{1F6E0}\u{1F5D1}"; // icon-only, see adminEditBtn comment above
      adminDelBtn.addEventListener("click", async (e) => {
        e.stopPropagation();
        adminDelBtn.disabled = true;
        try {
          const raw = await bridge.AdminDeleteMarketplaceItem(item.type, item.id);
          const result = JSON.parse(raw);
          if (result.success) {
            showToast(`Admin-deleted "${item.name}".`);
            tile.remove();
          } else {
            showToast("Admin delete failed -- try again.");
            adminDelBtn.disabled = false;
          }
        } catch (err) {
          console.error("AdminDeleteMarketplaceItem failed", err);
          showToast("Admin delete failed -- try again.");
          adminDelBtn.disabled = false;
        }
      });
      actions.appendChild(adminDelBtn);
    }

    tile.appendChild(actions);
  }

  return tile;
}

// ---- Shared song-preview bar (item 2) ---------------------------------------------------
// ONE player + ONE waveform-scrubber, reused by every preview surface (marketplace tile preview,
// My Downloads preview) instead of a per-surface implementation. No prior waveform-rendering code
// existed anywhere in app.js/wwwroot (the only existing waveform code, WaveformRenderer.cs, is a
// native WinForms/GDI+ component used exclusively by TrimmerForm's own clip-preview UI and can't
// be shared with the web side), so this builds the one JS version everything web-side goes
// through: renderWaveformScrubber(canvas, audioBuffer, onSeek).
let _previewAudio = null;
let _previewAudioCtx = null;
let _previewWaveformPeaks = null; // Float32Array of per-bucket peak amplitudes, 0..1
let _previewRaf = null;

function previewSong(item) {
  try {
    _previewAudio?.pause();
    bridge?.StopPreview(); // stop any native (local-file) preview -- separate audio pathway
    _previewAudio = new Audio(item.url);
    _previewAudio.crossOrigin = "anonymous";
    _previewAudio.volume = Number(document.getElementById("slider-volume")?.value ?? 72) / 100;
    _previewAudio.play().catch((err) => console.error("Song preview failed", err));
    // Only marketplace items carry an id (My Downloads tiles pass a bare {url} -- see
    // buildMyDownloadRow -- which have no server-side item to increment).
    if (item.id && item.type) recordItemView(item);
    showPreviewBar(item);
    loadPreviewWaveform(item.url);
  } catch (err) {
    console.error("Song preview failed", err);
  }
}

function showPreviewBar(item) {
  const bar = document.getElementById("preview-bar");
  if (!bar) return;
  const assignPanel = document.getElementById("clipper-assign");
  if (assignPanel && !assignPanel.hidden) closeClipperAssign();
  const empty = document.getElementById("clipper-empty");
  if (empty) empty.hidden = true;
  document.getElementById("preview-name").textContent = item.name || "Preview";
  document.getElementById("btn-preview-playpause").textContent = "⏸"; // pause glyph, we just started playing
  _previewWaveformPeaks = null; // clear the old track's waveform until the new one decodes
  drawPreviewWaveform(0);
  bar.hidden = false;
  cancelAnimationFrame(_previewRaf);
  const tick = () => {
    if (_previewAudio && !_previewAudio.paused) {
      updatePreviewTime();
      drawPreviewWaveform(previewProgress());
    }
    _previewRaf = requestAnimationFrame(tick);
  };
  _previewRaf = requestAnimationFrame(tick);
}

function previewProgress() {
  if (!_previewAudio || !_previewAudio.duration) return 0;
  return Math.min(1, _previewAudio.currentTime / _previewAudio.duration);
}

function updatePreviewTime() {
  const el = document.getElementById("preview-time");
  if (!el || !_previewAudio) return;
  const s = Math.max(0, Math.floor(_previewAudio.currentTime || 0));
  el.textContent = `${Math.floor(s / 60)}:${String(s % 60).padStart(2, "0")}`;
}

function stopPreview() {
  if (_previewAudio) {
    _previewAudio.pause();
    _previewAudio.currentTime = 0;
  }
  cancelAnimationFrame(_previewRaf);
  document.getElementById("btn-preview-playpause").textContent = "▶";
  updatePreviewTime();
  drawPreviewWaveform(0);
}

function toggPreviewPlayPause() {
  if (!_previewAudio) return;
  if (_previewAudio.paused) {
    _previewAudio.play().catch(() => {});
    document.getElementById("btn-preview-playpause").textContent = "⏸";
  } else {
    _previewAudio.pause();
    document.getElementById("btn-preview-playpause").textContent = "▶";
  }
}

/// Decodes the audio via Web Audio API purely to get peak amplitude data for the waveform --
/// playback itself still goes through the plain <audio> element above (simpler seeking/streaming
/// than routing playback through an AudioBufferSourceNode). Best-effort: a CORS/decode failure
/// just leaves the waveform flat/empty rather than breaking playback or the preview bar.
async function loadPreviewWaveform(url) {
  try {
    _previewAudioCtx ??= new (window.AudioContext || window.webkitAudioContext)();
    const resp = await fetch(url);
    const arrayBuf = await resp.arrayBuffer();
    const audioBuf = await _previewAudioCtx.decodeAudioData(arrayBuf);
    const channel = audioBuf.getChannelData(0);
    const buckets = 200;
    const peaks = new Float32Array(buckets);
    const bucketSize = Math.max(1, Math.floor(channel.length / buckets));
    for (let b = 0; b < buckets; b++) {
      let max = 0;
      const start = b * bucketSize;
      const end = Math.min(channel.length, start + bucketSize);
      for (let i = start; i < end; i++) max = Math.max(max, Math.abs(channel[i]));
      peaks[b] = max;
    }
    _previewWaveformPeaks = peaks;
    drawPreviewWaveform(previewProgress());
  } catch (err) {
    console.error("loadPreviewWaveform failed", err);
    _previewWaveformPeaks = null;
  }
}

function drawPreviewWaveform(progress) {
  const canvas = document.getElementById("preview-waveform");
  if (!canvas) return;
  renderWaveformScrubber(canvas, _previewWaveformPeaks, progress);
}

/// Shared waveform-scrubber renderer -- draws `peaks` (a Float32Array of 0..1 bucket amplitudes,
/// or null while still loading/unavailable) into `canvas`, with everything left of `progress`
/// (0..1) drawn in the accent color and the rest dimmed. The ONLY waveform-drawing function in
/// the web app; every preview surface funnels through drawPreviewWaveform -> here rather than
/// each having its own canvas math.
function renderWaveformScrubber(canvas, peaks, progress) {
  const ctx = canvas.getContext("2d");
  const w = canvas.width, h = canvas.height;
  ctx.clearRect(0, 0, w, h);
  const bars = peaks && peaks.length ? peaks.length : 60;
  const gap = 2;
  const barW = w / bars - gap;
  const mid = h / 2;
  for (let i = 0; i < bars; i++) {
    const amp = peaks && peaks.length ? peaks[i] : 0.15; // flat placeholder while loading
    const barH = Math.max(2, amp * (h - 4));
    const x = i * (barW + gap);
    const played = i / bars <= progress;
    ctx.fillStyle = played ? "rgba(255,255,255,0.9)" : "rgba(255,255,255,0.28)";
    ctx.fillRect(x, mid - barH / 2, barW, barH);
  }
}

function wirePreviewBar() {
  const bar = document.getElementById("preview-bar");
  if (!bar) return;
  document.getElementById("btn-preview-stop").addEventListener("click", stopPreview);
  document.getElementById("btn-preview-playpause").addEventListener("click", toggPreviewPlayPause);

  const canvas = document.getElementById("preview-waveform");
  const seekFromEvent = (e) => {
    if (!_previewAudio || !_previewAudio.duration) return;
    const rect = canvas.getBoundingClientRect();
    const frac = Math.min(1, Math.max(0, (e.clientX - rect.left) / rect.width));
    _previewAudio.currentTime = frac * _previewAudio.duration;
    updatePreviewTime();
    drawPreviewWaveform(frac);
  };
  let dragging = false;
  canvas.addEventListener("mousedown", (e) => { dragging = true; seekFromEvent(e); });
  window.addEventListener("mousemove", (e) => { if (dragging) seekFromEvent(e); });
  window.addEventListener("mouseup", () => { dragging = false; });
  canvas.addEventListener("click", seekFromEvent);
}

// ---- Clipping island: assign mode -------------------------------------------------------
// Replaces the native AssignTrackForm popup for the main "Assign / Edit" / "Assign PA" flow --
// the whole point of moving this here (owner request) is not having a separate window steal
// focus mid-game. Browse/Trim still hand off to native dialogs (OpenFileDialog, TrimmerForm)
// since those need real filesystem access / waveform-cut tooling a web view doesn't have.
let _clipperAssignTrigger = null;
let _clipperAssignIsPa = false;
let _clipperAssignLibrary = null; // cached [{name, path}], same list for every trigger
let _clipperAssignSelectedPath = null;
let _clipperAssignSelectedName = null;
let _clipperAssignMode = "event"; // "event" (assign/trim a situation's clip) | "whistle" (pick+trim the global lead-in whistle) | "alt-whistle" (pick+trim one event's own alt whistle -- keeps _clipperAssignTrigger, unlike "whistle")
// Owner request: default to just this team's Sound Bank (source "default") instead of every
// source dumped together -- "all" and the individual source names (see CLIPPER_ASSIGN_SOURCE_*
// below) are one pill-click away, not hidden. Resets to "default" every time the panel opens
// (openClipperAssign) so leaving it on "All Songs" for one event doesn't silently carry over and
// hide the Sound Bank on the next.
let _clipperAssignFilter = "default";
let _clipperAssignFiltersBound = false;

// "Browse another team's Sound Bank" -- separate from the pill filter above (which only ever
// scopes to state.activeTeam's own pack). Only one browsed team stacks at a time; picking a new
// one replaces the last so the list doesn't grow unbounded across a long session. Rendered as its
// own always-visible section (see renderClipperAssignList) regardless of which pill is active,
// since browsing a team is an explicit action independent of the source filter.
let _clipperBrowsedTeam = null;
let _clipperBrowsedSongs = [];
let _clipperPackTeams = null; // cached [{Team, Conference}], loaded once from bridge.GetDefaultPackTeams()
let _clipperBrowseTeamBound = false;

function initClipperAssignFilters() {
  if (_clipperAssignFiltersBound) return;
  _clipperAssignFiltersBound = true;
  document.getElementById("clipper-assign-filters").addEventListener("click", (e) => {
    const btn = e.target.closest(".clipper-assign-filter");
    if (!btn) return;
    document.querySelectorAll(".clipper-assign-filter").forEach((b) => b.classList.remove("active"));
    btn.classList.add("active");
    _clipperAssignFilter = btn.dataset.filter;
    renderClipperAssignList(document.getElementById("clipper-assign-search")?.value || "");
  });
}

/// "Browse another team's Sound Bank" popover -- lists every team with a pack slice on disk
/// (bridge.GetDefaultPackTeams, cached after first load) so a song can be found in a different
/// school's collection for the event currently being assigned. Picking a team fetches that team's
/// songs the same way the default pill already does (bridge.GetDefaultPackSongsForTeam) and folds
/// them into the Assign list as their own section -- see renderClipperAssignList.
function wireBrowseOtherTeamSoundBank() {
  if (_clipperBrowseTeamBound) return;
  _clipperBrowseTeamBound = true;

  const btn = document.getElementById("btn-clipper-browse-other-team");
  const popover = document.getElementById("clipper-browse-team-popover");
  const search = document.getElementById("clipper-browse-team-search");

  btn.addEventListener("click", async () => {
    const opening = popover.hidden;
    popover.hidden = !opening;
    if (!opening) return;
    search.value = "";
    search.focus();
    if (!_clipperPackTeams) {
      try {
        _clipperPackTeams = bridge ? JSON.parse(await bridge.GetDefaultPackTeams()) : [];
      } catch (err) {
        console.error("GetDefaultPackTeams failed", err);
        _clipperPackTeams = [];
      }
    }
    renderClipperBrowseTeamList("");
  });
  search.addEventListener("input", (e) => renderClipperBrowseTeamList(e.target.value));
}

function renderClipperBrowseTeamList(filter) {
  const list = document.getElementById("clipper-browse-team-list");
  const q = filter.toLowerCase().trim();
  const teams = (_clipperPackTeams || []).filter((t) => !q || t.Team.toLowerCase().includes(q));
  list.innerHTML = "";
  if (!teams.length) {
    list.innerHTML = `<div class="clipper-browse-team-empty">No teams found${q ? " for that search" : " -- the Default Song Pack may not be downloaded yet"}.</div>`;
    return;
  }
  for (const t of teams) {
    const row = document.createElement("div");
    row.className = "clipper-browse-team-row";
    row.innerHTML = `<span>${t.Team}</span><span class="clipper-browse-team-row-conf">${t.Conference}</span>`;
    row.addEventListener("click", () => pickBrowseTeam(t.Team));
    list.appendChild(row);
  }
}

async function pickBrowseTeam(teamName) {
  document.getElementById("clipper-browse-team-popover").hidden = true;
  if (!bridge) return;
  try {
    const songs = (JSON.parse(await bridge.GetDefaultPackSongsForTeam(teamName)) || [])
      .map((s) => ({ ...s, source: "browsed" }));
    _clipperBrowsedTeam = teamName;
    _clipperBrowsedSongs = songs;
  } catch (err) {
    console.error("GetDefaultPackSongsForTeam (browse) failed", err);
    showToast(`Couldn't load ${teamName}'s Sound Bank -- try again.`);
    return;
  }
  renderClipperAssignList(document.getElementById("clipper-assign-search")?.value || "");
}

function clearBrowsedTeam() {
  _clipperBrowsedTeam = null;
  _clipperBrowsedSongs = [];
  renderClipperAssignList(document.getElementById("clipper-assign-search")?.value || "");
}

async function openClipperAssign(trigger, eventName, isPa, currentFileName, mode = "event") {
  // Switching events (or into whistle mode) while the inline trimmer was left open for a
  // PREVIOUS session otherwise left its waveform/trim state on screen (clipper-assign-list
  // stays hidden, clipper-trim-panel stays visible) instead of resetting to the new song list.
  if (_trimTrigger || _trimForWhistle) closeInlineTrimmer();

  _clipperAssignMode = mode;
  _clipperAssignTrigger = trigger;
  _clipperAssignIsPa = isPa;
  _clipperAssignSelectedPath = null;
  _clipperAssignSelectedName = null;

  // "whistle" (global) and "alt-whistle" (one event's own override) both pick+trim a library song
  // with no pre-existing assignment to fall back on -- same UI shape, different save target (see
  // btn-trim-whistle's click handler below).
  const isWhistleMode = mode === "whistle" || mode === "alt-whistle";

  stopPreview();
  document.getElementById("clipper-empty").hidden = true;
  document.getElementById("preview-bar").hidden = true;
  document.getElementById("clipper-title-text").textContent =
    mode === "whistle" ? "Choose a Lead-In Whistle" : mode === "alt-whistle" ? "Choose an Alternate Whistle" : isPa ? "Assign PA Announcer Clip" : "Assign Track";
  document.getElementById("btn-clipper-close-assign").hidden = false;
  document.getElementById("clipper-assign-event").textContent = isWhistleMode
    ? (mode === "alt-whistle" ? `for ${friendlyEventName(eventName)}` : "")
    : `for ${friendlyEventName(eventName)}`;
  document.getElementById("clipper-assign-current").textContent =
    isWhistleMode ? "Pick a song below, then Trim... it down to your whistle sound." : currentFileName ? `Current: ${currentFileName}` : "Current: (none assigned)";
  document.getElementById("clipper-assign-search").value = "";
  initClipperAssignFilters();
  wireBrowseOtherTeamSoundBank();
  _clipperAssignFilter = "default";
  document.querySelectorAll(".clipper-assign-filter").forEach((b) => b.classList.toggle("active", b.dataset.filter === "default"));
  document.getElementById("btn-clipper-assign-select").disabled = true;
  document.getElementById("btn-clipper-assign-select").hidden = isWhistleMode;
  document.getElementById("btn-clipper-assign-clear").hidden = isWhistleMode;
  // Event mode: Trim... always trims whatever's already assigned to this trigger. Whistle modes
  // have no existing assignment to fall back on, so they need a library row (or browsed file)
  // picked first.
  document.getElementById("btn-clipper-assign-trim").disabled = isWhistleMode;
  document.getElementById("clipper-assign").hidden = false;

  if (!_clipperAssignLibrary) {
    // Previously only GetTrackLibrary (SongsFolder) was searched here -- default/conference pack
    // songs live in a separate folder and are assigned straight into event slots without ever
    // being copied into SongsFolder, so a team with the default pack loaded had real, currently-
    // assigned songs that Search could never find. Merge them in the same way the guided
    // Auto-Assign wizard's own library already does (see startAutoAssignWizard).
    try {
      const team = state.activeTeam;
      const [localJson, packJson, conferenceJson] = await Promise.all([
        bridge ? bridge.GetTrackLibrary() : Promise.resolve("[]"),
        bridge && team ? bridge.GetDefaultPackSongsForTeam(team) : Promise.resolve("[]"),
        bridge && team ? bridge.GetConferencePackSongsForTeam(team) : Promise.resolve("[]"),
      ]);
      const local = JSON.parse(localJson) || [];
      const pack = (JSON.parse(packJson) || []).map((s) => ({ ...s, source: "default" }));
      const conference = (JSON.parse(conferenceJson) || []).map((s) => ({ ...s, source: "default" }));
      const seenPaths = new Set(local.map((it) => it.path));
      const packAndConference = [...pack, ...conference].filter((it) => !seenPaths.has(it.path));
      for (const it of packAndConference) seenPaths.add(it.path);
      _clipperAssignLibrary = [...local, ...packAndConference];
    } catch (err) {
      console.error("GetTrackLibrary failed", err);
      _clipperAssignLibrary = [];
    }
  }
  renderClipperAssignList("");
}

async function openClipperAssignForWhistle() {
  await openClipperAssign(null, null, false, null, "whistle");
}

/// Per-event alt whistle -- picks up the Clipper's own pick+trim library flow instead of the bare
/// native OpenFileDialog BrowseAndSetEventAltWhistle used to be. Keeps `trigger` (unlike the
/// global "whistle" mode above, which has nothing to save back to) so btn-trim-whistle's click
/// handler knows to call SaveTrimAsEventAltWhistle instead of the global SaveTrimAsLeadInWhistle.
async function openClipperAssignForAltWhistle(trigger, eventName) {
  await openClipperAssign(trigger, eventName, false, null, "alt-whistle");
}

function closeClipperAssign() {
  bridge?.StopPreview();
  if (_trimTrigger || _trimForWhistle) closeInlineTrimmer();
  document.getElementById("clipper-assign").hidden = true;
  document.getElementById("btn-clipper-close-assign").hidden = true;
  document.getElementById("clipper-title-text").textContent = "Clip Preview";
  document.getElementById("clipper-empty").hidden = !!_previewAudio;
  document.getElementById("preview-bar").hidden = !_previewAudio;
  document.getElementById("btn-clipper-assign-select").hidden = false;
  document.getElementById("btn-clipper-assign-clear").hidden = false;
  _clipperAssignTrigger = null;
  _clipperAssignMode = "event";
}

// Source -> section label, in display order. Matches the "source" tag GetTrackLibraryFromWeb
// (WebMainForm.cs) now stamps on every entry -- keep these two lists in sync if a new source
// folder is ever added there, or new files will silently fall into "Imported Files" instead of
// getting their own labeled section.
const CLIPPER_ASSIGN_SOURCE_LABELS = {
  marketplace: "Marketplace Downloads",
  trimmed: "Trimmed Clips",
  local: "Your Imports",
  uploaded: "Imported Files",
  default: "Default Song Pack",
};
const CLIPPER_ASSIGN_SOURCE_ORDER = ["marketplace", "trimmed", "local", "uploaded", "default"];

/// Builds one song row -- same Play/Stop/DL per-row transport as before, factored out so both
/// the grouped assign list and any future reuse share one implementation instead of drifting.
function buildClipperAssignRow(item, list) {
  const row = document.createElement("div");
  row.className = "clipper-assign-row";
  // Sound Bank songs are named after the EventKey slot they auto-filled ("Defense_Third Down_5"),
  // not the actual song -- but pack files generally carry a real ID3 Title tag (see
  // ReadAudioTitleTag in WebMainForm.cs) that's far more useful to show. Falls back to the
  // filename-derived name/category for files with no tag or non-pack sources that never had one.
  // The real filename always stays in the tooltip so it's still identifiable/searchable.
  const displayLabel = item.title || item.name;
  row.title = item.title ? `${item.title}\n${item.name}` : item.path;

  const name = document.createElement("span");
  name.className = "clipper-assign-row-name";
  name.textContent = displayLabel;
  row.appendChild(name);

  // Condensed per-row transport -- same Play/Stop the toolbar above already does (just
  // scoped to this row's file instead of whatever's currently selected), plus DL to
  // register this library file in My Downloads (see AddLibraryFileToDownloadsFromWeb) so
  // it's reachable there too instead of only by re-browsing the whole Songs folder.
  const actions = document.createElement("span");
  actions.className = "clipper-assign-row-actions";

  const playBtn = document.createElement("button");
  playBtn.className = "clipper-assign-row-btn";
  playBtn.title = "Play";
  playBtn.textContent = "▶";
  playBtn.addEventListener("click", (e) => {
    e.stopPropagation();
    _previewAudio?.pause();
    bridge?.PreviewLocalFile(item.path);
  });
  actions.appendChild(playBtn);

  const stopBtn = document.createElement("button");
  stopBtn.className = "clipper-assign-row-btn";
  stopBtn.title = "Stop";
  stopBtn.textContent = "⏹";
  stopBtn.addEventListener("click", (e) => { e.stopPropagation(); bridge?.StopPreview(); });
  actions.appendChild(stopBtn);

  const dlBtn = document.createElement("button");
  dlBtn.className = "clipper-assign-row-btn";
  dlBtn.title = "Add to My Downloads";
  dlBtn.textContent = "⬇";
  dlBtn.addEventListener("click", async (e) => {
    e.stopPropagation();
    dlBtn.disabled = true;
    const ok = await bridge?.AddLibraryFileToDownloads(item.path);
    showToast(ok ? `Added "${item.name}" to My Downloads.` : "Couldn't add that -- try again.");
    dlBtn.disabled = false;
  });
  actions.appendChild(dlBtn);

  // "Share to..." -- owner reported no way to send a Clipper song straight onto another team's
  // event without leaving the picker; the existing situation-card "Share to..." only reaches
  // events on the SAME team (wireSituationShareToPopover, same-team `events` array). This is the
  // cross-team version: pick a team, then one of ITS events, assign this row's file there via
  // the new AssignLibraryFileToTeamEvent bridge call (WebMainForm.AssignLibraryFileToTeamEventFromWeb).
  const shareBtn = document.createElement("button");
  shareBtn.className = "clipper-assign-row-btn";
  shareBtn.title = "Share to another team's event...";
  shareBtn.textContent = "↗";
  shareBtn.addEventListener("click", (e) => {
    e.stopPropagation();
    openClipperSharePopover(row, item);
  });
  actions.appendChild(shareBtn);

  row.appendChild(actions);

  row.addEventListener("click", () => {
    list.querySelectorAll(".clipper-assign-row.selected").forEach((r) => r.classList.remove("selected"));
    row.classList.add("selected");
    _clipperAssignSelectedPath = item.path;
    _clipperAssignSelectedName = item.name;
    document.getElementById("btn-clipper-assign-select").disabled = false;
    if (_clipperAssignMode === "whistle" || _clipperAssignMode === "alt-whistle") document.getElementById("btn-clipper-assign-trim").disabled = false;
  });
  return row;
}

/// Builds/opens the "Share to..." popover for one Clipper song row -- a team <select> up top
/// (defaults to the active team) and a scrollable event list below it that re-fetches whenever
/// the team changes (bridge.GetEventsForTeam reads that team's saved profile directly, see
/// WebMainForm.GetEventsForTeamFromWeb -- doesn't touch/switch the active team). Only one open
/// at a time, same convention as the situation-card popovers.
let _clipperShareTeamsCache = null;
async function openClipperSharePopover(row, item) {
  document.querySelectorAll(".clipper-share-popover").forEach((p) => p.remove());

  const popover = document.createElement("div");
  popover.className = "clipper-share-popover";
  popover.addEventListener("click", (e) => e.stopPropagation());

  const select = document.createElement("select");
  select.className = "clipper-share-team-select";
  const listEl = document.createElement("div");
  listEl.className = "situation-copy-list";

  popover.appendChild(select);
  popover.appendChild(listEl);
  row.appendChild(popover);

  if (!_clipperShareTeamsCache) {
    _clipperShareTeamsCache = bridge ? JSON.parse(await bridge.GetTeams()) : [];
  }
  for (const t of _clipperShareTeamsCache) {
    const opt = document.createElement("option");
    opt.value = t.name;
    opt.textContent = t.name;
    if (t.name === state.activeTeam) opt.selected = true;
    select.appendChild(opt);
  }

  const loadEventsForTeam = async (teamName) => {
    listEl.innerHTML = `<div class="situation-copy-empty">Loading...</div>`;
    const events = bridge ? JSON.parse(await bridge.GetEventsForTeam(teamName, "All")) : [];
    listEl.innerHTML = "";
    if (!events.length) {
      listEl.innerHTML = `<div class="situation-copy-empty">No events found.</div>`;
      return;
    }
    for (const ev of events) {
      const opt = document.createElement("button");
      opt.className = "situation-copy-option";
      opt.innerHTML = `<span>${friendlyEventName(ev.eventName)}</span><span class="situation-copy-option-file">${ev.fileName ? "overwrites " + ev.fileName : "unassigned"}</span>`;
      opt.addEventListener("click", async (ce) => {
        ce.stopPropagation();
        const ok = bridge ? await bridge.AssignLibraryFileToTeamEvent(teamName, ev.trigger, item.path) : false;
        popover.remove();
        showToast(ok ? `Shared "${item.name}" to ${teamName} - ${friendlyEventName(ev.eventName)}.` : "Couldn't share that song.");
      });
      listEl.appendChild(opt);
    }
  };

  select.addEventListener("click", (e) => e.stopPropagation());
  select.addEventListener("change", () => loadEventsForTeam(select.value));
  await loadEventsForTeam(select.value);

  const closeOnOutsideClick = (e) => {
    if (!popover.contains(e.target)) {
      popover.remove();
      document.removeEventListener("click", closeOnOutsideClick);
    }
  };
  setTimeout(() => document.addEventListener("click", closeOnOutsideClick), 0);
}

/// Task queue item 1 (Session 10): the flat song list mixed marketplace downloads, drag-drop/
/// Browse imports, trimmed clips, and the "import my own song" pipeline with zero labeling --
/// "way too many songs, I don't even know where they came from." Root cause (see
/// GetTrackLibraryFromWeb in WebMainForm.cs): those really are three different physical folders
/// under ConfigStore.SongsFolder, scanned together with no source tag. The default song pack was
/// NOT part of this -- it's never copied into SongsFolder at all, so it isn't in this list.
/// Fixed by tagging each entry with its source folder/origin server-side and grouping by that
/// tag here, same shape as the marketplace hub's section-labeled shelves.
///
/// Also: item.path is a real, distinct filesystem path per entry, and GetTrackLibraryFromWeb
/// already Distinct()s by full path -- so two rows with the SAME display name are never the
/// exact same file counted twice, they're two different files that happen to share a name (e.g.
/// a marketplace download and a locally trimmed clip both named "Defense_Tackle for Loss"). The
/// per-row tooltip (row.title = item.path) plus the new source-section header disambiguates them
/// instead of silently merging them -- don't "fix" this by deduping on name, that would hide a
/// real second file the user can still assign separately.
function renderClipperAssignList(filter) {
  const list = document.getElementById("clipper-assign-list");
  list.innerHTML = "";
  const q = filter.toLowerCase().trim();
  let items = (_clipperAssignLibrary || []).filter((it) => !q || it.name.toLowerCase().includes(q));
  if (_clipperAssignFilter !== "all") {
    items = items.filter((it) => (it.source || "uploaded") === _clipperAssignFilter);
  }
  if (!items.length) {
    // Sound Bank pill specifically empty (vs. a real "no songs anywhere" or "no search matches")
    // gets its own nudge toward "All Songs" instead of just reading as broken -- this is the
    // pill someone lands on by default every time they open an event.
    const hint = _clipperAssignFilter === "default" && !q
      ? ` -- this team's Sound Bank may not be loaded yet. Try "All Songs".`
      : q ? " for that search" : " in the Songs library";
    list.innerHTML = `<div class="clipper-assign-row" style="cursor:default;">No songs found${hint}.</div>`;
  } else {
    const sources = _clipperAssignFilter === "all" ? CLIPPER_ASSIGN_SOURCE_ORDER : [_clipperAssignFilter];
    for (const source of sources) {
      const group = items.filter((it) => (it.source || "uploaded") === source);
      if (!group.length) continue;
      if (sources.length > 1) {
        const header = document.createElement("div");
        header.className = "clipper-assign-section-label";
        header.textContent = `${CLIPPER_ASSIGN_SOURCE_LABELS[source]} (${group.length})`;
        list.appendChild(header);
      }
      for (const item of group) list.appendChild(buildClipperAssignRow(item, list));
    }
  }

  // Browsed-team section (see wireBrowseOtherTeamSoundBank) -- always rendered when present,
  // independent of the pill filter above, since browsing another team is an explicit separate
  // action. Still respects the search box so typing narrows this section too.
  if (_clipperBrowsedTeam) {
    const browsedItems = _clipperBrowsedSongs.filter((it) => !q || it.name.toLowerCase().includes(q) || (it.title || "").toLowerCase().includes(q));
    const header = document.createElement("div");
    header.className = "clipper-assign-section-label";
    header.innerHTML = `Default Song Pack -- ${_clipperBrowsedTeam} (${browsedItems.length}) <button class="icon-btn" id="btn-clear-browsed-team" title="Stop browsing ${_clipperBrowsedTeam}'s Sound Bank" style="float:right;">&times;</button>`;
    list.appendChild(header);
    header.querySelector("#btn-clear-browsed-team").addEventListener("click", (e) => { e.stopPropagation(); clearBrowsedTeam(); });
    if (browsedItems.length) {
      for (const item of browsedItems) list.appendChild(buildClipperAssignRow(item, list));
    } else {
      const empty = document.createElement("div");
      empty.className = "clipper-assign-row";
      empty.style.cursor = "default";
      empty.textContent = q ? "No songs found for that search." : `${_clipperBrowsedTeam} has no Sound Bank songs.`;
      list.appendChild(empty);
    }
  }
}

// ---- Embedded waveform trimmer (round of item: replaces the native TrimmerForm popup) --------
// Lives inline in .clipper-assign-main, swapping in for clipper-assign-list (see index.html
// #clipper-trim-panel) when "Trim..." is clicked, instead of popping a separate WinForms dialog.
// Mirrors TrimmerForm's own behavior (draggable start/end handles, end-tail preview on release,
// same RMS-normalize-and-limit save path via AudioNormalizer on the C# side) but drawn on a
// <canvas> with the same waveform-decode approach as loadPreviewWaveform above.
let _trimTrigger = null;
let _trimForWhistle = false; // true when the trimmer opened via Clipper Island's whistle mode (no trigger to save back to)
let _trimSourceName = null; // filename actually loaded into the trimmer, for correctly naming the saved clip
let _trimIsPa = false;
let _trimUrl = null;
let _trimDurationSec = 0;
let _trimPeaks = null;
let _trimDecodeFailed = false; // true once loadTrimWaveform's decodeAudioData rejects, so drawTrimCanvas
                                 // can show an explicit "no preview" flat line instead of looking like real audio
let _trimStartSec = 0;
let _trimEndSec = 0;
let _trimAudio = null;
let _trimAudioCtx = null;
let _trimDragHandle = null; // "start" | "end" | null
// Zoom (owner request): canvas.width attribute stays the drawing-buffer resolution, but its
// CSS width scales with zoom while #clipper-trim-viewport scrolls -- widening the same 0..duration
// range across more on-screen pixels is what makes handle-dragging more precise, no re-decode
// of the audio needed since trimHandleAt/trimSecAt already work purely off getBoundingClientRect
// fractions, which stay correct at any canvas width.
const TRIM_CANVAS_BASE_WIDTH = 900;
const TRIM_MAX_ZOOM = 8;
let _trimZoom = 1;

async function openInlineTrimmer(trigger, isPa, overridePath) {
  if (!bridge) return;
  // overridePath is set when the user highlighted a different song in the Clipper list before
  // hitting Trim... -- previously this always ignored the highlighted row and re-prepped
  // whatever was already assigned to the trigger, so highlighting a song did nothing and Trim
  // silently reopened the old file instead of the one just clicked.
  const result = JSON.parse(overridePath ? await bridge.PrepareTrimForWhistle(overridePath) : await bridge.PrepareTrim(trigger, isPa));
  if (!result.ok) {
    showToast(result.error || "Couldn't open the trimmer.");
    return;
  }
  _trimTrigger = trigger;
  _trimForWhistle = false;
  _trimIsPa = isPa;
  _trimUrl = result.url;
  _trimDurationSec = result.durationSec;
  _trimStartSec = 0;
  _trimEndSec = Math.min(result.durationSec, 15);
  _trimPeaks = null;
  _trimDecodeFailed = false;

  document.getElementById("clipper-assign-list").hidden = true;
  document.getElementById("clipper-trim-panel").hidden = false;
  document.getElementById("clipper-assign-actions-default").hidden = true;
  document.getElementById("clipper-trim-actions").hidden = false;
  document.getElementById("btn-trim-save").hidden = false;
  document.getElementById("btn-trim-whistle").textContent = "Set as Lead-In Whistle";
  _trimSourceName = result.fileName;
  document.getElementById("clipper-trim-filename").textContent = result.fileName;
  updateTrimLabels();
  setTrimZoom(1);

  loadTrimWaveform(result.url);
}

/// Whistle-mode counterpart to openInlineTrimmer -- there's no "already assigned" file to pull for
/// either whistle mode, so this trims whatever library row/browsed file the user just picked in
/// Clipper Island (see PrepareTrimForWhistleFromWeb). "Save Trim" doesn't apply here (there's no
/// event slot to assign back to), only "Set as Whistle" (btn-trim-whistle). alt-whistle mode DOES
/// keep _trimTrigger (unlike global "whistle" mode) -- that's what tells btn-trim-whistle's click
/// handler to call SaveTrimAsEventAltWhistle for this one event instead of the global whistle.
async function openInlineTrimmerForWhistle(path, fileName) {
  if (!bridge) return;
  const result = JSON.parse(await bridge.PrepareTrimForWhistle(path));
  if (!result.ok) {
    showToast(result.error || "Couldn't open the trimmer.");
    return;
  }
  _trimTrigger = _clipperAssignMode === "alt-whistle" ? _clipperAssignTrigger : null;
  _trimForWhistle = true;
  _trimIsPa = false;
  _trimUrl = result.url;
  _trimDurationSec = result.durationSec;
  _trimStartSec = 0;
  _trimEndSec = Math.min(result.durationSec, 15);
  _trimPeaks = null;
  _trimDecodeFailed = false;

  document.getElementById("clipper-assign-list").hidden = true;
  document.getElementById("clipper-trim-panel").hidden = false;
  document.getElementById("clipper-assign-actions-default").hidden = true;
  document.getElementById("clipper-trim-actions").hidden = false;
  document.getElementById("btn-trim-save").hidden = true;
  document.getElementById("btn-trim-whistle").textContent =
    _clipperAssignMode === "alt-whistle" ? "Set as Alt Whistle for This Event" : "Set as Lead-In Whistle";
  _trimSourceName = result.fileName || fileName;
  document.getElementById("clipper-trim-filename").textContent = result.fileName || fileName;
  updateTrimLabels();
  setTrimZoom(1);

  loadTrimWaveform(result.url);
}

function closeInlineTrimmer() {
  stopTrimPreview();
  _trimTrigger = null;
  _trimForWhistle = false;
  _trimSourceName = null;
  _trimUrl = null;
  _trimPeaks = null;
  _trimDecodeFailed = false;
  // Leftover start/end/zoom from the closed session used to survive until the NEXT openInlineTrimmer
  // call reset them -- harmless while nothing else read them, but the new Tab-to-preview shortcut and
  // any other code that might fire between close and the next open should never see a stale range.
  _trimStartSec = 0;
  _trimEndSec = 0;
  _trimDragHandle = null;
  setTrimZoom(1);
  document.getElementById("clipper-assign-list").hidden = false;
  document.getElementById("clipper-trim-panel").hidden = true;
  document.getElementById("clipper-assign-actions-default").hidden = false;
  document.getElementById("clipper-trim-actions").hidden = true;
  document.getElementById("btn-trim-save").hidden = false;
}

/// Same decode-for-peaks approach as loadPreviewWaveform, just against the trimsrc:// copy and
/// with more buckets (this canvas is wider/the only thing on screen, vs. sharing the preview bar).
async function loadTrimWaveform(url) {
  try {
    _trimAudioCtx ??= new (window.AudioContext || window.webkitAudioContext)();
    const resp = await fetch(url);
    const arrayBuf = await resp.arrayBuffer();
    const audioBuf = await _trimAudioCtx.decodeAudioData(arrayBuf);
    const channel = audioBuf.getChannelData(0);
    const buckets = 300;
    const peaks = new Float32Array(buckets);
    const bucketSize = Math.max(1, Math.floor(channel.length / buckets));
    for (let b = 0; b < buckets; b++) {
      let max = 0;
      const start = b * bucketSize;
      const end = Math.min(channel.length, start + bucketSize);
      for (let i = start; i < end; i++) max = Math.max(max, Math.abs(channel[i]));
      peaks[b] = max;
    }
    _trimPeaks = peaks;
    _trimDecodeFailed = false;
    drawTrimCanvas();
  } catch (err) {
    console.error("loadTrimWaveform failed", err);
    _trimPeaks = null;
    _trimDecodeFailed = true;
    drawTrimCanvas();
    showToast("Couldn't draw a waveform for this file -- you can still trim it by time.");
  }
}

function formatTrimTime(sec) {
  const m = Math.floor(sec / 60);
  const s = (sec % 60).toFixed(1).padStart(4, "0");
  return `${m}:${s}`;
}

function updateTrimLabels() {
  document.getElementById("clipper-trim-start-label").textContent = formatTrimTime(_trimStartSec);
  document.getElementById("clipper-trim-end-label").textContent = formatTrimTime(_trimEndSec);
}

function drawTrimCanvas() {
  const canvas = document.getElementById("clipper-trim-canvas");
  if (!canvas) return;
  const ctx = canvas.getContext("2d");
  const w = canvas.width, h = canvas.height;
  ctx.clearRect(0, 0, w, h);
  const bars = _trimPeaks && _trimPeaks.length ? _trimPeaks.length : 80;
  const gap = 1;
  const barW = w / bars - gap;
  const mid = h / 2;
  const startFrac = _trimDurationSec > 0 ? _trimStartSec / _trimDurationSec : 0;
  const endFrac = _trimDurationSec > 0 ? _trimEndSec / _trimDurationSec : 1;
  // Decode failed outright (vs. still-loading, which looks the same otherwise) -- draw a thin
  // flat line instead of uniform full-height bars so it doesn't read as a real (broken) waveform.
  // The trim range/start/end markers below still work fine off duration alone.
  const placeholderAmp = _trimDecodeFailed ? 0.03 : 0.15;
  for (let i = 0; i < bars; i++) {
    const amp = _trimPeaks && _trimPeaks.length ? _trimPeaks[i] : placeholderAmp;
    const barH = Math.max(2, amp * (h - 8));
    const x = i * (barW + gap);
    const frac = i / bars;
    const inRange = frac >= startFrac && frac <= endFrac;
    ctx.fillStyle = inRange ? "rgba(255,255,255,0.9)" : "rgba(255,255,255,0.22)";
    ctx.fillRect(x, mid - barH / 2, barW, barH);
  }
  if (_trimDecodeFailed) {
    ctx.fillStyle = "rgba(255,255,255,0.5)";
    ctx.font = "12px sans-serif";
    ctx.textAlign = "center";
    ctx.fillText("No waveform preview -- trim by time below", w / 2, mid + 4);
  }
  const startX = startFrac * w;
  const endX = endFrac * w;
  ctx.fillStyle = "rgba(61, 220, 132, 0.9)"; // matches --success-ish green, same as TrimmerForm's start marker
  ctx.fillRect(Math.max(0, startX - 1), 0, 2, h);
  ctx.fillStyle = "rgba(245, 165, 36, 0.9)"; // matches TrimmerForm's warning-orange end marker
  ctx.fillRect(Math.min(w - 2, endX - 1), 0, 2, h);
}

function setTrimZoom(zoom) {
  _trimZoom = Math.max(1, Math.min(TRIM_MAX_ZOOM, zoom));
  const canvas = document.getElementById("clipper-trim-canvas");
  const label = document.getElementById("clipper-trim-zoom-label");
  if (!canvas) return;
  const px = Math.round(TRIM_CANVAS_BASE_WIDTH * _trimZoom);
  canvas.width = px; // drawing buffer -- keeps waveform bars crisp instead of CSS-upscaling a 900px buffer
  canvas.style.width = _trimZoom === 1 ? "100%" : `${px}px`;
  if (label) label.textContent = `${Math.round(_trimZoom * 100)}%`;
  drawTrimCanvas();
}

/// Keeps whichever handle was just dragged in view after a zoom change -- without this, zooming
/// in centers on the viewport's current scroll position (usually the start of the clip) and the
/// end handle you were trying to get more precision on can scroll off-screen.
function scrollTrimHandleIntoView(handle) {
  const viewport = document.getElementById("clipper-trim-viewport");
  const canvas = document.getElementById("clipper-trim-canvas");
  if (!viewport || !canvas || _trimDurationSec <= 0) return;
  const sec = handle === "end" ? _trimEndSec : _trimStartSec;
  const targetX = (sec / _trimDurationSec) * canvas.getBoundingClientRect().width;
  viewport.scrollLeft = Math.max(0, targetX - viewport.clientWidth / 2);
}

function trimHandleAt(canvas, clientX) {
  const rect = canvas.getBoundingClientRect();
  const x = clientX - rect.left;
  const w = rect.width;
  const startFrac = _trimDurationSec > 0 ? _trimStartSec / _trimDurationSec : 0;
  const endFrac = _trimDurationSec > 0 ? _trimEndSec / _trimDurationSec : 1;
  const startX = startFrac * w, endX = endFrac * w;
  if (Math.abs(x - startX) < 10) return "start";
  if (Math.abs(x - endX) < 10) return "end";
  return null;
}

function trimSecAt(canvas, clientX) {
  const rect = canvas.getBoundingClientRect();
  const ratio = Math.max(0, Math.min(1, (clientX - rect.left) / rect.width));
  return ratio * _trimDurationSec;
}

function playTrimRange(startSec, endSec) {
  stopTrimPreview();
  if (!_trimUrl) return;
  const audio = new Audio(_trimUrl);
  audio.volume = Number(document.getElementById("slider-volume")?.value ?? 72) / 100;
  _trimAudio = audio;
  audio.addEventListener("timeupdate", () => {
    if (_trimAudio === audio && audio.currentTime >= endSec) stopTrimPreview();
  });
  const start = () => { audio.currentTime = startSec; audio.play().catch(() => {}); };
  if (audio.readyState >= 1) start(); else audio.addEventListener("loadedmetadata", start, { once: true });
}

/// Owner request carried over from TrimmerForm: releasing the End handle immediately plays the
/// last few seconds up to the new end point, so you can hear exactly where the clip cuts off
/// without a separate Preview click.
const TRIM_END_TAIL_SECONDS = 4;
function previewTrimEndTail() {
  const tailStart = Math.max(_trimStartSec, _trimEndSec - TRIM_END_TAIL_SECONDS);
  playTrimRange(tailStart, _trimEndSec);
}

function stopTrimPreview() {
  if (_trimAudio) { _trimAudio.pause(); _trimAudio = null; }
}

function wireInlineTrimmer() {
  const canvas = document.getElementById("clipper-trim-canvas");
  const viewport = document.getElementById("clipper-trim-viewport");
  if (!canvas || !viewport) return;

  // Panning (owner request): dragging empty waveform space (not a start/end handle) scrolls the
  // viewport horizontally when zoomed in, same convention as a DAW waveform -- click a handle to
  // trim, click-drag anywhere else to pan. Only kicks in once zoom > 1 makes panning meaningful.
  let _trimPanning = false;
  let _trimPanStartX = 0;
  let _trimPanStartScroll = 0;

  const startDrag = (clientX) => {
    _trimDragHandle = trimHandleAt(canvas, clientX);
    if (!_trimDragHandle && _trimZoom > 1) {
      _trimPanning = true;
      _trimPanStartX = clientX;
      _trimPanStartScroll = viewport.scrollLeft;
      canvas.style.cursor = "grabbing";
    }
  };
  const moveDrag = (clientX) => {
    if (_trimPanning) {
      viewport.scrollLeft = _trimPanStartScroll - (clientX - _trimPanStartX);
      return;
    }
    if (!_trimDragHandle) return;
    const sec = trimSecAt(canvas, clientX);
    if (_trimDragHandle === "start") _trimStartSec = Math.max(0, Math.min(sec, _trimEndSec - 0.1));
    else _trimEndSec = Math.min(_trimDurationSec, Math.max(sec, _trimStartSec + 0.1));
    updateTrimLabels();
    drawTrimCanvas();
  };
  const endDrag = () => {
    const wasEnd = _trimDragHandle === "end";
    _trimDragHandle = null;
    if (_trimPanning) { _trimPanning = false; canvas.style.cursor = "pointer"; }
    if (wasEnd) previewTrimEndTail();
  };

  canvas.addEventListener("mousedown", (e) => startDrag(e.clientX));
  window.addEventListener("mousemove", (e) => moveDrag(e.clientX));
  window.addEventListener("mouseup", endDrag);
  canvas.addEventListener("touchstart", (e) => { if (e.touches[0]) startDrag(e.touches[0].clientX); }, { passive: true });
  canvas.addEventListener("touchmove", (e) => { if (e.touches[0]) moveDrag(e.touches[0].clientX); }, { passive: true });
  canvas.addEventListener("touchend", endDrag);

  document.getElementById("btn-trim-preview").addEventListener("click", () => playTrimRange(_trimStartSec, _trimEndSec));
  document.getElementById("btn-trim-stop").addEventListener("click", stopTrimPreview);

  document.getElementById("btn-trim-zoom-in").addEventListener("click", () => {
    setTrimZoom(_trimZoom * 2);
    scrollTrimHandleIntoView(_trimDragHandle === "end" ? "end" : "start");
  });
  document.getElementById("btn-trim-zoom-out").addEventListener("click", () => setTrimZoom(_trimZoom / 2));
  document.getElementById("btn-trim-zoom-reset").addEventListener("click", () => {
    setTrimZoom(1);
    viewport.scrollLeft = 0;
  });
  // Ctrl+wheel zooms (same convention as every other zoomable canvas in this app). Plain wheel
  // pans horizontally -- overflow-x:auto alone only responds to a real horizontal scroll device
  // (shift+wheel/trackpad), so a normal vertical mouse wheel would otherwise do nothing useful
  // here since the viewport has no vertical overflow to scroll.
  canvas.addEventListener("wheel", (e) => {
    e.preventDefault();
    if (e.ctrlKey) setTrimZoom(_trimZoom * (e.deltaY < 0 ? 1.4 : 1 / 1.4));
    else viewport.scrollLeft += e.deltaY;
  }, { passive: false });

  document.getElementById("btn-trim-save").addEventListener("click", async () => {
    if (!_trimTrigger || !bridge) return;
    const trigger = _trimTrigger, isPa = _trimIsPa;
    const result = JSON.parse(await bridge.SaveTrim(trigger, isPa, _trimStartSec, _trimEndSec, _trimSourceName));
    if (!result.ok) { showToast(result.error || "Couldn't save the trimmed clip."); return; }
    showToast(`Saved trimmed clip: ${result.fileName}`);
    closeInlineTrimmer();
    await refreshCategories();
    if (state.currentSituationsCategory) await openSituations(state.currentSituationsCategory);
    await afterClipperAssignAction(trigger, true);
    // Punch-list item 6: afterClipperAssignAction's closeClipperAssign() above just hides the
    // Clipper's assign sub-panel, dropping the user back on Clipper Island's generic song-list
    // browse view -- a dead end with no link back to which event this clip belongs to. Scroll
    // the situations panel (already refreshed above) to the event card this trim was just saved
    // for and flash it, so "you just finished editing this clip" actually lands somewhere with
    // context, not just an empty song list. Skipped for the auto-assign wizard case (it already
    // advances to its own next-event picker instead).
    if (!_autoAssignWizard) scrollToSituationRow(trigger);
  });

  document.getElementById("btn-trim-whistle").addEventListener("click", async () => {
    if (!bridge) return;
    // alt-whistle mode keeps _trimTrigger set (see openInlineTrimmerForWhistle) specifically so
    // this branch can tell "save to this one event's AltWhistlePath" apart from the global
    // "whistle" mode's "overwrite the single shared lead-in whistle".
    const result = JSON.parse(_clipperAssignMode === "alt-whistle" && _trimTrigger
      ? await bridge.SaveTrimAsEventAltWhistle(_trimTrigger, _trimStartSec, _trimEndSec)
      : await bridge.SaveTrimAsLeadInWhistle(_trimStartSec, _trimEndSec));
    if (!result.ok) { showToast(result.error || "Couldn't save the whistle clip."); return; }
    showToast(_clipperAssignMode === "alt-whistle" ? "Alternate whistle set for this event." : "Lead-in whistle updated.");
    flashPanel(document.getElementById("clipper-island"));
    if (_trimForWhistle) {
      closeClipperAssign();
      if (_clipperAssignMode === "alt-whistle") {
        if (state.currentSituationsCategory) await openSituations(state.currentSituationsCategory);
      } else {
        await refreshLeadInWhistleSection();
      }
    }
  });

  document.getElementById("btn-trim-cancel").addEventListener("click", closeInlineTrimmer);
}

function initClipperAssign() {
  document.getElementById("btn-clipper-close-assign").addEventListener("click", () => {
    if (_autoAssignWizard) {
      _autoAssignWizard.cancelled = true;
      closeClipperAssign();
      finishAutoAssignWizard(true);
      return;
    }
    closeClipperAssign();
  });
  document.getElementById("clipper-assign-search").addEventListener("input", (e) => renderClipperAssignList(e.target.value));

  document.getElementById("btn-clipper-assign-play").addEventListener("click", () => {
    if (!_clipperAssignSelectedPath) return;
    _previewAudio?.pause(); // stop any JS-pathway preview first -- separate audio output
    bridge?.PreviewLocalFile(_clipperAssignSelectedPath);
  });
  document.getElementById("btn-clipper-assign-stop").addEventListener("click", () => bridge?.StopPreview());

  document.getElementById("btn-clipper-assign-select").addEventListener("click", async () => {
    if (!_clipperAssignTrigger || !_clipperAssignSelectedPath) return;
    const trigger = _clipperAssignTrigger;
    const songName = _clipperAssignSelectedName || _clipperAssignSelectedPath;
    await bridge?.AssignTrackFile(trigger, _clipperAssignIsPa, _clipperAssignSelectedPath);
    await refreshCategories();
    if (state.currentSituationsCategory) await openSituations(state.currentSituationsCategory);
    await afterClipperAssignAction(trigger, true, songName);
  });

  document.getElementById("btn-clipper-assign-browse").addEventListener("click", async () => {
    const path = await bridge?.BrowseForAudioFile();
    if (!path) return;
    const songName = path.split(/[\\/]/).pop();

    if (_clipperAssignMode === "whistle" || _clipperAssignMode === "alt-whistle") {
      document.querySelectorAll("#clipper-assign-list .clipper-assign-row.selected").forEach((r) => r.classList.remove("selected"));
      _clipperAssignSelectedPath = path;
      _clipperAssignSelectedName = songName;
      document.getElementById("btn-clipper-assign-trim").disabled = false;
      showToast(`Selected "${songName}" -- click Trim... to set it as your whistle.`);
      return;
    }

    if (!_clipperAssignTrigger) return;
    const trigger = _clipperAssignTrigger;
    await bridge?.AssignTrackFile(trigger, _clipperAssignIsPa, path);
    await refreshCategories();
    if (state.currentSituationsCategory) await openSituations(state.currentSituationsCategory);
    await afterClipperAssignAction(trigger, true, songName);
  });

  // Batch "Add Songs..." -- owner request: pick several files at once instead of repeating
  // Browse for file... one at a time. Just adds them to the library (same
  // ConfigStore.ImportIntoSongsLibrary copy-in Browse uses); doesn't assign any of them to the
  // current trigger -- the user still picks from the (now-updated) list below like normal.
  document.getElementById("btn-clipper-assign-add-songs")?.addEventListener("click", async () => {
    if (!bridge) return;
    const btn = document.getElementById("btn-clipper-assign-add-songs");
    btn.disabled = true;
    try {
      const raw = await bridge.AddSongsBatch();
      const result = JSON.parse(raw);
      if (result.addedCount === 0 && (!result.failedNames || result.failedNames.length === 0)) return; // cancelled picker
      // Refetch + re-render the list only -- NOT the full openClipperAssign, which would
      // overwrite the "for <event name>"/"Current: <file>" labels with blanks since this handler
      // has no data for either of those (same bug class as the songpackready live-refresh above).
      _clipperAssignLibrary = null;
      const team = state.activeTeam;
      const [localJson, packJson, conferenceJson] = await Promise.all([
        bridge.GetTrackLibrary(),
        team ? bridge.GetDefaultPackSongsForTeam(team) : Promise.resolve("[]"),
        team ? bridge.GetConferencePackSongsForTeam(team) : Promise.resolve("[]"),
      ]);
      const local = JSON.parse(localJson) || [];
      const pack = (JSON.parse(packJson) || []).map((s) => ({ ...s, source: "default" }));
      const conference = (JSON.parse(conferenceJson) || []).map((s) => ({ ...s, source: "default" }));
      const seenPaths = new Set(local.map((it) => it.path));
      const packAndConference = [...pack, ...conference].filter((it) => !seenPaths.has(it.path));
      for (const it of packAndConference) seenPaths.add(it.path);
      _clipperAssignLibrary = [...local, ...packAndConference];
      renderClipperAssignList(document.getElementById("clipper-assign-search")?.value || "");
      const failed = result.failedNames?.length
        ? ` (${result.failedNames.length} failed: ${result.failedNames.join(", ")})`
        : "";
      showToast(`Added ${result.addedCount} song${result.addedCount === 1 ? "" : "s"}.${failed}`);
    } catch (err) {
      console.error("AddSongsBatch failed", err);
      showToast("Couldn't add those songs -- try again.");
    } finally {
      btn.disabled = false;
    }
  });

  document.getElementById("btn-clipper-assign-trim").addEventListener("click", async () => {
    if (_clipperAssignMode === "whistle" || _clipperAssignMode === "alt-whistle") {
      if (!_clipperAssignSelectedPath) return;
      await openInlineTrimmerForWhistle(_clipperAssignSelectedPath, _clipperAssignSelectedName);
      return;
    }
    if (!_clipperAssignTrigger) return;
    await openInlineTrimmer(_clipperAssignTrigger, _clipperAssignIsPa, _clipperAssignSelectedPath);
  });

  document.getElementById("btn-clipper-assign-import-pack").addEventListener("click", () => {
    document.getElementById("songpack-import-overlay").hidden = false;
  });

  // Skip Event -- leaves this event unassigned (no change made, unlike Clear which blanks an
  // existing assignment) and jumps to the next unassigned event in the same category, so working
  // through a long list of situations doesn't require closing/reopening the popup for each one
  // you don't want to touch right now. Owner request: the wizard already had this via "Skip
  // Event" on the guided-assign bar; the everyday Assign/Edit popup had no equivalent.
  document.getElementById("btn-clipper-assign-skip").addEventListener("click", async () => {
    if (_autoAssignWizard) { document.getElementById("btn-auto-assign-wizard-skip").click(); return; }
    const category = state.currentSituationsCategory;
    closeClipperAssign();
    if (!bridge || !category) return;
    try {
      const events = JSON.parse(await bridge.GetEventsForCategory(category)) || [];
      const next = events.find((ev) => !ev.fileName);
      if (next) openClipperAssign(next.trigger, next.eventName, false, next.fileName);
    } catch (err) {
      console.error("Skip Event: GetEventsForCategory failed", err);
    }
  });

  document.getElementById("btn-clipper-assign-clear").addEventListener("click", async () => {
    if (!_clipperAssignTrigger) return;
    const trigger = _clipperAssignTrigger;
    await bridge?.ClearTrackAssignment(trigger, _clipperAssignIsPa);
    await refreshCategories();
    if (state.currentSituationsCategory) await openSituations(state.currentSituationsCategory);
    await afterClipperAssignAction(trigger, false);
  });

  document.getElementById("btn-auto-assign-wizard-skip").addEventListener("click", async () => {
    const wiz = _autoAssignWizard;
    if (!wiz) return;
    const ev = wiz.queue[wiz.index];
    wiz.skipped++;
    wiz.log.push({ eventName: friendlyEventName(ev.eventName), songName: null, skipped: true });
    wiz.index++;
    closeClipperAssign();
    await advanceAutoAssignWizard();
  });

  document.getElementById("btn-auto-assign-wizard-cancel").addEventListener("click", () => {
    if (!_autoAssignWizard) return;
    _autoAssignWizard.cancelled = true;
    closeClipperAssign();
    finishAutoAssignWizard(true);
  });
}

// Optional one-time default song pack download (see DefaultSongPackService.cs). Pulled out of
// the installer as of v1.0.48 to stay under GitHub Releases' 2GB asset cap.
// Opt-out for the import progress/confirmation popups (owner request: "with the option to turn
// off pop ups") -- purely a local display preference, not app data worth round-tripping through
// ConfigStore/the bridge for. When set, imports still run identically, they just report through a
// single toast instead of holding the dialog open for a manual "Got it" dismiss.
const SONGPACK_POPUP_SKIP_KEY = "bandroom:hideSongpackProgressPopup";

function initDefaultSongPackPrompt() {
  const promptOverlay = document.getElementById("songpack-prompt-overlay");
  const importOverlay = document.getElementById("songpack-import-overlay");
  const progressOverlay = document.getElementById("songpack-progress-overlay");
  const progressTitle = document.getElementById("songpack-progress-title");
  const progressFill = document.getElementById("songpack-progress-fill");
  const progressSub = document.getElementById("songpack-progress-sub");
  const fileLog = document.getElementById("songpack-progress-filelog");
  const doneActions = document.getElementById("songpack-progress-done-actions");
  const locateBtn = document.getElementById("btn-songpack-progress-locate");
  const skipFutureCheckbox = document.getElementById("songpack-progress-skip-future");
  // try/catch, not a bare call -- every other localStorage usage in this file is guarded the same
  // way (WebView2's storage can throw depending on profile/permissions setup), and this one sits
  // directly in initDefaultSongPackPrompt's top-level scope: an uncaught throw here would abort
  // the whole function before any of its event listeners get wired.
  let popupsSkipped = false;
  try { popupsSkipped = localStorage.getItem(SONGPACK_POPUP_SKIP_KEY) === "1"; } catch (err) { console.error("localStorage read failed", err); }

  // Which team(s) the most recent import actually populated -- set right before doneActions
  // shows in the bandroom:songpackready handler below, so "Go to Sound Bank" can jump straight
  // there instead of just reporting a folder path in text (owner report: imports gave no way to
  // actually see what landed).
  let lastImportedTeams = [];

  document.getElementById("btn-songpack-progress-done").addEventListener("click", () => {
    progressOverlay.hidden = true;
    popupsSkipped = skipFutureCheckbox.checked;
    try { localStorage.setItem(SONGPACK_POPUP_SKIP_KEY, popupsSkipped ? "1" : "0"); } catch (err) { console.error("localStorage write failed", err); }
  });
  locateBtn.addEventListener("click", async () => {
    progressOverlay.hidden = true;
    // NOT openTeamSoundBank -- that's the marketplace-uploads grid, a different data source from
    // what a default-pack import actually touches (see the songpackready handler's comment on
    // this). The imported songs land directly in this team's event slots, so switching to the
    // team and letting selectTeam's refreshCategories() run is what actually shows them.
    if (lastImportedTeams.length >= 1) {
      await selectTeam(lastImportedTeams[0]);
    }
  });

  (async () => {
    if (!bridge) return;
    const has = await bridge.HasDefaultSongPack();
    if (!has) promptOverlay.hidden = false;
  })();

  document.getElementById("btn-songpack-skip").addEventListener("click", () => { promptOverlay.hidden = true; });
  document.getElementById("btn-songpack-prompt-close").addEventListener("click", () => { promptOverlay.hidden = true; });
  document.getElementById("btn-songpack-download").addEventListener("click", () => {
    promptOverlay.hidden = true;
    // The pack is too large for GitHub Releases / the in-app R2 pipeline isn't populated yet
    // (see DefaultSongPackService.cs), so this opens the pack's Google Drive link in the system
    // browser. importOverlay is the bridge back: once the user has the .zip, Locate & Import
    // extracts it locally (no R2 needed) via ImportDefaultSongPackZipFromWeb.
    bridge?.OpenExternalUrl("https://drive.google.com/file/d/1kZKcqfOSfMv9k2sppduTE9hWpaVrPerN/view");
    importOverlay.hidden = false;
  });

  document.getElementById("btn-songpack-import-later").addEventListener("click", () => { importOverlay.hidden = true; });
  document.getElementById("btn-songpack-import-close").addEventListener("click", () => { importOverlay.hidden = true; });
  document.getElementById("btn-songpack-import").addEventListener("click", async () => {
    const zipPath = await bridge?.BrowseForSongPackZip();
    if (!zipPath) return;
    importOverlay.hidden = true;
    bridge?.ImportDefaultSongPackZip(zipPath);
  });
  document.getElementById("btn-songpack-import-folder").addEventListener("click", async () => {
    const folderPath = await bridge?.BrowseForSongPackFolder();
    if (!folderPath) return;
    importOverlay.hidden = true;
    bridge?.ImportDefaultSongPackFolder(folderPath);
  });
  // "Load All (Overwrite)" -- unlike plain "Import from Folder" (merges, numbers collisions as
  // alternates), this replaces already-imported songs that share a filename AND re-assigns every
  // event slot for every team the folder turns out to contain (see
  // ImportDefaultSongPackFolderFromWeb's overwrite=true path). Destructive, so it reuses the same
  // #auto-assign-confirm-overlay Yes/Cancel pattern every other overwrite action in this file gates
  // behind an explicit confirm -- just with the Guided button hidden since it doesn't
  // apply to a folder-wide import.
  document.getElementById("btn-songpack-import-folder-all").addEventListener("click", async () => {
    const folderPath = await bridge?.BrowseForSongPackFolder();
    if (!folderPath) return;

    const overlay = document.getElementById("auto-assign-confirm-overlay");
    const cancelBtn = document.getElementById("btn-auto-assign-cancel");
    const yesBtn = document.getElementById("btn-auto-assign-confirm-yes");
    const guidedBtn = document.getElementById("btn-auto-assign-guided");
    document.getElementById("auto-assign-confirm-text").textContent =
      "This will overwrite any already-imported songs that share a filename, and re-assign every event slot for every team found in this folder. Continue?";
    guidedBtn.hidden = true;
    overlay.hidden = false;
    const proceed = await new Promise((resolve) => {
      const cleanup = () => {
        cancelBtn.removeEventListener("click", onCancel);
        yesBtn.removeEventListener("click", onYes);
        overlay.hidden = true;
        guidedBtn.hidden = false;
      };
      const onCancel = () => { cleanup(); resolve(false); };
      const onYes = () => { cleanup(); resolve(true); };
      cancelBtn.addEventListener("click", onCancel);
      yesBtn.addEventListener("click", onYes);
    });
    if (!proceed) return;

    importOverlay.hidden = true;
    bridge?.ImportDefaultSongPackFolder(folderPath, true);
  });

  window.addEventListener("bandroom:songpackdownloading", () => {
    if (popupsSkipped) return;
    progressTitle.textContent = "Downloading song pack…";
    progressFill.style.width = "0%";
    progressSub.textContent = "Hang tight -- this is a big one-time download.";
    fileLog.innerHTML = "";
    doneActions.hidden = true;
    progressOverlay.hidden = false;
  });
  window.addEventListener("bandroom:songpackprogress", (e) => {
    if (popupsSkipped) return;
    const { fraction, downloaded, total } = e.detail;
    progressFill.style.width = `${Math.max(0, Math.min(100, fraction * 100))}%`;
    const fmt = (b) => `${(b / 1073741824).toFixed(1)} GB`;
    progressSub.textContent = `${fmt(downloaded)} of ${fmt(total)}`;
  });
  window.addEventListener("bandroom:songpackimporting", () => {
    if (popupsSkipped) return;
    progressTitle.textContent = "Unpacking song pack…";
    progressFill.style.width = "5%";
    progressSub.textContent = "Extracting and indexing every team's songs.";
    fileLog.innerHTML = "";
    doneActions.hidden = true;
    progressOverlay.hidden = false;
  });
  // Live filename feed (owner report: nothing showed a file was even being touched between the
  // old 5%/90% milestones) -- appends the file just processed, keeps the last 8 lines so a
  // 100+ file pack doesn't grow the dialog unbounded, auto-scrolls to the newest.
  window.addEventListener("bandroom:songpackimportprogress", (e) => {
    if (popupsSkipped) return;
    progressFill.style.width = `${Math.max(0, Math.min(100, e.detail.fraction * 100))}%`;
    const file = e.detail?.file;
    if (file) {
      const line = document.createElement("span");
      line.textContent = file;
      fileLog.appendChild(line);
      while (fileLog.children.length > 8) fileLog.removeChild(fileLog.firstChild);
      fileLog.scrollTop = fileLog.scrollHeight;
    }
  });
  window.addEventListener("bandroom:songpackready", async (e) => {
    // Task queue item 7a (Session 10): be explicit about WHERE the files landed and WHAT
    // auto-fill actually does -- "every team can now auto-fill" was true but didn't say the pack
    // only fills events you haven't already assigned yourself (see ConfigStore.
    // ImportDefaultPackForTeam's "never overwrites existing assignments" doc comment), which the
    // owner specifically wants to be unambiguous rather than left to a silent success toast.
    // A folder import (see ImportDefaultSongPackFolderFromWeb) reports exactly which team(s) and
    // how many songs it found instead of this generic line -- that specific confirmation is what
    // tells the user "yes, it actually did something" for a single-team folder, since nothing
    // about that shows up in the Sound Bank album grid (that's marketplace uploads, a different
    // data source from the default-pack files this fills into event slots directly).
    let folderLine = "";
    try {
      const path = bridge ? await bridge.GetDefaultSongsFolderPath() : null;
      if (path) folderLine = ` Files live at: ${path}.`;
    } catch (err) { console.error("GetDefaultSongsFolderPath failed", err); }
    const specific = e.detail?.message;
    const fullMessage = specific
      ? `${specific}${folderLine}`
      : `Every team can now auto-fill any situation you haven't already assigned a song to yourself -- it never overwrites your own picks.${folderLine}`;
    refreshCategories?.();
    // BUG FIX: _clipperAssignLibrary is fetched once and cached (see openClipperAssign) --
    // previously only invalidated on a team switch, never after an import. A song pack imported
    // via "Import from Folder"/"Load All" would never show up in the Assign panel's song list
    // (even after searching) until the user happened to switch teams and back. Null it here so
    // the next openClipperAssign call re-fetches the library fresh, picking up the new files.
    _clipperAssignLibrary = null;
    // If the Assign panel happens to already be open (import triggered from inside it via
    // "Import Song Pack"), refetch and re-render its list live instead of leaving stale/missing
    // results on screen until the panel is closed and reopened. Re-render only -- doesn't touch
    // openClipperAssign's other meta text (event name/current-assignment labels), which this
    // event has no data for.
    if (!document.getElementById("clipper-assign")?.hidden && bridge) {
      try {
        const team = state.activeTeam;
        const [localJson, packJson, conferenceJson] = await Promise.all([
          bridge.GetTrackLibrary(),
          team ? bridge.GetDefaultPackSongsForTeam(team) : Promise.resolve("[]"),
          team ? bridge.GetConferencePackSongsForTeam(team) : Promise.resolve("[]"),
        ]);
        const local = JSON.parse(localJson) || [];
        const pack = (JSON.parse(packJson) || []).map((s) => ({ ...s, source: "default" }));
        const conference = (JSON.parse(conferenceJson) || []).map((s) => ({ ...s, source: "default" }));
        const seenPaths = new Set(local.map((it) => it.path));
        const packAndConference = [...pack, ...conference].filter((it) => !seenPaths.has(it.path));
        for (const it of packAndConference) seenPaths.add(it.path);
        _clipperAssignLibrary = [...local, ...packAndConference];
        renderClipperAssignList(document.getElementById("clipper-assign-search")?.value || "");
      } catch (err) { console.error("Live clipper library refresh after import failed", err); }
    }
    lastImportedTeams = Array.isArray(e.detail?.teamNames) ? e.detail.teamNames : [];
    if (popupsSkipped) {
      showToast(specific || "Song pack imported.");
      return;
    }
    // No auto-hide timer (owner report: the old 3.2-6s auto-dismiss was easy to miss entirely) --
    // stays open until the owner clicks "Got it", same as every other confirm-to-dismiss dialog
    // in this app.
    progressTitle.textContent = "✅ Song pack imported!";
    progressFill.style.width = "100%";
    progressSub.textContent = fullMessage;
    skipFutureCheckbox.checked = false;
    doneActions.hidden = false;
    locateBtn.hidden = lastImportedTeams.length === 0;
    progressOverlay.hidden = false;
  });
  window.addEventListener("bandroom:songpackfailed", () => {
    progressOverlay.hidden = true;
    showToast("Song pack download failed -- check your connection and try again from Settings.");
  });
  window.addEventListener("bandroom:songpackimportfailed", (e) => {
    progressOverlay.hidden = true;
    showToast(e.detail?.message || "Couldn't unpack that file -- make sure you picked the song pack .zip and try again.");
  });
}

function closeBandroomMarketplace() {
  document.getElementById("bandroom-overlay").hidden = true;
  _lastAlbumTeam = null;
  document.getElementById("btn-forward-bandroom-album").hidden = true;
}

function renderBandroomTeamGrid(filter) {
  renderTeamGridInto("bandroom-team-grid", filter, (name) => openTeamAlbum(name));
}

let albumTeam = null;
// Where the currently-open album was opened from, so its logo can go "back" to the right place
// instead of just closing: "bandroom" if it replaced the hub's team grid (The Bandroom -> pick a
// team), "soundbank" if it jumped straight in (Sound Bank button, skips the picker per its own
// design) -- in that case "back" opens the full team-picker/coverflow instead, since there's no
// hub view underneath to return to.
let _albumOpenedFrom = null;
// Last team whose album was open, so the hub's Forward button can jump straight back into it
// after backFromTeamAlbum() returns to the hub -- cleared once you pick a DIFFERENT team so
// Forward never points at something stale.
let _lastAlbumTeam = null;

function openTeamAlbum(name) {
  marketplaceGuard(() => {
    const team = state.teams.find((t) => t.name === name);
    if (!team) return;
    albumTeam = team;
    _albumOpenedFrom = document.getElementById("bandroom-overlay").hidden ? "soundbank" : "bandroom";
    document.getElementById("btn-forward-bandroom-album").hidden = true;
    document.getElementById("bandroom-overlay").hidden = true;
    document.getElementById("bandroom-album-overlay").hidden = false;
    fillTeamSwatch(document.getElementById("bandroom-album-icon"), team);
    document.getElementById("bandroom-album-name").textContent = team.name;
    fillTeamSwatch(document.getElementById("bandroom-album-hero-icon"), team);
    document.getElementById("bandroom-album-hero-title").textContent = team.name;
    document.getElementById("bandroom-album-hero-meta").textContent = "Loading...";
    document.getElementById("bandroom-album-instructions").textContent =
      "Click a song to preview it, or a background to set it live. Hit + Upload to add your own "
      + "-- songs are trimmed/normalized automatically, images resized to a consistent size.";
    const albumSearch = document.getElementById("bandroom-album-search");
    if (albumSearch) albumSearch.value = "";
    initAlbumFilters();
    renderTeamAlbumGrid();
  }, "openTeamAlbum");
}

function closeTeamAlbum() {
  document.getElementById("bandroom-album-overlay").hidden = true;
  _previewAudio?.pause();
  albumTeam = null;
}

/// Clicking the team logo in the album header (bandroom-album-icon) -- goes back to team select
/// instead of closing everything: the hub's team grid if that's where this album came from,
/// otherwise the full team-picker coverflow (Sound Bank's direct-entry path has no hub to return
/// to underneath it).
function backFromTeamAlbum() {
  document.getElementById("bandroom-album-overlay").hidden = true;
  _previewAudio?.pause();
  _lastAlbumTeam = albumTeam?.name ?? null;
  albumTeam = null;
  if (_albumOpenedFrom === "bandroom") {
    document.getElementById("bandroom-overlay").hidden = false;
    document.getElementById("btn-forward-bandroom-album").hidden = !_lastAlbumTeam;
  } else {
    openTeamPicker();
  }
}

// Cache of the currently-open album's items (both types together, songs + background images --
// Trophy Room used to be its own tab/fetch; folded into one indexed list here), so the in-album
// search box (item 7) can filter instantly client-side instead of re-hitting the worker on every
// keystroke.
let _albumItemsCache = { songs: [], images: [] };
// Whether the *last* fetch failed (vs. genuinely returned zero items) -- a fetch error must
// render as an explicit retry state, not silently collapse into the same "no uploads yet" empty
// state a real empty team gets. Reset on every fresh fetch.
let _albumFetchError = false;

// Local default-song-pack files for the currently open album's team (see
// GetDefaultPackSongsForTeamFromWeb) -- these come from "Import Song Pack", never from the
// marketplace, so they'd otherwise never appear anywhere in this view. Owner report: importing
// a team's pack and opening that team's Sound Bank looked like nothing happened, because
// nothing here ever showed it.
let _albumDefaultPackCache = [];

async function renderTeamAlbumGrid() {
  const grid = document.getElementById("bandroom-songs-grid");
  const team = albumTeam;
  grid.innerHTML = `<div class="bandroom-empty-state">Loading...</div>`;
  const [songsResult, imagesResult, defaultPackJson] = await Promise.all([
    fetchUploadListDetailed("song", team.name),
    fetchUploadListDetailed("image", team.name),
    bridge ? bridge.GetDefaultPackSongsForTeam(team.name) : Promise.resolve("[]"),
  ]);
  if (!albumTeam || albumTeam !== team) return; // closed/switched while awaiting
  _albumItemsCache = { songs: songsResult.items, images: imagesResult.items };
  _albumFetchError = songsResult.error || imagesResult.error;
  try { _albumDefaultPackCache = JSON.parse(defaultPackJson) || []; } catch { _albumDefaultPackCache = []; }
  const heroMeta = document.getElementById("bandroom-album-hero-meta");
  if (heroMeta) {
    // Marketplace-uploaded songs (_albumItemsCache.songs) and imported default-pack songs
    // (_albumDefaultPackCache) are two separate sources that both render into the same grid below
    // (see paintAlbumGrid) -- this count needs both too, or a team like LSU with a full default
    // pack imported but zero marketplace uploads shows "0 songs" while the list right underneath
    // is full of them.
    const songCount = _albumItemsCache.songs.length + _albumDefaultPackCache.length;
    const imageCount = _albumItemsCache.images.length;
    heroMeta.textContent = _albumFetchError
      ? "Couldn't load this team's uploads"
      : `${songCount} song${songCount === 1 ? "" : "s"} · ${imageCount} background${imageCount === 1 ? "" : "s"}`;
  }
  paintAlbumGrid(getAlbumSearchFilter());
}

function getAlbumSearchFilter() {
  return (document.getElementById("bandroom-album-search")?.value ?? "").trim().toLowerCase();
}

// Sound Bank type filter pills (All/Songs/Backgrounds) -- same pattern as My Downloads'
// _myDownloadsFilter/initMyDownloadsToolbar above, just for the album view's two content types.
let _albumTypeFilter = "all";
let _albumFiltersBound = false;
function initAlbumFilters() {
  if (_albumFiltersBound) return;
  _albumFiltersBound = true;
  document.getElementById("bandroom-album-filters")?.addEventListener("click", (e) => {
    const btn = e.target.closest(".bandroom-album-filter");
    if (!btn) return;
    document.querySelectorAll(".bandroom-album-filter").forEach((b) => b.classList.remove("active"));
    btn.classList.add("active");
    _albumTypeFilter = btn.dataset.type;
    paintAlbumGrid(getAlbumSearchFilter());
  });
}

/// Renders the combined songs+images list from the cached item lists, filtered by the in-album
/// search box -- called both after a fresh fetch and on every search keystroke, so searching
/// never re-hits the network.
function paintAlbumGrid(filter) {
  if (!albumTeam) return;
  const grid = document.getElementById("bandroom-songs-grid");
  const team = albumTeam;
  const all = [..._albumItemsCache.songs, ..._albumItemsCache.images];
  let items = filter ? all.filter((it) => it.name.toLowerCase().includes(filter)) : all;
  if (_albumTypeFilter !== "all") items = items.filter((it) => it.type === _albumTypeFilter);

  grid.innerHTML = "";

  // Default Song Pack section is song-only content -- hide it under the "Backgrounds" filter,
  // same as the marketplace items above respect _albumTypeFilter.
  const packSongs = _albumTypeFilter === "image" ? [] : (filter
    ? _albumDefaultPackCache.filter((s) => s.name.toLowerCase().includes(filter))
    : _albumDefaultPackCache);
  if (packSongs.length > 0) {
    const section = document.createElement("div");
    section.className = "bandroom-defaultpack-section";
    const header = document.createElement("div");
    header.className = "bandroom-defaultpack-header";
    header.textContent = `Default Song Pack (${packSongs.length}) -- already filling in matching situations for ${team.name}`;
    section.appendChild(header);

    // Grouped by "category" (the trigger name with its trailing dedupe index stripped, see
    // GetDefaultPackSongsForTeamFromWeb) instead of one flat alphabetical list -- a team with
    // 100+ pack extras used to read as unsorted noise ("Defense_Drive Starter_4/_5/_6..." one
    // after another with nothing telling them apart at a glance). Each group is its own
    // collapsible folder, same visual language as My Downloads' team grouping headers.
    const groups = new Map();
    for (const s of packSongs) {
      const key = s.category || "Other";
      if (!groups.has(key)) groups.set(key, []);
      groups.get(key).push(s);
    }
    const sortedKeys = [...groups.keys()].sort((a, b) => a.localeCompare(b));

    for (const key of sortedKeys) {
      const songs = groups.get(key);
      const groupEl = document.createElement("div");
      groupEl.className = "bandroom-defaultpack-group";

      const groupHeader = document.createElement("button");
      groupHeader.type = "button";
      groupHeader.className = "bandroom-defaultpack-group-header";
      groupHeader.innerHTML = `<span class="bandroom-defaultpack-group-chevron">›</span><span>${sanitizeHTML(key.replace(/_/g, " "))}</span><span class="bandroom-defaultpack-group-count">${songs.length}</span>`;

      const list = document.createElement("div");
      list.className = "bandroom-defaultpack-list";
      songs.forEach((s) => {
        const row = document.createElement("div");
        row.className = "bandroom-defaultpack-row";
        const name = document.createElement("span");
        name.className = "bandroom-defaultpack-name";
        name.textContent = s.name;
        const btn = document.createElement("button");
        btn.className = "preview-bar-btn";
        btn.title = "Preview";
        btn.textContent = "▶";
        btn.addEventListener("click", () => bridge?.PreviewLocalFile(s.path));
        row.appendChild(name);
        row.appendChild(btn);
        list.appendChild(row);
      });

      // Collapsed by default when there are several groups (a fresh 100+ song pack import can
      // produce a dozen+ categories) -- expanded automatically while actively searching, since a
      // filtered group is one the user specifically asked to see into.
      const collapsed = !filter && sortedKeys.length > 1;
      groupEl.classList.toggle("collapsed", collapsed);
      groupHeader.addEventListener("click", () => groupEl.classList.toggle("collapsed"));

      groupEl.appendChild(groupHeader);
      groupEl.appendChild(list);
      section.appendChild(groupEl);
    }
    grid.appendChild(section);
  }

  if (_albumFetchError && all.length === 0) {
    // Distinct from the "genuinely zero uploads" case below -- offers a retry instead of
    // implying there's really nothing here.
    const errorState = document.createElement("div");
    errorState.className = "bandroom-empty-state bandroom-error-state";
    errorState.textContent = `Couldn't load ${team.name}'s uploads -- check your connection. `;
    const retryBtn = document.createElement("button");
    retryBtn.className = "bandroom-item-action";
    retryBtn.textContent = "Retry";
    retryBtn.addEventListener("click", () => renderTeamAlbumGrid());
    errorState.appendChild(retryBtn);
    grid.appendChild(errorState);
  } else if (all.length === 0) {
    const empty = document.createElement("div");
    empty.className = "bandroom-empty-state";
    empty.textContent = `Nothing uploaded for ${team.name} yet -- be the first!`;
    grid.appendChild(empty);
  } else if (items.length === 0) {
    const empty = document.createElement("div");
    empty.className = "bandroom-empty-state";
    empty.textContent = `Nothing matches "${filter}".`;
    grid.appendChild(empty);
  } else {
    for (const item of items) {
      const tile = buildItemTile(item);
      if (item.type === "image") {
        // Pre-existing bug found during this pass: buildItemTile's thumb has always been
        // .marketplace-card-thumb here (this call site never passed the old inHub=true that
        // would've made it .bandroom-item-thumb) -- this querySelector always returned null,
        // throwing on every image-type item and silently truncating the rest of the album grid's
        // render loop. Fixed to the actual thumb class.
        const thumb = tile.querySelector(".marketplace-card-thumb");
        if (thumb) {
          thumb.style.setProperty("--tile-color", team.secondary);
          thumb.classList.add("bandroom-image-slot");
        }
      }
      grid.appendChild(tile);
    }
  }
  grid.appendChild(buildUploadTile("song"));
  grid.appendChild(buildUploadTile("image"));
}

function onAlbumSearchInput() {
  paintAlbumGrid(getAlbumSearchFilter());
}

/// Bulk-download (item 21): sequential downloads of every currently-visible item in the album's
/// active tab (respects the search filter, same as the grid it's downloading). No zipping --
/// keeping this to native browser downloads avoids pulling in a zip library with no build step
/// to vendor it through (same constraint noted on the audio compression path).
async function downloadAlbumAll() {
  if (!albumTeam) return;
  const filter = getAlbumSearchFilter();
  const all = [..._albumItemsCache.songs, ..._albumItemsCache.images];
  const items = filter ? all.filter((it) => it.name.toLowerCase().includes(filter)) : all;
  if (items.length === 0) { showToast("Nothing to download here."); return; }

  showToast(`Downloading ${items.length} item${items.length === 1 ? "" : "s"}...`);
  for (const item of items) {
    try {
      const res = await fetch(item.url);
      if (!res.ok) continue;
      const blob = await res.blob();
      const a = document.createElement("a");
      a.href = URL.createObjectURL(blob);
      // Prefer the real extension off the item's own URL (the worker's /file/<key> path keeps
      // the original uploaded filename) so a pre-compression-era upload (or a fallback upload
      // that kept its original format) doesn't get mislabeled -- falls back to the expected
      // compressed-format extension only if the URL has none.
      const urlExt = (item.url.split("?")[0].match(/\.([a-zA-Z0-9]{1,5})$/) || [])[1];
      const ext = urlExt || (item.type === "song" ? "webm" : "jpg");
      a.download = `${sanitizeForFilename(albumTeam.name)}-${sanitizeForFilename(item.name)}.${ext}`;
      document.body.appendChild(a);
      a.click();
      a.remove();
      setTimeout(() => URL.revokeObjectURL(a.href), 10000);
      // Small stagger between downloads -- firing many `a.click()` downloads back-to-back can
      // get some of them silently dropped by the browser's download manager.
      await new Promise((r) => setTimeout(r, 300));
    } catch (err) {
      console.error(`downloadAlbumAll failed for "${item.name}"`, err);
    }
  }
}

function sanitizeForFilename(s) {
  return String(s ?? "file").replace(/[^\w\s-]/g, "").trim().replace(/\s+/g, "_").slice(0, 60) || "file";
}

function buildUploadTile(type) {
  const tile = document.createElement("div");
  tile.className = "bandroom-slot";
  tile.innerHTML = `<span class="bandroom-slot-plus">+</span><span class="bandroom-slot-label">Upload ${type === "song" ? "Song" : "Image"}</span>`;
  tile.title = `${albumTeam.name} — upload a ${type === "song" ? "song" : "background image"}`;
  // Songs go through the native Clipper pipeline (pick -> name -> trim/normalize, same as
  // My Downloads local import) instead of uploading a raw untrimmed file straight to the
  // worker -- images have no clip concept so they keep the browser file-picker + compress flow.
  tile.addEventListener("click", () => type === "song" ? uploadSongViaClipper() : openUploadPicker(type));
  return tile;
}

/// Marketplace "+ Upload Song" for a team's Sound Bank: runs the native file-picker -> name ->
/// TrimmerForm pipeline (bridge.ImportAndUploadSongToMarketplace) instead of the raw browser
/// upload path, so every marketplace song is trimmed/normalized like every other song in the
/// app, not just locally-imported ones. Requires the native bridge (no browser-only fallback --
/// trimming needs the native TrimmerForm).
async function uploadSongViaClipper() {
  if (!albumTeam || !bridge) { showToast("Uploading needs the desktop app -- not available here."); return; }
  try {
    const raw = await bridge.ImportAndUploadSongToMarketplace(albumTeam.name);
    const result = raw ? JSON.parse(raw) : null;
    if (result?.cancelled) return;
    if (result?.success) {
      showToast(`Uploaded to ${albumTeam.name}'s Sound Bank!`);
      renderTeamAlbumGrid();
    } else {
      showToast(result?.error ?? "Couldn't upload that -- try again.");
    }
  } catch (err) {
    console.error("uploadSongViaClipper failed", err);
    showToast("Couldn't upload that -- try again.");
  }
}

// ---- Upload flow --------------------------------------------------------------------------
// School is never re-typed here -- it's always the album's own team (albumTeam.name), per the
// upload spec's "name AND school" requirement being satisfied implicitly by context instead of
// asking the user to retype something already known.
let pendingUpload = null; // { type, file }

function openUploadPicker(type) {
  pendingUpload = { type, file: null };
  const input = document.getElementById("bandroom-upload-file-input");
  input.accept = type === "song" ? "audio/*" : "image/*";
  input.value = ""; // allow picking the same file twice in a row
  input.click();
}

function onUploadFileChosen(e) {
  const file = e.target.files?.[0];
  if (!file || !pendingUpload) return;
  pendingUpload.file = file;

  const overlay = document.getElementById("bandroom-upload-overlay");
  document.getElementById("bandroom-upload-header").textContent =
    pendingUpload.type === "song" ? "Upload Song" : "Upload Background Image";
  document.getElementById("bandroom-upload-instructions").textContent =
    `Uploading to ${albumTeam.name}'s Sound Bank. `
    + (pendingUpload.type === "song"
      ? "It'll be compressed automatically so every upload plays at a consistent volume/size. "
        + "Name it Team + Situation + Description (e.g. “"
        + (albumTeam.initials || albumTeam.name) + " 3rd Down Stop”) -- that's what makes "
        + "auto-assign and profile sharing able to find and match it later."
      : "It'll be resized/compressed automatically so every background image is a consistent size.");
  const nameInput = document.getElementById("bandroom-upload-name");
  nameInput.value = "";
  nameInput.placeholder = pendingUpload.type === "song"
    ? `e.g. ${albumTeam.initials || albumTeam.name} Touchdown Hype`
    : "Name...";
  document.getElementById("bandroom-upload-subtext").textContent = "";
  document.getElementById("btn-bandroom-upload-confirm").disabled = false;
  overlay.hidden = false;
  nameInput.focus();
}

function closeUploadDialog() {
  document.getElementById("bandroom-upload-overlay").hidden = true;
  pendingUpload = null;
}

async function confirmUpload() {
  if (!pendingUpload?.file || !albumTeam) return;
  const name = document.getElementById("bandroom-upload-name").value.trim();
  if (!name) {
    document.getElementById("bandroom-upload-subtext").textContent = "Enter a name first.";
    return;
  }

  const confirmBtn = document.getElementById("btn-bandroom-upload-confirm");
  const subtext = document.getElementById("bandroom-upload-subtext");
  confirmBtn.disabled = true;

  // Elapsed-time counter -- audio compression runs in real time (MediaRecorder has to play
  // the clip through to capture it), so a long song with only a static "Compressing..."
  // string reads as a hang. Ticks a spinner + "Xs elapsed" every 250ms until upload settles.
  const spinnerFrames = ["⣾", "⣽", "⣻", "⣟", "⡿", "⢿"];
  let spinnerIdx = 0;
  const startedAt = Date.now();
  let progressLabel = "Compressing";
  const progressTimer = setInterval(() => {
    const secs = ((Date.now() - startedAt) / 1000).toFixed(1);
    spinnerIdx = (spinnerIdx + 1) % spinnerFrames.length;
    subtext.textContent = `${spinnerFrames[spinnerIdx]} ${progressLabel}... (${secs}s)`;
  }, 250);
  const stopProgress = () => clearInterval(progressTimer);

  try {
    let uploadFile = pendingUpload.file;
    try {
      uploadFile = pendingUpload.type === "image"
        ? await compressImageFile(pendingUpload.file)
        : await compressAudioFile(pendingUpload.file);
    } catch (err) {
      // Compression is a nice-to-have, not a hard requirement -- if the browser can't do it
      // (unsupported codec, huge file, etc.) fall back to the original file rather than
      // blocking the upload entirely.
      console.error("Upload compression failed, using original file", err);
      uploadFile = pendingUpload.file;
    }

    progressLabel = "Uploading";
    const form = new FormData();
    form.append("type", pendingUpload.type);
    form.append("name", name);
    form.append("school", albumTeam.name);
    form.append("file", uploadFile, uploadFile.name || pendingUpload.file.name);

    const res = await fetch(`${MARKETPLACE_URL}/upload`, { method: "POST", body: form });
    if (!res.ok) throw new Error(`upload failed: ${res.status} ${await res.text()}`);
    const uploadResult = await res.json().catch(() => null);

    stopProgress();
    recordMyUpload(pendingUpload.type, uploadResult);
    try { await bridge?.RecordMarketplaceUpload(); } catch (err) { console.error("RecordMarketplaceUpload failed", err); }
    closeUploadDialog();
    showToast(`Uploaded "${name}" to ${albumTeam.name}!`);
    renderTeamAlbumGrid();
  } catch (err) {
    stopProgress();
    console.error("Upload failed", err);
    subtext.textContent = "Upload failed -- check your connection and try again.";
    confirmBtn.disabled = false;
  }
}

/// Resizes/re-encodes an image client-side so every Trophy Room upload is a consistent max
/// size/format instead of whatever resolution and format the user's original file happened to
/// be -- caps the longer edge at 1600px and re-encodes as JPEG at a fixed quality.
function compressImageFile(file) {
  const MAX_DIM = 1600;
  const QUALITY = 0.85;
  return new Promise((resolve, reject) => {
    const img = new Image();
    const url = URL.createObjectURL(file);
    img.onload = () => {
      URL.revokeObjectURL(url);
      let { width, height } = img;
      const scale = Math.min(1, MAX_DIM / Math.max(width, height));
      width = Math.round(width * scale);
      height = Math.round(height * scale);
      const canvas = document.createElement("canvas");
      canvas.width = width;
      canvas.height = height;
      canvas.getContext("2d").drawImage(img, 0, 0, width, height);
      canvas.toBlob((blob) => {
        if (!blob) { reject(new Error("canvas.toBlob returned null")); return; }
        resolve(new File([blob], renameExt(file.name, "jpg"), { type: "image/jpeg" }));
      }, "image/jpeg", QUALITY);
    };
    img.onerror = () => { URL.revokeObjectURL(url); reject(new Error("image failed to load")); };
    img.src = url;
  });
}

/// Re-encodes an audio clip client-side to Opus/WebM at a fixed bitrate using only native
/// browser APIs (Web Audio decode + MediaRecorder capture -- no bundled encoder library, since
/// there's no build step/bundler in this project to vendor one through). Runs in real time
/// (MediaRecorder has to actually play the clip through to capture it), which is fine for the
/// short trimmed clips this app deals with. 160kbps Opus is transparent/"HD" for spoken/music
/// clips at a fraction of source WAV size.
function compressAudioFile(file) {
  const TARGET_BITRATE = 160000;
  return new Promise((resolve, reject) => {
    (async () => {
      const AudioContextCtor = window.AudioContext || window.webkitAudioContext;
      if (!AudioContextCtor || !window.MediaRecorder) throw new Error("audio compression not supported in this environment");

      const arrayBuffer = await file.arrayBuffer();
      const audioCtx = new AudioContextCtor();
      const audioBuffer = await audioCtx.decodeAudioData(arrayBuffer);

      const dest = audioCtx.createMediaStreamDestination();
      const src = audioCtx.createBufferSource();
      src.buffer = audioBuffer;
      src.connect(dest);

      const mimeType = MediaRecorder.isTypeSupported("audio/webm;codecs=opus")
        ? "audio/webm;codecs=opus"
        : "audio/webm";
      const recorder = new MediaRecorder(dest.stream, { mimeType, audioBitsPerSecond: TARGET_BITRATE });
      const chunks = [];
      recorder.ondataavailable = (e) => { if (e.data.size > 0) chunks.push(e.data); };
      recorder.onstop = () => {
        audioCtx.close().catch(() => {});
        // If MediaRecorder never emitted a single dataavailable chunk (can happen for a
        // very short clip, or a browser/driver hiccup), resolving with an empty File would
        // "succeed" but upload a 0-byte, unplayable file. Reject instead so confirmUpload's
        // fallback path uploads the original file -- silently corrupt beats silently empty.
        const totalBytes = chunks.reduce((n, c) => n + c.size, 0);
        if (totalBytes === 0) { reject(new Error("audio compression produced no data")); return; }
        resolve(new File(chunks, renameExt(file.name, "webm"), { type: mimeType }));
      };
      recorder.onerror = (e) => { audioCtx.close().catch(() => {}); reject(e.error ?? new Error("MediaRecorder error")); };

      recorder.start();
      src.start(0);
      // +200ms safety margin over the buffer's exact duration so the tail isn't clipped.
      // Guard against a degenerate (zero/NaN-duration) decoded buffer -- a bad/corrupt source
      // file could decode "successfully" into a buffer with no real length, and
      // setTimeout(fn, NaN) never fires, which would hang the upload forever instead of
      // falling back to the original file.
      const stopDelayMs = Number.isFinite(audioBuffer.duration) && audioBuffer.duration > 0
        ? audioBuffer.duration * 1000 + 200
        : 200;
      setTimeout(() => { try { recorder.stop(); } catch { /* already stopped */ } }, stopDelayMs);
    })().catch(reject);
  });
}

function renameExt(filename, ext) {
  const base = (filename || "upload").replace(/\.[^./\\]+$/, "");
  return `${base}.${ext}`;
}

let _onboardingPicked = null;

async function maybeShowOnboarding() {
  if (!bridge || !(await bridge.IsFirstRun())) return;
  const overlay = document.getElementById("onboarding-overlay");
  overlay.hidden = false;

  renderOnboardingCoverflow("");
  document.getElementById("onboarding-search").addEventListener("input", (e) =>
    renderOnboardingCoverflow(e.target.value));
  document.querySelectorAll("#onboarding .coverflow-arrow").forEach((btn) => {
    btn.addEventListener("click", () => shiftOnboardingCoverflow(parseInt(btn.dataset.dir, 10)));
  });
  document.getElementById("btn-onboarding-confirm").addEventListener("click", async () => {
    if (!_onboardingPicked) return;
    try {
      await bridge.CompleteFirstRun(_onboardingPicked);
      state.activeTeam = _onboardingPicked;
      setActiveTeam(_onboardingPicked); // also resets the glow off whatever was just browsed
      overlay.hidden = true;
      pointOutTheBandroom();
    } catch (err) {
      console.error("CompleteFirstRun failed", err);
      showToast("Couldn't save that pick -- try again.");
    }
  });
}

/// Same cover-flow pattern as renderMatchupCoverflow (matchupCoverflowTeams filter, cf-l2/l1/
/// center/r1/r2 positions) but a single team column with an explicit Confirm step instead of
/// browsing-is-picking -- first-run team choice shouldn't lock in on a stray arrow click.
function renderOnboardingCoverflow(filter) {
  const track = document.getElementById("onboarding-track");
  const nameEl = document.getElementById("onboarding-name");
  if (!track || !nameEl) return;
  const teams = matchupCoverflowTeams(filter);
  track.innerHTML = "";
  if (!teams.length) {
    nameEl.textContent = "No teams found";
    document.getElementById("btn-onboarding-confirm").disabled = true;
    return;
  }

  let centerIdx = teams.findIndex((t) => t.name === _onboardingPicked);
  if (centerIdx === -1) centerIdx = 0;

  const positions = [[-2, "cf-l2"], [-1, "cf-l1"], [0, "cf-center"], [1, "cf-r1"], [2, "cf-r2"]];
  for (const [offset, cls] of positions) {
    const idx = ((centerIdx + offset) % teams.length + teams.length) % teams.length;
    const t = teams[idx];
    const tile = document.createElement("div");
    tile.className = "team-swatch " + cls;
    tile.title = t.name;
    fillTeamSwatch(tile, t, true);
    tile.addEventListener("click", () => {
      _onboardingPicked = t.name;
      renderOnboardingCoverflow(filter);
    });
    track.appendChild(tile);
  }

  _onboardingPicked = teams[centerIdx].name;
  nameEl.textContent = _onboardingPicked;
  document.getElementById("btn-onboarding-confirm").disabled = false;
  previewTeamGlow(teams[centerIdx]);
}

function shiftOnboardingCoverflow(dir) {
  const filter = document.getElementById("onboarding-search")?.value || "";
  const teams = matchupCoverflowTeams(filter);
  if (!teams.length) return;
  let idx = teams.findIndex((t) => t.name === _onboardingPicked);
  if (idx === -1) idx = 0;
  idx = ((idx + dir) % teams.length + teams.length) % teams.length;
  _onboardingPicked = teams[idx].name;
  renderOnboardingCoverflow(filter);
}

// ---- Favorite Team picker (task queue item 3, Session 10) -----------------------------
// Was a plain <select> in the Profile dialog -- the owner's "should be the coverflow" -- now the
// same cover-flow carousel pattern as onboarding directly above (which itself mirrors Set
// Matchup's). Kept as its own set of functions rather than sharing renderOnboardingCoverflow
// outright: that function is wired to onboarding-specific element ids and a first-run-only
// "CompleteFirstRun" confirm action, and forcing a shared function to take an id-prefix/callback
// parameter for one more caller wasn't worth the indirection. matchupCoverflowTeams and
// fillTeamSwatch -- the actual reusable primitives -- ARE reused, same as onboarding does.
let _favoriteCoverflowPicked = null;
let _favoriteCoverflowWired = false;

function openFavoriteTeamCoverflow() {
  const overlay = document.getElementById("favorite-team-overlay");
  if (!overlay) return;
  overlay.hidden = false;
  _favoriteCoverflowPicked = document.getElementById("profile-favorite-team-label")?.textContent;
  if (_favoriteCoverflowPicked === "None selected") _favoriteCoverflowPicked = null;
  const search = document.getElementById("favorite-team-search");
  search.value = "";
  renderFavoriteCoverflow("");

  // Bound once -- reopening this overlay on every Favorite Team click would otherwise stack up
  // duplicate listeners, same guard pattern as _hubSortListenerBound elsewhere in this file.
  if (!_favoriteCoverflowWired) {
    _favoriteCoverflowWired = true;
    search.addEventListener("input", (e) => renderFavoriteCoverflow(e.target.value));
    document.querySelectorAll("#favorite-team-dialog .coverflow-arrow").forEach((btn) => {
      btn.addEventListener("click", () => shiftFavoriteCoverflow(parseInt(btn.dataset.dir, 10)));
    });
    document.getElementById("btn-close-favorite-team").addEventListener("click", () => { overlay.hidden = true; restoreActiveTeamGlow(); });
    document.getElementById("btn-favorite-team-confirm").addEventListener("click", async () => {
      const team = _favoriteCoverflowPicked;
      try {
        await bridge.SetFavoriteTeam(team ?? "");
        // Setting a favorite team also switches the app's active team/theme -- same effect as
        // clicking that team's tile in the Teams panel (see selectTeam) -- so picking one here
        // visibly does something instead of silently saving a preference nobody can see. Same
        // behavior the old <select>'s change handler had.
        if (team) await selectTeam(team);
        updateFavoriteTeamJumpButton(team); // otherwise the header star button stays stale until Profile is closed/reopened
        document.getElementById("profile-favorite-team-label").textContent = team || "None selected";
        showToast(team ? `Favorite team set to ${team}.` : "Favorite team cleared.");
      } catch (err) {
        console.error("SetFavoriteTeam failed", err);
        showToast("Couldn't save favorite team -- try again.");
        // Error path skips the selectTeam() call above, which is what would have otherwise reset
        // the glow to the real active team -- without this it stays stuck on whatever team was
        // last browsed/previewed, not the team that's actually still active.
        restoreActiveTeamGlow();
      }
      overlay.hidden = true;
    });
  }
}

function renderFavoriteCoverflow(filter) {
  const track = document.getElementById("favorite-team-track");
  const nameEl = document.getElementById("favorite-team-name");
  if (!track || !nameEl) return;
  const teams = matchupCoverflowTeams(filter);
  track.innerHTML = "";
  if (!teams.length) {
    nameEl.textContent = "No teams found";
    document.getElementById("btn-favorite-team-confirm").disabled = true;
    return;
  }

  let centerIdx = teams.findIndex((t) => t.name === _favoriteCoverflowPicked);
  if (centerIdx === -1) centerIdx = 0;

  const positions = [[-2, "cf-l2"], [-1, "cf-l1"], [0, "cf-center"], [1, "cf-r1"], [2, "cf-r2"]];
  for (const [offset, cls] of positions) {
    const idx = ((centerIdx + offset) % teams.length + teams.length) % teams.length;
    const t = teams[idx];
    const tile = document.createElement("div");
    tile.className = "team-swatch " + cls;
    tile.title = t.name;
    fillTeamSwatch(tile, t, true);
    tile.addEventListener("click", () => {
      _favoriteCoverflowPicked = t.name;
      renderFavoriteCoverflow(filter);
    });
    track.appendChild(tile);
  }

  _favoriteCoverflowPicked = teams[centerIdx].name;
  nameEl.textContent = _favoriteCoverflowPicked;
  document.getElementById("btn-favorite-team-confirm").disabled = false;
  previewTeamGlow(teams[centerIdx]);
}

function shiftFavoriteCoverflow(dir) {
  const filter = document.getElementById("favorite-team-search")?.value || "";
  const teams = matchupCoverflowTeams(filter);
  if (!teams.length) return;
  let idx = teams.findIndex((t) => t.name === _favoriteCoverflowPicked);
  if (idx === -1) idx = 0;
  idx = ((idx + dir) % teams.length + teams.length) % teams.length;
  _favoriteCoverflowPicked = teams[idx].name;
  renderFavoriteCoverflow(filter);
}

/// First-run onboarding only ever asked for a favorite team -- it never mentioned The Bandroom
/// (the community marketplace) at all. Points a one-time highlight tooltip at that header button
/// right after onboarding finishes, dismissed by clicking it (which also opens it) or by a timeout.
function pointOutTheBandroom() {
  const btn = document.getElementById("btn-bandroom-cloud");
  if (!btn) return;
  btn.classList.add("onboarding-spotlight");

  const tip = document.createElement("div");
  tip.className = "onboarding-tooltip";
  tip.textContent = "New here? Check out The Bandroom -- a community library of songs and backgrounds other bands have shared.";
  document.body.appendChild(tip);

  const positionTip = () => {
    const r = btn.getBoundingClientRect();
    tip.style.top = `${r.bottom + 10}px`;
    tip.style.left = `${Math.max(8, r.left + r.width / 2 - tip.offsetWidth / 2)}px`;
  };
  requestAnimationFrame(positionTip);

  let dismissed = false;
  const dismiss = () => {
    if (dismissed) return;
    dismissed = true;
    btn.classList.remove("onboarding-spotlight");
    tip.remove();
    btn.removeEventListener("click", dismiss);
    window.removeEventListener("resize", positionTip);
  };
  btn.addEventListener("click", dismiss);
  window.addEventListener("resize", positionTip);
  setTimeout(dismiss, 9000);
}

function showToast(text) {
  if (state.toastsEnabled === false) return; // Profile tab's "Show toast notifications" toggle
  const t = document.createElement("div");
  t.className = "toast";
  t.textContent = text;
  document.body.appendChild(t);
  requestAnimationFrame(() => t.classList.add("toast-visible"));
  setTimeout(() => { t.classList.remove("toast-visible"); setTimeout(() => t.remove(), 300); }, 2600);
}

function flashPanel(el) {
  el.classList.add("panel-flash");
  setTimeout(() => el.classList.remove("panel-flash"), 900);
}

/// Punch-list item 6 helper: scrolls the situations-panel to the event card matching `trigger`
/// (see the row.dataset.trigger set in the situations-list render loop) and flashes it, so
/// finishing a trim-save lands the user back on the specific card the clip belongs to instead of
/// a generic, context-free song-list view.
function scrollToSituationRow(trigger) {
  const row = document.querySelector(`#situations-list .situation-row[data-trigger="${CSS.escape(trigger)}"]`);
  if (!row) return;
  row.scrollIntoView({ behavior: "smooth", block: "center" });
  flashPanel(row);
}

function updateMatchupLabel() {
  const btn = document.getElementById("btn-matchup");
  const unlockBtn = document.getElementById("btn-unlock-matchup");
  if (!btn) return;
  btn.classList.toggle("locked", state.matchupLocked);
  if (unlockBtn) unlockBtn.hidden = !state.matchupLocked;
  // Locked-in mode (owner request, 2026-08-11): hides the TEAM logo grid (#left-panel) once a
  // matchup is locked in -- see the #app.locked-in-mode rule in style.css. Nav-rack (The
  // Bandroom/Sound Bank/etc) stays, owner explicitly wanted that kept.
  document.getElementById("app")?.classList.toggle("locked-in-mode", state.matchupLocked);
  if (state.matchupLocked) {
    btn.textContent = `\u{1F512} ${state.matchupAway} @ ${state.matchupHome}`;
    btn.title = "Locked in for this game -- press Stop Watching when it ends to change it, or use the unlock button to correct it without stopping";
  } else {
    btn.textContent = state.matchupHome && state.matchupAway
      ? `${state.matchupAway} @ ${state.matchupHome}`
      : "LOCK IN?";
    // Clicking this again (whether or not a matchup is already picked) reopens the dialog --
    // openMatchupDialog() only refuses while state.matchupLocked (mid-game), so this is already
    // the "change matchup teams" entry point, not just the first-time picker.
    btn.title = "Pick who's home and away for this game";
  }
  updateWatchGate();
  updateMatchupSideBar();
  updateTeamPresetBar();
}

// Start Watching stays disabled until both matchup teams are actually picked -- previously it
// was always clickable and just alerted "no-matchup" after a round trip to the host. Gating it
// client-side makes the requirement visible up front instead of discovered by clicking.
function updateWatchGate() {
  const status = document.getElementById("watch-status");
  if (!status) return;
  const ready = !!(state.matchupHome && state.matchupAway);
  status.title = ready ? "Press GAMETIME to start watching" : "Set Matchup first";
}

async function loadMatchup() {
  if (!bridge) return;
  try {
    const raw = await bridge.GetGameTeams();
    if (!raw) return;
    const { home, away, locked } = JSON.parse(raw);
    state.matchupHome = home;
    state.matchupAway = away;
    state.matchupLocked = !!locked;
    updateMatchupLabel();
    if (state.matchupLocked) await applyVsBackdrop();
  } catch (err) { console.error("GetGameTeams failed", err); }
}

document.getElementById("btn-unlock-matchup")?.addEventListener("click", async () => {
  await bridge?.UnlockMatchup();
  state.matchupLocked = false;
  exitGameDayMode();
  updateMatchupLabel();
  showToast("Matchup unlocked -- watching is still running.");
});

function openMatchupDialog() {
  if (state.matchupLocked) {
    showToast("Matchup is locked for this game -- press Stop Watching at the top when it ends.");
    return;
  }
  const overlay = document.getElementById("matchup-overlay");
  document.getElementById("matchup-home-search").value = "";
  document.getElementById("matchup-away-search").value = "";
  // Unhide BEFORE rendering: squareUpTiles measures rendered tile width via
  // getBoundingClientRect, which is 0 while the overlay is still display:none.
  overlay.hidden = false;
  renderMatchupCoverflow("home", "");
  renderMatchupCoverflow("away", "");
  renderMatchupSideGrid("home", "");
  renderMatchupSideGrid("away", "");
  updateMatchupSubtext();
  loadScorebugSwitcher();
  // No re-fetch needed here -- applyBigGameEnabled (see wireBigGameSection) already keeps
  // #toggle-matchup-big-game in sync with the real setting the moment it's loaded at startup
  // (refreshBigGameSection) or changed by any of the three Big Game controls.
  initMatchupLastPill();
  wireMatchupGameSettingsPill();
}

// Owner request: one-click re-select of the previous game's teams. Queries
// WebBridge.GetLastMatchup() (backed by ConfigStore.SaveLastMatchup, written every time
// ConfirmGametimeFromWeb locks in a matchup) fresh on every dialog open rather than caching --
// cheap single JSON read, and guarantees the pill never shows a stale pair from earlier in the
// session. Hidden entirely (not just disabled) when nothing's been recorded yet.
async function initMatchupLastPill() {
  const pill = document.getElementById("matchup-last-pill");
  if (!pill || !bridge) return;
  pill.hidden = true;
  try {
    const raw = await bridge.GetLastMatchup();
    const last = raw ? JSON.parse(raw) : null;
    if (!last || !last.home || !last.away) return;
    pill.textContent = `Last: ${last.away} @ ${last.home}`;
    pill.hidden = false;
    pill.onclick = () => {
      state.matchupHome = last.home;
      state.matchupAway = last.away;
      renderMatchupCoverflow("home", document.getElementById("matchup-home-search")?.value || "");
      renderMatchupCoverflow("away", document.getElementById("matchup-away-search")?.value || "");
      updateMatchupSubtext();
    };
  } catch (err) { console.error("GetLastMatchup failed", err); }
}

/// Fast vertical scrub list beside each side's coverflow (owner request) -- same click-to-select
/// convention as the sidebar team grid (renderTeamGridInto), not the coverflow's browse-to-center
/// pattern. Picking a team here re-centers that side's coverflow on it too, so both stay in sync
/// no matter which one you used.
function renderMatchupSideGrid(side, filter) {
  const gridId = `matchup-${side}-side-grid`;
  renderTeamGridInto(gridId, filter, (name) => {
    if (side === "home") state.matchupHome = name; else state.matchupAway = name;
    renderMatchupCoverflow(side, document.getElementById(`matchup-${side}-search`)?.value || "");
    renderMatchupSideGrid(side, filter);
  });
  wireMatchupSideGridDock(gridId);
  wireMatchupSideGridWheel(gridId);
  const activeTeam = side === "home" ? state.matchupHome : state.matchupAway;
  scrollActiveTileToCenter(gridId, activeTeam);
}

/// Owner request 2026-08-11: the side-grid strip used to open scrolled to the top of the A-Z list
/// no matter which team was already centered in the big coverflow next to it. Center the tile
/// matching that team instead, so the two stay visually lined up on open/pick/re-render.
function scrollActiveTileToCenter(gridId, teamName) {
  const grid = document.getElementById(gridId);
  if (!grid || !teamName) return;
  const tile = Array.from(grid.querySelectorAll(".team-swatch")).find((el) => el.title === teamName);
  if (!tile) return;
  const targetTop = tile.offsetTop - grid.clientHeight / 2 + tile.offsetHeight / 2;
  grid.scrollTop = Math.max(0, targetTop);
}

// ---- Matchup side-grid fast + looping wheel scroll (owner request 2026-08-11) -----------------
// Native wheel scrolling felt slow for a full A-Z roster, and stopped dead at the very top/bottom
// instead of wrapping back around -- owner wants to be able to keep scrolling past Z and land back
// on A (and vice versa past A back to Z) instead of hitting a hard edge.
const MATCHUP_SIDE_GRID_WHEEL_SPEED = 2.75;

function wireMatchupSideGridWheel(gridId) {
  const grid = document.getElementById(gridId);
  if (!grid || grid._wheelWired) return;
  grid._wheelWired = true;

  grid.addEventListener("wheel", (e) => {
    e.preventDefault();
    const max = grid.scrollHeight - grid.clientHeight;
    if (max <= 0) return;
    let next = grid.scrollTop + e.deltaY * MATCHUP_SIDE_GRID_WHEEL_SPEED;
    if (next < 0) next = max; // scrolled past A -- wrap to Z
    else if (next > max) next = 0; // scrolled past Z -- wrap to A
    grid.scrollTop = next;
  }, { passive: false });
}

// ---- Matchup side-grid "Mac Dock" hover-magnify (owner request, finished 2026-08-11) -----------
// Real proximity-based magnify, not achievable with hover-only CSS -- tiles near the cursor scale
// up, tapering off for neighbors the farther they are, same feel as macOS's Dock. --dock-scale is
// read by .matchup-side-grid .team-swatch's own `transform: scale(var(--dock-scale, 1))` (style.css)
// so the actual visual transform/transition lives in CSS; this just drives the variable per tile.
// Wired once per grid container (idempotent via _dockWired) -- the container DOM node persists
// across re-renders (renderTeamGridInto only replaces its children), so binding once here is
// enough even though renderMatchupSideGrid re-calls this on every keystroke/pick.
const MATCHUP_DOCK_MAX_SCALE = 1.55;
const MATCHUP_DOCK_FALLOFF_PX = 60;

function wireMatchupSideGridDock(gridId) {
  const grid = document.getElementById(gridId);
  if (!grid || grid._dockWired) return;
  grid._dockWired = true;

  function apply(mouseY) {
    for (const tile of grid.querySelectorAll(".team-swatch")) {
      let scale = 1;
      if (mouseY !== null) {
        const rect = tile.getBoundingClientRect();
        const dist = Math.abs(rect.top + rect.height / 2 - mouseY);
        const t = Math.max(0, 1 - dist / MATCHUP_DOCK_FALLOFF_PX);
        scale = 1 + (MATCHUP_DOCK_MAX_SCALE - 1) * t;
      }
      tile.style.setProperty("--dock-scale", scale.toFixed(3));
      // Lift the magnified tile above its (still resting-size) neighbors so growth doesn't render
      // partly behind them -- only while actually magnified, so resting tiles keep normal stacking.
      tile.style.zIndex = scale > 1.02 ? "5" : "";
    }
  }

  grid.addEventListener("mousemove", (e) => apply(e.clientY));
  grid.addEventListener("mouseleave", () => apply(null));
}

// ---- Scorebug switcher (matchup screen pill + arrows) -----------------------------------
// Which scorebug layout GameWatcher watches for (PC CBS skins vs. Console/Remote Play) --
// previously only reachable via the gear-icon Settings dialog; owner asked for a pill+arrows
// switcher on the matchup screen itself, same visual language as the coverflow's own arrows.
let _scorebugPresetNames = [];
let _scorebugPresetActive = "";
let _scorebugSwitcherBound = false;

async function loadScorebugSwitcher() {
  if (!bridge) return;
  try {
    const data = JSON.parse(await bridge.GetScorebugPresets());
    _scorebugPresetNames = data.names || [];
    _scorebugPresetActive = data.active || _scorebugPresetNames[0] || "";
  } catch (err) {
    console.error("GetScorebugPresets failed", err);
    return;
  }
  renderScorebugSwitcher();
  initScorebugSwitcher();
}

function renderScorebugSwitcher() {
  const el = document.getElementById("scorebug-switcher-name");
  if (el) el.textContent = _scorebugPresetActive || "Scorebug";
}

function initScorebugSwitcher() {
  if (_scorebugSwitcherBound) return;
  _scorebugSwitcherBound = true;
  document.getElementById("btn-scorebug-prev")?.addEventListener("click", () => cycleScorebugPreset(-1));
  document.getElementById("btn-scorebug-next")?.addEventListener("click", () => cycleScorebugPreset(1));
}

async function cycleScorebugPreset(dir) {
  if (!_scorebugPresetNames.length || !bridge) return;
  let idx = _scorebugPresetNames.indexOf(_scorebugPresetActive);
  if (idx === -1) idx = 0;
  idx = ((idx + dir) % _scorebugPresetNames.length + _scorebugPresetNames.length) % _scorebugPresetNames.length;
  _scorebugPresetActive = _scorebugPresetNames[idx];
  renderScorebugSwitcher();
  try { await bridge.SetScorebugPreset(_scorebugPresetActive); }
  catch (err) { console.error("SetScorebugPreset failed", err); }
}

// ---- Custom team logo crop tool ---------------------------------------------------------
// Draws the source image onto a fixed-size square canvas at a user-controlled pan/zoom, so
// whatever's visible is a guaranteed 1:1 square regardless of the source image's own shape --
// that's what keeps every team's logo uniform across the app no matter what file someone picks.
const LOGO_CROP_SIZE = 400; // canvas pixel size AND the saved output size
let _logoCropTeam = null;
let _logoCropImg = null;
let _logoCropScale = 1; // 1.0 = image's shorter edge exactly fills the square
let _logoCropOffsetX = 0; // pan offset in canvas pixels
let _logoCropOffsetY = 0;
let _logoCropDragging = false;
let _logoCropDragStart = null;

function openLogoCropTool(teamName) {
  _logoCropTeam = teamName;
  _logoCropImg = null;
  _logoCropScale = 1;
  _logoCropOffsetX = 0;
  _logoCropOffsetY = 0;
  document.getElementById("logo-crop-header").textContent = `Set Logo — ${teamName}`;
  document.getElementById("logo-crop-empty").hidden = false;
  document.getElementById("logo-crop-zoom").disabled = true;
  document.getElementById("logo-crop-zoom").value = 100;
  document.getElementById("btn-logo-crop-confirm").disabled = true;
  clearLogoCropCanvas();
  document.getElementById("logo-crop-overlay").hidden = false;
}

function closeLogoCropTool() {
  document.getElementById("logo-crop-overlay").hidden = true;
  _logoCropTeam = null;
  _logoCropImg = null;
  // Batch cleanup lives here (not just in advanceBatchLogoImport) so Escape/Cancel mid-batch
  // (item 20) can't leave a half-finished queue bleeding into the next single-logo use of this
  // same tool.
  _batchLogoQueue = [];
  _batchLogoIndex = 0;
  const batchRow = document.getElementById("batch-logo-team-row");
  if (batchRow) batchRow.hidden = true;
}

function clearLogoCropCanvas() {
  const canvas = document.getElementById("logo-crop-canvas");
  const ctx = canvas.getContext("2d");
  ctx.clearRect(0, 0, canvas.width, canvas.height);
}

function onLogoCropFileChosen(e) {
  const file = e.target.files?.[0];
  e.target.value = ""; // allow re-choosing the same file later
  if (!file) return;
  const url = URL.createObjectURL(file);
  const img = new Image();
  img.onload = () => {
    URL.revokeObjectURL(url);
    _logoCropImg = img;
    _logoCropScale = 1;
    _logoCropOffsetX = 0;
    _logoCropOffsetY = 0;
    document.getElementById("logo-crop-empty").hidden = true;
    document.getElementById("logo-crop-zoom").disabled = false;
    document.getElementById("logo-crop-zoom").value = 100;
    document.getElementById("btn-logo-crop-confirm").disabled = false;
    drawLogoCrop();
  };
  img.onerror = () => showToast("Couldn't open that image -- try a different file.");
  img.src = url;
}

function drawLogoCrop() {
  const canvas = document.getElementById("logo-crop-canvas");
  const ctx = canvas.getContext("2d");
  ctx.clearRect(0, 0, LOGO_CROP_SIZE, LOGO_CROP_SIZE);
  if (!_logoCropImg) return;
  const img = _logoCropImg;
  // Base fit: cover the square with the image's shorter edge, before user zoom/pan.
  const baseScale = LOGO_CROP_SIZE / Math.min(img.width, img.height);
  const scale = baseScale * _logoCropScale;
  const drawW = img.width * scale;
  const drawH = img.height * scale;
  const x = (LOGO_CROP_SIZE - drawW) / 2 + _logoCropOffsetX;
  const y = (LOGO_CROP_SIZE - drawH) / 2 + _logoCropOffsetY;
  ctx.drawImage(img, x, y, drawW, drawH);
}

function clampLogoCropOffsets() {
  if (!_logoCropImg) return;
  const img = _logoCropImg;
  const baseScale = LOGO_CROP_SIZE / Math.min(img.width, img.height);
  const scale = baseScale * _logoCropScale;
  const drawW = img.width * scale;
  const drawH = img.height * scale;
  // Never let the image pan far enough to leave a gap at any edge of the square.
  const maxOffX = Math.max(0, (drawW - LOGO_CROP_SIZE) / 2);
  const maxOffY = Math.max(0, (drawH - LOGO_CROP_SIZE) / 2);
  _logoCropOffsetX = Math.max(-maxOffX, Math.min(maxOffX, _logoCropOffsetX));
  _logoCropOffsetY = Math.max(-maxOffY, Math.min(maxOffY, _logoCropOffsetY));
}

// ---- Custom team BACKGROUND crop tool ---------------------------------------------------
// Mirrors the logo crop tool above exactly (same drag/zoom/canvas math), except the output is a
// fixed 16:9 rectangle instead of a square -- matches the full-screen cover backdrop's own aspect
// ratio (see #backdrop-vs-away/#backdrop-vs-home in style.css) rather than the logo's badge shape.
const BG_CROP_W = 960, BG_CROP_H = 540; // 16:9, canvas pixel size AND the saved output size
let _bgCropTeam = null;
let _bgCropImg = null;
let _bgCropScale = 1;
let _bgCropOffsetX = 0;
let _bgCropOffsetY = 0;
let _bgCropDragging = false;
let _bgCropDragStart = null;

function openBackgroundCropTool(teamName) {
  _bgCropTeam = teamName;
  _bgCropImg = null;
  _bgCropScale = 1;
  _bgCropOffsetX = 0;
  _bgCropOffsetY = 0;
  document.getElementById("bg-crop-header").textContent = `Set Background — ${teamName}`;
  document.getElementById("bg-crop-empty").hidden = false;
  document.getElementById("bg-crop-zoom").disabled = true;
  document.getElementById("bg-crop-zoom").value = 100;
  document.getElementById("btn-bg-crop-confirm").disabled = true;
  clearBgCropCanvas();
  document.getElementById("bg-crop-overlay").hidden = false;
}

function closeBackgroundCropTool() {
  document.getElementById("bg-crop-overlay").hidden = true;
  _bgCropTeam = null;
  _bgCropImg = null;
}

function clearBgCropCanvas() {
  const canvas = document.getElementById("bg-crop-canvas");
  const ctx = canvas.getContext("2d");
  ctx.clearRect(0, 0, canvas.width, canvas.height);
}

function onBgCropFileChosen(e) {
  const file = e.target.files?.[0];
  e.target.value = "";
  if (!file) return;
  const url = URL.createObjectURL(file);
  const img = new Image();
  img.onload = () => {
    URL.revokeObjectURL(url);
    _bgCropImg = img;
    _bgCropScale = 1;
    _bgCropOffsetX = 0;
    _bgCropOffsetY = 0;
    document.getElementById("bg-crop-empty").hidden = true;
    document.getElementById("bg-crop-zoom").disabled = false;
    document.getElementById("bg-crop-zoom").value = 100;
    document.getElementById("btn-bg-crop-confirm").disabled = false;
    drawBgCrop();
  };
  img.onerror = () => showToast("Couldn't open that image -- try a different file.");
  img.src = url;
}

// "Cover" fit against a 16:9 box instead of a square -- base scale is the larger of
// width-to-fit and height-to-fit so the image always fully covers the rectangle, same idea as
// the logo tool's Math.min(img.width, img.height) shortcut, just generalized for a non-1:1 box.
function bgCropBaseScale(img) {
  return Math.max(BG_CROP_W / img.width, BG_CROP_H / img.height);
}

function drawBgCrop() {
  const canvas = document.getElementById("bg-crop-canvas");
  const ctx = canvas.getContext("2d");
  ctx.clearRect(0, 0, BG_CROP_W, BG_CROP_H);
  if (!_bgCropImg) return;
  const img = _bgCropImg;
  const scale = bgCropBaseScale(img) * _bgCropScale;
  const drawW = img.width * scale;
  const drawH = img.height * scale;
  const x = (BG_CROP_W - drawW) / 2 + _bgCropOffsetX;
  const y = (BG_CROP_H - drawH) / 2 + _bgCropOffsetY;
  ctx.drawImage(img, x, y, drawW, drawH);
}

function clampBgCropOffsets() {
  if (!_bgCropImg) return;
  const img = _bgCropImg;
  const scale = bgCropBaseScale(img) * _bgCropScale;
  const drawW = img.width * scale;
  const drawH = img.height * scale;
  const maxOffX = Math.max(0, (drawW - BG_CROP_W) / 2);
  const maxOffY = Math.max(0, (drawH - BG_CROP_H) / 2);
  _bgCropOffsetX = Math.max(-maxOffX, Math.min(maxOffX, _bgCropOffsetX));
  _bgCropOffsetY = Math.max(-maxOffY, Math.min(maxOffY, _bgCropOffsetY));
}

function wireBgCropTool() {
  document.getElementById("btn-bg-crop-choose").addEventListener("click", () =>
    document.getElementById("bg-crop-file-input").click());
  document.getElementById("bg-crop-file-input").addEventListener("change", onBgCropFileChosen);
  document.getElementById("btn-bg-crop-cancel").addEventListener("click", closeBackgroundCropTool);
  document.getElementById("btn-bg-crop-close").addEventListener("click", closeBackgroundCropTool);

  const viewport = document.getElementById("bg-crop-viewport");
  const canvas = document.getElementById("bg-crop-canvas");

  const startDrag = (clientX, clientY) => {
    if (!_bgCropImg) return;
    _bgCropDragging = true;
    _bgCropDragStart = { x: clientX, y: clientY, offX: _bgCropOffsetX, offY: _bgCropOffsetY };
  };
  const moveDrag = (clientX, clientY) => {
    if (!_bgCropDragging) return;
    const rect = canvas.getBoundingClientRect();
    // Canvas keeps a fixed 16:9 internal resolution but its CSS size can vary (min(480px, 84vw))
    // -- scale drag distance by width so panning still tracks the cursor 1:1 on screen.
    const scaleFactor = BG_CROP_W / rect.width;
    _bgCropOffsetX = _bgCropDragStart.offX + (clientX - _bgCropDragStart.x) * scaleFactor;
    _bgCropOffsetY = _bgCropDragStart.offY + (clientY - _bgCropDragStart.y) * scaleFactor;
    clampBgCropOffsets();
    drawBgCrop();
  };
  const endDrag = () => { _bgCropDragging = false; _bgCropDragStart = null; };

  viewport.addEventListener("mousedown", (e) => startDrag(e.clientX, e.clientY));
  window.addEventListener("mousemove", (e) => moveDrag(e.clientX, e.clientY));
  window.addEventListener("mouseup", endDrag);
  viewport.addEventListener("touchstart", (e) => {
    if (e.touches[0]) startDrag(e.touches[0].clientX, e.touches[0].clientY);
  }, { passive: true });
  viewport.addEventListener("touchmove", (e) => {
    if (e.touches[0]) moveDrag(e.touches[0].clientX, e.touches[0].clientY);
  }, { passive: true });
  viewport.addEventListener("touchend", endDrag);

  document.getElementById("bg-crop-zoom").addEventListener("input", (e) => {
    _bgCropScale = Number(e.target.value) / 100;
    clampBgCropOffsets();
    drawBgCrop();
  });

  document.getElementById("btn-bg-crop-confirm").addEventListener("click", async () => {
    if (!_bgCropImg || !_bgCropTeam) return;
    const confirmBtn = document.getElementById("btn-bg-crop-confirm");
    confirmBtn.disabled = true;
    confirmBtn.textContent = "Saving...";
    try {
      const dataUrl = canvas.toDataURL("image/png");
      const base64 = dataUrl.split(",")[1];
      const ok = bridge ? await bridge.SaveCustomTeamBackground(_bgCropTeam, base64) : false;
      if (ok) {
        showToast(`Saved a new background for ${_bgCropTeam}.`);
        if (state.activeTeam === _bgCropTeam) applyBackground(_bgCropTeam);
        closeBackgroundCropTool();
      } else {
        showToast("Couldn't save that background -- try again.");
      }
    } catch (err) {
      console.error("SaveCustomTeamBackground failed", err);
      showToast("Couldn't save that background -- try again.");
    } finally {
      confirmBtn.disabled = false;
      confirmBtn.textContent = "Save Background";
    }
  });
}

function wireLogoCropTool() {
  document.getElementById("btn-logo-crop-choose").addEventListener("click", () =>
    document.getElementById("logo-crop-file-input").click());
  document.getElementById("logo-crop-file-input").addEventListener("change", onLogoCropFileChosen);
  document.getElementById("btn-logo-crop-cancel").addEventListener("click", closeLogoCropTool);
  document.getElementById("btn-logo-crop-close").addEventListener("click", closeLogoCropTool);

  const viewport = document.getElementById("logo-crop-viewport");
  const canvas = document.getElementById("logo-crop-canvas");

  const startDrag = (clientX, clientY) => {
    if (!_logoCropImg) return;
    _logoCropDragging = true;
    _logoCropDragStart = { x: clientX, y: clientY, offX: _logoCropOffsetX, offY: _logoCropOffsetY };
  };
  const moveDrag = (clientX, clientY) => {
    if (!_logoCropDragging) return;
    // canvas is CSS-scaled to the 260px viewport but its internal resolution is LOGO_CROP_SIZE --
    // convert screen-pixel drag distance into canvas-pixel offset so panning tracks the cursor.
    const rect = canvas.getBoundingClientRect();
    const scaleFactor = LOGO_CROP_SIZE / rect.width;
    _logoCropOffsetX = _logoCropDragStart.offX + (clientX - _logoCropDragStart.x) * scaleFactor;
    _logoCropOffsetY = _logoCropDragStart.offY + (clientY - _logoCropDragStart.y) * scaleFactor;
    clampLogoCropOffsets();
    drawLogoCrop();
  };
  const endDrag = () => { _logoCropDragging = false; _logoCropDragStart = null; };

  viewport.addEventListener("mousedown", (e) => startDrag(e.clientX, e.clientY));
  window.addEventListener("mousemove", (e) => moveDrag(e.clientX, e.clientY));
  window.addEventListener("mouseup", endDrag);
  viewport.addEventListener("touchstart", (e) => {
    if (e.touches[0]) startDrag(e.touches[0].clientX, e.touches[0].clientY);
  }, { passive: true });
  viewport.addEventListener("touchmove", (e) => {
    if (e.touches[0]) moveDrag(e.touches[0].clientX, e.touches[0].clientY);
  }, { passive: true });
  viewport.addEventListener("touchend", endDrag);

  document.getElementById("logo-crop-zoom").addEventListener("input", (e) => {
    _logoCropScale = Number(e.target.value) / 100;
    clampLogoCropOffsets();
    drawLogoCrop();
  });

  document.getElementById("btn-logo-crop-confirm").addEventListener("click", async () => {
    // Batch mode (item 20) overrides which team the save targets -- the operator can correct
    // a bad filename-match via the team select shown only while a batch queue is active (see
    // openBatchLogoImportTool). Outside batch mode this select doesn't exist/is hidden and
    // _logoCropTeam (set by the normal single-team "Set Logo" entry point) is used as before.
    const batchSelect = document.getElementById("batch-logo-team-select");
    const targetTeam = (_batchLogoQueue.length > 0 && batchSelect) ? batchSelect.value : _logoCropTeam;
    if (!_logoCropImg || !targetTeam) return;
    const confirmBtn = document.getElementById("btn-logo-crop-confirm");
    confirmBtn.disabled = true;
    confirmBtn.textContent = "Saving...";
    try {
      const dataUrl = canvas.toDataURL("image/png");
      const base64 = dataUrl.split(",")[1];
      const result = bridge ? JSON.parse(await bridge.SaveCustomTeamLogo(targetTeam, base64)) : { ok: false, pushFailed: false };
      if (result.ok) {
        showToast(`Saved a new logo for ${targetTeam}.`);
        // Distinct from the local-save failure above -- the save to disk already succeeded, this
        // is just the cloud mirror not going through (network/server issue). Doesn't block or
        // undo the local save; the next successful sync catches up on its own.
        if (result.pushFailed) showToast(`Logo for ${targetTeam} saved locally but couldn't sync -- will retry.`);
        await refreshTeamsAfterLogoChange();
        if (_batchLogoQueue.length > 0) {
          advanceBatchLogoImport();
        } else {
          closeLogoCropTool();
        }
      } else {
        showToast("Couldn't save that logo -- try again.");
      }
    } catch (err) {
      console.error("SaveCustomTeamLogo failed", err);
      showToast("Couldn't save that logo -- try again.");
    } finally {
      confirmBtn.disabled = false;
      confirmBtn.textContent = "Save Logo";
    }
  });

  document.getElementById("btn-logo-crop-skip")?.addEventListener("click", () => {
    if (_batchLogoQueue.length > 0) advanceBatchLogoImport();
  });
}

// ---- Creator-only batch logo/icon import (item 20) -------------------------------------
// Thin sequential wrapper around the SAME crop tool used for single-team logo uploads above --
// no new crop math, just a queue that feeds openLogoCropTool one file at a time and auto-advances
// on confirm. Entry point is the Ctrl+Alt+Shift+L chord wired in the keydown handler above; there
// is no button/menu item anywhere else, on purpose (see that handler's comment).
let _batchLogoQueue = []; // [{ file, teamName }]
let _batchLogoIndex = 0;

function normalizeForMatch(s) {
  return String(s ?? "").toLowerCase().replace(/[^a-z0-9]+/g, " ").trim();
}

/// Best-effort filename -> team-name match (e.g. "ohio_state.png" -> "Ohio State"). Returns null
/// if nothing matches well enough -- the operator picks/corrects via the team select instead of
/// this guessing wrong silently.
function matchTeamFromFilename(filename) {
  const base = normalizeForMatch(filename.replace(/\.[^.]+$/, ""));
  if (!base) return null;
  const exact = state.teams?.find((t) => normalizeForMatch(t.name) === base);
  if (exact) return exact.name;
  // Fall back to substring containment either direction (e.g. "bama.png" won't match "Alabama"
  // this way, but "ohio-state-logo.png" -> base "ohio state logo" contains "ohio state").
  const contains = state.teams?.find((t) => {
    const name = normalizeForMatch(t.name);
    return base.includes(name) || name.includes(base);
  });
  return contains?.name ?? null;
}

async function openBatchLogoImportTool() {
  if (!state.teams || state.teams.length === 0) {
    showToast("Teams haven't loaded yet -- try again in a moment.");
    return;
  }
  const input = document.getElementById("batch-logo-folder-input");
  if (!input) return;
  input.value = "";
  input.onchange = () => {
    const files = [...(input.files ?? [])].filter((f) => f.type.startsWith("image/"));
    if (files.length === 0) {
      showToast("No image files found in that folder.");
      return;
    }
    _batchLogoQueue = files.map((file) => ({ file, teamName: matchTeamFromFilename(file.name) }));
    _batchLogoIndex = 0;
    showToast(`Batch logo import: ${files.length} image(s) queued.`);
    loadCurrentBatchLogoItem();
  };
  input.click();
}

function populateBatchTeamSelect(selected) {
  const select = document.getElementById("batch-logo-team-select");
  if (!select) return;
  select.innerHTML = "";
  for (const team of state.teams ?? []) {
    const opt = document.createElement("option");
    opt.value = team.name;
    opt.textContent = team.name;
    if (team.name === selected) opt.selected = true;
    select.appendChild(opt);
  }
}

function loadCurrentBatchLogoItem() {
  if (_batchLogoIndex >= _batchLogoQueue.length) {
    showToast("Batch logo import complete.");
    _batchLogoQueue = [];
    _batchLogoIndex = 0;
    closeLogoCropTool();
    return;
  }
  const { file, teamName } = _batchLogoQueue[_batchLogoIndex];
  const guessedTeam = teamName ?? state.teams[0].name;

  openLogoCropTool(guessedTeam);
  document.getElementById("logo-crop-header").textContent =
    `Batch Import (${_batchLogoIndex + 1}/${_batchLogoQueue.length}) — ${file.name}`;
  document.getElementById("batch-logo-team-row").hidden = false;
  populateBatchTeamSelect(guessedTeam);
  if (!teamName) showToast(`Couldn't guess a team for "${file.name}" -- pick one manually.`);

  const url = URL.createObjectURL(file);
  const img = new Image();
  img.onload = () => {
    URL.revokeObjectURL(url);
    _logoCropImg = img;
    _logoCropScale = 1;
    _logoCropOffsetX = 0;
    _logoCropOffsetY = 0;
    document.getElementById("logo-crop-empty").hidden = true;
    document.getElementById("logo-crop-zoom").disabled = false;
    document.getElementById("logo-crop-zoom").value = 100;
    document.getElementById("btn-logo-crop-confirm").disabled = false;
    drawLogoCrop();
  };
  img.onerror = () => showToast(`Couldn't open "${file.name}" -- skipping.`);
  img.src = url;
}

function advanceBatchLogoImport() {
  _batchLogoIndex++;
  loadCurrentBatchLogoItem();
}

// Re-fetches state.teams (picking up the new logoUrl) and repaints whatever team UI is
// currently visible, so the new logo shows immediately instead of needing a restart.
async function refreshTeamsAfterLogoChange() {
  if (!bridge) return;
  try {
    state.teams = JSON.parse(await bridge.GetTeams());
  } catch (err) {
    console.error("GetTeams refresh failed", err);
    return;
  }
  if (!document.getElementById("team-picker-overlay").hidden)
    renderTeamPickerCoverflow(document.getElementById("team-picker-search").value);
  const active = state.teams.find((t) => t.name === state.activeTeam);
  if (active) updateHeaderTeamBadge(active);
}

function closeMatchupDialog() {
  document.getElementById("matchup-overlay").hidden = true;
}

// Owner request 2026-08-11: instructions paragraph + the Big-Game/last-matchup/scorebug-side/
// away-band toggles both moved behind large header pills (both were always-visible, eating screen
// space over the new full-bleed photo backdrop) -- click toggles each as its own popover anchored
// under the header, next to the scorebug switcher. Wired once each (idempotent) since
// openMatchupDialog doesn't re-run per-open setup like this. Shared helper since both pills follow
// the exact same open/close-on-outside-click pattern.
function wireMatchupPopoverPill(btnId, panelId) {
  const btn = document.getElementById(btnId);
  const panel = document.getElementById(panelId);
  if (!btn || !panel || btn._popoverWired) return;
  btn._popoverWired = true;
  btn.addEventListener("click", (e) => {
    e.stopPropagation();
    panel.hidden = !panel.hidden;
  });
  document.addEventListener("click", (e) => {
    if (!panel.hidden && !panel.contains(e.target) && e.target !== btn) panel.hidden = true;
  });
}

function wireMatchupGameSettingsPill() {
  wireMatchupPopoverPill("btn-matchup-game-settings", "matchup-controls-island");
}

/// Cover-flow team select (CFB 27 team-select reference) -- browsing IS picking, same as the
/// reference screen: the center tile is always the currently-picked team for that side, and
/// cycling (arrows or clicking a side tile) immediately updates state.matchupHome/Away and the
/// name label. GAMETIME is the real commit point, not this.
function matchupCoverflowTeams(filter) {
  const q = (filter || "").trim().toLowerCase();
  return state.teams.filter((t) => t.name !== "General" && (!q || t.name.toLowerCase().includes(q)));
}

function renderMatchupCoverflow(side, filter) {
  const track = document.getElementById(`matchup-${side}-track`);
  const nameEl = document.getElementById(`matchup-${side}-name`);
  if (!track || !nameEl) return;
  const teams = matchupCoverflowTeams(filter);
  track.innerHTML = "";
  if (!teams.length) {
    nameEl.textContent = "No teams found";
    return;
  }

  const picked = side === "home" ? state.matchupHome : state.matchupAway;
  let centerIdx = teams.findIndex((t) => t.name === picked);
  if (centerIdx === -1) centerIdx = 0;

  // Owner request 2026-08-11: drop the tilted neighbor tiles either side of the big center logo on
  // THIS screen specifically -- the side-grid's own fast/looping scroll (wireMatchupSideGridWheel)
  // already covers browsing here, so the neighbors were redundant clutter. Center tile only; the
  // other coverflow screens (team picker, onboarding, favorite-team) keep their neighbors, this
  // array is local to renderMatchupCoverflow.
  const positions = [[0, "cf-center"]];
  for (const [offset, cls] of positions) {
    const idx = ((centerIdx + offset) % teams.length + teams.length) % teams.length;
    const t = teams[idx];
    const tile = document.createElement("div");
    tile.className = "team-swatch " + cls;
    tile.title = t.name;
    fillTeamSwatch(tile, t, true);
    tile.addEventListener("click", () => {
      if (side === "home") state.matchupHome = t.name; else state.matchupAway = t.name;
      renderMatchupCoverflow(side, filter);
      updateMatchupSubtext();
    });
    track.appendChild(tile);
  }

  const centerTeam = teams[centerIdx];
  nameEl.textContent = centerTeam.name;
  if (side === "home") state.matchupHome = centerTeam.name; else state.matchupAway = centerTeam.name;

  // Tint this side's half of the split screen toward the centered team's own color -- same
  // --half-color pattern applyVsBackdrop already uses for the in-game VS backdrop, just applied
  // to the picker column instead of the backdrop half.
  const column = track.closest(".matchup-column");
  const sideColor = centerTeam.secondary || centerTeam.primary;
  if (column && sideColor) column.style.setProperty("--side-color", sideColor);
  // Coverflow arrows (owner request): primary-color filled circle, secondary-color arrow glyph --
  // separate from --side-color above (which favors secondary and drives the tint/glow) since the
  // arrows specifically need both colors at once, primary as the fill and secondary as the ink.
  if (column) {
    column.style.setProperty("--side-primary", centerTeam.primary || sideColor);
    column.style.setProperty("--side-secondary", centerTeam.secondary || sideColor);
  }
  const badge = document.getElementById("matchup-vs-badge");
  if (badge && sideColor) badge.style.setProperty(`--${side}-badge-color`, sideColor);

  // Same team-background-behind-the-logo treatment applyVsBackdrop already does for the locked-in
  // VS screen, applied here to the team-picker column too. Guarded by a request token so a fast
  // arrow-click/search-keystroke burst can't let an earlier, now-stale fetch overwrite a newer one.
  if (column) {
    const requestToken = (column._bgRequestToken = (column._bgRequestToken || 0) + 1);
    if (bridge) {
      bridge.GetTeamBackgroundUrl(centerTeam.name).then(async (bgUrl) => {
        if (column._bgRequestToken !== requestToken) return; // superseded by a newer pick
        if (!bgUrl) {
          // Owner report 2026-08-11: the matchup split backdrop only ever showed a photo for
          // teams with an explicitly-pinned "active" background -- most teams never set one, so
          // their whole half just showed the plain color-tint gradient with no band room photo
          // at all (confirmed via screenshot: Georgia had a photo, Florida's half didn't). Same
          // richer source openBandroomViewer already falls back to (that team's marketplace-
          // uploaded photos) so any team with at least one uploaded image gets a real backdrop
          // here too, not just ones with a manually-pinned background.
          try {
            const uploads = await fetchUploadList("image", centerTeam.name, null);
            bgUrl = uploads?.[0]?.url || null;
          } catch { /* fetchUploadList already swallows its own errors -- extra safety only */ }
          if (column._bgRequestToken !== requestToken) return; // re-check after the extra await
        }
        column.style.setProperty("--team-bg-image", bgUrl ? `url("${bgUrl}")` : "none");
      }).catch(() => {});
    }
  }

  updateMatchupSubtext();
}

/// Owner request: mouse-wheel scrolling through the coverflow pickers (Set Matchup, Choose a
/// Team), as smooth/responsive as the left-side team grid's native scroll. The coverflow itself
/// isn't a real scroll container -- it's a fixed 5-tile window re-centered on an index (see
/// renderMatchupCoverflow/renderTeamPickerCoverflow) -- so "scrolling" here means stepping that
/// index one team per wheel notch via the same shift function the arrow buttons already call,
/// throttled to the tiles' own 0.22s CSS transition (style.css .coverflow-track .team-swatch) so
/// a fast trackpad fling steps cleanly through teams instead of skipping dozens at once.
function wireCoverflowWheel(track, shiftFn) {
  if (!track) return;
  const container = track.closest(".matchup-column, .coverflow-stage") || track;
  let lastShift = 0;
  container.addEventListener("wheel", (e) => {
    e.preventDefault();
    const now = performance.now();
    if (now - lastShift < 220) return; // one step per tile-transition, not one per wheel tick
    lastShift = now;
    const delta = Math.abs(e.deltaX) > Math.abs(e.deltaY) ? e.deltaX : e.deltaY;
    if (delta === 0) return;
    shiftFn(delta > 0 ? 1 : -1);
  }, { passive: false });
}

function shiftCoverflow(side, dir) {
  const filter = document.getElementById(`matchup-${side}-search`)?.value || "";
  const teams = matchupCoverflowTeams(filter);
  if (!teams.length) return;
  const picked = side === "home" ? state.matchupHome : state.matchupAway;
  let idx = teams.findIndex((t) => t.name === picked);
  if (idx === -1) idx = 0;
  idx = ((idx + dir) % teams.length + teams.length) % teams.length;
  if (side === "home") state.matchupHome = teams[idx].name; else state.matchupAway = teams[idx].name;
  renderMatchupCoverflow(side, filter);
}

// Owner report 2026-08-12: this team-specific sentence used to live in #matchup-subtext, which
// sat right on top of the coverflow logo and overlapped it. Moved into the ticker
// (#matchup-ticker-scroll-text) alongside the rest of the how-to copy instead; captured here once
// so a picked-then-cleared matchup can restore the ticker's static default text.
const MATCHUP_TICKER_DEFAULT_TEXT = document.getElementById("matchup-ticker-scroll-text")?.textContent || "";

function updateMatchupSubtext() {
  const el = document.getElementById("matchup-subtext");
  const tickerEl = document.getElementById("matchup-ticker-scroll-text");
  const ready = state.matchupHome && state.matchupAway && state.matchupHome !== state.matchupAway;
  if (!state.matchupHome || !state.matchupAway) {
    el.textContent = "Pick both a home and an away team.";
    if (tickerEl) tickerEl.textContent = MATCHUP_TICKER_DEFAULT_TEXT;
  } else if (state.matchupHome === state.matchupAway) {
    el.textContent = "Home and away can't be the same team.";
    if (tickerEl) tickerEl.textContent = MATCHUP_TICKER_DEFAULT_TEXT;
  } else {
    el.textContent = "";
    if (tickerEl) tickerEl.textContent = `${state.matchupAway} (away) at ${state.matchupHome} (home) -- each team's own saved profile loads automatically. Hit GAMETIME while you're still on CFB 27's team-select screen.`;
  }
  document.getElementById("btn-matchup-confirm").disabled = !ready;
}

/// GAMETIME -- locks in who's home/away for OCR event routing (see WebMainForm._matchupLocked)
/// and swaps the backdrop to the two-team VS screen. The Home/Away toggle bar still works
/// after this for editing songs; only the routing itself is locked until Stop Watching.
async function confirmMatchup() {
  if (!state.matchupHome || !state.matchupAway || state.matchupHome === state.matchupAway) return;

  // Task queue item 6 (Session 11): WebMainForm.ConfirmGametimeFromWeb already silently
  // auto-fills an empty team's profile from the default pack the moment the matchup locks (see
  // its own comment) -- untouched here, still happens exactly as before. This check runs BEFORE
  // that call so the user gets a real, visible choice instead of it happening invisibly: either
  // proceed now (accepting the starter-profile auto-fill), or back out and assign songs
  // themselves first via the Clipper, then re-open Set Matchup once ready.
  try {
    const needsDefault = bridge ? JSON.parse(await bridge.GetTeamsNeedingDefaultProfile(state.matchupHome, state.matchupAway)) : [];
    if (needsDefault.length > 0) {
      const proceed = await showDefaultProfilePrompt(needsDefault);
      if (!proceed) return; // user chose to assign songs themselves first -- matchup stays unlocked
      // Explicitly apply now (rather than relying on ConfirmGametime's own silent safety-net
      // fallback, which would do the same thing invisibly) so we can report real numbers and so
      // the "Use Starter Profile" button actually does what it says instead of being a no-op that
      // just trusts a side effect elsewhere.
      let totalAssigned = 0;
      for (const name of needsDefault) {
        try { totalAssigned += (await bridge?.ApplyDefaultProfileForTeam(name)) ?? 0; }
        catch (err) { console.error(`ApplyDefaultProfileForTeam(${name}) failed`, err); }
      }
      if (totalAssigned > 0) showToast(`Filled ${totalAssigned} starter song${totalAssigned === 1 ? "" : "s"} from the Default Song Pack.`);
    }
  } catch (err) {
    console.error("GetTeamsNeedingDefaultProfile failed", err);
    // Best-effort -- if the check itself fails, fall through to the normal confirm flow rather
    // than blocking GAMETIME entirely over a broken informational prompt.
  }

  const userTeamOnLeft = document.getElementById("matchup-screen-side-left")?.checked ?? false;
  await bridge?.SetUserTeamScreenSide(userTeamOnLeft);
  await bridge?.ConfirmGametime(state.matchupHome, state.matchupAway);
  state.matchupLocked = true;
  updateMatchupLabel();
  closeMatchupDialog();
  // NOTE: previously called applyVsBackdrop() here to show the two-team VS split as the backdrop.
  // Owner request (this session): GAMETIME now opens the Band Room Viewer instead (single team's
  // photo gallery, switchable via the Away/Home bar) -- see openBandroomViewer() call below.
  // applyVsBackdrop/revertVsBackdrop are left in place (revertVsBackdrop still runs in
  // setWatching's "off" branch) in case #backdrop-vs is ever wanted again, just no longer called
  // from this flow.
  // GAMETIME now locks the matchup AND starts watching in one press (WebMainForm.ConfirmGametimeFromWeb)
  // -- reflect that immediately instead of requiring a separate Start Watching click.
  setWatching("waiting");
  // "Game Day" fullscreen layout: replaces the old "Sound Booth pops up as a centered modal
  // dimming everything behind it" behavior with event cards/assignments docked on one side and
  // the Sound Booth permanently docked on the other, VS-style header up top -- see the
  // .gameday-mode CSS block in style.css and enterGameDayMode below. Turned off in setWatching's
  // "off" branch (Stop Watching / Unlock Matchup), the same real "this game is over" boundary
  // that already reverts the VS backdrop.
  enterGameDayMode();
  // Punch-list item 2: locking in used to just close the dialog and drop the user back on the
  // Band Room's Assignments panel (already the base screen underneath), leaving Sound Booth a
  // separate extra click away. Open it right on top now (docked, not a centered modal, while
  // Game Day mode is active) so both are visible together instead of requiring that extra
  // navigation step.
  openSoundBooth();
  // Owner request: GAMETIME opens the Band Room Viewer as Game Day mode's backdrop (see
  // .gameday-mode overrides on #bandroom-viewer-overlay in style.css, which turn it from a
  // blocking fullscreen modal into a non-blocking backdrop behind the docked panels). Which
  // team's band room loads first is the matchup-my-team-away checkbox above -- switching sides
  // afterward still works the normal way via the Away/Home bar (btn-side-away/btn-side-home
  // already call openBandroomViewer() on switch, see wireMatchupSideBar).
  const myTeamIsAway = document.getElementById("matchup-my-team-away")?.checked ?? false;
  await selectTeam(myTeamIsAway ? state.matchupAway : state.matchupHome);
  await openBandroomViewer();
  showToast(`GAMETIME! ${state.matchupAway} @ ${state.matchupHome} -- watching started`);
}

/// Glass-styled confirm prompt (task queue item 6) -- resolves true if the user wants to proceed
/// with GAMETIME (accepting the default-pack starter-profile auto-fill for whichever team(s)
/// don't have one yet), false if they'd rather back out and assign songs themselves first.
/// Reuses #default-profile-prompt-overlay's markup (see index.html), matching the rest of the
/// app's glass-island/pill styling instead of a plain confirm() dialog.
function showDefaultProfilePrompt(teamNames) {
  return new Promise((resolve) => {
    const overlay = document.getElementById("default-profile-prompt-overlay");
    document.getElementById("default-profile-prompt-teams").textContent = teamNames.join(" and ");
    overlay.hidden = false;

    const cleanup = () => {
      overlay.hidden = true;
      btnApply.removeEventListener("click", onApply);
      btnSkip.removeEventListener("click", onSkip);
    };
    const onApply = () => { cleanup(); resolve(true); };
    const onSkip = () => { cleanup(); resolve(false); };
    const btnApply = document.getElementById("btn-default-profile-apply");
    const btnSkip = document.getElementById("btn-default-profile-skip");
    btnApply.addEventListener("click", onApply);
    btnSkip.addEventListener("click", onSkip);
  });
}

/// Populates the two-team VS backdrop (photo + logo + name + team-color underglow per side)
/// and swaps it in over the normal single #backdrop. Reuses the same team data (colors/logos)
/// and background lookup already used for the sidebar/header everywhere else.
async function applyVsBackdrop() {
  const away = state.teams.find((t) => t.name === state.matchupAway);
  const home = state.teams.find((t) => t.name === state.matchupHome);
  if (!away || !home) return;

  const fill = async (side, team) => {
    const half = document.getElementById(`backdrop-vs-${side}`);
    const logo = document.getElementById(`backdrop-vs-${side}-logo`);
    const name = document.getElementById(`backdrop-vs-${side}-name`);
    half.style.setProperty("--half-color", team.secondary || team.primary);
    if (team.logoUrl) logo.src = team.logoUrl; else logo.removeAttribute("src");
    name.textContent = team.name;
    const bgUrl = bridge ? await bridge.GetTeamBackgroundUrl(team.name) : null;
    half.style.backgroundImage = bgUrl ? `url("${bgUrl}")` : "none";
  };
  await Promise.all([fill("away", away), fill("home", home)]);
  const seam = document.getElementById("backdrop-vs-seam");
  seam.style.setProperty("--away-color", away.secondary || away.primary);
  seam.style.setProperty("--home-color", home.secondary || home.primary);
  document.getElementById("backdrop-vs").hidden = false;
}

function revertVsBackdrop() {
  document.getElementById("backdrop-vs").hidden = true;
}

/// "Game Day" fullscreen layout -- replaces the old "Sound Booth pops up as a centered modal
/// dimming the Assignment panel behind it" post-GAMETIME behavior with event cards/assignments
/// docked on one side and the Sound Booth permanently docked on the other, VS-style header up
/// top. Pure CSS-driven (see .gameday-mode in style.css): this toggles one class on <body> and
/// hides #adjust-panel (the older, plainer mixer sidebar Sound Booth's rack UI supersedes while
/// docked) -- no DOM nodes move, so every existing #sound-booth-overlay/#sound-booth id-based
/// selector, tab-switching, and knob-binding logic elsewhere in this file keeps working unchanged.
function enterGameDayMode() {
  document.body.classList.add("gameday-mode");
  updateMatchupSideBar(); // repopulate logos/names/glow now that the bar renders VS-prominent
  updateTeamPresetBar();
}

function exitGameDayMode() {
  document.body.classList.remove("gameday-mode");
}

function openSaveProfileDialog() {
  const overlay = document.getElementById("save-profile-overlay");
  const input = document.getElementById("save-profile-name");
  const subtext = document.getElementById("save-profile-subtext");
  input.value = state.activeTeam;
  updateSaveProfileSubtext();
  overlay.hidden = false;
  input.focus();
  input.select();
}

function updateSaveProfileSubtext() {
  const input = document.getElementById("save-profile-name");
  const subtext = document.getElementById("save-profile-subtext");
  const name = input.value.trim();
  if (!name) { subtext.textContent = ""; return; }
  subtext.textContent = name === state.activeTeam
    ? `Overwrites ${state.activeTeam}'s current save.`
    : `Creates a new, separate profile named "${name}" — ${state.activeTeam}'s own save is untouched.`;
}

document.getElementById("btn-save-profile-done-close")?.addEventListener("click", closeSaveProfileDialog);
document.getElementById("btn-save-profile-done-export")?.addEventListener("click", () => bridge?.ExportProfile());

function closeSaveProfileDialog() {
  document.getElementById("save-profile-overlay").hidden = true;
  document.getElementById("save-profile-ask").hidden = false;
  document.getElementById("save-profile-done").hidden = true;
}

async function confirmSaveProfile() {
  const name = document.getElementById("save-profile-name").value.trim();
  if (!name) return;
  const saved = await bridge?.SaveProfileAs(name);
  if (bridge) state.savedProfiles = JSON.parse(await bridge.GetSavedProfiles());
  renderTeamGrid();
  await updateProfileStatus();
  const t = await bridge?.GetProfileSavedAt(saved ?? name);
  showToast(`Saved "${saved ?? name}"${t ? ` at ${t}` : ""}`);

  document.getElementById("save-profile-ask").hidden = true;
  document.getElementById("save-profile-done-text").textContent =
    `"${saved ?? name}" is saved${t ? ` (${t})` : ""} -- every song currently assigned to this team's situations is locked in and will reload automatically next time you pick ${state.activeTeam}.`;
  document.getElementById("save-profile-done").hidden = false;
}

// ---- Load Profile from Others -----------------------------------------------------------
// Shares/loads a trigger->song ASSIGNMENT MAP (filenames only), not audio bytes -- applying a
// downloaded profile matches filenames against the applier's OWN Songs library and reports
// what it could/couldn't auto-assign. See ShareCurrentProfileToMarketplace/
// GetMarketplaceProfiles/ApplyMarketplaceProfile in WebBridge.cs for the real portability
// constraint this works within (paths aren't portable across machines, filenames sort of are).
async function shareCurrentProfile() {
  const btn = document.getElementById("btn-share-profile");
  btn.disabled = true;
  btn.textContent = "Sharing...";
  try {
    const raw = await bridge?.ShareCurrentProfileToMarketplace();
    const result = raw ? JSON.parse(raw) : null;
    showToast(result?.success
      ? `Shared ${state.activeTeam}'s profile (${result.count} songs) to the marketplace!`
      : (result?.error || "Couldn't share that profile -- try again."));
  } catch (err) {
    console.error("shareCurrentProfile failed", err);
    showToast("Couldn't share that profile -- try again.");
  }
  btn.disabled = false;
  btn.textContent = "Share Profile";
}

async function openLoadProfileDialog() {
  document.getElementById("load-profile-title").textContent = `Load Profile from Others -- for ${state.activeTeam}`;
  const list = document.getElementById("load-profile-list");
  list.innerHTML = `<div class="clipper-assign-row" style="cursor:default;">Loading...</div>`;
  document.getElementById("load-profile-overlay").hidden = false;

  let items = [];
  try {
    const raw = await bridge?.GetMarketplaceProfiles(state.activeTeam);
    items = raw ? (JSON.parse(raw).items || []) : [];
  } catch (err) {
    console.error("GetMarketplaceProfiles failed", err);
  }

  list.innerHTML = "";
  if (!items.length) {
    list.innerHTML = `<div class="clipper-assign-row" style="cursor:default;">No one's shared a profile for ${state.activeTeam} yet -- be the first with Share Profile.</div>`;
    return;
  }
  for (const item of items) {
    const row = document.createElement("div");
    row.className = "clipper-assign-row";
    row.textContent = `${item.name} -- ${new Date(item.uploadedAt).toLocaleDateString()}`;
    row.addEventListener("click", () => applyMarketplaceProfile(item.url, item.name));
    list.appendChild(row);
  }
}

function closeLoadProfileDialog() {
  document.getElementById("load-profile-overlay").hidden = true;
}

async function applyMarketplaceProfile(url, name) {
  closeLoadProfileDialog();
  showToast(`Applying "${name}"...`);
  try {
    const raw = await bridge?.ApplyMarketplaceProfile(url);
    const result = raw ? JSON.parse(raw) : null;
    if (!result?.success) {
      showToast(result?.error || "Couldn't apply that profile -- try again.");
      return;
    }
    await refreshCategories();
    if (state.currentSituationsCategory) await openSituations(state.currentSituationsCategory);
    const missed = result.total - result.applied;
    showToast(missed > 0
      ? `Applied ${result.applied} of ${result.total} songs -- ${missed} need a manual upload (filenames didn't match anything in your Songs library).`
      : `Applied all ${result.applied} songs from "${name}"!`);
  } catch (err) {
    console.error("applyMarketplaceProfile failed", err);
    showToast("Couldn't apply that profile -- try again.");
  }
}

function runRailAction(action) {
  switch (action) {
    case "focus-teams":
      openTeamPicker();
      break;
    case "focus-adjust":
      flashPanel(document.getElementById("adjust-panel"));
      document.getElementById("adjust-panel").scrollIntoView({ block: "nearest" });
      break;
    case "save-profile":
      openSaveProfileDialog();
      break;
    case "help":
      bridge?.OpenHelp();
      break;
  }
}

// --- What's New popup ---
// Used to be a hardcoded WHATS_NEW_CHANGELOG array + a manually-bumped WHATS_NEW_VERSION
// constant -- whoever cut a release had to remember to edit both here, completely separate from
// the real release notes the sidebar "What's New" panel already pulls live (see loadChangelog/
// GetChangelog above). Forgetting either one meant this popup either kept showing old text
// forever or silently stopped showing up for real new releases. Now sourced from the same live
// GetChangelog() feed, gated on the actual latest release title instead of a hand-maintained
// version string.
let _whatsNewEntries = [];

async function maybeShowWhatsNew() {
  if (!bridge) return;
  let entries = [];
  try {
    const raw = JSON.parse(await bridge.GetChangelog());
    entries = raw
      .map((e) => ({ ...e, notes: e.notes.filter((n) => !CHANGELOG_FILLER_PATTERN.test(n)) }))
      .filter((e) => e.notes.length > 0);
  } catch (err) { console.error("GetChangelog (What's New) failed", err); }
  if (!entries.length) return;

  let seen = null;
  try { seen = localStorage.getItem("bandroom-whatsnew-seen"); } catch (_) {}
  if (seen === entries[0].title) return;

  _whatsNewEntries = entries;
  setTimeout(showWhatsNewWhenClear, 600);
}

// Every other overlay in this app is opened by a direct user action (click a button), so two
// of them landing open at once was never a real risk until this one: it's the only overlay that
// pops itself open on a blind timer with no user action behind it. BUG FIX: if the user opened
// something else (Save Profile, Matchup, etc.) in that 600ms window, both used to render stacked
// on top of each other. Now it waits for whatever's open to close first, checking every 500ms,
// rather than barging in on top of it.
const WHATS_NEW_BLOCKING_OVERLAY_IDS = [
  "save-profile-overlay", "matchup-overlay", "bandroom-upload-overlay", "load-profile-overlay",
  "logo-crop-overlay", "bg-crop-overlay", "auto-assign-confirm-overlay", "auto-assign-summary-overlay",
  "quick-load-confirm-overlay", "track-info-overlay", "team-picker-overlay", "bandroom-overlay",
  "my-downloads-overlay", "sound-booth-overlay", "profile-overlay", "onboarding-overlay",
  "add-school-overlay", "import-target-team-overlay", "songpack-prompt-overlay",
  "songpack-import-overlay", "songpack-progress-overlay", "band-director-overlay",
  "band-director-settings-overlay",
];
function showWhatsNewWhenClear() {
  const anyOpen = WHATS_NEW_BLOCKING_OVERLAY_IDS.some((id) => {
    const el = document.getElementById(id);
    return el && !el.hidden;
  });
  if (anyOpen) { setTimeout(showWhatsNewWhenClear, 500); return; }
  showWhatsNew();
}

// BUG FIX: the check above only stops What's New from opening ONTO something else -- it does
// nothing for the reverse case, where What's New is ALREADY open (shown at launch, before the
// user had touched anything, so nothing was "open" yet to block it) and the user hasn't
// dismissed it yet when they open Save Profile or any other dialog. Every one of those ~20
// dialogs sets `.hidden = false` directly at dozens of call sites across this file -- rather
// than touch every one of them, watch the `hidden` attribute on each blocking overlay and
// auto-close What's New the instant any of them becomes visible while it's still up.
(() => {
  const whatsNewOverlay = document.getElementById("whats-new-overlay");
  if (!whatsNewOverlay) return;
  const observer = new MutationObserver((mutations) => {
    for (const m of mutations) {
      const el = m.target;
      if (el.id !== "whats-new-overlay" && !el.hidden && !whatsNewOverlay.hidden) {
        dismissWhatsNew();
        return;
      }
    }
  });
  for (const id of WHATS_NEW_BLOCKING_OVERLAY_IDS) {
    const el = document.getElementById(id);
    if (el) observer.observe(el, { attributes: true, attributeFilter: ["hidden"] });
  }
})();

function showWhatsNew() {
  const overlay = document.getElementById("whats-new-overlay");
  const changelog = document.getElementById("whats-new-changelog");
  if (!overlay || !changelog || !_whatsNewEntries.length) return;

  let html = "";
  for (const entry of _whatsNewEntries.slice(0, 4)) {
    html += `<div class="whats-new-card">
      <div class="whats-new-card-version">${entry.title}</div>
      <div class="whats-new-card-text">${entry.notes.join(" ")}</div>
    </div>`;
  }
  changelog.innerHTML = html;
  overlay.hidden = false;
}

function dismissWhatsNew() {
  document.getElementById("whats-new-overlay").hidden = true;
  try {
    if (_whatsNewEntries.length) localStorage.setItem("bandroom-whatsnew-seen", _whatsNewEntries[0].title);
  } catch (_) {}
}

document.getElementById("btn-whats-new-gotit")?.addEventListener("click", dismissWhatsNew);
document.getElementById("btn-close-whats-new")?.addEventListener("click", dismissWhatsNew);

// --- Event test hook (owner debug tool) --------------------------------------------------
// Fires any EventKey for home/away straight through WebMainForm.FireEventForSide, bypassing
// OCR/live game feed entirely. Opened with Ctrl+Shift+T since it has no place in the normal
// user-facing nav. See WebBridge.FireTestEvent / GetAllEventKeys.
async function openTestHook() {
  const panel = document.getElementById("test-hook-panel");
  const select = document.getElementById("test-hook-event");
  const selectA = document.getElementById("test-hook-event-a");
  const selectB = document.getElementById("test-hook-event-b");
  if (!panel || !select) return;
  if (bridge && select.options.length === 0) {
    // BUG FIX: this used to have no try/catch -- if GetAllEventKeys ever threw/rejected, the
    // whole function aborted before `panel.hidden = false` ran, so Ctrl+Shift+T looked like it
    // did nothing at all. Silent, too, since F12 DevTools is suppressed by
    // AreBrowserAcceleratorKeysEnabled = false. Now falls through to still open the panel.
    try {
      const keys = JSON.parse(await bridge.GetAllEventKeys());
      // Raw EventKey, not friendlyEventName -- this is a debug tool, and several distinct keys
      // (e.g. "Offense: Second Down" / "Defense: Second Down") collapse to the identical friendly
      // label, which made it impossible to tell which one you'd actually selected/fired.
      const optionsHtml = keys.map(k => `<option value="${k}">${k}</option>`).join("");
      select.innerHTML = optionsHtml;
      if (selectA) selectA.innerHTML = optionsHtml;
      if (selectB) selectB.innerHTML = optionsHtml;
    } catch (err) {
      console.error("openTestHook: GetAllEventKeys failed", err);
      showToast("Test hook: couldn't load event list, dropdowns may be stale.");
    }
  }
  panel.hidden = false;
}

document.addEventListener("keydown", (e) => {
  if (e.ctrlKey && e.shiftKey && e.key.toLowerCase() === "t") openTestHook();
});

document.getElementById("btn-close-test-hook")?.addEventListener("click", () => {
  document.getElementById("test-hook-panel").hidden = true;
});

document.getElementById("btn-test-hook-fire")?.addEventListener("click", async () => {
  const side = document.getElementById("test-hook-side").value;
  const eventKey = document.getElementById("test-hook-event").value;
  const result = await bridge?.FireTestEvent(side, eventKey);
  if (!result) return;
  if (result.startsWith("fired:")) showToast(`Fired: ${result.slice(6)}`);
  else if (result === "unassigned") showToast(`No song assigned to "${friendlyEventName(eventKey)}" for ${side}.`);
  else if (result === "file-missing") showToast(`Assigned file is missing on disk for "${friendlyEventName(eventKey)}".`);
  else if (result === "no-profile") showToast("No matchup/team profile loaded yet -- pick a team or Set Matchup first.");
});

document.getElementById("btn-test-hook-stop")?.addEventListener("click", () => bridge?.StopPreview());

document.getElementById("btn-test-hook-fire-routed")?.addEventListener("click", async () => {
  const possessionSide = document.getElementById("test-hook-possession").value;
  const eventKey = document.getElementById("test-hook-event").value;
  const isEarnedBigEvent = document.getElementById("test-hook-earned").checked;
  const result = await bridge?.FireTestEventRouted(possessionSide, eventKey, isEarnedBigEvent);
  if (!result) return;
  if (result.startsWith("blocked:")) { showToast(`Blocked: ${result.slice(8)}`); return; }
  const [routedSide, fireResult] = result.split("|");
  if (fireResult.startsWith("fired:")) showToast(`Routed to ${routedSide}, fired: ${fireResult.slice(6)}`);
  else if (fireResult === "unassigned") showToast(`Routed to ${routedSide} -- no song assigned to "${friendlyEventName(eventKey)}".`);
  else if (fireResult === "file-missing") showToast(`Routed to ${routedSide} -- assigned file is missing on disk.`);
  else if (fireResult === "no-profile") showToast("No matchup/team profile loaded yet -- pick a team or Set Matchup first.");
  else showToast(`Routed to ${routedSide}: ${fireResult}`);
});

document.getElementById("btn-test-hook-fire-pair")?.addEventListener("click", async () => {
  const possessionSide = document.getElementById("test-hook-possession").value;
  const eventKeyA = document.getElementById("test-hook-event-a").value;
  const eventKeyB = document.getElementById("test-hook-event-b").value;
  const isEarnedBigEvent = document.getElementById("test-hook-earned").checked;
  const result = await bridge?.FireTestEventPair(possessionSide, eventKeyA, eventKeyB, isEarnedBigEvent);
  if (!result) return;
  // "EventKey=side|result;EventKey=side|result" -- see WebMainForm.FireTestEventPairFromWeb.
  const parts = result.split(";").map(part => {
    const [key, rest] = part.split("=");
    const [routedSide, fireResult] = rest.split("|");
    if (fireResult.startsWith("blocked:")) return `${friendlyEventName(key)} -> ${routedSide}: blocked`;
    if (fireResult.startsWith("fired:")) return `${friendlyEventName(key)} -> ${routedSide}: fired`;
    return `${friendlyEventName(key)} -> ${routedSide}: ${fireResult}`;
  });
  showToast(parts.join(" | "));
});

// Plain-English labels for EventKeys -- "Offense:"/"Defense:"/"Other:" prefixes and helper-name
// jargon (Midfield, Iced Game, etc) mean nothing to someone assigning songs. EventKey stays the
// real internal ID (zero risk to saved profiles) -- this is a display-only lookup, falls back to
// the raw key untouched if a new EventKey shows up here before this map is updated.
const EVENT_FRIENDLY_NAMES = {
  "Offense: Earned First Down": "1st Down (1st & 10)",
  "Offense: Earned First Down (Big Gain)": "Got 1st Down - Big Gain",
  "Offense: Earned First Down Short": "1st & Short",
  "Offense: 3rd Down Conversion": "Converted 3rd Down",
  "Offense: Earned First Down (Midfield)": "Got 1st Down - Past Midfield",
  "Offense: Second Down": "2nd Down",
  "Offense: Second Down (Midfield)": "2nd Down - Past Midfield",
  "Offense: Second Down Short": "2nd & Short",
  "Offense: Third Down": "3rd Down",
  "Offense: Third Down Short": "3rd & Short",
  "Offense: 1st Down After Punt": "1st Down After Punt",
  "Offense: PAT Made": "Extra Point Good",
  "Offense: 2-Point Conversion Made": "2-Point Conversion Good",
  "Offense: Field Goal Made": "Field Goal Good",
  "Offense: Iced Game by First Down": "Game Sealed - Got 1st Down",
  "Offense: Victory in Hand": "Game Won",
  "Offense: Touchdown Scored": "Touchdown",
  "Defense: After Opening Kick": "Defense After Opening Kick",
  "Defense: Third Down": "3rd & Long",
  "Defense: Third Down Short": "3rd & Short (Defense)",
  "Defense: Fourth Down": "Stopped Them on 4th",
  "Defense: Second Down": "2nd & Long",
  "Defense: Second Down (Midfield)": "2nd Down - Past Midfield",
  "Defense: Second Down (Loss)": "2nd Down After a Loss",
  "Defense: Fourth Down (Loss)": "Stopped Them on 4th After a Loss",
  "Defense: After Punt": "Defense After Punt",
  "Defense: Field Goal Missed by Opponent": "Opponent Missed Field Goal",
  "Defense: Turnover Forced": "Turnover Forced",
  "Defense: Iced Game by Turnover": "Game Sealed by Turnover",
  "Defense: Safety": "Safety",
  "Defense: Tackle for Loss": "Tackle for Loss / Fumble",
  "Defense: Touchdown Scored": "Touchdown",
  "Defense: Timeout (4 Remaining)": "Opponent's 2nd Timeout Used",
  "Defense: Timeout (3 Remaining)": "Opponent's 3rd Timeout Used",
  "Defense: Timeout (2 Remaining)": "Opponent's 4th Timeout Used",
  "Defense: Timeout (1 Remaining)": "Opponent's 5th Timeout Used",
  "Defense: Timeout (0 Remaining)": "Opponent's Last Timeout Used",
  "Other: Start of 2nd Quarter": "Start of 2nd Quarter",
  "Other: Start of 4th Quarter": "Start of 4th Quarter",
  "Other: Pregame Take the Field": "Pregame - Team Takes the Field",
  "Other: Opening Kickoff": "Opening Kickoff",
  "Other: Second-Half Kickoff": "Second-Half Kickoff",
  "Other: Kickoff on Kick (Receiving)": "Kickoff - Receiving",
  "Other: Kickoff on Kick (Kicking)": "Kickoff - Kicking",
  "Penalty: Offense": "Penalty on Offense",
  "Penalty: Defense": "Penalty on Defense",
  "Defense: No Punt Return": "Punt Return Stopped",
};
function friendlyEventName(eventKey) {
  return EVENT_FRIENDLY_NAMES[eventKey] || eventKey;
}

// ================================================================
// SEARCH DEBOUNCE (200ms) — marketplace & team picker
// ================================================================
function debounce(fn, delay) {
  let timer = null;
  return function (...args) {
    clearTimeout(timer);
    timer = setTimeout(() => fn.apply(this, args), delay);
  };
}
(function setupSearchDebounce() {
  const teamPickerSearch = document.getElementById("team-picker-search");
  const bandroomSearch = document.getElementById("bandroom-search");
  const bandroomAlbumSearch = document.getElementById("bandroom-album-search");
  const cmdInput = document.getElementById("cmd-input");
  if (teamPickerSearch) teamPickerSearch.addEventListener("input", debounce(() => filterTeamPicker(teamPickerSearch.value), 200));
  if (bandroomSearch) bandroomSearch.addEventListener("input", debounce(() => filterBandroomTeams(bandroomSearch.value), 200));
  if (bandroomAlbumSearch) bandroomAlbumSearch.addEventListener("input", debounce(() => filterAlbumSearch(bandroomAlbumSearch.value), 200));
  if (cmdInput) cmdInput.addEventListener("input", debounce(() => filterCommandPalette(cmdInput.value), 100));
})();

// ================================================================
// TEAM DATA VALIDATION FALLBACK
// ================================================================
function validateTeamData(teams) {
  if (!Array.isArray(teams) || teams.length === 0) {
    console.warn("[team-validation] Invalid team data, using fallback");
    return [{ name: "General", primary: "#22d3ee", secondary: "#22d3ee", initials: "GEN" }];
  }
  return teams.filter((t) => t && typeof t.name === "string" && t.name.length > 0);
}

// ================================================================
// BRIDGE FALLBACK — detect real browser vs WebView2
// ================================================================
function isRealBrowser() {
  return !bridge && !window.chrome?.webview;
}
function logPlatformInfo() {
  console.log("[platform]", isRealBrowser() ? "Browser preview (no bridge)" : "WebView2 (bridge active)");
}

// ================================================================
// PREVIEW WAVEFORM CANVAS SIZING
// ================================================================
function resizePreviewWaveform() {
  const canvas = document.getElementById("preview-waveform");
  if (!canvas) return;
  const container = canvas.parentElement;
  if (!container) return;
  const rect = container.getBoundingClientRect();
  canvas.width = Math.max(260, rect.width - 200);
  canvas.height = 40;
}
window.addEventListener("resize", resizePreviewWaveform);

// ================================================================
// XSS SANITIZATION helper for marketplace innerHTML
// ================================================================
// Called by buildItemTile's uploader line ("Uploaded by X · 3h ago") but had no definition
// anywhere in this file -- a genuine ReferenceError on any marketplace item whose uploadedAt is
// set (i.e. any real upload), which crashed the whole tile-rendering loop for that team's album
// and left the grid blank (caught by the global JS error guard's toast, with no per-tile
// fallback -- one bad item took the entire render down instead of just that tile).
function relativeTime(isoString) {
  const then = new Date(isoString).getTime();
  if (Number.isNaN(then)) return "";
  const seconds = Math.max(0, Math.floor((Date.now() - then) / 1000));
  if (seconds < 60) return "just now";
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.floor(hours / 24);
  if (days < 30) return `${days}d ago`;
  const months = Math.floor(days / 30);
  if (months < 12) return `${months}mo ago`;
  return `${Math.floor(months / 12)}y ago`;
}

function sanitizeHTML(str) {
  const div = document.createElement("div");
  div.textContent = str;
  return div.innerHTML;
}
function sanitizeMarketplaceItem(item) {
  return {
    ...item,
    name: sanitizeHTML(item.name || "Untitled"),
    school: sanitizeHTML(item.school || "Unknown"),
    uploadedBy: sanitizeHTML(item.uploadedBy || "anonymous"),
  };
}

// ================================================================
// COMMAND PALETTE (Ctrl+K)
// ================================================================
const COMMANDS = [
  { icon: "🎵", label: "The Bandroom", hint: "marketplace", action: () => toggleBandroom() },
  { icon: "🏆", label: "Sound Bank", hint: "songs + backgrounds", action: () => openTeamSoundBank(state.activeTeam) },
  { icon: "⬇️", label: "My Downloads", hint: "library", action: () => toggleMyDownloads() },
  { icon: "💬", label: "Discord Chat", hint: "chat", action: () => toggleDiscordChat() },
  { icon: "⚔️", label: "Set Matchup", hint: "home/away", action: () => openMatchupPicker() },
  { icon: "💾", label: "Save Profile", hint: "save", action: () => openSaveProfileDialog() },
  { icon: "🎮", label: "Streamer Mode", hint: "toggle", action: () => toggleStreamerMode() },
  { icon: "⌨️", label: "Keyboard Shortcuts", hint: "hotkeys", action: () => openHotkeyPanel() },
  { icon: "📋", label: "Tips", hint: "show tip", action: () => showNextTip() },
  { icon: "👤", label: "Profile", hint: "dashboard", action: () => openProfile() },
  { icon: "⚙️", label: "Settings", hint: "preferences", action: () => document.getElementById("btn-settings")?.click() },
  { icon: "ℹ️", label: "Help", hint: "guide", action: () => document.getElementById("btn-help-pill")?.click() },
  { icon: "🔄", label: "Reset Team Profile", hint: "reset", action: () => resetTeamProfile() },
  { icon: "📁", label: "Move Default Song Pack Folder", hint: "relocate", action: () => relocateDefaultSongsFolder() },
  { icon: "🎵", label: "Download / Import Default Song Pack", hint: "song pack", action: () => { document.getElementById("songpack-prompt-overlay").hidden = false; } },
  // Direct entry point for someone who already has the .zip on disk (downloaded earlier, or
  // handed to them by someone else) -- previously the ONLY way to reach the "Locate & Import"
  // button was to click "Download" first (which re-opens the Google Drive page), even if the
  // user already had the file. #songpack-import-overlay's own buttons are wired independently in
  // initDefaultSongPackPrompt, so just showing it here reuses that wiring with no duplication.
  { icon: "📂", label: "Locate & Import Song Pack (I already have the .zip)", hint: "song pack", action: () => { document.getElementById("songpack-import-overlay").hidden = false; } },
];

/// Task queue item 7b (Session 10) -- lets the user move the default song pack (2,241 files,
/// ~2.8GB, see initDefaultSongPackPrompt above) to a different drive/folder instead of it being
/// stuck under AppData forever. Reachable from the command palette (Ctrl+K) rather than a new
/// always-visible button, since this is a rare one-off action, not a frequent one -- same
/// treatment "Reset Team Profile" above already gets.
async function relocateDefaultSongsFolder() {
  if (!bridge) return;
  let result;
  try {
    result = JSON.parse(await bridge.RelocateDefaultSongsFolder());
  } catch (err) {
    console.error("RelocateDefaultSongsFolder failed", err);
    showToast("Couldn't move the song pack folder -- try again.");
    return;
  }
  if (result.cancelled) return; // user backed out of the folder picker, not an error
  if (result.success) {
    showToast(`Default song pack now lives at: ${result.path}`);
  } else {
    showToast(result.error ?? "Couldn't move the song pack folder -- try again.");
  }
}

let _cmdActiveIndex = 0;
function openCommandPalette() {
  const overlay = document.getElementById("command-palette-overlay");
  const input = document.getElementById("cmd-input");
  overlay.hidden = false;
  input.value = "";
  _cmdActiveIndex = 0;
  filterCommandPalette("");
  setTimeout(() => input.focus(), 50);
}
function closeCommandPalette() {
  document.getElementById("command-palette-overlay").hidden = true;
}
function filterCommandPalette(query) {
  const results = document.getElementById("cmd-results");
  const q = query.toLowerCase().trim();
  const matches = q ? COMMANDS.filter((c) => c.label.toLowerCase().includes(q) || c.hint.toLowerCase().includes(q)) : COMMANDS;
  _cmdActiveIndex = Math.min(_cmdActiveIndex, Math.max(0, matches.length - 1));
  results.innerHTML = "";
  matches.forEach((cmd, i) => {
    const item = document.createElement("div");
    item.className = "cmd-result-item" + (i === _cmdActiveIndex ? " active" : "");
    item.innerHTML = `<span class="cmd-result-icon">${cmd.icon}</span><span class="cmd-result-label">${cmd.label}</span><span class="cmd-result-hint">${cmd.hint}</span>`;
    item.addEventListener("click", () => { closeCommandPalette(); cmd.action(); });
    item.addEventListener("mouseenter", () => { _cmdActiveIndex = i; filterCommandPalette(query); });
    results.appendChild(item);
  });
}
document.addEventListener("keydown", (e) => {
  const overlay = document.getElementById("command-palette-overlay");
  if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k") {
    e.preventDefault();
    if (overlay.hidden) openCommandPalette();
    else closeCommandPalette();
    return;
  }
  if (!overlay.hidden) {
    if (e.key === "Escape") { e.preventDefault(); closeCommandPalette(); }
    else if (e.key === "ArrowDown") { e.preventDefault(); _cmdActiveIndex = Math.min(_cmdActiveIndex + 1, COMMANDS.length - 1); filterCommandPalette(document.getElementById("cmd-input").value); }
    else if (e.key === "ArrowUp") { e.preventDefault(); _cmdActiveIndex = Math.max(_cmdActiveIndex - 1, 0); filterCommandPalette(document.getElementById("cmd-input").value); }
    else if (e.key === "Enter") { e.preventDefault(); const items = document.querySelectorAll(".cmd-result-item"); if (items[_cmdActiveIndex]) items[_cmdActiveIndex].click(); }
  }
});
document.getElementById("command-palette-overlay")?.addEventListener("click", (e) => {
  if (e.target === e.currentTarget) closeCommandPalette();
});

// ================================================================
// RIGHT-CLICK CONTEXT MENUS
// ================================================================
let _contextMenuTarget = null;
function buildContextMenu(teamName, x, y) {
  const menu = document.getElementById("context-menu");
  _contextMenuTarget = teamName;
  menu.innerHTML = "";
  const items = [
    { label: "Set as Active Team", icon: "🎯", action: () => { if (_contextMenuTarget) selectTeam(_contextMenuTarget); } },
    { label: "Open Sound Bank", icon: "🎵", action: () => { if (_contextMenuTarget) openTeamSoundBank(_contextMenuTarget); } },
    { sep: true },
    { label: "Pin to Top", icon: "📌", action: () => { if (_contextMenuTarget) pinTeam(_contextMenuTarget); } },
    { label: "Duplicate Profile To...", icon: "📋", action: () => { if (_contextMenuTarget) duplicateTeamProfile(_contextMenuTarget); } },
  ];
  items.forEach((it) => {
    if (it.sep) {
      const sep = document.createElement("div");
      sep.className = "context-menu-sep";
      menu.appendChild(sep);
    } else {
      const el = document.createElement("div");
      el.className = "context-menu-item";
      el.innerHTML = `<span>${it.icon}</span> ${it.label}`;
      el.addEventListener("click", () => { closeContextMenu(); it.action(); });
      menu.appendChild(el);
    }
  });
  menu.style.left = Math.min(x, window.innerWidth - 200) + "px";
  menu.style.top = Math.min(y, window.innerHeight - 250) + "px";
  menu.hidden = false;
}
function closeContextMenu() {
  document.getElementById("context-menu").hidden = true;
}
document.addEventListener("click", closeContextMenu);
document.addEventListener("contextmenu", (e) => {
  const swatch = e.target.closest(".team-swatch");
  if (swatch && swatch.title) {
    e.preventDefault();
    buildContextMenu(swatch.title.replace(" ✓", ""), e.clientX, e.clientY);
  }
});

// ================================================================
// HUD OVERLAY (live game data)
// ================================================================
function updateHUD(homeScore, awayScore, quarter, downDistance) {
  const hud = document.getElementById("hud-overlay");
  if (state.watching !== "watching") { hud.hidden = true; return; }
  hud.hidden = false;
  document.getElementById("hud-home-score").textContent = homeScore ?? 0;
  document.getElementById("hud-away-score").textContent = awayScore ?? 0;
  document.getElementById("hud-quarter").textContent = quarter ?? "1ST";
  document.getElementById("hud-down-distance").textContent = downDistance ?? "1st & 10";
}

// ================================================================
// STREAMER MODE TOGGLE
// ================================================================
let _streamerMode = false;
function toggleStreamerMode() {
  _streamerMode = !_streamerMode;
  document.getElementById("streamer-mode-indicator").hidden = !_streamerMode;
  // In streamer mode: hide sensitive info, mute UI sounds
  document.getElementById("presence-dot").style.display = _streamerMode ? "none" : "";
  showToast(_streamerMode ? "Streamer Mode ON — personal info hidden" : "Streamer Mode OFF");
  try { localStorage.setItem("bandroom-streamer-mode", _streamerMode.toString()); } catch (_) {}
}

// ================================================================
// SOUNDBOARD FAVORITES SYSTEM
// ================================================================
let _soundboardSlots = {};
function loadSoundboard() {
  try { _soundboardSlots = JSON.parse(localStorage.getItem("bandroom-soundboard") || "{}"); } catch (_) { _soundboardSlots = {}; }
  // Scoped to #soundboard-bar specifically -- .soundboard-btn is also reused (for its visual
  // style only) by the Band Director dashboard's Quick Trigger buttons, which have their own
  // dataset (data-trigger-slot, not data-key) and their own click handler
  // (onBandDirectorQuickTriggerClick); a bare ".soundboard-btn" query here used to clobber their
  // text and double-fire clicks.
  document.querySelectorAll("#soundboard-bar .soundboard-btn").forEach((btn) => {
    const key = btn.dataset.key;
    const entry = _soundboardSlots[key];
    btn.title = entry ? entry.label : `Favorite ${key} (unassigned)`;
    btn.textContent = entry ? entry.label.slice(0, 3) : key;
  });
}
function assignSoundboardSlot(key, label, songPath) {
  _soundboardSlots[key] = { label, path: songPath };
  try { localStorage.setItem("bandroom-soundboard", JSON.stringify(_soundboardSlots)); } catch (_) {}
  loadSoundboard();
  showToast(`Soundboard slot ${key} set to "${label}"`);
}
document.querySelectorAll("#soundboard-bar .soundboard-btn").forEach((btn) => {
  btn.addEventListener("click", () => {
    const key = btn.dataset.key;
    const entry = _soundboardSlots[key];
    if (entry) {
      bridge?.PlaySoundboardSlot(key, entry.path);
      btn.classList.add("playing");
      setTimeout(() => btn.classList.remove("playing"), 500);
    } else {
      showToast("Assign a song to this slot first — right-click on any song tile and choose 'Add to Soundboard'");
    }
  });
});
loadSoundboard();

// ================================================================
// SOUND VISUALIZER ANIMATION
// ================================================================
function initSoundVisualizer() {
  const container = document.getElementById("sound-visualizer");
  if (!container) return;
  for (let i = 0; i < 16; i++) {
    const bar = document.createElement("div");
    bar.className = "sound-visualizer-bar";
    container.appendChild(bar);
  }
  // Simulated visualizer — animates bars randomly for aesthetic
  function animate() {
    if (document.getElementById("sound-visualizer").closest("[hidden]")) { requestAnimationFrame(animate); return; }
    container.querySelectorAll(".sound-visualizer-bar").forEach((bar) => {
      const h = Math.random() * 28 + 4;
      bar.style.height = h + "px";
    });
    setTimeout(() => requestAnimationFrame(animate), 120);
  }
  requestAnimationFrame(animate);
}
setTimeout(initSoundVisualizer, 1000);

// ================================================================
// TIPS SYSTEM — 100 tips + auto-cycle + context-aware
// ================================================================
const TIPS_DATABASE = [
  "Press Ctrl+K to quickly search for any action in Bandroom.",
  "Right-click any team tile to quickly open that team's Sound Bank.",
  "Assigning PA announcer clips makes game moments feel like a real broadcast.",
  "You can import your own MP3/WAV songs from the My Downloads panel.",
  "Bandroom automatically switches profiles when you set a matchup.",
  "Pin teams to the top of your team grid for quick access.",
  "Collapse the side panels with the ◀ button for more screen space.",
  "Soundboard favorites let you trigger any sound with one click.",
  "Streamer mode hides personal info when you're broadcasting.",
  "The kill-feed shows live game events as they happen.",
  "You can set custom team logos from any image on your computer.",
  "Team backgrounds can be any 16:9 image — stadium photos work great.",
  "Each team has its own independent song profile.",
  "Download songs from The Bandroom to build your library.",
  "Upload your own songs to share with other Bandroom users.",
  "Open a team's Sound Bank to download custom background art too.",
  "The sensitivity slider controls how long songs play before fading.",
  "Reverb settings make your sounds feel like they're in a stadium.",
  "You can export your team profiles to share with friends.",
  "The bottom ticker shows recent uploads from the community.",
  "FPS/ping indicator shows your app performance in the corner.",
  "Use the test hook (Ctrl+Shift+T) to fire events manually.",
  "Search debounce makes typing in the marketplace feel faster.",
  "Team logos are lazy-loaded for better performance.",
  "You can duplicate a team's profile to another team in one click.",
  "The HUD overlay shows live score during watched games.",
  "Bandroom supports light mode if your system prefers it.",
  "Reduced-motion mode respects your accessibility settings.",
  "You can preview any song before downloading it from the marketplace.",
  "The leaderboard shows which teams have the most uploads.",
  "Add your favorite team as the active team for quick access.",
  "Log wins and losses for your favorite team in your profile.",
  "Each achievement has a rarity tier: bronze, silver, gold, or diamond.",
  "You can follow other users and see their public profiles.",
  "The command palette is the fastest way to navigate Bandroom.",
  "Season pass tracks your progress through the season.",
  "Match history shows a timeline of your recent games.",
  "The Discord panel lets you chat while watching a game.",
  "You can set custom hotkeys for common actions.",
  "QR code sharing makes it easy for friends to find your profile.",
  "Resume last session picks up right where you left off.",
  "Offline mode keeps Bandroom working when you lose connection.",
  "Sound pack recommendations help you discover new sounds.",
  "Dynasty mode tracks your season stats and recruiting.",
  "Rivalry alerts notify you when your rival team plays.",
  "Top-25 scoreboard keeps you updated on ranked teams.",
  "Conference standings track every team's record.",
  "Bowl projections predict where teams are heading.",
  "Coach cards show your dynasty coaching record.",
  "Recruiting class tracker follows your incoming freshmen.",
  "Award watch lists track Heisman and other candidates.",
  "Milestone alerts celebrate your achievements in dynasty.",
  "XP bonuses reward you for completing dynasty objectives.",
  "The brand mark pulses in your team's color.",
  "Glass panels have different blur depths for visual hierarchy.",
  "Crosshair cursors give the UI a gamer feel.",
  "Rubber-band scrolling makes lists feel natural.",
  "Haptic feedback gives buttons a physical feel.",
  "Skeleton loading shows content structure before data loads.",
  "Focus-visible outlines help keyboard users navigate.",
  "The green dot on team tiles means that team has a profile.",
  "You can add up to 8 favorites on your soundboard.",
  "Bandroom auto-detects when you're in a WebView2 container.",
  "All marketplace content is sanitized for security.",
  "UI Bot automatically scans for bugs on every page load.",
  "The VS split backdrop shows both teams during a matchup.",
  "You can batch import team logos with Ctrl+Alt+Shift+L.",
  "The update button shows your current version and pending updates.",
  "Toast notifications tell you what just happened.",
  "The onboarding screen helps new users pick their first team.",
  "You can choose between stadium, dome, and night game reverb.",
  "The preview bar shows a waveform you can scrub through.",
  "Every situation card shows its assignment status at a glance.",
  "Team tiles use dock-style magnification on hover.",
  "The header drag handle lets you move the window.",
  "Window controls follow macOS traffic-light design.",
  "Bandroom saves your session automatically.",
  "You can set separate volume levels for home and away.",
  "PA announcer volume is independent of music volume.",
  "Dynamic backgrounds change based on your active team.",
  "The presence dot shows how many users are online.",
  "All team data is validated before rendering.",
  "Lazy-loaded images only load when they're about to be visible.",
  "Search results update 200ms after you stop typing.",
  "Your profile dashboard has a public shareable URL.",
  "Custom team backgrounds are stored locally on your PC.",
  "Song assignments can be undone with the undo system.",
  "Multi-select lets you operate on multiple teams at once.",
  "Keyboard arrow keys navigate most lists and grids.",
  "Breadcrumb navigation helps you track where you are.",
  "Context menus appear on right-click throughout the app.",
  "Party sync lets groups coordinate their Bandroom setups.",
  "Clip integration lets you save and replay game moments.",
  "Season pass rewards carry over between games.",
  "Dynasty save scanner auto-detects your game files.",
  "Join the Discord to share tips and songs with the community.",
  "Bandroom is built by one developer — feedback is always welcome!",
];
let _tipIndex = 0;
let _tipTimer = null;
function showNextTip() {
  const widget = document.getElementById("tip-widget");
  document.getElementById("tip-text").textContent = TIPS_DATABASE[_tipIndex % TIPS_DATABASE.length];
  _tipIndex++;
  widget.hidden = false;
  clearTimeout(_tipTimer);
  _tipTimer = setTimeout(() => { widget.hidden = true; }, 12000);
}
function startTipAutoCycle() {
  setInterval(() => {
    const widget = document.getElementById("tip-widget");
    if (!widget.hidden) return; // don't interrupt an already-showing tip
    // Don't show if any overlay is open
    const overlays = document.querySelectorAll("#team-picker-overlay:not([hidden]), #bandroom-overlay:not([hidden]), #bandroom-album-overlay:not([hidden]), #matchup-overlay:not([hidden]), #command-palette-overlay:not([hidden])");
    if (overlays.length > 0) return;
    showNextTip();
  }, Math.random() * 45000 + 45000); // 45-90s interval
}
setTimeout(startTipAutoCycle, 30000);

document.getElementById("tip-never-show")?.addEventListener("click", () => {
  document.getElementById("tip-widget").hidden = true;
  clearTimeout(_tipTimer);
  try { localStorage.setItem("bandroom-tips-disabled", "true"); } catch (_) {}
  showToast("Tips disabled. Re-enable from Settings.");
});
document.getElementById("tip-next")?.addEventListener("click", showNextTip);

// ================================================================
// LEADERBOARDS
// ================================================================
function renderLeaderboardTable(container, data, type) {
  if (!container) return;
  container.innerHTML = "";
  if (!data || data.length === 0) {
    container.innerHTML = '<div class="bandroom-recent-empty">No entries yet</div>';
    return;
  }
  data.forEach((entry, i) => {
    const row = document.createElement("div");
    row.className = "leaderboard-row";
    const rankClass = i === 0 ? "top1" : i === 1 ? "top2" : i === 2 ? "top3" : "";
    row.innerHTML = `<span class="leaderboard-rank ${rankClass}">#${i + 1}</span><span class="leaderboard-user">${sanitizeHTML(entry.name || entry.school || "Unknown")}</span><span class="leaderboard-score">${entry.score || entry.count || 0}</span>`;
    // Player rows (type "users") carry a `sub` -- clickable through to their public profile.
    // School rows (marketplace upload counts) have no such identity, so stay inert.
    if (type === "users" && entry.sub) {
      row.classList.add("leaderboard-row-clickable");
      row.addEventListener("click", () => openPublicProfile(entry.sub));
    }
    container.appendChild(row);
  });
}

// ================================================================
// FOLLOW/FRIEND SYSTEM
// ================================================================
let _followedUsers = [];
function loadFollowedUsers() {
  try { _followedUsers = JSON.parse(localStorage.getItem("bandroom-followed") || "[]"); } catch (_) { _followedUsers = []; }
}
function followUser(username) {
  if (!_followedUsers.includes(username)) {
    _followedUsers.push(username);
    try { localStorage.setItem("bandroom-followed", JSON.stringify(_followedUsers)); } catch (_) {}
    showToast(`Following ${username}`);
  }
}
function unfollowUser(username) {
  _followedUsers = _followedUsers.filter((u) => u !== username);
  try { localStorage.setItem("bandroom-followed", JSON.stringify(_followedUsers)); } catch (_) {}
  showToast(`Unfollowed ${username}`);
}
loadFollowedUsers();

// ================================================================
// DYNASTY FEATURES — save scanner, stats, recruiting, rivalry
// ================================================================
let _dynastyData = null;
function scanDynastySave() {
  if (!bridge) { showToast("Dynasty scanning requires the full app."); return; }
  bridge.ScanDynastySave().then((raw) => {
    if (raw) {
      _dynastyData = JSON.parse(raw);
      showToast(`Loaded dynasty: ${_dynastyData.teamName} (Year ${_dynastyData.year})`);
    }
  }).catch(() => showToast("No dynasty save found."));
}
function getDynastyRecord() {
  if (!_dynastyData) return "—";
  return `${_dynastyData.wins || 0}-${_dynastyData.losses || 0}`;
}
function getRecruitingRank() {
  if (!_dynastyData) return "—";
  return `#${_dynastyData.recruitingRank || "—"}`;
}

// ================================================================
// COLLAPSIBLE SIDE PANELS
// ================================================================
function toggleLeftPanel() {
  document.getElementById("left-panel").classList.toggle("collapsed");
}
function toggleRightPanel() {
  document.getElementById("adjust-panel").classList.toggle("collapsed");
}

// ================================================================
// TABBED RIGHT PANEL SWITCHING
// ================================================================
(function initAdjustTabs() {
  const panel = document.getElementById("adjust-panel");
  if (!panel) return;
  const tabs = document.createElement("div");
  tabs.className = "adjust-tabs";
  tabs.innerHTML = `
    <button class="adjust-tab active" data-tab="mixer">Mixer</button>
    <button class="adjust-tab" data-tab="effects">Effects</button>
    <button class="adjust-tab" data-tab="changelog">Changelog</button>
    <button class="adjust-tab" data-tab="help">Help</button>`;
  panel.insertBefore(tabs, panel.firstChild);
  tabs.querySelectorAll(".adjust-tab").forEach((tab) => {
    tab.addEventListener("click", () => {
      tabs.querySelectorAll(".adjust-tab").forEach((t) => t.classList.remove("active"));
      tab.classList.add("active");
    });
  });
})();

// ================================================================
// BREADCRUMB NAVIGATION
// ================================================================
function setBreadcrumb(path) {
  const bc = document.getElementById("breadcrumb");
  if (!bc) return;
  bc.innerHTML = "";
  path.forEach((segment, i) => {
    const item = document.createElement("span");
    item.className = "breadcrumb-item";
    item.textContent = segment.label;
    if (segment.action) item.addEventListener("click", segment.action);
    bc.appendChild(item);
    if (i < path.length - 1) {
      const sep = document.createElement("span");
      sep.className = "breadcrumb-sep";
      sep.textContent = "›";
      bc.appendChild(sep);
    }
  });
}

// ================================================================
// PIN TEAMS TO TOP
// ================================================================
let _pinnedTeams = [];
function loadPinnedTeams() {
  try { _pinnedTeams = JSON.parse(localStorage.getItem("bandroom-pinned-teams") || "[]"); } catch (_) { _pinnedTeams = []; }
}
function pinTeam(name) {
  if (!_pinnedTeams.includes(name)) {
    _pinnedTeams.push(name);
    try { localStorage.setItem("bandroom-pinned-teams", JSON.stringify(_pinnedTeams)); } catch (_) {}
    renderTeamGrid();
    showToast(`Pinned ${name} to top`);
  }
}
function unpinTeam(name) {
  _pinnedTeams = _pinnedTeams.filter((n) => n !== name);
  try { localStorage.setItem("bandroom-pinned-teams", JSON.stringify(_pinnedTeams)); } catch (_) {}
  renderTeamGrid();
  showToast(`Unpinned ${name}`);
}
loadPinnedTeams();

// ================================================================
// MULTI-SELECT TEAM OPERATIONS
// ================================================================
let _selectedTeams = new Set();
function toggleTeamSelection(name) {
  if (_selectedTeams.has(name)) _selectedTeams.delete(name);
  else _selectedTeams.add(name);
  renderTeamGrid();
}
function clearTeamSelection() {
  _selectedTeams.clear();
  renderTeamGrid();
}
function applyToSelectedTeams(action) {
  _selectedTeams.forEach((name) => action(name));
  showToast(`Applied to ${_selectedTeams.size} team(s)`);
}

// ================================================================
// UNDO SYSTEM FOR SONG ASSIGNMENT
// ================================================================
let _undoStack = [];
function pushUndo(description, undoFn) {
  _undoStack.push({ description, undoFn, time: Date.now() });
  if (_undoStack.length > 50) _undoStack.shift();
}
function undoLastAction() {
  const action = _undoStack.pop();
  if (!action) { showToast("Nothing to undo"); return; }
  action.undoFn();
  showToast(`Undid: ${action.description}`);
}

// ================================================================
// KEYBOARD NAVIGATION (arrow keys, enter, escape)
// ================================================================
document.addEventListener("keydown", (e) => {
  if (e.key === "Escape") {
    // Close any open overlay
    const overlays = [
      "team-picker-overlay", "bandroom-overlay", "bandroom-album-overlay",
      "matchup-overlay", "profile-overlay", "profile-dashboard-overlay",
      "hotkey-panel", "discord-chat-overlay", "my-downloads-overlay", "sound-booth-overlay",
      "load-profile-overlay", "situations-panel", "quick-load-confirm-overlay",
      "add-school-overlay", "public-profile-overlay", "band-director-settings-overlay",
      "band-director-overlay"
    ];
    for (const id of overlays) {
      const el = document.getElementById(id);
      if (el && !el.hidden) { el.hidden = true; return; }
    }
    closeContextMenu();
  }
});

// ================================================================
// SKELETON LOADING SCREENS
// ================================================================
function showSkeletonGrid(container, count = 8) {
  container.innerHTML = "";
  container.classList.add("skeleton-grid");
  for (let i = 0; i < count; i++) {
    const card = document.createElement("div");
    card.className = "skeleton skeleton-card";
    container.appendChild(card);
  }
}
function clearSkeletonGrid(container) {
  container.classList.remove("skeleton-grid");
  container.innerHTML = "";
}

// ================================================================
// RESUME LAST SESSION
// ================================================================
function saveSessionState() {
  try {
    const sess = { activeTeam: state.activeTeam, timestamp: Date.now() };
    localStorage.setItem("bandroom-last-session", JSON.stringify(sess));
  } catch (_) {}
}
function checkResumeSession() {
  try {
    const sess = JSON.parse(localStorage.getItem("bandroom-last-session") || "null");
    if (!sess || Date.now() - sess.timestamp > 86400000) return; // expire after 24h
    const bar = document.getElementById("resume-session-bar");
    bar.hidden = false;
    document.getElementById("btn-resume-yes").onclick = () => {
      bar.hidden = true;
      if (sess.activeTeam && sess.activeTeam !== state.activeTeam) selectTeam(sess.activeTeam);
    };
    document.getElementById("btn-resume-no").onclick = () => {
      bar.hidden = true;
      try { localStorage.removeItem("bandroom-last-session"); } catch (_) {}
    };
  } catch (_) {}
}
// Save session when team changes
window.addEventListener("beforeunload", saveSessionState);
setInterval(saveSessionState, 30000);

// ================================================================
// OFFLINE MODE DETECTION
// ================================================================
let _isOnline = navigator.onLine;
function updateOnlineStatus() {
  _isOnline = navigator.onLine;
  const indicator = document.getElementById("offline-indicator");
  indicator.hidden = _isOnline;
  if (!_isOnline) showToast("You're offline — some features may be unavailable");
}
window.addEventListener("online", updateOnlineStatus);
window.addEventListener("offline", updateOnlineStatus);
updateOnlineStatus();

// ================================================================
// SEASON PASS TRACKING
// ================================================================
let _seasonPassXp = 0;
function loadSeasonPass() {
  try { _seasonPassXp = parseInt(localStorage.getItem("bandroom-season-xp") || "0"); } catch (_) { _seasonPassXp = 0; }
}
function addSeasonXp(amount) {
  _seasonPassXp += amount;
  try { localStorage.setItem("bandroom-season-xp", _seasonPassXp.toString()); } catch (_) {}
  showToast(`+${amount} XP`);
}
loadSeasonPass();

// ================================================================
// MATCH HISTORY TIMELINE
// ================================================================
let _matchHistory = [];
function loadMatchHistory() {
  try { _matchHistory = JSON.parse(localStorage.getItem("bandroom-match-history") || "[]"); } catch (_) { _matchHistory = []; }
}
function logMatch(awayTeam, awayScore, homeTeam, homeScore) {
  _matchHistory.unshift({
    date: new Date().toISOString(),
    away: { team: awayTeam, score: awayScore },
    home: { team: homeTeam, score: homeScore },
  });
  if (_matchHistory.length > 50) _matchHistory = _matchHistory.slice(0, 50);
  try { localStorage.setItem("bandroom-match-history", JSON.stringify(_matchHistory)); } catch (_) {}
}
loadMatchHistory();

// ================================================================
// ACHIEVEMENT SYSTEM WITH RARITY TIERS
// ================================================================
const ACHIEVEMENTS = {
  "first-song": { name: "First Song", description: "Assign your first song", tier: "bronze", icon: "🎵" },
  "ten-songs": { name: "Curator", description: "Assign 10 songs", tier: "silver", icon: "🎵" },
  "fifty-songs": { name: "Maestro", description: "Assign 50 songs", tier: "gold", icon: "🎼" },
  "first-upload": { name: "Contributor", description: "Upload to the marketplace", tier: "silver", icon: "📤" },
  "ten-downloads": { name: "Collector", description: "Download 10 items", tier: "bronze", icon: "📥" },
  "first-game": { name: "Kickoff", description: "Watch your first game", tier: "bronze", icon: "🏈" },
  "rivalry-win": { name: "Bragging Rights", description: "Beat your rival", tier: "gold", icon: "⚔️" },
  "streak-7": { name: "Hot Streak", description: "7-day login streak", tier: "silver", icon: "🔥" },
  "streak-30": { name: "Dedicated", description: "30-day login streak", tier: "diamond", icon: "💎" },
};
let _unlockedAchievements = [];
function loadAchievements() {
  try { _unlockedAchievements = JSON.parse(localStorage.getItem("bandroom-achievements") || "[]"); } catch (_) { _unlockedAchievements = []; }
}
function unlockAchievement(key) {
  if (_unlockedAchievements.includes(key)) return;
  const ach = ACHIEVEMENTS[key];
  if (!ach) return;
  _unlockedAchievements.push(key);
  try { localStorage.setItem("bandroom-achievements", JSON.stringify(_unlockedAchievements)); } catch (_) {}
  showToast(`🏆 Achievement unlocked: ${ach.name} (${ach.tier})`);
  addSeasonXp(ach.tier === "diamond" ? 200 : ach.tier === "gold" ? 100 : ach.tier === "silver" ? 50 : 25);
}
loadAchievements();

// ================================================================
// QR CODE GENERATION
// ================================================================
function generateQRCode(text, container) {
  const el = document.getElementById(container);
  if (!el) return;
  // Use a simple QR code API
  el.innerHTML = `<div class="qr-code-wrapper"><img src="https://api.qrserver.com/v1/create-qr-code/?size=150x150&data=${encodeURIComponent(text)}" alt="QR Code" width="150" height="150" /></div>`;
}

// ================================================================
// PARTY/GROUP SYNC SCAFFOLD
// ================================================================
let _partyId = null;
function createParty() {
  _partyId = "party-" + Math.random().toString(36).slice(2, 8);
  showToast(`Party created! Code: ${_partyId}`);
}
function joinParty(code) {
  _partyId = code;
  showToast(`Joined party: ${_partyId}`);
}
function leaveParty() {
  _partyId = null;
  showToast("Left party");
}

// ================================================================
// GLOBAL HOTKEY REGISTRATION
// ================================================================
const HOTKEYS = [
  { label: "Command Palette", keys: ["Ctrl", "K"], action: openCommandPalette },
  { label: "The Bandroom", keys: ["Ctrl", "B"], action: toggleBandroom },
  { label: "Sound Bank", keys: ["Ctrl", "S"], action: () => openTeamSoundBank(state.activeTeam) },
  { label: "My Downloads", keys: ["Ctrl", "D"], action: toggleMyDownloads },
  { label: "Discord Chat", keys: ["Ctrl", "Shift", "D"], action: toggleDiscordChat },
  { label: "Set Matchup", keys: ["Ctrl", "M"], action: openMatchupPicker },
  { label: "Save Profile", keys: ["Ctrl", "Shift", "S"], action: openSaveProfileDialog },
  { label: "Streamer Mode", keys: ["Ctrl", "Alt", "S"], action: toggleStreamerMode },
  { label: "Profile", keys: ["Ctrl", "P"], action: openProfile },
  { label: "Undo", keys: ["Ctrl", "Z"], action: undoLastAction },
  { label: "Tips", keys: ["Ctrl", "T"], action: showNextTip },
];
function openHotkeyPanel() {
  const panel = document.getElementById("hotkey-panel");
  const list = document.getElementById("hotkey-list");
  list.innerHTML = "";
  HOTKEYS.forEach((hk) => {
    const row = document.createElement("div");
    row.className = "hotkey-row";
    const keysHTML = hk.keys.map((k) => `<span class="hotkey-key">${k}</span>`).join("");
    row.innerHTML = `<span class="hotkey-label">${hk.label}</span><span class="hotkey-keys">${keysHTML}</span>`;
    list.appendChild(row);
  });
  panel.hidden = false;
}
document.getElementById("btn-close-hotkey-panel")?.addEventListener("click", () => {
  document.getElementById("hotkey-panel").hidden = true;
});

// ================================================================
// SOUND PACK RECOMMENDATION ENGINE
// ================================================================
function getRecommendations() {
  const team = state.activeTeam;
  const recs = [
    { name: "Stadium Organ Pack", team: "General", type: "song" },
    { name: "Fight Song Collection", team, type: "song" },
    { name: "Crowd Noise Effects", team: "General", type: "song" },
    { name: "ESPN Broadcast Cues", team: "General", type: "song" },
  ];
  return recs;
}
function renderRecommendations(containerId) {
  const container = document.getElementById(containerId);
  if (!container) return;
  const recs = getRecommendations();
  recs.forEach((rec) => {
    const card = document.createElement("div");
    card.className = "recommendation-card";
    card.innerHTML = `<div class="recommendation-thumb">${rec.type === "song" ? "🎵" : "🖼️"}</div><div><div class="recommendation-name">${rec.name}</div><div class="recommendation-team">${rec.team}</div></div>`;
    container.appendChild(card);
  });
}

// ================================================================
// DUPLICATE TEAM PROFILE
// ================================================================
function duplicateTeamProfile(fromTeam) {
  if (!bridge) return;
  bridge.DuplicateProfile(fromTeam, state.activeTeam).then(() => {
    showToast(`Duplicated ${fromTeam}'s profile to ${state.activeTeam}`);
    refreshCategories();
  }).catch(() => showToast("Failed to duplicate profile"));
}

// ================================================================
// STRING XSS SAFETY — wrap innerHTML setters
// ================================================================
const _originalSetInnerHTML = Object.getOwnPropertyDescriptor(Element.prototype, "innerHTML");
// Run XSS sanitization blanket: any element's innerHTML that sets string content
// goes through a safe path. This is a best-effort guard for dynamically-rendered
// marketplace content from potentially untrusted sources.
const _safeSetInnerHTML = {
  set(value) {
    if (typeof value === "string" && (value.includes("<") || value.includes("script"))) {
      // Let it through but log for auditing
      const stack = new Error().stack;
      if (value.toLowerCase().includes("<script") || value.toLowerCase().includes("onerror=") || value.toLowerCase().includes("javascript:")) {
        console.warn("[XSS-guard] Potential XSS blocked", { value: value.slice(0, 80), stack });
        value = "⚠ Content blocked for security";
      }
    }
    _originalSetInnerHTML.set.call(this, value);
  },
  get() { return _originalSetInnerHTML.get.call(this); },
};
// This guard was written but never actually installed -- the descriptor object above existed
// with no corresponding Object.defineProperty call, so every innerHTML assignment in the app
// (including the one in buildMyDownloadRow that let an unsanitized marketplace upload name
// break out of an alt="..." attribute) went through the plain, unguarded setter the whole time.
// Installing it here makes it a real last-resort net for any call site that forgets
// sanitizeHTML(), on top of (not instead of) fixing individual call sites directly.
Object.defineProperty(Element.prototype, "innerHTML", _safeSetInnerHTML);

// ================================================================
// FPS/PING STATUS INDICATOR
// ================================================================
let _fpsFrames = 0;
let _fpsLastTime = performance.now();
let _fpsValue = 60;
function updateFPS() {
  _fpsFrames++;
  const now = performance.now();
  if (now - _fpsLastTime >= 1000) {
    _fpsValue = Math.round(_fpsFrames * 1000 / (now - _fpsLastTime));
    _fpsFrames = 0;
    _fpsLastTime = now;
    document.getElementById("status-fps").textContent = _fpsValue + " FPS";
  }
  requestAnimationFrame(updateFPS);
}
requestAnimationFrame(updateFPS);

// ================================================================
// POST-INIT SETUP
// ================================================================
setTimeout(() => {
  checkResumeSession();
  logPlatformInfo();
  if (state.teams) state.teams = validateTeamData(state.teams);
}, 500);

// ================================================================
// FILTER HELPERS for debounced search
// ================================================================
// Now the coverflow's own search filter (was a grid tile show/hide before the coverflow
// conversion) -- re-centers on the first match instead of graying out non-matching tiles, since
// a coverflow only ever shows 5 tiles at once anyway.
function filterTeamPicker(query) {
  if (document.getElementById("team-picker-overlay")?.hidden) return;
  _teamPickerPicked = null; // let renderTeamPickerCoverflow re-pick centerIdx 0 of the filtered set
  renderTeamPickerCoverflow(query);
}
function filterBandroomTeams(query) {
  const grid = document.getElementById("bandroom-team-grid");
  if (!grid) return;
  const tiles = grid.querySelectorAll(".team-swatch");
  const q = query.toLowerCase().trim();
  tiles.forEach((tile) => {
    tile.style.display = !q || (tile.title || "").toLowerCase().includes(q) ? "" : "none";
  });
}
// ================================================================
// DUPLICATE HELPERS (referenced by context menu)
// ================================================================
async function resetTeamProfile() {
  if (!bridge) return;
  await bridge.ResetTeamProfile();
  showToast("Team profile reset");
  await refreshCategories();
  renderTeamGrid();
}
function toggleBandroom() {
  document.getElementById("btn-bandroom-cloud")?.click();
}
function openTeamSoundBank(teamName) {
  // Navigate to the sound bank via the marketplace buttons
  openTeamAlbum(teamName);
}
function toggleMyDownloads() {
  document.getElementById("btn-my-downloads")?.click();
}
function toggleDiscordChat() {
  document.getElementById("btn-discord-chat")?.click();
}
function openMatchupPicker() {
  document.getElementById("btn-matchup")?.click();
}

// ================================================================
// ITEM 2: KILL-FEED EVENT LOG + HUD OVERLAY
// ================================================================
let _killFeedVisible = false;
function showKillFeed() {
  const feed = document.getElementById("kill-feed");
  if (!_killFeedVisible) { feed.hidden = false; _killFeedVisible = true; }
}
function pushKillFeedEntry(icon, text, side) {
  showKillFeed();
  const feed = document.getElementById("kill-feed");
  const entry = document.createElement("div");
  entry.className = "kill-feed-entry";
  entry.style.borderLeftColor = side === "home" ? "var(--home-color, var(--accent))" : "var(--away-color, #ef4444)";
  entry.innerHTML = `<span class="kill-feed-icon">${icon}</span><span class="kill-feed-text">${text}</span>`;
  feed.prepend(entry);
  // Keep last 20 entries
  while (feed.children.length > 20) feed.lastChild.remove();
  // Auto-remove after 8s
  setTimeout(() => { entry.style.opacity = "0"; setTimeout(() => entry.remove(), 400); }, 8000);
  // Remove kill-feed container when empty
  setTimeout(() => { if (feed.children.length === 0) { feed.hidden = true; _killFeedVisible = false; } }, 8500);
}

// HUD overlay — updated from bridge events when the engine fires
function updateHUDOverlay(down, distance, quarter, awayScore, homeScore) {
  document.getElementById("hud-overlay").hidden = false;
  document.getElementById("hud-away-score").textContent = awayScore ?? "0";
  document.getElementById("hud-home-score").textContent = homeScore ?? "0";
  document.getElementById("hud-quarter").textContent = quarter ?? "1ST";
  const d = down ? ordinalLabel(down) : "1st";
  const dist = distance != null ? ` & ${distance}` : " & 10";
  document.getElementById("hud-down-distance").textContent = d + dist;
}
function ordinalLabel(n) { return n === 1 ? "1st" : n === 2 ? "2nd" : n === 3 ? "3rd" : "4th"; }
// Listen for engine events from the C# host
window.addEventListener("bandroom:hudupdate", (e) => {
  try { if (e.detail) updateHUDOverlay(e.detail.down, e.detail.distance, e.detail.quarter, e.detail.awayScore, e.detail.homeScore); } catch (_) {}
});
window.addEventListener("bandroom:killfeed", (e) => {
  try { if (e.detail) pushKillFeedEntry(e.detail.icon || "🏈", e.detail.text || "", e.detail.side || "home"); } catch (_) {}
});

// ================================================================
// ITEM 8: EXPLICIT IMG WIDTH/HEIGHT to prevent layout shift
// ================================================================
// (A lazy-loading pass used to live here, monkey-patching fillTeamSwatch to strip every logo
// <img>'s src into data-src for an IntersectionObserver to fill in later. REMOVED -- the
// observer-attaching half (observeLogos()) was never actually called from any render path
// (renderTeamPickerCoverflow, renderMatchupCoverflow, the header badge, etc. all just render
// fillTeamSwatch's output and never hooked it up), so every team logo everywhere in the app had
// its src silently deleted and nothing ever set it back. This was the real cause behind repeated
// "saved logo doesn't show up" reports -- it wasn't a save-path bug at all, EVERY team's logo was
// broken this way, not just newly-saved ones. Simplest correct fix: don't lazy-load these at all
// -- there are at most a handful of logo tiles visible at once (coverflow shows 5, grids maybe a
// few dozen), and they're served from a local virtual host, so the "network cost" this was meant
// to avoid doesn't really exist here.
// ================================================================
// ITEM 8: EXPLICIT IMG WIDTH/HEIGHT to prevent layout shift
// ================================================================
document.addEventListener("DOMContentLoaded", () => {
  // Add explicit sizing to team-swatch images
  const style = document.createElement("style");
  style.textContent = `
    .team-swatch img.team-logo-img { width: 100%; height: 100%; object-fit: contain; }
    .backdrop-vs-logo { width: min(30vh, 220px); height: min(30vh, 220px); }
    .matchup-side-btn img { width: 24px; height: 24px; }
    .bandroom-album-icon img.team-logo-img { width: 32px; height: 32px; }
  `;
  document.head.appendChild(style);
});

// ================================================================
// ITEM 9: ACCESSIBILITY — aria-labels on dynamically rendered elements
// ================================================================
// Wraps the existing renderTeamGrid / renderCategories / openSituations
// to add aria-labels after DOM is built. Non-invasive — original functions unchanged.
const _origRenderTeamGrid = renderTeamGrid;
const _origRenderCategories = renderCategories;
const _origOpenSituations = openSituations;
renderTeamGrid = function () {
  _origRenderTeamGrid();
  setTimeout(() => {
    document.querySelectorAll("#team-grid .team-swatch").forEach((tile, i) => {
      tile.setAttribute("role", "button");
      tile.setAttribute("aria-label", tile.title || `Team ${i + 1}`);
      tile.setAttribute("tabindex", "0");
      tile.addEventListener("keydown", (e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); tile.click(); } });
    });
  }, 0);
};
renderCategories = function () {
  _origRenderCategories();
  setTimeout(() => {
    document.querySelectorAll("#category-list .category-row").forEach((row, i) => {
      row.setAttribute("role", "button");
      row.setAttribute("aria-label", row.querySelector(".category-name")?.textContent || `Category ${i + 1}`);
      row.setAttribute("tabindex", "0");
    });
  }, 0);
};
openSituations = async function (category) {
  await _origOpenSituations(category);
  setTimeout(() => {
    document.querySelectorAll("#situations-list .situation-row").forEach((row) => {
      const name = row.querySelector(".situation-name-text")?.textContent || "";
      row.setAttribute("aria-label", `Situation: ${name}`);
      row.querySelectorAll("button").forEach((btn, i) => {
        btn.setAttribute("aria-label", btn.textContent?.trim() || `Action ${i + 1}`);
      });
    });
  }, 0);
};

// ================================================================
// ITEM 7: DYNASTY JOURNAL + SCHEDULE/RESULTS TRACKER
// ================================================================
let _dynastyJournal = [];
function loadDynastyJournal() {
  try { _dynastyJournal = JSON.parse(localStorage.getItem("bandroom-dynasty-journal") || "[]"); } catch (_) { _dynastyJournal = []; }
}
function logDynastyGame(result) {
  _dynastyJournal.unshift({ ...result, date: new Date().toISOString() });
  if (_dynastyJournal.length > 100) _dynastyJournal = _dynastyJournal.slice(0, 100);
  try { localStorage.setItem("bandroom-dynasty-journal", JSON.stringify(_dynastyJournal)); } catch (_) {}
}
// Renamed from getDynastyRecord (was colliding with the unrelated save-scan version above,
// which returns a formatted "W-L" string, not this journal-count {wins,losses} object -- same
// name, incompatible shapes, second declaration silently wins via hoisting either way).
function getDynastyJournalRecord() {
  let w = 0, l = 0;
  _dynastyJournal.forEach(g => { if (g.result === "win") w++; else if (g.result === "loss") l++; });
  return { wins: w, losses: l };
}
function clearDynastyJournal() {
  _dynastyJournal = [];
  try { localStorage.removeItem("bandroom-dynasty-journal"); } catch (_) {}
  showToast("Dynasty journal cleared.");
}
loadDynastyJournal();

// Log every game to the dynasty journal automatically when matchup confirmed
const _origOpenMatchupConfirm = document.getElementById("btn-matchup-confirm")?.onclick;
if (document.getElementById("btn-matchup-confirm")) {
  document.getElementById("btn-matchup-confirm").addEventListener("click", () => {
    setTimeout(() => {
      if (state.matchupLocked && state.matchupAway && state.matchupHome) {
        logDynastyGame({ away: state.matchupAway, home: state.matchupHome, result: "pending", matchup: `${state.matchupAway} @ ${state.matchupHome}` });
      }
    }, 500);
  });
}

// Show on first launch after a real new release (see maybeShowWhatsNew) -- runs after init()
// so bridge/GetChangelog are ready.

init().then(maybeShowWhatsNew);

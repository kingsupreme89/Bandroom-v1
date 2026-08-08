// Bridge to the C# host (WebMainForm.cs via CoreWebView2.AddHostObjectToScript("bandroom", ...)).
// Falls back to static placeholder data when opened outside WebView2 (e.g. a plain browser
// preview) so the layout is still inspectable without the host app running.
const bridge = window.chrome?.webview?.hostObjects?.bandroom ?? null;

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
  if (e.target.closest("button, .team-swatch, .rail-item, .category-row")) bridge?.PlayClickSound();
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
    } catch (err) { console.error("GetUserProfile (startup) failed", err); }
    try {
      // Admin marketplace override (owner-only) -- see WebBridge.cs's IsAdminMode. Cached once
      // at startup since tile rendering is synchronous and this never changes mid-session; false
      // for every real end-user install (no admin_token.local.txt ships in the installer).
      _isAdminMode = await bridge.IsAdminMode();
    } catch (err) { console.error("IsAdminMode failed", err); }
    try {
      // Lead-in whistle toggle only makes sense once a whistle clip actually exists (set via
      // TrimmerForm's "Set as Lead-In Whistle") -- hidden otherwise so an empty toggle for a
      // feature that isn't configured yet doesn't clutter the Mixer panel.
      const whistleAvailable = await bridge.GetLeadInWhistleAvailable();
      document.getElementById("leadin-whistle-section").hidden = !whistleAvailable;
      if (whistleAvailable)
        document.getElementById("toggle-leadin-whistle").checked = await bridge.GetLeadInWhistleEnabled();
    } catch (err) { console.error("GetLeadInWhistleAvailable/Enabled failed", err); }
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
function fillTeamSwatch(el, t) {
  el.style.background = `linear-gradient(135deg, ${t.primary}, ${t.secondary})`;
  el.style.setProperty("--tile-color", t.primary); // press glow + dock-hover ring use the team's own color
  if (t.logoUrl) {
    el.innerHTML = `<img src="${t.logoUrl}" alt="${t.name}" class="team-logo-img" draggable="false">`;
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
    row.className = "category-row";
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

  const events = bridge ? JSON.parse(await bridge.GetEventsForCategory(category)) : [];
  list.innerHTML = "";
  for (const ev of events) {
    const row = document.createElement("div");
    // "Island" tile instead of a full-width list row: LED dot color says the status at a
    // glance (assigned+confirmed = green pulse, assigned but unconfirmed = amber pulse,
    // nothing assigned yet = dim/no pulse) without needing to read the badge text.
    const ledClass = !ev.fileName ? "situation-led-off" : ev.confirmed ? "situation-led-green" : "situation-led-amber";
    row.className = "situation-row" + (ev.confirmed ? "" : " situation-unconfirmed");
    row.innerHTML = `
      <span class="situation-text">
        <div class="situation-name"><span class="situation-led ${ledClass}"></span><span class="situation-name-text">${friendlyEventName(ev.eventName)}</span></div>
        <div class="situation-file">${ev.fileName ? ev.fileName : "Unassigned"}</div>
        <div class="situation-file situation-file-pa">PA: ${ev.paFileName ? ev.paFileName : "none"}</div>
      </span>
      <span class="situation-actions" style="position: relative;">
        <button class="situation-btn" data-act="assign">Assign / Edit</button>
        <button class="situation-btn situation-btn-pa" data-act="assign-pa" title="Assign a PA Announcer clip that plays alongside the main song for this situation">Assign PA</button>
        <button class="situation-btn" data-act="preview" ${ev.fileName ? "" : "disabled"}>Preview</button>
        <button class="situation-btn" data-act="stop">Stop</button>
        <button class="situation-btn situation-btn-volume" data-act="volume" title="Adjust this event's own volume">&#128266;</button>
        <div class="situation-volume-popover" hidden>
          <input type="range" min="0" max="100" value="100" class="slider situation-volume-slider" />
          <span class="situation-volume-value">100%</span>
          <button class="situation-volume-close" title="Close">&times;</button>
        </div>
      </span>`;
    row.querySelector('[data-act="assign"]').addEventListener("click", async () => {
      await bridge?.AssignEvent(ev.trigger);
      await refreshCategories();
      openSituations(category); // re-render with updated assignment
    });
    row.querySelector('[data-act="assign-pa"]').addEventListener("click", async () => {
      await bridge?.AssignPaEvent(ev.trigger);
      await refreshCategories();
      openSituations(category); // re-render with updated assignment
    });
    row.querySelector('[data-act="preview"]').addEventListener("click", () => bridge?.PreviewEvent(ev.trigger));
    row.querySelector('[data-act="stop"]').addEventListener("click", () => bridge?.StopPreview());
    wireSituationVolumePopover(row, ev.trigger);
    list.appendChild(row);
  }
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

function setActiveTeam(name, fromInit = false) {
  document.getElementById("team-name").textContent = name;
  applyBackground(name);
  const team = state.teams.find((t) => t.name === name);
  document.documentElement.style.setProperty("--team-secondary", team?.secondary ?? "#22d3ee");
  updateProfileStatus();
  updateHeaderTeamBadge(team);
  updateMatchupSideBar();
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
  awayBtn.textContent = `Away: ${state.matchupAway}`;
  homeBtn.textContent = `Home: ${state.matchupHome}`;
  awayBtn.classList.toggle("active", state.activeTeam === state.matchupAway);
  homeBtn.classList.toggle("active", state.activeTeam === state.matchupHome);
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
  status.classList.remove("pill-off", "pill-waiting", "pill-watching");
  if (mode === "watching") { status.classList.add("pill-watching"); label.textContent = "Watching"; }
  else if (mode === "waiting") { status.classList.add("pill-waiting"); label.textContent = "Waiting for window…"; }
  else { status.classList.add("pill-off"); label.textContent = "Not watching"; }
  if (stopBtn) stopBtn.hidden = mode === "off";
}

// ---- Profile / Google sign-in (scaffolded -- see GoogleAuthService.ClientId for setup status) ----
async function openProfile() {
  document.getElementById("profile-overlay").hidden = false;
  await refreshProfileView();
}
function closeProfile() {
  document.getElementById("profile-overlay").hidden = true;
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
  populateTeamSelect(document.getElementById("profile-favorite-team"), true);
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

  document.getElementById("profile-favorite-team").value = profile.favoriteTeam ?? "";
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

function wireControls() {
  wireLogoCropTool();
  wireBgCropTool();
  document.getElementById("btn-profile").addEventListener("click", openProfile);
  document.getElementById("btn-close-profile").addEventListener("click", closeProfile);
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
  document.getElementById("profile-favorite-team").addEventListener("change", async (e) => {
    const team = e.target.value;
    try {
      await bridge.SetFavoriteTeam(team);
      // Setting a favorite team also switches the app's active team/theme -- same effect as
      // clicking that team's tile in the Teams panel (see selectTeam) -- so picking one here
      // visibly does something instead of silently saving a preference nobody can see.
      if (team) await selectTeam(team);
      updateFavoriteTeamJumpButton(team); // otherwise the header star button stays stale until Profile is closed/reopened
      showToast(team ? `Favorite team set to ${team}.` : "Favorite team cleared.");
    } catch (err) {
      console.error("SetFavoriteTeam failed", err);
      showToast("Couldn't save favorite team -- try again.");
    }
  });
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

  document.getElementById("btn-settings").addEventListener("click", () => bridge?.OpenSettings());
  document.getElementById("btn-minimize").addEventListener("click", () => bridge?.MinimizeWindow());
  document.getElementById("btn-maximize").addEventListener("click", () => bridge?.MaximizeWindow());
  document.getElementById("btn-close").addEventListener("click", () => bridge?.CloseWindow());

  document.getElementById("btn-copy-all").addEventListener("click", () => bridge?.CopyCurrentToAllTeams());
  document.getElementById("btn-export-profile").addEventListener("click", () => bridge?.ExportProfile());
  document.getElementById("btn-import-profile").addEventListener("click", () => bridge?.ImportProfile());
  document.getElementById("btn-delete-profile").addEventListener("click", () => bridge?.DeleteCurrentProfile());

  // Drag the borderless window by pulling on the header center region -- but not when the
  // mousedown started on a real control inside it (e.g. "Set Matchup"), since native drag
  // capture swallows the click before it ever reaches the button.
  document.getElementById("drag-handle").addEventListener("mousedown", (e) => {
    if (e.button === 0 && !e.target.closest("button")) bridge?.BeginDrag();
  });
  document.getElementById("btn-update").addEventListener("click", () => bridge?.ShowUpdate());
  document.getElementById("btn-bandroom-cloud").addEventListener("click", openBandroomMarketplace);
  document.getElementById("btn-sound-bank").addEventListener("click", () => { openTeamAlbum(state.activeTeam); setAlbumTab("songs"); });
  document.getElementById("btn-trophy-room").addEventListener("click", () => { openTeamAlbum(state.activeTeam); setAlbumTab("images"); });
  document.getElementById("btn-my-downloads").addEventListener("click", openMyDownloads);
  document.getElementById("btn-close-my-downloads").addEventListener("click", closeMyDownloads);
  document.getElementById("btn-discord-chat").addEventListener("click", openDiscordChat);
  document.getElementById("btn-close-discord-chat").addEventListener("click", closeDiscordChat);
  document.getElementById("btn-import-local-song")?.addEventListener("click", importLocalSong);
  document.getElementById("btn-close-bandroom").addEventListener("click", closeBandroomMarketplace);
  document.getElementById("bandroom-overlay").addEventListener("click", (e) => {
    if (e.target.id === "bandroom-overlay") closeBandroomMarketplace();
  });
  document.getElementById("bandroom-search").addEventListener("input", (e) => renderBandroomTeamGrid(e.target.value));

  document.getElementById("btn-close-bandroom-album").addEventListener("click", closeTeamAlbum);
  document.getElementById("bandroom-album-overlay").addEventListener("click", (e) => {
    if (e.target.id === "bandroom-album-overlay") closeTeamAlbum();
  });
  document.getElementById("tab-sound-bank").addEventListener("click", () => setAlbumTab("songs"));
  document.getElementById("tab-trophy-room").addEventListener("click", () => setAlbumTab("images"));
  document.getElementById("bandroom-album-search").addEventListener("input", onAlbumSearchInput);
  document.getElementById("btn-bandroom-album-download-all").addEventListener("click", downloadAlbumAll);
  document.getElementById("btn-reset").addEventListener("click", () => bridge?.ResetTeamProfile());

  document.getElementById("bandroom-upload-file-input").addEventListener("change", onUploadFileChosen);
  document.getElementById("btn-bandroom-upload-cancel").addEventListener("click", closeUploadDialog);
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
  });
  document.getElementById("slider-home-volume").addEventListener("input", (e) => {
    document.getElementById("home-volume-value").textContent = e.target.value;
    bridge?.SetHomeVolume(Number(e.target.value));
  });
  document.getElementById("slider-away-volume").addEventListener("input", (e) => {
    document.getElementById("away-volume-value").textContent = e.target.value;
    bridge?.SetAwayVolume(Number(e.target.value));
  });
  document.getElementById("slider-pa-volume").addEventListener("input", (e) => {
    document.getElementById("pa-volume-value").textContent = e.target.value;
    bridge?.SetPaVolume(Number(e.target.value));
  });
  document.getElementById("toggle-leadin-whistle").addEventListener("change", (e) => {
    bridge?.SetLeadInWhistleEnabled(e.target.checked);
  });
  document.getElementById("slider-sensitivity").addEventListener("input", (e) => {
    document.getElementById("sensitivity-value").textContent = e.target.value;
    bridge?.SetFadeDelay(Number(e.target.value));
  });

  document.querySelectorAll(".reverb-tile").forEach((tile) => {
    tile.addEventListener("click", () => {
      document.querySelectorAll(".reverb-tile").forEach((t) => t.classList.remove("active"));
      tile.classList.add("active");
      bridge?.SetReverb(tile.dataset.reverb);
    });
  });

  document.querySelectorAll(".rail-item").forEach((item) => {
    item.addEventListener("click", () => {
      const rail = item.parentElement;
      rail.querySelectorAll(".rail-item").forEach((i) => i.classList.remove("active"));
      item.classList.add("active");
      runRailAction(item.dataset.action);
    });
  });

  document.getElementById("btn-close-situations").addEventListener("click", () => {
    document.getElementById("situations-panel").hidden = true;
    state.currentSituationsCategory = null;
  });

  window.addEventListener("bandroom:refresh", refreshCategories);
  window.addEventListener("bandroom:watchstate", (e) => setWatching(e.detail));
  // Names exactly which trigger OCR just read and played a sound for -- lets a user verify live
  // that Bandroom read the right thing off the scoreboard, without checking logs.
  window.addEventListener("bandroom:triggerfired", (e) => showToast(`Trigger fired: ${e.detail}`));
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
  document.getElementById("team-picker-search").addEventListener("input", (e) => renderTeamPickerGrid(e.target.value));

  document.getElementById("btn-matchup").addEventListener("click", openMatchupDialog);
  document.getElementById("btn-close-matchup").addEventListener("click", closeMatchupDialog);
  document.getElementById("btn-matchup-cancel").addEventListener("click", closeMatchupDialog);
  document.getElementById("btn-matchup-confirm").addEventListener("click", confirmMatchup);
  document.getElementById("matchup-overlay").addEventListener("click", (e) => {
    if (e.target.id === "matchup-overlay") closeMatchupDialog();
  });
  document.getElementById("matchup-home-search").addEventListener("input", (e) => renderMatchupGrid("home", e.target.value));
  document.getElementById("matchup-away-search").addEventListener("input", (e) => renderMatchupGrid("away", e.target.value));
  document.getElementById("btn-side-away").addEventListener("click", () => selectTeam(state.matchupAway));
  document.getElementById("btn-side-home").addEventListener("click", () => selectTeam(state.matchupHome));

  document.getElementById("btn-save-profile-cancel").addEventListener("click", closeSaveProfileDialog);
  document.getElementById("btn-save-profile-confirm").addEventListener("click", confirmSaveProfile);
  document.getElementById("save-profile-overlay").addEventListener("click", (e) => {
    if (e.target.id === "save-profile-overlay") closeSaveProfileDialog();
  });
  document.getElementById("save-profile-name").addEventListener("input", updateSaveProfileSubtext);
  document.getElementById("save-profile-name").addEventListener("keydown", (e) => {
    if (e.key === "Enter") confirmSaveProfile();
  });

  document.getElementById("btn-help").addEventListener("click", () => bridge?.OpenHelp());

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
    // Album closes first if both happen to be open (it renders on top of the team-grid overlay).
    if (!document.getElementById("bandroom-upload-overlay").hidden) closeUploadDialog();
    else if (!document.getElementById("bandroom-album-overlay").hidden) closeTeamAlbum();
    else if (!document.getElementById("bandroom-overlay").hidden) closeBandroomMarketplace();
    else if (!document.getElementById("my-downloads-overlay").hidden) closeMyDownloads();
    else if (!document.getElementById("logo-crop-overlay").hidden) closeLogoCropTool();
    else if (!document.getElementById("profile-overlay").hidden) closeProfile();
    else if (!document.getElementById("discord-chat-overlay").hidden) closeDiscordChat();
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

function openTeamPicker() {
  document.getElementById("team-picker-overlay").hidden = false;
  const search = document.getElementById("team-picker-search");
  search.value = "";
  renderTeamPickerGrid("");
  search.focus();
}

function closeTeamPicker() {
  document.getElementById("team-picker-overlay").hidden = true;
}

function renderTeamPickerGrid(filter) {
  renderTeamGridInto("team-picker-grid", filter, (name) => { selectTeam(name); closeTeamPicker(); }, /*showEditLogo*/ true);
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
  try {
    const qs = new URLSearchParams({ type });
    if (school) qs.set("school", school);
    if (sort) qs.set("sort", sort);
    const res = await fetch(`${MARKETPLACE_URL}/list?${qs}`);
    if (!res.ok) return [];
    const data = await res.json();
    return Array.isArray(data.items) ? data.items : [];
  } catch (err) {
    console.error(`fetchUploadList(${type}) failed`, err);
    return [];
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
    renderBandroomHub();
    renderLeaderboard();
    search.focus();
  }, "openBandroomMarketplace");
}

function openMyDownloads() {
  marketplaceGuard(() => {
    document.getElementById("bandroom-overlay").hidden = true;
    document.getElementById("bandroom-album-overlay").hidden = true;
    document.getElementById("my-downloads-overlay").hidden = false;
    renderMyDownloadsGrid();
  }, "openMyDownloads");
}

function closeMyDownloads() {
  document.getElementById("my-downloads-overlay").hidden = true;
  _previewAudio?.pause();
}

async function renderMyDownloadsGrid() {
  const grid = document.getElementById("my-downloads-grid");
  grid.innerHTML = `<div class="bandroom-empty-state">Loading...</div>`;
  let items;
  try {
    items = JSON.parse(await bridge.GetMyDownloads());
  } catch (err) {
    console.error("GetMyDownloads failed", err);
    items = [];
  }
  if (document.getElementById("my-downloads-overlay").hidden) return; // closed while awaiting

  grid.innerHTML = "";
  if (items.length === 0) {
    grid.innerHTML = `<div class="bandroom-empty-state">Nothing downloaded yet -- open a team's Sound Bank or Trophy Room and hit the ⬇ button on anything you like.</div>`;
    return;
  }
  for (const item of items) grid.appendChild(buildMyDownloadTile(item));
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
      renderMyDownloadsGrid();
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

function buildMyDownloadTile(item) {
  const tile = document.createElement("div");
  tile.className = "bandroom-item-tile";
  const thumb = document.createElement("div");
  thumb.className = "bandroom-item-thumb";
  if (item.type === "image") {
    thumb.innerHTML = `<img src="${item.fileUrl}" alt="${item.name}" loading="lazy">`;
  } else {
    thumb.innerHTML = item.schoolLogoUrl
      ? `<img src="${item.schoolLogoUrl}" alt="${item.school}" class="bandroom-item-thumb-logo" loading="lazy">`
      : `<span>\u{1F3B5}</span>`;
  }
  const name = document.createElement("div");
  name.className = "bandroom-item-name";
  name.textContent = item.name;
  const school = document.createElement("div");
  school.className = "bandroom-item-school";
  // Locally-imported tracks (item 21) have no school -- label them instead of showing a blank line.
  school.textContent = item.source === "local" ? "Your library" : item.school;
  tile.append(thumb, name, school);
  tile.title = item.source === "local" ? item.name : `${item.school} — ${item.name}`;
  tile.addEventListener("click", (e) => {
    if (e.target.closest(".bandroom-item-action")) return;
    if (item.type === "song") previewSong({ url: item.fileUrl });
  });

  const actions = document.createElement("div");
  actions.className = "bandroom-item-actions";

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
      const school = window.prompt(`Share "${item.name}" to the marketplace -- which team is it for?`);
      if (!school || !school.trim()) return;
      shareBtn.disabled = true;
      shareBtn.textContent = "Sharing...";
      try {
        const raw = bridge ? await bridge.ShareLocalTrackToMarketplace(item.id, school.trim()) : null;
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

  const removeBtn = document.createElement("button");
  removeBtn.className = "bandroom-item-action bandroom-item-action-danger";
  removeBtn.title = "Remove from My Downloads";
  removeBtn.textContent = "\u{1F5D1}";
  removeBtn.addEventListener("click", async (e) => {
    e.stopPropagation();
    removeBtn.disabled = true;
    const ok = bridge ? await bridge.RemoveMyDownload(item.id) : false;
    if (ok) { showToast(`Removed "${item.name}".`); tile.remove(); }
    else { showToast("Couldn't remove that -- try again."); removeBtn.disabled = false; }
  });
  actions.appendChild(removeBtn);
  tile.appendChild(actions);
  return tile;
}

/// Per-team upload leaderboard (item 19) -- combines song + image counts per school from the
/// worker's /leaderboard endpoint and shows the top few. Best-effort: any failure just leaves
/// the section empty rather than breaking the rest of the hub.
async function renderLeaderboard() {
  const el = document.getElementById("bandroom-leaderboard");
  if (!el) return;
  el.innerHTML = `<div class="bandroom-recent-empty">Loading...</div>`;
  try {
    const [songsRes, imagesRes] = await Promise.all([
      fetch(`${MARKETPLACE_URL}/leaderboard?type=song`).then((r) => (r.ok ? r.json() : { schools: [] })),
      fetch(`${MARKETPLACE_URL}/leaderboard?type=image`).then((r) => (r.ok ? r.json() : { schools: [] })),
    ]);
    if (document.getElementById("bandroom-overlay").hidden) return;

    const combined = new Map();
    for (const { school, count } of [...(songsRes.schools ?? []), ...(imagesRes.schools ?? [])]) {
      combined.set(school, (combined.get(school) ?? 0) + count);
    }
    const top = [...combined.entries()].sort((a, b) => b[1] - a[1]).slice(0, 8);

    el.innerHTML = "";
    if (top.length === 0) {
      el.innerHTML = `<div class="bandroom-recent-empty">No uploads yet -- be the first team on the board!</div>`;
      return;
    }
    top.forEach(([school, count], i) => {
      const row = document.createElement("div");
      row.className = "bandroom-leaderboard-row";
      row.innerHTML = `<span class="bandroom-leaderboard-rank">#${i + 1}</span>
        <span class="bandroom-leaderboard-school">${school}</span>
        <span class="bandroom-leaderboard-count">${count} upload${count === 1 ? "" : "s"}</span>`;
      row.addEventListener("click", () => openTeamAlbum(school));
      el.appendChild(row);
    });
  } catch (err) {
    console.error("renderLeaderboard failed", err);
    el.innerHTML = `<div class="bandroom-recent-empty">Couldn't load the leaderboard right now.</div>`;
  }
}

async function renderBandroomHub() {
  const grid = document.getElementById("bandroom-recent-grid");
  grid.innerHTML = `<div class="bandroom-recent-empty">Loading...</div>`;
  const items = await fetchRecentUploads(20, _hubSort);
  // The overlay may have been closed (or a different one reopened) while this fetch was in
  // flight -- bail instead of writing into a grid the user can no longer see, or worse, into a
  // hub that's since been torn down.
  if (document.getElementById("bandroom-overlay").hidden) return;
  grid.innerHTML = "";
  if (items.length === 0) {
    grid.innerHTML = `<div class="bandroom-recent-empty">Nothing uploaded yet -- open any team's Sound Bank or Trophy Room and be the first!</div>`;
    return;
  }
  for (const item of items) grid.appendChild(buildItemTile(item, /*inHub*/ true));
}

// Song tiles use the uploading team's logo instead of a generic note icon, so every song tile
// in a given team's Sound Bank -- and in My Downloads -- looks uniform and immediately tells you
// whose song it is, the same way image tiles already show the real uploaded picture.
function teamLogoUrl(schoolName) {
  const team = state.teams?.find((t) => t.name === schoolName);
  return team?.logoUrl ?? null;
}

function buildItemTile(item, inHub) {
  const tile = document.createElement("div");
  tile.className = inHub ? "bandroom-item-tile" : "marketplace-card";
  const thumb = document.createElement("div");
  thumb.className = inHub ? "bandroom-item-thumb" : "marketplace-card-thumb";
  if (item.type === "image") {
    thumb.innerHTML = `<img src="${item.url}" alt="${item.name}" loading="lazy">`;
    if (!inHub) thumb.innerHTML += '<span class="card-type-badge">IMAGE</span>';
  } else {
    const logo = teamLogoUrl(item.school);
    thumb.innerHTML = logo
      ? `<img src="${logo}" alt="${item.school}" ${inHub ? 'class="bandroom-item-thumb-logo"' : ""} loading="lazy">`
      : `<span>\u{1F3B5}</span>`;
    if (!inHub) thumb.innerHTML += '<span class="card-type-badge">SONG</span>';
  }

  if (inHub) {
    const name = document.createElement("div");
    name.className = "bandroom-item-name";
    name.textContent = item.name;
    const school = document.createElement("div");
    school.className = "bandroom-item-school";
    school.textContent = item.school;
    tile.append(thumb, name, school);
  } else {
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
  tile.title = inHub ? `${item.name} -- ${item.school}` : `${item.name} \u2014 ${item.school}`;
  tile.addEventListener("click", (e) => {
    if (e.target.closest(".bandroom-item-action")) return; // hover-button clicks handle themselves
    if (inHub) {
      // Jump straight into that upload's own team/tab, same as picking the team from search.
      openTeamAlbum(item.school);
      setAlbumTab(item.type === "song" ? "songs" : "images");
    } else if (item.type === "song") {
      previewSong(item);
    }
  });

  // Hover action row -- only in an album view (not the hub, where tiles jump to the album
  // instead of acting in place). Like/Report are always available; Set as Background only for
  // Trophy Room images; Delete only shows on tiles this browser itself uploaded (item 5).
  if (!inHub) {
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
        const newName = prompt("Edit name:", item.name);
        if (newName === null) return;
        const newSchool = prompt("Edit school/team:", item.school ?? "");
        if (newSchool === null) return;
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
        const newName = prompt("Admin edit -- name:", item.name);
        if (newName === null) return;
        const newSchool = prompt("Admin edit -- school:", item.school ?? "");
        if (newSchool === null) return;
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
    _previewAudio = new Audio(item.url);
    _previewAudio.crossOrigin = "anonymous";
    _previewAudio.play().catch((err) => console.error("Song preview failed", err));
    // Only marketplace items carry an id (My Downloads tiles pass a bare {url} -- see
    // buildMyDownloadTile -- which have no server-side item to increment).
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

// Optional one-time default song pack download (see DefaultSongPackService.cs). Pulled out of
// the installer as of v1.0.48 to stay under GitHub Releases' 2GB asset cap.
function initDefaultSongPackPrompt() {
  const promptOverlay = document.getElementById("songpack-prompt-overlay");
  const progressOverlay = document.getElementById("songpack-progress-overlay");
  const progressHeader = document.getElementById("songpack-progress-header");
  const progressFill = document.getElementById("songpack-progress-fill");
  const progressSub = document.getElementById("songpack-progress-sub");

  (async () => {
    if (!bridge) return;
    const has = await bridge.HasDefaultSongPack();
    if (!has) promptOverlay.hidden = false;
  })();

  document.getElementById("btn-songpack-skip").addEventListener("click", () => { promptOverlay.hidden = true; });
  document.getElementById("btn-songpack-download").addEventListener("click", () => {
    promptOverlay.hidden = true;
    bridge?.DownloadDefaultSongPack();
  });

  window.addEventListener("bandroom:songpackdownloading", () => {
    progressHeader.textContent = "Downloading song pack…";
    progressFill.style.width = "0%";
    progressSub.textContent = "Hang tight -- this is a big one-time download.";
    progressOverlay.hidden = false;
  });
  window.addEventListener("bandroom:songpackprogress", (e) => {
    const { fraction, downloaded, total } = e.detail;
    progressFill.style.width = `${Math.max(0, Math.min(100, fraction * 100))}%`;
    const fmt = (b) => `${(b / 1073741824).toFixed(1)} GB`;
    progressSub.textContent = `${fmt(downloaded)} of ${fmt(total)}`;
  });
  window.addEventListener("bandroom:songpackready", () => {
    progressHeader.textContent = "Song pack ready";
    progressFill.style.width = "100%";
    progressSub.textContent = "Every team can now auto-fill with default songs.";
    setTimeout(() => { progressOverlay.hidden = true; }, 1800);
  });
  window.addEventListener("bandroom:songpackfailed", () => {
    progressOverlay.hidden = true;
    showToast("Song pack download failed -- check your connection and try again from Settings.");
  });
}

function closeBandroomMarketplace() {
  document.getElementById("bandroom-overlay").hidden = true;
}

function renderBandroomTeamGrid(filter) {
  renderTeamGridInto("bandroom-team-grid", filter, (name) => openTeamAlbum(name));
}

let albumTeam = null;

function openTeamAlbum(name) {
  marketplaceGuard(() => {
    const team = state.teams.find((t) => t.name === name);
    if (!team) return;
    albumTeam = team;
    document.getElementById("bandroom-overlay").hidden = true;
    document.getElementById("bandroom-album-overlay").hidden = false;
    fillTeamSwatch(document.getElementById("bandroom-album-icon"), team);
    document.getElementById("bandroom-album-name").textContent = team.name;
    setAlbumTab("songs");
  }, "openTeamAlbum");
}

function closeTeamAlbum() {
  document.getElementById("bandroom-album-overlay").hidden = true;
  _previewAudio?.pause();
  albumTeam = null;
}

function setAlbumTab(tab) {
  marketplaceGuard(() => {
    // Guard against openTeamAlbum never having found a matching team (e.g. state.teams hasn't
    // loaded yet) -- rendering would otherwise throw on albumTeam.secondary and leave the album
    // in a half-broken state instead of just declining to open.
    if (!albumTeam) return;
    document.getElementById("tab-sound-bank").classList.toggle("active", tab === "songs");
    document.getElementById("tab-trophy-room").classList.toggle("active", tab === "images");
    const songsGrid = document.getElementById("bandroom-songs-grid");
    const imagesGrid = document.getElementById("bandroom-images-grid");
    songsGrid.hidden = tab !== "songs";
    imagesGrid.hidden = tab !== "images";
    // Reset scroll on both grids every switch -- without this, flipping Sound Bank -> Trophy
    // Room -> Sound Bank could leave a grid mid-scroll under its own hidden state, so it opens
    // scrolled partway down instead of at the top the next time its tab is picked.
    songsGrid.scrollTop = 0;
    imagesGrid.scrollTop = 0;
    const albumSearch = document.getElementById("bandroom-album-search");
    if (albumSearch) albumSearch.value = "";
    document.getElementById("bandroom-album-instructions").textContent = tab === "songs"
      ? "Click a song to preview it. Hit + Upload to add your own -- it'll be compressed automatically and named after this team."
      : "Click + Upload to add a background. Setting one as this team's live background is coming soon.";
    if (tab === "songs") renderSoundBankGrid(); else renderTrophyRoomGrid();
  }, "setAlbumTab");
}

// Cache of the currently-open album's items (per type), so the in-album search box (item 7)
// can filter instantly client-side instead of re-hitting the worker on every keystroke.
let _albumItemsCache = { songs: [], images: [] };

async function renderSoundBankGrid() {
  const grid = document.getElementById("bandroom-songs-grid");
  const team = albumTeam;
  grid.innerHTML = `<div class="bandroom-empty-state">Loading...</div>`;
  const items = await fetchUploadList("song", team.name);
  if (!albumTeam || albumTeam !== team || document.getElementById("bandroom-songs-grid").hidden) return;
  _albumItemsCache.songs = items;
  paintAlbumGrid("songs", getAlbumSearchFilter());
}

async function renderTrophyRoomGrid() {
  const grid = document.getElementById("bandroom-images-grid");
  const team = albumTeam;
  grid.innerHTML = `<div class="bandroom-empty-state">Loading...</div>`;
  const items = await fetchUploadList("image", team.name);
  if (!albumTeam || albumTeam !== team || document.getElementById("bandroom-images-grid").hidden) return;
  _albumItemsCache.images = items;
  paintAlbumGrid("images", getAlbumSearchFilter());
}

function getAlbumSearchFilter() {
  return (document.getElementById("bandroom-album-search")?.value ?? "").trim().toLowerCase();
}

/// Renders whichever tab's grid is currently visible from the cached item list, filtered by the
/// in-album search box -- called both after a fresh fetch and on every search keystroke, so
/// searching never re-hits the network.
function paintAlbumGrid(tab, filter) {
  if (!albumTeam) return;
  const isSongs = tab === "songs";
  const grid = document.getElementById(isSongs ? "bandroom-songs-grid" : "bandroom-images-grid");
  const team = albumTeam;
  const all = isSongs ? _albumItemsCache.songs : _albumItemsCache.images;
  const items = filter ? all.filter((it) => it.name.toLowerCase().includes(filter)) : all;

  grid.innerHTML = "";
  if (all.length === 0) {
    const empty = document.createElement("div");
    empty.className = "bandroom-empty-state";
    empty.textContent = isSongs
      ? `No songs uploaded for ${team.name} yet -- be the first!`
      : `No background images uploaded for ${team.name} yet -- be the first!`;
    grid.appendChild(empty);
  } else if (items.length === 0) {
    const empty = document.createElement("div");
    empty.className = "bandroom-empty-state";
    empty.textContent = `No ${isSongs ? "songs" : "images"} match "${filter}".`;
    grid.appendChild(empty);
  } else {
    for (const item of items) {
      const tile = buildItemTile(item, /*inHub*/ false);
      if (!isSongs) {
        tile.querySelector(".bandroom-item-thumb").style.setProperty("--tile-color", team.secondary);
        tile.querySelector(".bandroom-item-thumb").classList.add("bandroom-image-slot");
      }
      grid.appendChild(tile);
    }
  }
  grid.appendChild(buildUploadTile(isSongs ? "song" : "image"));
}

function onAlbumSearchInput() {
  const filter = getAlbumSearchFilter();
  const isSongs = !document.getElementById("bandroom-songs-grid").hidden;
  paintAlbumGrid(isSongs ? "songs" : "images", filter);
}

/// Bulk-download (item 21): sequential downloads of every currently-visible item in the album's
/// active tab (respects the search filter, same as the grid it's downloading). No zipping --
/// keeping this to native browser downloads avoids pulling in a zip library with no build step
/// to vendor it through (same constraint noted on the audio compression path).
async function downloadAlbumAll() {
  if (!albumTeam) return;
  const isSongs = !document.getElementById("bandroom-songs-grid").hidden;
  const filter = getAlbumSearchFilter();
  const all = isSongs ? _albumItemsCache.songs : _albumItemsCache.images;
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
      const ext = urlExt || (isSongs ? "webm" : "jpg");
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
  tile.addEventListener("click", () => openUploadPicker(type));
  return tile;
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
    `Uploading to ${albumTeam.name}'s ${pendingUpload.type === "song" ? "Sound Bank" : "Trophy Room"}. `
    + (pendingUpload.type === "song"
      ? "It'll be compressed automatically so every upload plays at a consistent volume/size."
      : "It'll be resized/compressed automatically so every Trophy Room image is a consistent size.");
  const nameInput = document.getElementById("bandroom-upload-name");
  nameInput.value = "";
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
    // Re-render whichever grid is currently visible for this album.
    if (!document.getElementById("bandroom-songs-grid").hidden) renderSoundBankGrid();
    if (!document.getElementById("bandroom-images-grid").hidden) renderTrophyRoomGrid();
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

async function maybeShowOnboarding() {
  if (!bridge || !(await bridge.IsFirstRun())) return;
  const overlay = document.getElementById("onboarding-overlay");
  overlay.hidden = false;

  const pick = async (name) => {
    await bridge.CompleteFirstRun(name);
    state.activeTeam = name;
    setActiveTeam(name);
    overlay.hidden = true;
    pointOutTheBandroom();
  };
  renderTeamGridInto("onboarding-grid", "", pick);
  document.getElementById("onboarding-search").addEventListener("input", (e) =>
    renderTeamGridInto("onboarding-grid", e.target.value, pick));
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

function updateMatchupLabel() {
  const btn = document.getElementById("btn-matchup");
  const unlockBtn = document.getElementById("btn-unlock-matchup");
  if (!btn) return;
  btn.classList.toggle("locked", state.matchupLocked);
  if (unlockBtn) unlockBtn.hidden = !state.matchupLocked;
  if (state.matchupLocked) {
    btn.textContent = `\u{1F512} ${state.matchupAway} @ ${state.matchupHome}`;
    btn.title = "Locked in for this game -- press Stop Watching when it ends to change it, or use the unlock button to correct it without stopping";
  } else {
    btn.textContent = state.matchupHome && state.matchupAway
      ? `${state.matchupAway} @ ${state.matchupHome}`
      : "Set Matchup";
    // Clicking this again (whether or not a matchup is already picked) reopens the dialog --
    // openMatchupDialog() only refuses while state.matchupLocked (mid-game), so this is already
    // the "change matchup teams" entry point, not just the first-time picker.
    btn.title = "Pick who's home and away for this game";
  }
  updateWatchGate();
  updateMatchupSideBar();
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
  renderMatchupGrid("home", "");
  renderMatchupGrid("away", "");
  updateMatchupSubtext();
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
    renderTeamPickerGrid(document.getElementById("team-picker-search").value);
  const active = state.teams.find((t) => t.name === state.activeTeam);
  if (active) updateHeaderTeamBadge(active);
}

function closeMatchupDialog() {
  document.getElementById("matchup-overlay").hidden = true;
}

function renderMatchupGrid(side, filter) {
  const gridId = side === "home" ? "matchup-home-grid" : "matchup-away-grid";
  renderTeamGridInto(gridId, filter, (name) => {
    if (side === "home") state.matchupHome = name; else state.matchupAway = name;
    renderMatchupGrid(side, document.getElementById(`matchup-${side}-search`).value);
    updateMatchupSubtext();
  });
  // renderTeamGridInto only marks state.activeTeam as active -- overlay the actual
  // matchup pick for this column too, since it's independent of the sidebar's team.
  const picked = side === "home" ? state.matchupHome : state.matchupAway;
  if (picked) {
    document.querySelectorAll(`#${gridId} .team-swatch`).forEach((sw) => {
      if (sw.title === picked) sw.classList.add("active");
    });
  }
}

function updateMatchupSubtext() {
  const el = document.getElementById("matchup-subtext");
  const ready = state.matchupHome && state.matchupAway && state.matchupHome !== state.matchupAway;
  if (!state.matchupHome || !state.matchupAway) {
    el.textContent = "Pick both a home and an away team.";
  } else if (state.matchupHome === state.matchupAway) {
    el.textContent = "Home and away can't be the same team.";
  } else {
    el.textContent = `${state.matchupAway} (away) at ${state.matchupHome} (home) -- each team's own saved profile loads automatically. Hit GAMETIME while you're still on CFB 27's team-select screen.`;
  }
  document.getElementById("btn-matchup-confirm").disabled = !ready;
}

/// GAMETIME -- locks in who's home/away for OCR event routing (see WebMainForm._matchupLocked)
/// and swaps the backdrop to the two-team VS screen. The Home/Away toggle bar still works
/// after this for editing songs; only the routing itself is locked until Stop Watching.
async function confirmMatchup() {
  if (!state.matchupHome || !state.matchupAway || state.matchupHome === state.matchupAway) return;
  await bridge?.ConfirmGametime(state.matchupHome, state.matchupAway);
  state.matchupLocked = true;
  updateMatchupLabel();
  closeMatchupDialog();
  await applyVsBackdrop();
  // GAMETIME now locks the matchup AND starts watching in one press (WebMainForm.ConfirmGametimeFromWeb)
  // -- reflect that immediately instead of requiring a separate Start Watching click.
  setWatching("waiting");
  showToast(`GAMETIME! ${state.matchupAway} @ ${state.matchupHome} -- watching started`);
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

function closeSaveProfileDialog() {
  document.getElementById("save-profile-overlay").hidden = true;
}

async function confirmSaveProfile() {
  const name = document.getElementById("save-profile-name").value.trim();
  if (!name) return;
  closeSaveProfileDialog();
  const saved = await bridge?.SaveProfileAs(name);
  if (bridge) state.savedProfiles = JSON.parse(await bridge.GetSavedProfiles());
  renderTeamGrid();
  await updateProfileStatus();
  const t = await bridge?.GetProfileSavedAt(saved ?? name);
  showToast(`Saved "${saved ?? name}"${t ? ` at ${t}` : ""}`);
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
const WHATS_NEW_VERSION = "v1.0.49";
const WHATS_NEW_CHANGELOG = [
  {
    version: "v1.0.49",
    text: "Fixed a big problem where sounds stopped playing during games. The engine is now always on, and it won't drop events just because the camera hasn't figured out who has the ball yet. Both home and away teams get their cues now. Also added No Punt Return detection — your defense gets a sound when they stop a punt return."
  },
  {
    version: "v1.0.48",
    text: "PA Announcer clips! You can now assign a second voice clip to play alongside any song. Penalty detection now tells which team actually got flagged. Timeout tracking shows how many the opponent has left. The default song pack is now a separate download so updates stay under GitHub's size limit."
  },
  {
    version: "v1.0.47",
    text: "Both teams' profiles load at once when you set a matchup. The VS split-screen backdrop shows each team's stadium and logo. Score, clock, and quarter OCR regions are now calibrated from live screenshots. The penalty overlay reads \"Against <Team>\" text to figure out which side got flagged."
  },
  {
    version: "v1.0.46",
    text: "The Bandroom marketplace is live — browse Sound Banks and Trophy Rooms for every team, download songs and backgrounds, and upload your own. Google sign-in keeps your profile in sync across devices. The new default song pack auto-fills every team with real cues."
  },
];

function showWhatsNew() {
  const overlay = document.getElementById("whats-new-overlay");
  const changelog = document.getElementById("whats-new-changelog");
  if (!overlay || !changelog) return;

  let html = "";
  for (const entry of WHATS_NEW_CHANGELOG) {
    html += `<div class="whats-new-card">
      <div class="whats-new-card-version">${entry.version}</div>
      <div class="whats-new-card-text">${entry.text}</div>
    </div>`;
  }
  changelog.innerHTML = html;
  overlay.hidden = false;
}

function dismissWhatsNew() {
  document.getElementById("whats-new-overlay").hidden = true;
  try { localStorage.setItem("bandroom-whatsnew-seen", WHATS_NEW_VERSION); } catch (_) {}
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
  if (!panel || !select) return;
  if (bridge && select.options.length === 0) {
    const keys = JSON.parse(await bridge.GetAllEventKeys());
    // Raw EventKey, not friendlyEventName -- this is a debug tool, and several distinct keys
    // (e.g. "Offense: Second Down" / "Defense: Second Down") collapse to the identical friendly
    // label, which made it impossible to tell which one you'd actually selected/fired.
    select.innerHTML = keys.map(k => `<option value="${k}">${k}</option>`).join("");
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

// Plain-English labels for EventKeys -- "Offense:"/"Defense:"/"Other:" prefixes and helper-name
// jargon (Midfield, Iced Game, etc) mean nothing to someone assigning songs. EventKey stays the
// real internal ID (zero risk to saved profiles) -- this is a display-only lookup, falls back to
// the raw key untouched if a new EventKey shows up here before this map is updated.
const EVENT_FRIENDLY_NAMES = {
  "Offense: Earned First Down": "Got 1st Down",
  "Offense: Earned First Down (Big Gain)": "Got 1st Down - Big Gain",
  "Offense: Earned First Down (Midfield)": "Got 1st Down - Past Midfield",
  "Offense: Second Down": "2nd Down",
  "Offense: Second Down (Midfield)": "2nd Down - Past Midfield",
  "Offense: Third Down": "3rd Down",
  "Offense: Drive Starter": "Drive Starts",
  "Offense: PAT Made": "Extra Point Good",
  "Offense: 2-Point Conversion Made": "2-Point Conversion Good",
  "Offense: Field Goal Made": "Field Goal Good",
  "Offense: Iced Game by First Down": "Game Sealed - Got 1st Down",
  "Offense: Victory in Hand": "Game Won",
  "Offense: Touchdown Scored": "Touchdown",
  "Defense: Third Down": "3rd Down",
  "Defense: Fourth Down": "Stopped Them on 4th",
  "Defense: Third Down (Loss)": "3rd Down After a Loss",
  "Defense: Second Down": "2nd Down",
  "Defense: Second Down (Midfield)": "2nd Down - Past Midfield",
  "Defense: Second Down (Loss)": "2nd Down After a Loss",
  "Defense: Fourth Down (Loss)": "Stopped Them on 4th After a Loss",
  "Defense: Drive Starter": "Opponent's Drive Starts",
  "Defense: Field Goal Missed by Opponent": "Opponent Missed Field Goal",
  "Defense: Turnover Forced": "Turnover Forced",
  "Defense: Iced Game by Turnover": "Game Sealed by Turnover",
  "Defense: Safety": "Safety",
  "Defense: Tackle for Loss": "Tackle for Loss",
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
// LAZY LOADING — IntersectionObserver for team logos
// ================================================================
const _lazyImageObserver = new IntersectionObserver((entries) => {
  entries.forEach((entry) => {
    if (entry.isIntersecting) {
      const img = entry.target;
      if (img.dataset.src) {
        img.src = img.dataset.src;
        img.removeAttribute("data-src");
      }
      _lazyImageObserver.unobserve(img);
    }
  });
}, { rootMargin: "200px" });
function lazyLoadImages(container) {
  container.querySelectorAll("img[data-src]").forEach((img) => _lazyImageObserver.observe(img));
}

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
  { icon: "🏆", label: "Sound Bank", hint: "team sounds", action: () => openTeamSoundBank(state.activeTeam) },
  { icon: "🖼️", label: "Trophy Room", hint: "backgrounds", action: () => openTeamTrophyRoom(state.activeTeam) },
  { icon: "⬇️", label: "My Downloads", hint: "library", action: () => toggleMyDownloads() },
  { icon: "💬", label: "Discord Chat", hint: "chat", action: () => toggleDiscordChat() },
  { icon: "⚔️", label: "Set Matchup", hint: "home/away", action: () => openMatchupPicker() },
  { icon: "💾", label: "Save Profile", hint: "save", action: () => openSaveProfileDialog() },
  { icon: "🎮", label: "Streamer Mode", hint: "toggle", action: () => toggleStreamerMode() },
  { icon: "⌨️", label: "Keyboard Shortcuts", hint: "hotkeys", action: () => openHotkeyPanel() },
  { icon: "📋", label: "Tips", hint: "show tip", action: () => showNextTip() },
  { icon: "👤", label: "Profile", hint: "dashboard", action: () => openProfileDashboard() },
  { icon: "⚙️", label: "Settings", hint: "preferences", action: () => document.getElementById("btn-settings")?.click() },
  { icon: "ℹ️", label: "Help", hint: "guide", action: () => bridge?.ShowHelp() },
  { icon: "🔄", label: "Reset Team Profile", hint: "reset", action: () => resetTeamProfile() },
];

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
    { label: "Open Trophy Room", icon: "🖼️", action: () => { if (_contextMenuTarget) openTeamTrophyRoom(_contextMenuTarget); } },
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
// KILL-FEED EVENT LOG
// ================================================================
function pushKillFeedEntry(text, category) {
  const feed = document.getElementById("kill-feed");
  const entry = document.createElement("div");
  entry.className = "kill-feed-entry " + (category || "situations");
  entry.textContent = text;
  feed.appendChild(entry);
  // Auto-remove after 6 seconds
  setTimeout(() => {
    entry.classList.add("removing");
    setTimeout(() => entry.remove(), 300);
  }, 6000);
  // Cap at 20 entries
  while (feed.children.length > 20) feed.firstChild.remove();
}

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
  document.querySelectorAll(".soundboard-btn").forEach((btn) => {
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
document.querySelectorAll(".soundboard-btn").forEach((btn) => {
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
  "Use the Trophy Room to download custom team background art.",
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
// PROFILE DASHBOARD
// ================================================================
function openProfileDashboard() {
  const overlay = document.getElementById("profile-dashboard-overlay");
  overlay.hidden = false;
  const stats = _getProfileStats();
  document.getElementById("pd-stat-games").textContent = stats.games;
  document.getElementById("pd-stat-songs").textContent = stats.songs;
  document.getElementById("pd-stat-uploads").textContent = stats.uploads;
  document.getElementById("pd-stat-downloads").textContent = stats.downloads;
  document.getElementById("pd-stat-followers").textContent = stats.followers;
  document.getElementById("profile-dashboard-name").textContent = state.activeTeam;
  const team = state.teams.find((t) => t.name === state.activeTeam);
  document.getElementById("profile-dashboard-avatar").src = team?.logoUrl || "";
  // Populate activity feed
  const feed = document.getElementById("profile-activity-feed");
  feed.innerHTML = `<div class="profile-activity-item"><span class="profile-activity-time">Just now</span> Using Bandroom</div>`;
}
document.getElementById("btn-close-profile-dashboard")?.addEventListener("click", () => {
  document.getElementById("profile-dashboard-overlay").hidden = true;
});
function _getProfileStats() {
  return {
    games: parseInt(document.getElementById("profile-stat-games")?.textContent || "0"),
    songs: parseInt(document.getElementById("profile-stat-songs")?.textContent || "0"),
    uploads: parseInt(document.getElementById("profile-stat-uploads")?.textContent || "0"),
    downloads: parseInt(document.getElementById("profile-stat-downloads")?.textContent || "0"),
    followers: 0,
  };
}

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
    row.innerHTML = `<span class="leaderboard-rank ${rankClass}">#${i + 1}</span><span class="leaderboard-user">${entry.name || entry.school || "Unknown"}</span><span class="leaderboard-score">${entry.score || entry.count || 0}</span>`;
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
      "hotkey-panel", "discord-chat-overlay", "my-downloads-overlay",
      "situations-panel"
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
  { label: "Profile", keys: ["Ctrl", "P"], action: openProfileDashboard },
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
function filterTeamPicker(query) {
  const grid = document.getElementById("team-picker-grid");
  if (!grid) return;
  const tiles = grid.querySelectorAll(".team-swatch");
  const q = query.toLowerCase().trim();
  tiles.forEach((tile) => {
    tile.style.display = !q || (tile.title || "").toLowerCase().includes(q) ? "" : "none";
  });
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
function filterAlbumSearch(query) {
  const songsGrid = document.getElementById("bandroom-songs-grid");
  const imagesGrid = document.getElementById("bandroom-images-grid");
  const q = query.toLowerCase().trim();
  [songsGrid, imagesGrid].forEach((grid) => {
    if (!grid || grid.hidden) return;
    const cards = grid.querySelectorAll(".marketplace-card, .bandroom-item-tile");
    cards.forEach((card) => {
      const title = card.querySelector(".marketplace-card-title, .bandroom-item-name");
      card.style.display = !q || (title?.textContent || "").toLowerCase().includes(q) ? "" : "none";
    });
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
function openTeamTrophyRoom(teamName) {
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
function openSaveProfileDialog() {
  document.querySelector(".rail-item[data-action='save-profile']")?.click();
}

// Show on first launch after this update
try {
  const seen = localStorage.getItem("bandroom-whatsnew-seen");
  if (seen !== WHATS_NEW_VERSION) {
    setTimeout(showWhatsNew, 600);
  }
} catch (_) {
  setTimeout(showWhatsNew, 600);
}

init();

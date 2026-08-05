// Bridge to the C# host (WebMainForm.cs via CoreWebView2.AddHostObjectToScript("bandroom", ...)).
// Falls back to static placeholder data when opened outside WebView2 (e.g. a plain browser
// preview) so the layout is still inspectable without the host app running.
const bridge = window.chrome?.webview?.hostObjects?.bandroom ?? null;

// The Bandroom community marketplace worker (cloudflare-marketplace/worker.js) -- R2 for files,
// KV for name/school metadata. See that file for the exact /upload, /list, /file contract.
const MARKETPLACE_URL = "https://bandroom-marketplace.bandroom.workers.dev";

const categoryColors = {
  Downs: "#2f6f78",
  Scoring: "#2f7d55",
  Turnovers: "#7a6a2a",
  "Special Teams": "#5c4fa0",
  Penalties: "#7a3a3a",
  Hype: "#2f6f78",
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
  } else {
    state.teams = [{ name: "General", primary: "#22d3ee", secondary: "#22d3ee" }];
    state.categories = [
      { name: "Downs", assigned: 7, total: 17 },
      { name: "Scoring", assigned: 0, total: 6 },
      { name: "Turnovers", assigned: 0, total: 2 },
      { name: "Special Teams", assigned: 1, total: 6 },
      { name: "Penalties", assigned: 0, total: 1 },
      { name: "Hype", assigned: 0, total: 7 },
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
    const items = await fetchRecentUploads(8);
    const el = document.getElementById("ticker-text");
    if (items.length > 0) {
      el.textContent = items
        .map((it) => `${it.name} (${it.type === "song" ? "song" : "background"}) uploaded by ${it.school}`)
        .join("      •      ");
    }
  } catch (err) {
    console.error("pollTickerActivity failed", err);
  }
  setTimeout(pollTickerActivity, 60000);
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
        <div class="situation-name"><span class="situation-led ${ledClass}"></span>${ev.eventName}${ev.confirmed ? "" : ' <span class="situation-badge" title="Wired but not yet confirmed working in a live game">not yet confirmed</span>'}</div>
        <div class="situation-file">${ev.fileName ? ev.fileName : "Unassigned"}</div>
      </span>
      <span class="situation-actions">
        <button class="situation-btn" data-act="assign">Assign / Edit</button>
        <button class="situation-btn" data-act="preview" ${ev.fileName ? "" : "disabled"}>Preview</button>
        <button class="situation-btn" data-act="stop">Stop</button>
      </span>`;
    row.querySelector('[data-act="assign"]').addEventListener("click", async () => {
      await bridge?.AssignEvent(ev.trigger);
      await refreshCategories();
      openSituations(category); // re-render with updated assignment
    });
    row.querySelector('[data-act="preview"]').addEventListener("click", () => bridge?.PreviewEvent(ev.trigger));
    row.querySelector('[data-act="stop"]').addEventListener("click", () => bridge?.StopPreview());
    list.appendChild(row);
  }
}

async function selectTeam(name) {
  if (name === state.activeTeam) return;
  state.activeTeam = name;
  if (bridge) await bridge.SelectTeam(name);
  setActiveTeam(name);
  renderTeamGrid();
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
  const btn = document.getElementById("btn-watch");
  const label = document.getElementById("watch-label");
  btn.classList.remove("pill-off", "pill-waiting", "pill-watching");
  if (mode === "watching") { btn.classList.add("pill-watching"); label.textContent = "Watching"; }
  else if (mode === "waiting") { btn.classList.add("pill-waiting"); label.textContent = "Waiting for window…"; }
  else { btn.classList.add("pill-off"); label.textContent = "Start Watching"; }
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
  if (user.signedIn) {
    document.getElementById("profile-name").textContent = user.name ?? "";
    document.getElementById("profile-email").textContent = user.email ?? "";
    const avatar = document.getElementById("profile-avatar");
    if (user.picture) avatar.src = user.picture; else avatar.removeAttribute("src");
  }
}

function wireControls() {
  wireLogoCropTool();
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
  document.getElementById("btn-watch").addEventListener("click", async () => {
    if (!(state.matchupHome && state.matchupAway)) {
      showToast("Set Matchup first — pick both teams to unlock watching.");
      return;
    }
    try {
      const next = await bridge?.ToggleWatching();
      if (next === "no-matchup") {
        alert("Set Matchup first — Bandroom needs to know both teams' colors before it can watch the game.");
        return;
      }
      setWatching(next ?? (state.watching === "off" ? "watching" : "off"));
    } catch (err) {
      console.error("ToggleWatching failed", err);
      showToast("Couldn't toggle watching -- try again.");
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
async function fetchUploadList(type, school) {
  try {
    const qs = new URLSearchParams({ type });
    if (school) qs.set("school", school);
    const res = await fetch(`${MARKETPLACE_URL}/list?${qs}`);
    if (!res.ok) return [];
    const data = await res.json();
    return Array.isArray(data.items) ? data.items : [];
  } catch (err) {
    console.error(`fetchUploadList(${type}) failed`, err);
    return [];
  }
}

async function fetchRecentUploads(limit) {
  const [songs, images] = await Promise.all([fetchUploadList("song"), fetchUploadList("image")]);
  return [...songs, ...images]
    .sort((a, b) => (a.uploadedAt < b.uploadedAt ? 1 : -1))
    .slice(0, limit);
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

async function downloadMarketplaceItem(item) {
  try {
    const raw = await bridge.DownloadMarketplaceItem(item.type, item.name, item.school, item.url);
    const result = JSON.parse(raw);
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

function openBandroomMarketplace() {
  marketplaceGuard(() => {
    document.getElementById("bandroom-overlay").hidden = false;
    const search = document.getElementById("bandroom-search");
    search.value = "";
    renderBandroomTeamGrid("");
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
  school.textContent = item.school;
  tile.append(thumb, name, school);
  tile.title = `${item.school} — ${item.name}`;
  tile.addEventListener("click", (e) => {
    if (e.target.closest(".bandroom-item-action")) return;
    if (item.type === "song") previewSong({ url: item.fileUrl });
  });

  const actions = document.createElement("div");
  actions.className = "bandroom-item-actions";
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
  const items = await fetchRecentUploads(20);
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
  tile.className = "bandroom-item-tile";
  const thumb = document.createElement("div");
  thumb.className = "bandroom-item-thumb";
  if (item.type === "image") {
    thumb.innerHTML = `<img src="${item.url}" alt="${item.name}" loading="lazy">`;
  } else {
    const logo = teamLogoUrl(item.school);
    thumb.innerHTML = logo
      ? `<img src="${logo}" alt="${item.school}" class="bandroom-item-thumb-logo" loading="lazy">`
      : `<span>\u{1F3B5}</span>`; // no logo on file for this team -- fall back to a musical note
  }
  const name = document.createElement("div");
  name.className = "bandroom-item-name";
  name.textContent = item.name;
  const school = document.createElement("div");
  school.className = "bandroom-item-school";
  school.textContent = item.school;
  tile.append(thumb, name, school);
  tile.title = `${item.name} -- ${item.school}`;
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

    tile.appendChild(actions);
  }

  return tile;
}

let _previewAudio = null;
function previewSong(item) {
  try {
    _previewAudio?.pause();
    _previewAudio = new Audio(item.url);
    _previewAudio.play().catch((err) => console.error("Song preview failed", err));
  } catch (err) {
    console.error("Song preview failed", err);
  }
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
  if (!btn) return;
  btn.classList.toggle("locked", state.matchupLocked);
  if (state.matchupLocked) {
    btn.textContent = `\u{1F512} ${state.matchupAway} @ ${state.matchupHome}`;
    btn.title = "Locked in for this game -- press Stop Watching when it ends to change it";
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
  const btn = document.getElementById("btn-watch");
  if (!btn) return;
  const ready = !!(state.matchupHome && state.matchupAway);
  btn.disabled = !ready;
  btn.title = ready ? "Toggle watching" : "Set Matchup first to unlock watching";
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
    if (!_logoCropImg || !_logoCropTeam) return;
    const confirmBtn = document.getElementById("btn-logo-crop-confirm");
    confirmBtn.disabled = true;
    confirmBtn.textContent = "Saving...";
    try {
      const dataUrl = canvas.toDataURL("image/png");
      const base64 = dataUrl.split(",")[1];
      const ok = bridge ? await bridge.SaveCustomTeamLogo(_logoCropTeam, base64) : false;
      if (ok) {
        showToast(`Saved a new logo for ${_logoCropTeam}.`);
        await refreshTeamsAfterLogoChange();
        closeLogoCropTool();
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
  showToast(`GAMETIME! ${state.matchupAway} @ ${state.matchupHome}`);
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

init();

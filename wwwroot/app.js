// Bridge to the C# host (WebMainForm.cs via CoreWebView2.AddHostObjectToScript("bandroom", ...)).
// Falls back to static placeholder data when opened outside WebView2 (e.g. a plain browser
// preview) so the layout is still inspectable without the host app running.
const bridge = window.chrome?.webview?.hostObjects?.bandroom ?? null;

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
}

async function pollUserCount() {
  const el = document.getElementById("ticker-text");
  const count = bridge ? await bridge.GetActiveUserCount() : -1;
  el.textContent = count < 0
    ? "-- band members online"
    : `${count} band member${count === 1 ? "" : "s"} online`;
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
  state.watching = mode;
  const btn = document.getElementById("btn-watch");
  const label = document.getElementById("watch-label");
  btn.classList.remove("pill-off", "pill-waiting", "pill-watching");
  if (mode === "watching") { btn.classList.add("pill-watching"); label.textContent = "Watching"; }
  else if (mode === "waiting") { btn.classList.add("pill-waiting"); label.textContent = "Waiting for window…"; }
  else { btn.classList.add("pill-off"); label.textContent = "Start Watching"; }
}

function wireControls() {
  document.getElementById("btn-watch").addEventListener("click", async () => {
    const next = await bridge?.ToggleWatching();
    if (next === "no-matchup") {
      alert("Set Matchup first — Bandroom needs to know both teams' colors before it can watch the game.");
      return;
    }
    setWatching(next ?? (state.watching === "off" ? "watching" : "off"));
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
  document.getElementById("btn-reset").addEventListener("click", () => bridge?.ResetTeamProfile());

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
    if (!document.getElementById("bandroom-album-overlay").hidden) closeTeamAlbum();
    else if (!document.getElementById("bandroom-overlay").hidden) closeBandroomMarketplace();
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
  renderTeamGridInto("team-picker-grid", filter, (name) => { selectTeam(name); closeTeamPicker(); });
}

function renderTeamGridInto(gridId, filter, onPick) {
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
    grid.appendChild(sw);
  }
  squareUpTiles(grid);
}

function openBandroomMarketplace() {
  document.getElementById("bandroom-overlay").hidden = false;
  const search = document.getElementById("bandroom-search");
  search.value = "";
  renderBandroomTeamGrid("");
  search.focus();
}

function closeBandroomMarketplace() {
  document.getElementById("bandroom-overlay").hidden = true;
}

function renderBandroomTeamGrid(filter) {
  renderTeamGridInto("bandroom-team-grid", filter, (name) => openTeamAlbum(name));
}

let albumTeam = null;

function openTeamAlbum(name) {
  const team = state.teams.find((t) => t.name === name);
  if (!team) return;
  albumTeam = team;
  document.getElementById("bandroom-overlay").hidden = true;
  document.getElementById("bandroom-album-overlay").hidden = false;
  fillTeamSwatch(document.getElementById("bandroom-album-icon"), team);
  document.getElementById("bandroom-album-name").textContent = team.name;
  setAlbumTab("songs");
}

function closeTeamAlbum() {
  document.getElementById("bandroom-album-overlay").hidden = true;
  albumTeam = null;
}

function setAlbumTab(tab) {
  document.getElementById("tab-sound-bank").classList.toggle("active", tab === "songs");
  document.getElementById("tab-trophy-room").classList.toggle("active", tab === "images");
  document.getElementById("bandroom-songs-grid").hidden = tab !== "songs";
  document.getElementById("bandroom-images-grid").hidden = tab !== "images";
  if (tab === "songs") renderSoundBankGrid(); else renderTrophyRoomGrid();
}

// Sound Bank: fixed 6x5 grid (30 slots). Community upload backend doesn't exist yet -- every
// slot renders as an empty "+ Upload Song" tile for now, labeled clearly, rather than faking
// data. Once uploads are wired up, real entries (song name + school name, per the upload
// prompt spec) replace the empty slots here.
function renderSoundBankGrid() {
  const grid = document.getElementById("bandroom-songs-grid");
  grid.innerHTML = "";
  for (let i = 0; i < 30; i++) {
    const tile = document.createElement("div");
    tile.className = "bandroom-slot bandroom-song-slot";
    tile.innerHTML = `<span class="bandroom-slot-plus">+</span><span class="bandroom-slot-label">Upload Song</span>`;
    tile.title = `${albumTeam.name} — upload a song`;
    tile.addEventListener("click", () => alert("Community sound uploads are coming soon!"));
    grid.appendChild(tile);
  }
}

// Trophy Room: 5x5 grid, scrolls if a team ever has more than fits (scrolling handled by CSS
// overflow, not JS). Each filled tile would show the team's glowing pulse outline (same
// technique as .situation-row's team-secondary glow) with a "Set as team background" option --
// not wired yet since there's no real image data to show.
function renderTrophyRoomGrid() {
  const grid = document.getElementById("bandroom-images-grid");
  grid.innerHTML = "";
  for (let i = 0; i < 25; i++) {
    const tile = document.createElement("div");
    tile.className = "bandroom-slot bandroom-image-slot";
    tile.style.setProperty("--tile-color", albumTeam.secondary);
    tile.innerHTML = `<span class="bandroom-slot-plus">+</span><span class="bandroom-slot-label">Upload Image</span>`;
    tile.title = `${albumTeam.name} — upload a background image`;
    tile.addEventListener("click", () => alert("Community background uploads are coming soon!"));
    grid.appendChild(tile);
  }
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
  };
  renderTeamGridInto("onboarding-grid", "", pick);
  document.getElementById("onboarding-search").addEventListener("input", (e) =>
    renderTeamGridInto("onboarding-grid", e.target.value, pick));
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
    btn.title = "Pick who's home and away for this game";
  }
  updateMatchupSideBar();
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

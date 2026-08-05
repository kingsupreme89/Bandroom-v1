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

let state = {
  teams: [],
  categories: [],
  savedProfiles: [],
  activeTeam: "General",
  watching: "off", // off | waiting | watching
  matchupHome: null,
  matchupAway: null,
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
  maybeShowOnboarding();
  pollUserCount();
}

async function pollUserCount() {
  if (!bridge) return;
  const el = document.getElementById("user-count");
  const count = await bridge.GetActiveUserCount();
  if (count < 0) {
    el.hidden = true;
  } else {
    el.hidden = false;
    el.textContent = `· ${count} watching now`;
  }
  setTimeout(pollUserCount, 30000);
}

function renderTeamGrid() {
  const grid = document.getElementById("team-grid");
  grid.innerHTML = "";
  for (const t of state.teams) {
    const sw = document.createElement("div");
    const configured = state.savedProfiles.includes(t.name);
    sw.className = "team-swatch" + (t.name === state.activeTeam ? " active" : "") + (configured ? " configured" : "");
    sw.title = t.name + (configured ? " ✓" : "");
    sw.style.background = `linear-gradient(135deg, ${t.primary}, ${t.secondary})`;
    sw.textContent = t.initials ?? "";
    sw.addEventListener("click", () => selectTeam(t.name));
    grid.appendChild(sw);
  }
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
    row.className = "situation-row";
    row.innerHTML = `
      <span class="situation-text">
        <div class="situation-name">${ev.eventName}</div>
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
}

function updateHeaderTeamBadge(team) {
  const badge = document.getElementById("header-team-badge");
  if (!badge) return;
  if (team) {
    badge.style.background = `linear-gradient(135deg, ${team.primary}, ${team.secondary})`;
    badge.textContent = team.initials ?? "";
    badge.title = `${team.name} -- click to change team`;
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
  });
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
    sw.style.background = `linear-gradient(135deg, ${t.primary}, ${t.secondary})`;
    sw.textContent = t.initials ?? "";
    sw.addEventListener("click", () => onPick(t.name));
    grid.appendChild(sw);
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
  btn.textContent = state.matchupHome && state.matchupAway
    ? `${state.matchupAway} @ ${state.matchupHome}`
    : "Set Matchup";
}

async function loadMatchup() {
  if (!bridge) return;
  try {
    const raw = await bridge.GetGameTeams();
    if (!raw) return;
    const { home, away } = JSON.parse(raw);
    state.matchupHome = home;
    state.matchupAway = away;
    updateMatchupLabel();
  } catch (err) { console.error("GetGameTeams failed", err); }
}

function openMatchupDialog() {
  const overlay = document.getElementById("matchup-overlay");
  document.getElementById("matchup-home-search").value = "";
  document.getElementById("matchup-away-search").value = "";
  renderMatchupGrid("home", "");
  renderMatchupGrid("away", "");
  updateMatchupSubtext();
  overlay.hidden = false;
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
  if (!state.matchupHome || !state.matchupAway) {
    el.textContent = "Pick both a home and an away team.";
  } else if (state.matchupHome === state.matchupAway) {
    el.textContent = "Home and away can't be the same team.";
  } else {
    el.textContent = `${state.matchupAway} (away) at ${state.matchupHome} (home) -- each team's own saved profile loads automatically.`;
  }
}

async function confirmMatchup() {
  if (!state.matchupHome || !state.matchupAway || state.matchupHome === state.matchupAway) return;
  await bridge?.SetGameTeams(state.matchupHome, state.matchupAway);
  updateMatchupLabel();
  closeMatchupDialog();
  showToast(`Matchup set: ${state.matchupAway} @ ${state.matchupHome}`);
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

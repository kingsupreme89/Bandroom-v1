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
};

async function init() {
  if (bridge) {
    state.teams = JSON.parse(await bridge.GetTeams());
    state.categories = JSON.parse(await bridge.GetCategories());
    state.activeTeam = await bridge.GetActiveTeam();
    document.getElementById("app-version").textContent = "v" + await bridge.GetAppVersion();
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
  if (bridge) state.savedProfiles = JSON.parse(await bridge.GetSavedProfiles());
  renderTeamGrid();
  renderCategories();
  setActiveTeam(state.activeTeam, /*fromInit*/ true);
  updateProfileStatus();
  wireControls();
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

function updateProfileStatus() {
  const el = document.getElementById("profile-status");
  if (!el) return;
  const configured = state.savedProfiles.includes(state.activeTeam);
  const total = state.savedProfiles.length;
  el.innerHTML = configured
    ? `<span class="profile-saved">&#10003; ${state.activeTeam} saved &mdash; ${total} team${total !== 1 ? "s" : ""} configured</span>`
    : `<span class="profile-unsaved">No tracks assigned yet for ${state.activeTeam}</span>`;
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

  // Drag the borderless window by pulling on the header center region
  document.getElementById("drag-handle").addEventListener("mousedown", (e) => {
    if (e.button === 0) bridge?.BeginDrag();
  });
  document.getElementById("btn-update").addEventListener("click", () => bridge?.ShowUpdate());
  document.getElementById("btn-reset").addEventListener("click", () => bridge?.ResetTeamProfile());

  document.getElementById("slider-volume").addEventListener("input", (e) => {
    document.getElementById("volume-value").textContent = e.target.value;
    bridge?.SetVolume(Number(e.target.value));
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
    document.getElementById("btn-update").hidden = false;
  });

  document.getElementById("btn-close-picker").addEventListener("click", closeTeamPicker);
  document.getElementById("team-picker-overlay").addEventListener("click", (e) => {
    if (e.target.id === "team-picker-overlay") closeTeamPicker();
  });
  document.getElementById("team-picker-search").addEventListener("input", (e) => renderTeamPickerGrid(e.target.value));
  document.addEventListener("keydown", (e) => {
    if (e.key === "Escape" && !document.getElementById("team-picker-overlay").hidden) closeTeamPicker();
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
  const grid = document.getElementById("team-picker-grid");
  grid.innerHTML = "";
  const q = filter.trim().toLowerCase();
  for (const t of state.teams) {
    if (q && !t.name.toLowerCase().includes(q)) continue;
    const sw = document.createElement("div");
    sw.className = "team-swatch" + (t.name === state.activeTeam ? " active" : "");
    sw.title = t.name;
    sw.style.background = `linear-gradient(135deg, ${t.primary}, ${t.secondary})`;
    sw.textContent = t.initials ?? "";
    sw.addEventListener("click", () => { selectTeam(t.name); closeTeamPicker(); });
    grid.appendChild(sw);
  }
}

function flashPanel(el) {
  el.classList.add("panel-flash");
  setTimeout(() => el.classList.remove("panel-flash"), 900);
}

function runRailAction(action) {
  switch (action) {
    case "focus-teams":
      openTeamPicker();
      break;
    case "focus-categories":
      openSituations("All");
      break;
    case "focus-adjust":
      flashPanel(document.getElementById("adjust-panel"));
      document.getElementById("adjust-panel").scrollIntoView({ block: "nearest" });
      break;
    case "assign":
      openSituations("All");
      break;
    case "effects":
      bridge?.TriggerEffectsTest();
      break;
    case "help":
      bridge?.OpenHelp();
      break;
  }
}

init();

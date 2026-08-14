
'use strict';

// Plain-English translation of the RAM reader's state, built for one purpose:
// a user who says "it isn't reading" can open the Diagnostics tab, press one
// button, and paste a report that names the actual cause. Every sentence here
// maps to a diagnostic the reader already publishes; this file only translates,
// it never guesses.
//
// The report deliberately contains no local paths, window handles, or ids -
// it is meant to be pasted into a public Discord or GitHub thread as-is.

const OK = 'ok';
const INFO = 'info';
const WARN = 'warn';
const BAD = 'bad';

function parseTimeMs(text) {
  const value = Date.parse(String(text || ''));
  return Number.isFinite(value) ? value : null;
}

function line(state, label, text) {
  return { state, label, text };
}

function fieldAvailable(ramField) {
  return Boolean(ramField && ramField.available === true);
}

// The exporter's discovery strings are precise but written for the developer.
// Each translator keeps the raw string available below while the visible line
// says what a user can act on.
function translateTeamRole(teamRole, awayName, homeName, namesReading) {
  if (namesReading) {
    return line(OK, 'Team names', `Reading (${awayName} at ${homeName}).`);
  }
  const raw = String(teamRole || '');
  if (/no usable pair|no authoritative/i.test(raw)) {
    return line(WARN, 'Team names',
      'The reader sees the game but could not identify the two team names. '
      + 'Known causes: TeamBuilder or renamed/custom teams, or a mode that does not expose the name markers. '
      + 'Every other field keeps working; the scorebug shows AWAY/HOME meanwhile.');
  }
  if (/labels home=0 away=0/i.test(raw)) {
    return line(WARN, 'Team names',
      'The name markers were not in memory on the last look. The reader keeps retrying in the background.');
  }
  if (/ambiguous/i.test(raw)) {
    return line(WARN, 'Team names',
      'The reader found name candidates but could not safely tell which team is home and which is away, so it shows neither.');
  }
  return line(WARN, 'Team names', 'Not identified yet. The reader keeps retrying in the background.');
}

function translateRankBind(rankBind) {
  const raw = String(rankBind || '');
  if (/^bound/i.test(raw)) {
    return line(OK, 'Ranks & records', 'Reading.');
  }
  if (/tied, so possession is required/i.test(raw)) {
    return line(INFO, 'Ranks & records',
      'Waiting for the first score. At a tied score the reader cannot yet prove which team is which, '
      + 'so ranks and records stay hidden until someone scores. This is normal.');
  }
  if (/confirming/i.test(raw)) {
    return line(INFO, 'Ranks & records', 'Found - double-checking before showing them.');
  }
  if (/only \d+ ScoreHud/i.test(raw)) {
    return line(WARN, 'Ranks & records',
      'The scoreboard objects these come from were not found in the game\'s memory. '
      + 'If College Football 27 was recently updated, this reader build may not match the new game version - '
      + 'include this report when asking for help.');
  }
  if (/matchup transition/i.test(raw)) {
    return line(INFO, 'Ranks & records', 'A new game was detected - re-finding these fields now.');
  }
  if (/lost bind/i.test(raw)) {
    return line(WARN, 'Ranks & records', 'Lost track of the team objects - re-finding them now.');
  }
  return line(WARN, 'Ranks & records', 'Not reading yet. The reader keeps retrying in the background.');
}

function translateTimeoutBind(timeoutBind) {
  const raw = String(timeoutBind || '');
  if (/^bound/i.test(raw)) {
    return line(OK, 'Timeouts', 'Reading.');
  }
  return line(WARN, 'Timeouts',
    'Not verified yet. The reader waits until two matching copies in memory agree before trusting timeout counts.');
}

function buildRamReaderReport(input = {}) {
  const now = Number.isFinite(input.now) ? input.now : Date.now();
  const status = input.status && typeof input.status === 'object' ? input.status : null;
  const live = input.live && typeof input.live === 'object' ? input.live : null;
  const lines = [];
  let headline = '';
  let level = OK;

  const statusAtMs = status ? parseTimeMs(status.updatedAt) : null;
  const statusFresh = statusAtMs !== null && now - statusAtMs <= 5000;
  const statusMessage = String(status?.message || '');
  const liveAtMs = live ? parseTimeMs(live.updatedAt) : null;
  const liveFresh = liveAtMs !== null && now - liveAtMs <= 20000;
  const gameVersion = live?.process?.exeVersion || status?.gameExeVersion || null;

  if (input.readerEnabled === false) {
    headline = 'The memory reader is turned off because the Screen reader is selected. '
      + 'Switch the scoreboard source to RAM to use it.';
    level = INFO;
  } else if (input.readerExePresent === false) {
    headline = 'The reader program file is missing from this install. '
      + 'An antivirus program most likely quarantined it. '
      + 'Restore CollegeFB27RamReader.exe from your antivirus history (and add it to the exclusions), then restart this app.';
    level = BAD;
  } else if (input.readerProcessRunning === false && !statusFresh) {
    headline = 'The reader is not running. Restart the app; if this keeps happening, '
      + 'check whether your antivirus is blocking CollegeFB27RamReader.exe.';
    level = BAD;
  } else if (/would not grant read-only access|access is denied/i.test(statusMessage)) {
    headline = 'Windows refused to let the reader look at the game. '
      + 'This usually means the game is running as administrator and this app is not. '
      + 'Close this app and run it as administrator, then try again.';
    level = BAD;
  } else if (/RAM service error/i.test(statusMessage)) {
    headline = 'The reader hit an error and is retrying: ' + statusMessage;
    level = BAD;
  } else if (/waiting for CollegeFB27\.exe/i.test(statusMessage)) {
    headline = input.gameWindowDetected
      ? 'A game window is open, but the reader cannot find a game process named CollegeFB27.exe. '
        + 'If you are playing a trial or unusual copy of the game, its program name may be different - include this report when asking for help.'
      : 'College Football 27 is not running. Start the game; the reader connects automatically.';
    level = WARN;
  } else if (!live || !liveFresh) {
    if (/waiting for live progression|confirming|locating|locator|matchup changed|diverged|reset detected/i.test(statusMessage)) {
      headline = 'Connected to the game and locking on. The reader proves it found the real scoreboard '
        + 'by watching the clock tick, so it needs a few seconds of live, unpaused gameplay. '
        + 'Unpause and run a play - fields appear a few seconds later.';
      level = INFO;
    } else if (statusFresh) {
      headline = 'Connected to the game but not publishing yet: ' + (statusMessage || 'starting up.');
      level = WARN;
    } else {
      headline = 'The reader has not reported recently. It may be restarting; '
        + 'if this stays for more than a minute, restart the app.';
      level = WARN;
    }
  } else {
    headline = 'Reading the game.';
    level = OK;
  }

  // Per-field detail is only meaningful once a live document exists.
  if (live && liveFresh) {
    const ram = live.ram || {};
    const away = live.away || {};
    const home = live.home || {};
    const game = live.game || {};
    const discovery = live.discovery || {};

    const coreReading = fieldAvailable(ram.quarter) && fieldAvailable(ram.gameClockSeconds)
      && fieldAvailable(ram.homeScore) && fieldAvailable(ram.awayScore);
    lines.push(coreReading
      ? line(OK, 'Scores & clock', 'Reading.')
      : line(BAD, 'Scores & clock',
        'Not reading - this is the core scoreboard, so nothing else can work. '
        + 'Unpause the game and let the clock run for a few seconds.'));

    lines.push(game.downDistanceSource === 'ram'
      ? line(OK, 'Down & distance', `Reading (${game.downDistance || 'live'}).`)
      : line(WARN, 'Down & distance', 'Not reading yet.'));

    const namesReading = /^ram(?:-cached)?$/i.test(String(away.nameSource || ''))
      && /^ram(?:-cached)?$/i.test(String(home.nameSource || ''));
    lines.push(translateTeamRole(discovery.teamRole, away.name, home.name, namesReading));
    lines.push(translateRankBind(discovery.rankBind));
    lines.push(translateTimeoutBind(discovery.timeoutBind));
    lines.push(line(INFO, 'Possession',
      'Not shown in this version by design - the reader does not yet trust its possession source.'));
  }

  const connectedToGame = Boolean(live || status?.gameProcessId);
  lines.push(line(INFO, 'Game version', gameVersion
    ? String(gameVersion)
    : (connectedToGame
      ? 'Not reported. If fields are missing after a game update, include this report when asking for help.'
      : 'Unknown (the reader has not connected to the game yet).')));

  // The pasteable report: the plain-English summary on top, the raw
  // diagnostics underneath so the developer can see exactly what the
  // reader saw without asking the user to dig for files.
  const stamp = new Date(now).toISOString();
  const text = [];
  text.push('=== CFB27 Scoreboard Overlay - reader report ===');
  text.push('Generated: ' + stamp);
  text.push('App version: ' + (input.appVersion || 'unknown'));
  text.push('Game version: ' + (gameVersion || 'unknown'));
  text.push('');
  text.push('Overall: ' + headline);
  if (lines.length) {
    text.push('');
    text.push('Field check:');
    for (const item of lines) {
      text.push('  [' + item.state.toUpperCase() + '] ' + item.label + ': ' + item.text);
    }
  }
  text.push('');
  text.push('Raw details (for the developer):');
  text.push('  reader status: ' + (statusMessage || '(no status file)'));
  text.push('  status age: ' + (statusAtMs === null ? 'n/a' : Math.round((now - statusAtMs) / 1000) + 's'));
  text.push('  live data age: ' + (liveAtMs === null ? 'no live file' : Math.round((now - liveAtMs) / 1000) + 's'));
  if (live?.discovery) {
    const discovery = live.discovery;
    for (const key of ['automaticLocator', 'teamRole', 'matchupBind', 'rankBind', 'teamIdNames',
      'timeoutBind', 'timeoutInstall', 'timeoutCatalog', 'possessionBind']) {
      if (discovery[key] !== undefined && discovery[key] !== null) {
        text.push('  ' + key + ': ' + String(discovery[key]));
      }
    }
    if (discovery.scoreboardCandidates !== undefined) {
      text.push('  scoreboardCandidates: ' + String(discovery.scoreboardCandidates));
    }
  }
  text.push('  reader process running: ' + (input.readerProcessRunning === false ? 'no' : 'yes'));
  text.push('  reader exe present: ' + (input.readerExePresent === false ? 'no' : 'yes'));
  text.push('  game window detected: ' + (input.gameWindowDetected ? 'yes' : 'no'));
  if (status?.gameModuleSize) text.push('  game module size: ' + String(status.gameModuleSize));

  return {
    headline,
    level,
    lines,
    reportText: text.join('\n'),
  };
}

module.exports = { buildRamReaderReport };
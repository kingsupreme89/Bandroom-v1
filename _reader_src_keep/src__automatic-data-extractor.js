"
  }
}'use strict';

const fs = require('node:fs');
const path = require('node:path');

const SCOREBOARD_COLUMNS = Object.freeze([
  'capturedAt',
  'awayRank',
  'awayName',
  'awayRecord',
  'awayScore',
  'awayTimeouts',
  'homeRank',
  'homeName',
  'homeRecord',
  'homeScore',
  'homeTimeouts',
  'quarter',
  'gameClock',
  'playClock',
  'down',
  'distance',
  'downDistance',
  'ballOn',
  'gameStatus',
  'possession',
  'visible',
  'confidence',
]);

const PENALTY_PATTERNS = Object.freeze([
  ['roughing-the-passer', /\bROUGHING\s+THE\s+PASSER\b/i],
  ['defensive-pass-interference', /\bDEFENSIVE\s+PASS\s+INTERFERENCE\b/i],
  ['offensive-pass-interference', /\bOFFENSIVE\s+PASS\s+INTERFERENCE\b/i],
  ['pass-interference', /\bPASS\s+INTERFERENCE\b/i],
  ['unsportsmanlike-conduct', /\bUNSPORTSMANLIKE\s+CONDUCT\b/i],
  ['delay-of-game', /\bDELAY\s+OF\s+GAME\b/i],
  ['false-start', /\bFALSE\s+START\b/i],
  ['illegal-block', /\bILLEGAL\s+(?:BLOCK|FORMATION|MOTION|SHIFT|TOUCHING)\b/i],
  ['face-mask', /\bFACE\s*MASK\b/i],
  ['encroachment', /\bENCROACHMENT\b/i],
  ['offside', /\bOFFSIDES?\b/i],
  ['holding', /\bHOLDING\b/i],
  ['clipping', /\bCLIPPING\b/i],
  ['personal-foul', /\bPERSONAL\s+FOUL\b/i],
]);

function safeTimestamp(value = Date.now()) {
  const numeric = Number(value);
  const parsed = Number.isFinite(numeric) && numeric > 0
    ? numeric
    : Date.parse(String(value || ''));
  return new Date(Number.isFinite(parsed) ? parsed : Date.now()).toISOString();
}

function sessionId(value = Date.now()) {
  return safeTimestamp(value).replace(/[:.]/g, '-');
}

function finiteNumber(value) {
  const numeric = Number(value);
  return Number.isFinite(numeric) ? numeric : null;
}

function integerOrNull(value) {
  const numeric = finiteNumber(value);
  return numeric === null ? null : Math.round(numeric);
}

function textOrNull(value, maximum = 200) {
  const text = String(value ?? '').replace(/\s+/g, ' ').trim();
  return text ? text.slice(0, maximum) : null;
}

function csvCell(value) {
  if (value === null || value === undefined) return '';
  const text = String(value);
  return /[",\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
}

function normalizedScoreboardState(state = {}, capturedAt = Date.now()) {
  const away = state.away || {};
  const home = state.home || {};
  const game = state.game || {};
  const possession = away.possession === true
    ? 'away'
    : (home.possession === true ? 'home' : null);
  return {
    capturedAt: safeTimestamp(state.meta?.updatedAt || capturedAt),
    awayRank: integerOrNull(away.rank),
    awayName: textOrNull(away.name),
    awayRecord: textOrNull(away.record, 20),
    awayScore: integerOrNull(away.score),
    awayTimeouts: integerOrNull(away.timeouts),
    homeRank: integerOrNull(home.rank),
    homeName: textOrNull(home.name),
    homeRecord: textOrNull(home.record, 20),
    homeScore: integerOrNull(home.score),
    homeTimeouts: integerOrNull(home.timeouts),
    quarter: textOrNull(game.quarter, 20),
    gameClock: textOrNull(game.clock, 20),
    playClock: integerOrNull(game.playClock),
    down: integerOrNull(game.down),
    distance: game.distance === null || game.distance === undefined
      ? null
      : (finiteNumber(game.distance) ?? textOrNull(game.distance, 20)),
    downDistance: textOrNull(game.downDistance, 40),
    ballOn: textOrNull(game.ballOn, 40),
    gameStatus: textOrNull(game.status, 80),
    possession,
    visible: state.meta?.visible === true,
    confidence: finiteNumber(state.meta?.confidence),
  };
}

function exportableScoreboardState(state = {}, capturedAt = Date.now()) {
  const serialized = JSON.stringify(state, (key, value) => {
    if (key === 'logo' || key === 'teamLogoLayouts') return undefined;
    if (typeof value === 'string' && value.startsWith('data:image/')) return undefined;
    return value;
  });
  const scoreboard = serialized ? JSON.parse(serialized) : {};
  scoreboard.meta ||= {};
  scoreboard.meta.updatedAt = safeTimestamp(scoreboard.meta.updatedAt || capturedAt);
  return scoreboard;
}

function stateKey(state) {
  const copy = { ...state };
  delete copy.capturedAt;
  delete copy.confidence;
  return JSON.stringify(copy);
}

function exportStateKey(state) {
  const copy = JSON.parse(JSON.stringify(state));
  if (copy.meta) {
    delete copy.meta.updatedAt;
    delete copy.meta.confidence;
  }
  return JSON.stringify(copy);
}

function normalizeScreenText(value) {
  return String(value ?? '')
    .replace(/\r/g, '')
    .split('\n')
    .map((line) => line.replace(/\s+/g, ' ').trim())
    .filter(Boolean)
    .join('\n')
    .slice(0, 16_000);
}

function classifyScreenText(value) {
  const text = normalizeScreenText(value);
  const flat = text.replace(/\n/g, ' ');
  const categories = [];
  if (/\bTOUCHDOWN\b|\bTD\b/i.test(flat)) categories.push('touchdown');
  if (/\bPENALTY\b|\bFLAG\b|\bACCEPT(?:ED)?\b|\bDECLIN(?:E|ED)\b/i.test(flat)
      || PENALTY_PATTERNS.some(([, pattern]) => pattern.test(flat))) {
    categories.push('penalty');
  }
  if ((/\bPASS(?:ING)?\b/i.test(flat) || /\bCMP\b|\bCOMP\b/i.test(flat))
      && /\bATT\b/i.test(flat)
      && /\bYDS?\b|\bYARDS?\b/i.test(flat)) {
    categories.push('passing-stats');
  }
  if (/\bRUSH(?:ING)?\b/i.test(flat)
      && /\bATT\b|\bCAR\b|\bCARRIES\b/i.test(flat)
      && /\bYDS?\b|\bYARDS?\b/i.test(flat)) {
    categories.push('rushing-stats');
  }
  if (/\bRECEIV(?:ING|ER|ERS)?\b/i.test(flat)
      && /\bREC\b|\bRECEPTIONS?\b/i.test(flat)
      && /\bYDS?\b|\bYARDS?\b/i.test(flat)) {
    categories.push('receiving-stats');
  }
  if (/\bPLAYER\s+STATS?\b|\bTEAM\s+STATS?\b|\bGAME\s+STATS?\b/i.test(flat)) {
    categories.push('statistics-screen');
  }
  const penalty = PENALTY_PATTERNS.find(([, pattern]) => pattern.test(flat));
  const decision = /\bDECLIN(?:E|ED)\b/i.test(flat)
    ? 'declined'
    : (/\bACCEPT(?:ED)?\b/i.test(flat) ? 'accepted' : null);
  const yardMatch = /\b(\d{1,2})\s*(?:YD|YDS|YARD|YARDS)\b/i.exec(flat);
  return {
    categories: [...new Set(categories)],
    penaltyType: penalty?.[0] || null,
    penaltyDecision: decision,
    penaltyYards: yardMatch ? Number(yardMatch[1]) : null,
  };
}

class AutomaticDataExtractor {
  constructor(options = {}) {
    if (!options.rootPath) throw new TypeError('rootPath is required');
    this.rootPath = path.resolve(options.rootPath);
    this.now = typeof options.now === 'function' ? options.now : Date.now;
    this.id = options.sessionId || sessionId(this.now());
    this.sessionPath = path.join(this.rootPath, this.id);
    this.screenshotPath = path.join(this.sessionPath, 'screenshots');
    this.scoreboardPath = path.join(this.sessionPath, 'scoreboard.csv');
    this.scoreboardJsonlPath = path.join(this.sessionPath, 'scoreboard.jsonl');
    this.latestScoreboardPath = path.join(this.sessionPath, 'latest-scoreboard.json');
    this.liveScoreboardPath = path.join(this.rootPath, 'live-scoreboard.json');
    this.liveScreenScoreboardPath = path.join(this.rootPath, 'live-screen-scoreboard.json');
    this.eventsPath = path.join(this.sessionPath, 'events.jsonl');
    this.screenTextPath = path.join(this.sessionPath, 'screen-text.jsonl');
    this.lastScoreboardKey = '';
    this.lastScoreboard = null;
    this.lastScreenScoreboardKey = '';
    this.lastScreenTextKey = '';
    this.lastScreenSampleAt = 0;
    this.contextCaptureUntil = 0;
    this.contextReason = null;
    this.lastSignals = new Map();
    this.counts = { scoreboardRows: 0, events: 0, screenReads: 0, screenshots: 0 };
    this.started = false;
  }

  start() {
    if (this.started) return this.snapshot();
    fs.mkdirSync(this.screenshotPath, { recursive: true });
    fs.writeFileSync(this.scoreboardPath, `${SCOREBOARD_COLUMNS.join(',')}\n`, { flag: 'wx' });
    fs.writeFileSync(path.join(this.rootPath, 'latest-session.txt'), `${this.sessionPath}\n`, 'utf8');
    fs.appendFileSync(this.eventsPath, `${JSON.stringify({
      event: 'session-started',
      capturedAt: safeTimestamp(this.now()),
      sessionId: this.id,
    })}\n`, 'utf8');
    this.started = true;
    return this.snapshot();
  }

  ensureStarted() {
    if (!this.started) this.start();
  }

  appendJsonLine(filePath, value) {
    this.ensureStarted();
    fs.appendFileSync(filePath, `${JSON.stringify(value)}\n`, 'utf8');
  }

  writeEvent(value) {
    this.appendJsonLine(this.eventsPath, value);
    this.counts.events += 1;
    return value;
  }

  observeScoreboard(state, capturedAt = this.now()) {
    const normalized = normalizedScoreboardState(state, capturedAt);
    if (!normalized.awayName && !normalized.homeName
        && normalized.awayScore === null && normalized.homeScore === null) return null;
    const exportable = exportableScoreboardState(state, capturedAt);
    const key = exportStateKey(exportable);
    if (key === this.lastScoreboardKey) return null;
    this.ensureStarted();
    const row = SCOREBOARD_COLUMNS.map((column) => csvCell(normalized[column])).join(',');
    fs.appendFileSync(this.scoreboardPath, `${row}\n`, 'utf8');
    fs.appendFileSync(this.scoreboardJsonlPath, `${JSON.stringify({
      capturedAt: normalized.capturedAt,
      scoreboard: exportable,
    })}\n`, 'utf8');
    const temporaryLatest = `${this.latestScoreboardPath}.tmp`;
    fs.writeFileSync(temporaryLatest, `${JSON.stringify(exportable, null, 2)}\n`, 'utf8');
    fs.renameSync(temporaryLatest, this.latestScoreboardPath);
    const temporaryLive = `${this.liveScoreboardPath}.tmp`;
    fs.writeFileSync(temporaryLive, `${JSON.stringify(exportable, null, 2)}\n`, 'utf8');
    fs.renameSync(temporaryLive, this.liveScoreboardPath);
    this.counts.scoreboardRows += 1;

    const previous = this.lastScoreboard;
    this.lastScoreboard = normalized;
    this.lastScoreboardKey = key;
    if (!previous) return { state: normalized, scoreEvents: [] };

    const scoreEvents = [];
    for (const side of ['away', 'home']) {
      const scoreKey = `${side}Score`;
      const nameKey = `${side}Name`;
      if (!Number.isInteger(previous[scoreKey]) || !Number.isInteger(normalized[scoreKey])) continue;
      const delta = normalized[scoreKey] - previous[scoreKey];
      if (delta <= 0) continue;
      const likelyType = delta === 6
        ? 'touchdown-candidate'
        : (delta === 3 ? 'field-goal-candidate' : (delta === 1 ? 'conversion-candidate' : 'score-change'));
      const event = this.writeEvent({
        event: 'score-change',
        likelyType,
        capturedAt: normalized.capturedAt,
        side,
        team: normalized[nameKey],
        pointsAdded: delta,
        previousScore: previous[scoreKey],
        newScore: normalized[scoreKey],
        quarter: normalized.quarter,
        gameClock: normalized.gameClock,
      });
      scoreEvents.push(event);
      this.requestContextCapture(likelyType, 12_000, capturedAt);
    }
    return { state: normalized, scoreEvents };
  }

  observeScreenScoreboard(state, capturedAt = this.now()) {
    const normalized = normalizedScoreboardState(state, capturedAt);
    if (!normalized.awayName && !normalized.homeName
        && normalized.awayScore === null && normalized.homeScore === null
        && !normalized.quarter && !normalized.gameClock) return null;
    const exportable = exportableScoreboardState(state, capturedAt);
    const key = exportStateKey(exportable);
    if (key === this.lastScreenScoreboardKey) return null;
    this.ensureStarted();
    const temporary = `${this.liveScreenScoreboardPath}.tmp`;
    fs.writeFileSync(temporary, `${JSON.stringify(exportable, null, 2)}\n`, 'utf8');
    fs.renameSync(temporary, this.liveScreenScoreboardPath);
    this.lastScreenScoreboardKey = key;
    return exportable;
  }

  requestContextCapture(reason, durationMs = 8_000, requestedAt = this.now()) {
    this.contextReason = textOrNull(reason, 80) || 'event';
    this.contextCaptureUntil = Math.max(
      this.contextCaptureUntil,
      Number(requestedAt) + Math.max(1_000, Number(durationMs) || 8_000),
    );
  }

  observeSignal(signal, details = {}, cooldownMs = 5_000) {
    const name = textOrNull(signal, 80);
    if (!name) return null;
    const now = Number(details.capturedAt) || this.now();
    const previous = this.lastSignals.get(name) || 0;
    if (now - previous < Math.max(500, Number(cooldownMs) || 5_000)) return null;
    this.lastSignals.set(name, now);
    this.requestContextCapture(name, 8_000, now);
    return this.writeEvent({
      event: 'screen-signal',
      signal: name,
      capturedAt: safeTimestamp(now),
      quarter: this.lastScoreboard?.quarter || null,
      gameClock: this.lastScoreboard?.gameClock || null,
      rawText: textOrNull(details.rawText, 200),
    });
  }

  needsContextCapture(at = this.now()) {
    return Number(at) <= this.contextCaptureUntil;
  }

  shouldSampleScreen(at = this.now(), normalIntervalMs = 2_000, eventIntervalMs = 500) {
    const interval = this.needsContextCapture(at) ? eventIntervalMs : normalIntervalMs;
    return Number(at) - this.lastScreenSampleAt >= interval;
  }

  markScreenSampled(at = this.now()) {
    this.lastScreenSampleAt = Number(at) || this.now();
  }

  saveScreenshot(pngBytes, reason = 'screen', capturedAt = this.now()) {
    this.ensureStarted();
    const safeReason = String(reason || 'screen').toLowerCase().replace(/[^a-z0-9-]+/g, '-').slice(0, 50);
    const fileName = `${sessionId(capturedAt)}-${safeReason}-${String(this.counts.screenshots + 1).padStart(4, '0')}.png`;
    const filePath = path.join(this.screenshotPath, fileName);
    fs.writeFileSync(filePath, Buffer.from(pngBytes));
    this.counts.screenshots += 1;
    return filePath;
  }

  observeScreenText(textValue, details = {}) {
    const text = normalizeScreenText(textValue);
    if (!text) return null;
    const classification = classifyScreenText(text);
    const key = JSON.stringify({ text, categories: classification.categories });
    if (key === this.lastScreenTextKey) return null;
    this.lastScreenTextKey = key;
    const capturedAtMs = Number(details.capturedAt) || this.now();
    let screenshot = null;
    if (details.pngBytes && (classification.categories.length || this.needsContextCapture(capturedAtMs))) {
      screenshot = this.saveScreenshot(
        details.pngBytes,
        classification.categories[0] || this.contextReason || 'event-context',
        capturedAtMs,
      );
    }
    const observation = {
      capturedAt: safeTimestamp(capturedAtMs),
      categories: classification.categories,
      penaltyType: classification.penaltyType,
      penaltyDecision: classification.penaltyDecision,
      penaltyYards: classification.penaltyYards,
      text,
      screenshot,
      scoreboard: this.lastScoreboard,
    };
    this.appendJsonLine(this.screenTextPath, observation);
    this.counts.screenReads += 1;
    if (classification.categories.length) {
      this.writeEvent({ event: 'screen-text-match', ...observation });
      this.requestContextCapture(classification.categories[0], 8_000, capturedAtMs);
    }
    return observation;
  }

  recordError(error, stage = 'data-extraction') {
    return this.writeEvent({
      event: 'error',
      stage: textOrNull(stage, 80),
      capturedAt: safeTimestamp(this.now()),
      message: String(error?.message || error || 'Unknown extraction error').slice(0, 500),
    });
  }

  snapshot() {
    return {
      enabled: true,
      sessionId: this.id,
      sessionPath: this.sessionPath,
      latestSessionFile: path.join(this.rootPath, 'latest-session.txt'),
      liveScoreboardPath: this.liveScoreboardPath,
      liveScreenScoreboardPath: this.liveScreenScoreboardPath,
      contextCaptureActive: this.needsContextCapture(),
      contextReason: this.contextReason,
      counts: { ...this.counts },
    };
  }
}

module.exports = {
  AutomaticDataExtractor,
  SCOREBOARD_COLUMNS,
  classifyScreenText,
  exportableScoreboardState,
  normalizedScoreboardState,
  normalizeScreen
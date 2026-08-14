alThis);
'use strict';

/**
 * Publish normalized scoreboard state to a Claude Standalone/DC document.
 *
 * Keep this function self-contained. The overlay serializes it with
 * Function#toString and evaluates it inside the isolated theme guest, where
 * module-scope helpers and CommonJS are unavailable.
 */
function applyClaudeDcScoreboardState(scope, scoreboardState = {}) {
  const report = {
    detected: false,
    applied: false,
    didApply: false,
    reason: 'runtime-unavailable',
    rootName: null,
    appliedFields: [],
    appliedProps: [],
    supported: [],
    supportedProps: [],
    staticMissing: [],
  };
  const host = scope && (typeof scope === 'object' || typeof scope === 'function') ? scope : null;
  if (!host || typeof host.__dcSetProps !== 'function'
    || typeof host.__dcRootName !== 'function' || !host.__dcRegistry) {
    return report;
  }

  let rootName;
  let registryEntry;
  try {
    rootName = host.__dcRootName();
    const registry = host.__dcRegistry;
    registryEntry = registry && typeof registry.get === 'function'
      ? registry.get(rootName)
      : registry?.[rootName];
  } catch (error) {
    report.reason = 'runtime-inspection-failed';
    report.error = error instanceof Error ? error.message : String(error);
    return report;
  }
  report.rootName = typeof rootName === 'string' ? rootName : null;

  const propsMeta = registryEntry?.propsMeta;
  if (!report.rootName || !propsMeta || typeof propsMeta !== 'object') {
    report.reason = 'props-metadata-unavailable';
    return report;
  }

  const declaredProps = Object.keys(propsMeta).filter((name) => name && name[0] !== '$');
  if (!declaredProps.length) {
    report.reason = 'no-declared-props';
    return report;
  }
  const declaredByLower = new Map(declaredProps.map((name) => [name.toLowerCase(), name]));
  const findDeclared = (...candidates) => {
    for (const candidate of candidates.flat()) {
      const found = declaredByLower.get(String(candidate || '').toLowerCase());
      if (found) return found;
    }
    return null;
  };
  const hasOwn = (object, key) => Boolean(object)
    && Object.prototype.hasOwnProperty.call(object, key);
  const readFirst = (object, keys) => {
    for (const key of keys) {
      if (hasOwn(object, key)) return object[key];
    }
    return undefined;
  };
  const normalizeForProp = (prop, value) => {
    if (value === undefined) return undefined;
    const meta = propsMeta[prop] || {};
    const numeric = meta.tsType === 'number' || meta.editor === 'int'
      || meta.editor === 'number' || typeof meta.default === 'number';
    const boolean = meta.tsType === 'boolean' || meta.editor === 'boolean'
      || typeof meta.default === 'boolean';
    if (numeric) {
      if (value === null || value === '') return undefined;
      const number = Number(value);
      return Number.isFinite(number) ? number : undefined;
    }
    if (boolean) return Boolean(value);
    if (value === null) return '';
    return typeof value === 'string' ? value : String(value);
  };
  const parseDownDistance = (value) => {
    const match = /^\s*([1-4])(?:st|nd|rd|th)?\s*(?:&|and)\s*(.+?)\s*$/i.exec(String(value || ''));
    return match ? { down: Number(match[1]), distance: match[2] } : {};
  };
  const parseQuarter = (value) => {
    if (Number.isFinite(Number(value)) && String(value).trim() !== '') return Number(value);
    const text = String(value || '').trim();
    if (/ot/i.test(text)) return 5;
    const match = /([1-4])/.exec(text);
    return match ? Number(match[1]) : undefined;
  };
  const unique = (values) => [...new Set(values.filter(Boolean))];

  const away = scoreboardState?.away || {};
  const home = scoreboardState?.home || {};
  const game = scoreboardState?.game || {};
  const parsedDownDistance = parseDownDistance(game.downDistance);

  const prop = {
    awayName: findDeclared('awayName'),
    awayNickname: findDeclared('awayNickname', 'awayMascot', 'awayWordmark'),
    awayRecord: findDeclared('awayRecord'),
    awayColor: findDeclared('awayColor'),
    awayRank: findDeclared('awayRank'),
    awayScore: findDeclared('awayScore'),
    awayLogo: findDeclared('awayLogo', 'awayLogoUrl'),
    homeName: findDeclared('homeName'),
    homeNickname: findDeclared('homeNickname', 'homeMascot', 'homeWordmark'),
    homeRecord: findDeclared('homeRecord'),
    homeColor: findDeclared('homeColor'),
    homeRank: findDeclared('homeRank'),
    homeScore: findDeclared('homeScore'),
    homeLogo: findDeclared('homeLogo', 'homeLogoUrl'),
    down: findDeclared('down', 'downNumber'),
    distance: findDeclared('distance', 'yardsToGo'),
    quarter: findDeclared('quarter', 'period'),
    clock: findDeclared('clock', 'gameClock'),
    playClock: findDeclared('playClock'),
    possession: findDeclared('possession', 'possessionSide', 'possessingTeam'),
    awayPossession: findDeclared('awayPossession'),
    homePossession: findDeclared('homePossession'),
  };

  const timeoutProps = declaredProps.filter((name) => /timeouts?$/i.test(name));
  prop.awayTimeouts = findDeclared('awayTimeouts', 'awayTimeout');
  prop.homeTimeouts = findDeclared('homeTimeouts', 'homeTimeout');
  const unclaimedTimeoutProps = () => timeoutProps.filter((name) => (
    name !== prop.awayTimeouts && name !== prop.homeTimeouts
  ));
  if (!prop.awayTimeouts) prop.awayTimeouts = unclaimedTimeoutProps()[0] || null;
  if (!prop.homeTimeouts) prop.homeTimeouts = unclaimedTimeoutProps()[0] || null;

  // The bundled 2013 theme also carries Claude's DC runtime, but its authored
  // props are unrelated (team1Name, playClockLength, and so on). Requiring the
  // two scores plus the game clock keeps that proven DOM bridge authoritative
  // and opts only compatible standalone scoreboards into this native bridge.
  if (!prop.awayScore || !prop.homeScore || !prop.clock) {
    report.reason = 'incompatible-scoreboard-props';
    report.supportedProps = unique(Object.values(prop));
    return report;
  }
  report.detected = true;

  const supported = new Set();
  const support = (field, property) => {
    if (property) supported.add(field);
  };
  support('away.name', prop.awayName);
  support('away.nickname', prop.awayNickname);
  support('away.record', prop.awayRecord);
  support('away.color', prop.awayColor);
  support('away.rank', prop.awayRank);
  support('away.score', prop.awayScore);
  support('away.timeouts', prop.awayTimeouts);
  support('away.logo', prop.awayLogo);
  support('home.name', prop.homeName);
  support('home.nickname', prop.homeNickname);
  support('home.record', prop.homeRecord);
  support('home.color', prop.homeColor);
  support('home.rank', prop.homeRank);
  support('home.score', prop.homeScore);
  support('home.timeouts', prop.homeTimeouts);
  support('home.logo', prop.homeLogo);
  support('game.down', prop.down);
  support('game.distance', prop.distance);
  if (prop.down && prop.distance) supported.add('game.downDistance');
  support('game.quarter', prop.quarter);
  support('game.clock', prop.clock);
  support('game.playClock', prop.playClock);
  if (prop.possession || prop.awayPossession) supported.add('away.possession');
  if (prop.possession || prop.homePossession) supported.add('home.possession');

  const capabilityFields = [
    'away.name', 'away.nickname', 'away.record', 'away.color', 'away.rank', 'away.score',
    'away.timeouts', 'away.possession', 'away.logo',
    'home.name', 'home.nickname', 'home.record', 'home.color', 'home.rank', 'home.score',
    'home.timeouts', 'home.possession', 'home.logo',
    'game.down', 'game.distance', 'game.downDistance', 'game.quarter',
    'game.clock', 'game.playClock',
  ];
  report.supported = capabilityFields.filter((field) => supported.has(field));
  report.staticMissing = capabilityFields.filter((field) => !supported.has(field));
  report.supportedProps = unique(Object.values(prop));

  const mapped = {};
  const propFields = {};
  const mapValue = (property, field, value) => {
    if (!property) return;
    const normalized = normalizeForProp(property, value);
    if (normalized === undefined) return;
    mapped[property] = normalized;
    propFields[property] = field;
  };

  mapValue(prop.awayName, 'away.name', readFirst(away, ['name']));
  mapValue(prop.awayNickname, 'away.nickname', readFirst(away, ['nickname', 'mascot']));
  mapValue(prop.awayRecord, 'away.record', readFirst(away, ['record']));
  mapValue(prop.awayColor, 'away.color', readFirst(away, ['color']));
  mapValue(prop.awayRank, 'away.rank', readFirst(away, ['rank']));
  mapValue(prop.awayScore, 'away.score', readFirst(away, ['score']));
  mapValue(prop.awayTimeouts, 'away.timeouts', readFirst(away, ['timeouts']));
  mapValue(prop.awayLogo, 'away.logo', readFirst(away, ['logo', 'logoUrl']));
  mapValue(prop.homeName, 'home.name', readFirst(home, ['name']));
  mapValue(prop.homeNickname, 'home.nickname', readFirst(home, ['nickname', 'mascot']));
  mapValue(prop.homeRecord, 'home.record', readFirst(home, ['record']));
  mapValue(prop.homeColor, 'home.color', readFirst(home, ['color']));
  mapValue(prop.homeRank, 'home.rank', readFirst(home, ['rank']));
  mapValue(prop.homeScore, 'home.score', readFirst(home, ['score']));
  mapValue(prop.homeTimeouts, 'home.timeouts', readFirst(home, ['timeouts']));
  mapValue(prop.homeLogo, 'home.logo', readFirst(home, ['logo', 'logoUrl']));
  mapValue(prop.down, 'game.down', game.down ?? parsedDownDistance.down);
  mapValue(prop.distance, 'game.distance', game.distance ?? parsedDownDistance.distance);
  mapValue(prop.quarter, 'game.quarter', parseQuarter(game.quarter));
  mapValue(prop.clock, 'game.clock', readFirst(game, ['clock']));
  mapValue(prop.playClock, 'game.playClock', readFirst(game, ['playClock']));

  let possessionSide = 'none';
  const gamePossession = String(game.possession || '').toLowerCase();
  if (gamePossession === 'away' || gamePossession === 'home') possessionSide = gamePossession;
  else if (away.possession === true) possessionSide = 'away';
  else if (home.possession === true) possessionSide = 'home';
  mapValue(prop.awayPossession, 'away.possession', possessionSide === 'away');
  mapValue(prop.homePossession, 'home.possession', possessionSide === 'home');

  if (prop.possession) {
    const optionValues = Array.isArray(propsMeta[prop.possession]?.options)
      ? propsMeta[prop.possession].options.map((option) => (
        option && typeof option === 'object' ? option.value : option
      )).filter((value) => value !== undefined && value !== null)
      : [];
    const timeoutTeamName = (property) => {
      if (!property || /^(away|home)timeouts?$/i.test(property)) return '';
      const raw = property.replace(/timeouts?$/i, '').replace(/[_-]+/g, ' ').trim();
      return raw ? raw.replace(/\b\w/g, (letter) => letter.toUpperCase()) : '';
    };
    const matchOption = (candidates) => {
      for (const candidate of candidates) {
        const found = optionValues.find((option) => String(option).toLowerCase() === String(candidate || '').toLowerCase());
        if (found !== undefined) return found;
      }
      return undefined;
    };
    const candidates = possessionSide === 'away'
      ? ['away', away.name, timeoutTeamName(prop.awayTimeouts)]
      : possessionSide === 'home'
        ? ['home', home.name, timeoutTeamName(prop.homeTimeouts)]
        : ['none', 'neither', 'no possession'];
    const possessionValue = matchOption(candidates) ?? possessionSide;
    mapValue(prop.possession, 'game.possession', possessionValue);
  }

  const mappedProps = Object.keys(mapped);
  if (!mappedProps.length) {
    report.reason = 'no-state-values';
    return report;
  }

  const cacheKey = '__CFB27_CLAUDE_DC_BRIDGE_CACHE__';
  const cached = host[cacheKey];
  const liveOverrides = registryEntry.propOverrides && typeof registryEntry.propOverrides === 'object'
    ? registryEntry.propOverrides
    : null;
  const previous = liveOverrides || (
    cached?.registryEntry === registryEntry && cached?.rootName === report.rootName
      ? cached.mapped
      : {}
  );
  const sameValue = (left, right) => left === right || (Number.isNaN(left) && Number.isNaN(right));
  const changedProps = mappedProps.filter((name) => !sameValue(previous?.[name], mapped[name]));
  if (!changedProps.length) {
    report.reason = 'unchanged';
    return report;
  }

  const nextOverrides = {};
  for (const name of declaredProps) {
    if (liveOverrides && hasOwn(liveOverrides, name)) nextOverrides[name] = liveOverrides[name];
  }
  Object.assign(nextOverrides, mapped);
  try {
    host.__dcSetProps.call(host, report.rootName, nextOverrides);
  } catch (error) {
    report.reason = 'apply-failed';
    report.error = error instanceof Error ? error.message : String(error);
    return report;
  }

  try {
    host[cacheKey] = { registryEntry, rootName: report.rootName, mapped: { ...mapped } };
  } catch {
    // The native registry remains the authoritative repeat-call cache when the
    // guest global is non-extensible.
  }
  report.didApply = true;
  report.reason = 'applied';
  report.appliedProps = changedProps;
  report.applied = true;
  report.appliedFields = unique(changedProps.map((name) => propFields[name]));
  return report;
}

// Keep the browser-global binding name unique. This file is loaded as a
// classic script beside logo-source.js, whose own top-level `const api` would
// otherwise make Chromium reject this script before the bridge is exposed.
const claudeDcBridgeApi = Object.freeze({ applyClaudeDcScoreboardState });

if (typeof module !== 'undefined' && module.exports) module.exports = claudeDcBridgeApi;
if (typeof globalThis !== 'undefined') globalThis.CFB27ClaudeDcBridge = claudeDcBr
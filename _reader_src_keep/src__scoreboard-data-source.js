tate,
};
'use strict';

const SCOREBOARD_DATA_SOURCES = Object.freeze({
  AUTO: 'auto',
  RAM: 'ram',
  SCREEN: 'screen',
});

function normalizeScoreboardDataSource(value) {
  const normalized = String(value || '').trim().toLowerCase();
  return Object.values(SCOREBOARD_DATA_SOURCES).includes(normalized)
    ? normalized
    : SCOREBOARD_DATA_SOURCES.AUTO;
}

function scoreboardDataSourceLabel(value) {
  switch (normalizeScoreboardDataSource(value)) {
    case SCOREBOARD_DATA_SOURCES.RAM: return 'RAM reader';
    case SCOREBOARD_DATA_SOURCES.SCREEN: return 'Screen reader';
    default: return 'Automatic';
  }
}

function usesRamReader(value) {
  return normalizeScoreboardDataSource(value) !== SCOREBOARD_DATA_SOURCES.SCREEN;
}

function normalizeRamInteger(value, { min = Number.MIN_SAFE_INTEGER, max = Number.MAX_SAFE_INTEGER } = {}) {
  if (value === null || value === undefined) return null;
  if (typeof value === 'string' && value.trim() === '') return null;
  const number = Number(value);
  return Number.isInteger(number) && number >= min && number <= max ? number : null;
}

function applyScoreboardDataSource(screenState, ramState, value) {
  const mode = normalizeScoreboardDataSource(value);
  const screen = screenState && typeof screenState === 'object' ? screenState : {};
  const ram = ramState && typeof ramState === 'object' ? ramState : null;
  if (mode === SCOREBOARD_DATA_SOURCES.SCREEN) return screen;
  if (!ram && mode === SCOREBOARD_DATA_SOURCES.AUTO) return screen;

  const screenMeta = screen.meta || {};
  if (!ram) {
    return {
      away: {},
      home: {},
      game: {},
      meta: {
        source: 'ram-waiting',
        visible: screenMeta.visible,
        confidence: 0,
        updatedAt: screenMeta.updatedAt || new Date().toISOString(),
      },
    };
  }

  const base = mode === SCOREBOARD_DATA_SOURCES.RAM
    ? { away: {}, home: {}, game: {}, meta: {} }
    : screen;
  return {
    ...base,
    away: { ...(base.away || {}), ...(ram.away || {}) },
    home: { ...(base.home || {}), ...(ram.home || {}) },
    game: { ...(base.game || {}), ...(ram.game || {}) },
    meta: {
      ...(base.meta || {}),
      ...(ram.meta || {}),
      source: mode === SCOREBOARD_DATA_SOURCES.RAM ? 'ram' : 'ram+screen',
      // RAM-only mode has no screen reader. A live RAM document is itself the
      // visibility signal; Automatic mode still follows the native scorebug.
      visible: mode === SCOREBOARD_DATA_SOURCES.RAM ? true : screenMeta.visible,
      confidence: mode === SCOREBOARD_DATA_SOURCES.RAM ? 1 : screenMeta.confidence,
      updatedAt: ram.meta?.ramUpdatedAt || screenMeta.updatedAt || new Date().toISOString(),
    },
  };
}

module.exports = {
  SCOREBOARD_DATA_SOURCES,
  applyScoreboardDataSource,
  normalizeRamInteger,
  normalizeScoreboardDataSource,
  scoreboardDataSourceLabel,
  usesRamRe
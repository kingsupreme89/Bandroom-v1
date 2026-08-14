
'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { buildRamReaderReport } = require('../src/ram-reader-report');

const NOW = Date.parse('2026-08-12T12:00:00Z');

function freshStatus(message) {
  return { updatedAt: new Date(NOW - 1000).toISOString(), message };
}

function liveDocument(overrides = {}) {
  return {
    status: 'live',
    updatedAt: new Date(NOW - 1000).toISOString(),
    process: { name: 'CollegeFB27', id: 1234, exeVersion: '1.0.5.0', ...overrides.process },
    ram: {
      quarter: { available: true },
      gameClockSeconds: { available: true },
      homeScore: { available: true },
      awayScore: { available: true },
      ...overrides.ram,
    },
    away: { name: 'USC', nameSource: 'ram', ...overrides.away },
    home: { name: 'Pittsburgh', nameSource: 'ram', ...overrides.home },
    game: { downDistanceSource: 'ram', downDistance: '1st & 10', ...overrides.game },
    discovery: {
      teamRole: 'no labeled-vector evidence; recovered names from pool fallback',
      rankBind: 'bound (away rank 15 record 0-0, home rank 0 record 0-0, order away-lower)',
      timeoutBind: 'bound (home=3 away=3)',
      ...overrides.discovery,
    },
  };
}

function baseInput(overrides = {}) {
  return {
    now: NOW,
    appVersion: '1.3.55',
    readerEnabled: true,
    readerExePresent: true,
    readerProcessRunning: true,
    status: freshStatus('RAM export LIVE: 1st 4:41 | ...'),
    live: liveDocument(),
    gameWindowDetected: true,
    ...overrides,
  };
}

test('a healthy live game reads as OK on every field', () => {
  const report = buildRamReaderReport(baseInput());
  assert.equal(report.level, 'ok');
  const states = Object.fromEntries(report.lines.map((line) => [line.label, line.state]));
  assert.equal(states['Scores & clock'], 'ok');
  assert.equal(states['Down & distance'], 'ok');
  assert.equal(states['Team names'], 'ok');
  assert.equal(states['Ranks & records'], 'ok');
  assert.equal(states.Timeouts, 'ok');
  assert.match(report.reportText, /Game version: 1\.0\.5\.0/);
});

test('a missing reader exe names the antivirus cause', () => {
  const report = buildRamReaderReport(baseInput({
    readerExePresent: false, readerProcessRunning: false, status: null, live: null,
  }));
  assert.equal(report.level, 'bad');
  assert.match(report.headline, /antivirus/i);
});

test('access denied explains the administrator mismatch', () => {
  const report = buildRamReaderReport(baseInput({
    status: freshStatus('RAM service error: Windows would not grant read-only access to CollegeFB27.exe.'),
    live: null,
  }));
  assert.equal(report.level, 'bad');
  assert.match(report.headline, /administrator/i);
});

test('waiting for the game with a visible game window flags the process name', () => {
  const report = buildRamReaderReport(baseInput({
    status: freshStatus('RAM service: waiting for CollegeFB27.exe'),
    live: null,
    gameWindowDetected: true,
  }));
  assert.match(report.headline, /cannot find a game process/i);
});

test('acquisition states tell the user to unpause and play', () => {
  const report = buildRamReaderReport(baseInput({
    status: freshStatus('RAM export: confirming synchronized scoreboard (1/3)'),
    live: null,
  }));
  assert.equal(report.level, 'info');
  assert.match(report.headline, /unpause/i);
});

test('a tied-score rank wait is explained as normal', () => {
  const report = buildRamReaderReport(baseInput({
    live: liveDocument({
      discovery: { rankBind: 'no orientation: scores 0-0 (tied, so possession is required), possession unavailable, 2 objects' },
    }),
  }));
  const ranks = report.lines.find((line) => line.label === 'Ranks & records');
  assert.equal(ranks.state, 'info');
  assert.match(ranks.text, /normal/i);
});

test('missing ScoreHud objects point at a possible game update', () => {
  const report = buildRamReaderReport(baseInput({
    live: liveDocument({
      discovery: { rankBind: 'no bind: only 0 ScoreHud team object(s) found, need 2' },
    }),
  }));
  const ranks = report.lines.find((line) => line.label === 'Ranks & records');
  assert.equal(ranks.state, 'warn');
  assert.match(ranks.text, /updated|version/i);
});

test('unidentified names mention custom teams and keep other fields working', () => {
  const report = buildRamReaderReport(baseInput({
    live: liveDocument({
      away: { name: 'Away', nameSource: 'ram-pending' },
      home: { name: 'Home', nameSource: 'ram-pending' },
      discovery: { teamRole: 'no labeled-vector evidence; pool fallback found no usable pair' },
    }),
  }));
  const names = report.lines.find((line) => line.label === 'Team names');
  assert.equal(names.state, 'warn');
  assert.match(names.text, /TeamBuilder/i);
});

test('the pasted report carries the raw diagnostics for the developer', () => {
  const report = buildRamReaderReport(baseInput());
  assert.match(report.reportText, /rankBind: bound/);
  assert.match(report.reportText, /timeoutBind: bound/);
  assert.match(report.reportText, /teamRole: /);
});
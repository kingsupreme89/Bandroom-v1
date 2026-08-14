
'use strict';

const SAFE_PROFILE = 'safe';
const AGGRESSIVE_PROFILE = 'aggressive';
const PROFILE_KEYS = Object.freeze([SAFE_PROFILE, AGGRESSIVE_PROFILE]);

function clampInteger(value, fallback, minimum, maximum) {
  const numeric = Number(value);
  if (!Number.isFinite(numeric)) return fallback;
  return Math.min(maximum, Math.max(minimum, Math.round(numeric)));
}

function normalizeReadingProfile(value, fallback = SAFE_PROFILE) {
  const key = String(value || '').trim().toLowerCase();
  if (PROFILE_KEYS.includes(key)) return key;
  return PROFILE_KEYS.includes(fallback) ? fallback : SAFE_PROFILE;
}

function configuredConfidence(recognition) {
  return Math.max(0, Math.min(1, Number(recognition.minimumConfidence ?? 20) / 100));
}

/**
 * Safe deliberately reproduces the complete v1.3.26 live-reader contract.
 * Aggressive is isolated here so selecting it cannot silently mutate Safe.
 */
function resolveReaderBehavior(settings = {}) {
  const recognition = settings.recognition || {};
  const capture = settings.capture || {};
  const key = normalizeReadingProfile(recognition.readingProfile);
  const confidence = configuredConfidence(recognition);
  const safe = {
    key: SAFE_PROFILE,
    label: 'Safe',
    description: 'The exact proven v1.3.26 reader behavior.',
    experimental: false,
    staticIntervalMs: 900,
    captureFps: clampInteger(capture.fps, 4, 1, 10),
    validatorOptions: {
      fieldConfidence: confidence,
      gameplayFieldConfidence: 0.35,
      anchorConfidence: Math.min(confidence, 0.35),
      stableFrames: clampInteger(recognition.stableFrames, 2, 1, 10),
      visibleFrames: clampInteger(recognition.presentFramesToShow, 2, 1, 10),
      hiddenFrames: clampInteger(recognition.missingFramesToHide, 3, 1, 10),
      strictScores: true,
      strictPlayClock: true,
      scoreStableFrames: 2,
      scoreBaselineStableFrames: 2,
      requireClockResetForQuarterAdvance: true,
      repairSkippedQuarterReads: true,
    },
  };
  if (key === SAFE_PROFILE) return safe;
  return {
    key: AGGRESSIVE_PROFILE,
    label: 'Aggressive',
    description: 'Faster capture and acceptance with plausibility guards still enabled.',
    experimental: true,
    staticIntervalMs: 350,
    captureFps: Math.max(6, safe.captureFps),
    validatorOptions: {
      ...safe.validatorOptions,
      fieldConfidence: Math.min(confidence, 0.24),
      gameplayFieldConfidence: 0.28,
      anchorConfidence: Math.min(confidence, 0.24),
      stableFrames: 1,
      identityStableFrames: 1,
      visibleFrames: 1,
      scoreStableFrames: 1,
      scoreBaselineStableFrames: 1,
      correctionStableFrames: 3,
      homeTimeoutDropStableFrames: 2,
    },
  };
}

module.exports = Object.freeze({
  AGGRESSIVE_PROFILE,
  PROFILE_KEYS,
  SAFE_PROFILE,
  normalizeReadingProfile,
  resolveReaderBehavior,
});
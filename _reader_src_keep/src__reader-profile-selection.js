
'use strict';

/**
 * Deterministic reader-profile selection from captured game pixels.
 *
 * Exact standard, ultrawide, and super-ultrawide shapes get their own
 * calibration profile. Windowed/custom shapes select the closest combination
 * of height and aspect ratio. The configured resolution is only a startup
 * fallback until the app has measured the game or the live capture stream.
 */

const { PROFILE_DIMENSIONS, normalizeProfileKey } = require('./reader-profile');

const STANDARD_ASPECT_RATIO = 16 / 9;
// Covers tiny window-border differences without classifying 21:9 as 16:9.
const ASPECT_TOLERANCE = 0.05;

function positiveNumber(value) {
  const number = Number(value);
  return Number.isFinite(number) && number > 0 ? number : null;
}

function dimensionPair(width, height) {
  const resolvedWidth = positiveNumber(width);
  const resolvedHeight = positiveNumber(height);
  return resolvedWidth && resolvedHeight
    ? { width: resolvedWidth, height: resolvedHeight }
    : null;
}

/** Nearest standard-aspect profile by height; ties favor the larger size. */
function profileKeyForHeight(height) {
  let bestKey = null;
  let bestDistance = Infinity;
  for (const [key, dimensions] of Object.entries(PROFILE_DIMENSIONS)) {
    if (Math.abs((dimensions.width / dimensions.height) - STANDARD_ASPECT_RATIO) > ASPECT_TOLERANCE) {
      continue;
    }
    const distance = Math.abs(dimensions.height - height);
    if (distance < bestDistance
      || (distance === bestDistance && dimensions.height > PROFILE_DIMENSIONS[bestKey].height)) {
      bestDistance = distance;
      bestKey = key;
    }
  }
  return { key: bestKey, heightDelta: bestDistance };
}

function profileKeyForDimensions(width, height) {
  const evidence = dimensionPair(width, height);
  if (!evidence) return profileKeyForHeight(Number(height));
  const aspectRatio = evidence.width / evidence.height;
  let bestKey = null;
  let bestScore = Infinity;
  let bestHeightDelta = Infinity;
  for (const [key, dimensions] of Object.entries(PROFILE_DIMENSIONS)) {
    const heightDelta = Math.abs(dimensions.height - evidence.height);
    const heightScore = Math.abs(Math.log(evidence.height / dimensions.height));
    const aspectScore = Math.abs(Math.log(aspectRatio / (dimensions.width / dimensions.height)));
    const score = (heightScore * 2) + aspectScore;
    if (score < bestScore
      || (score === bestScore && heightDelta < bestHeightDelta)
      || (score === bestScore && heightDelta === bestHeightDelta
        && dimensions.height > PROFILE_DIMENSIONS[bestKey].height)) {
      bestScore = score;
      bestHeightDelta = heightDelta;
      bestKey = key;
    }
  }
  return { key: bestKey, heightDelta: bestHeightDelta };
}

/**
 * Precedence: live capture dimensions, game-window bounds, configured value,
 * then the catalog default.
 */
function selectReaderProfileKey(input = {}) {
  const configuredKey = normalizeProfileKey(input.configuredKey, null);
  const fallbackKey = configuredKey
    || normalizeProfileKey(input.defaultKey, null)
    || normalizeProfileKey(null);

  const measured = dimensionPair(input.sourceWidth, input.sourceHeight);
  const windowed = dimensionPair(input.gameWidth, input.gameHeight);
  const evidence = measured || windowed;
  const origin = measured ? 'capture' : (windowed ? 'game-window' : null);

  if (!evidence) {
    return {
      key: fallbackKey,
      origin: configuredKey ? 'configured' : 'default',
      sourceWidth: null,
      sourceHeight: null,
      aspectRatio: null,
      standardAspect: true,
      heightDelta: null,
      configuredKey,
      overriddenConfiguration: false,
    };
  }

  const aspectRatio = evidence.width / evidence.height;
  const { key, heightDelta } = profileKeyForDimensions(evidence.width, evidence.height);
  return {
    key,
    origin,
    sourceWidth: evidence.width,
    sourceHeight: evidence.height,
    aspectRatio,
    standardAspect: Math.abs(aspectRatio - STANDARD_ASPECT_RATIO) <= ASPECT_TOLERANCE,
    heightDelta,
    configuredKey,
    overriddenConfiguration: Boolean(configuredKey) && configuredKey !== key,
  };
}

module.exports = {
  ASPECT_TOLERANCE,
  STANDARD_ASPECT_RATIO,
  profileKeyForDimensions,
  profileKeyForHeight,
  selectReaderProfileKey,
};

'use strict';

const fs = require('node:fs');
const path = require('node:path');

const CATALOG_SCHEMA = 'cfb27-reader-profiles/1';
const DEFAULT_PROFILE_KEY = '2160p';
const PROFILE_KEYS = Object.freeze([
  '720p',
  '1080p',
  '1080p-ultrawide',
  '1440p',
  '1440p-ultrawide',
  '1440p-super-ultrawide',
  '1600p-ultrawide',
  '2160p',
]);
const PROFILE_DIMENSIONS = deepFreeze({
  '720p': { width: 1280, height: 720 },
  '1080p': { width: 1920, height: 1080 },
  '1080p-ultrawide': { width: 2560, height: 1080 },
  '1440p': { width: 2560, height: 1440 },
  '1440p-ultrawide': { width: 3440, height: 1440 },
  '1440p-super-ultrawide': { width: 5120, height: 1440 },
  '1600p-ultrawide': { width: 3840, height: 1600 },
  '2160p': { width: 3840, height: 2160 },
});
const LEGACY_REQUIRED_ROI_KEYS = Object.freeze([
  'away.name',
  'away.score',
  'home.name',
  'home.score',
  'game.quarter',
  'game.clock',
  'game.playClock',
  'game.downDistance',
]);
const ADDED_ROI_KEYS = Object.freeze([
  'away.record',
  'away.timeouts',
  'away.possession',
  'home.record',
  'home.timeouts',
]);
const REQUIRED_ROI_KEYS = Object.freeze([
  'away.name',
  'away.record',
  'away.timeouts',
  'away.score',
  'away.possession',
  'home.name',
  'home.record',
  'home.timeouts',
  'home.score',
  'game.quarter',
  'game.clock',
  'game.playClock',
  'game.downDistance',
]);
// These guard against nonsense geometry - a box dragged down to a few pixels -
// so they must sit BELOW what a legitimate donor actually measures. They did
// not. The horizontal donor's canvas is 1101x156.41 design px at rimeScale
// 0.337, which renders 372x53 real pixels at 1080p. The old 72px floor made the
// true donor geometry impossible to describe, and the profile that satisfied it
// was 108px tall - twice the donor - which is a large part of why the shipped
// boxes sat off the artwork.
//
// The 2px ROI floor is marginal, and it is the DONOR that is marginal: the
// timeouts bar is authored 6 design px tall, which is 2 real px at 1080p. That
// bar should be made taller in a future donor build, but refusing to load the
// true geometry over it helps nobody.
const MINIMUM_READ_PIXELS = Object.freeze({ width: 96, height: 40 });
const MINIMUM_ROI_PIXELS = Object.freeze({ width: 6, height: 2 });
const DEFAULT_CATALOG_PATH = path.resolve(__dirname, '../config/reader-profiles.json');

const PROFILE_ALIASES = Object.freeze({
  '720': '720p',
  '720p': '720p',
  '1280x720': '720p',
  '1280×720': '720p',
  '1080': '1080p',
  '1080p': '1080p',
  '1920x1080': '1080p',
  '1080p-ultrawide': '1080p-ultrawide',
  '2560x1080': '1080p-ultrawide',
  '1920×1080': '1080p',
  '1440': '1440p',
  '1440p': '1440p',
  '2560x1440': '1440p',
  '1440p-ultrawide': '1440p-ultrawide',
  '3440x1440': '1440p-ultrawide',
  '1440p-super-ultrawide': '1440p-super-ultrawide',
  '5120x1440': '1440p-super-ultrawide',
  '1600p-ultrawide': '1600p-ultrawide',
  '3840x1600': '1600p-ultrawide',
  '2560×1440': '1440p',
  '2160': '2160p',
  '2160p': '2160p',
  '3840x2160': '2160p',
  '3840×2160': '2160p',
  '4k': '2160p',
  'uhd': '2160p',
});

function isPlainObject(value) {
  return Boolean(value && typeof value === 'object' && !Array.isArray(value));
}

function cloneJson(value) {
  if (value === undefined) return undefined;
  return JSON.parse(JSON.stringify(value));
}

function deepFreeze(value) {
  if (!value || typeof value !== 'object' || Object.isFrozen(value)) return value;
  for (const child of Object.values(value)) deepFreeze(child);
  return Object.freeze(value);
}

function normalizedAlias(value) {
  return String(value ?? '')
    .trim()
    .toLowerCase()
    .replaceAll(' ', '');
}

function normalizeProfileKey(value, fallback = DEFAULT_PROFILE_KEY) {
  const normalized = PROFILE_ALIASES[normalizedAlias(value)];
  if (normalized) return normalized;
  if (fallback === null) return null;
  return PROFILE_ALIASES[normalizedAlias(fallback)] || DEFAULT_PROFILE_KEY;
}

function validationError(code, pathName, message) {
  return Object.freeze({ code, path: pathName, message });
}

function regionErrors(value, pathName) {
  if (!isPlainObject(value)) {
    return [validationError('region-object', pathName, `${pathName} must be a rectangle object.`)];
  }

  const parts = ['x', 'y', 'width', 'height'];
  const errors = [];
  for (const part of parts) {
    if (typeof value[part] !== 'number' || !Number.isFinite(value[part])) {
      errors.push(validationError(
        'region-number',
        `${pathName}.${part}`,
        `${pathName}.${part} must be a finite number.`,
      ));
    }
  }
  if (errors.length) return errors;

  if (value.x < 0 || value.x > 1) {
    errors.push(validationError('region-x', `${pathName}.x`, `${pathName}.x must be between 0 and 1.`));
  }
  if (value.y < 0 || value.y > 1) {
    errors.push(validationError('region-y', `${pathName}.y`, `${pathName}.y must be between 0 and 1.`));
  }
  if (value.width <= 0 || value.width > 1) {
    errors.push(validationError('region-width', `${pathName}.width`, `${pathName}.width must be greater than 0 and at most 1.`));
  }
  if (value.height <= 0 || value.height > 1) {
    errors.push(validationError('region-height', `${pathName}.height`, `${pathName}.height must be greater than 0 and at most 1.`));
  }
  if (value.x + value.width > 1 + Number.EPSILON * 8) {
    errors.push(validationError('region-right', pathName, `${pathName} extends beyond its right edge.`));
  }
  if (value.y + value.height > 1 + Number.EPSILON * 8) {
    errors.push(validationError('region-bottom', pathName, `${pathName} extends beyond its bottom edge.`));
  }
  return errors;
}

function regionToPixels(region, width, height) {
  const left = Math.round(region.x * width);
  const top = Math.round(region.y * height);
  const right = Math.round((region.x + region.width) * width);
  const bottom = Math.round((region.y + region.height) * height);
  return {
    x: left,
    y: top,
    width: Math.max(0, right - left),
    height: Math.max(0, bottom - top),
  };
}

function validateReaderProfileDraft(profileKeyValue, draft) {
  const key = normalizeProfileKey(profileKeyValue, null);
  const errors = [];
  const pixelGeometry = { readRegion: null, rois: {} };

  if (!key) {
    errors.push(validationError(
      'profile-key',
      'key',
      `Unsupported reader profile key: ${String(profileKeyValue ?? '')}.`,
    ));
  }
  if (!isPlainObject(draft)) {
    errors.push(validationError('profile-object', 'profile', 'Reader profile must be an object.'));
    return deepFreeze({ ok: false, key, errors, pixelGeometry });
  }

  const expected = key ? PROFILE_DIMENSIONS[key] : null;
  if (!Number.isInteger(draft.captureWidth) || draft.captureWidth <= 0) {
    errors.push(validationError('capture-width', 'captureWidth', 'captureWidth must be a positive integer.'));
  } else if (expected && draft.captureWidth !== expected.width) {
    errors.push(validationError(
      'capture-width-profile',
      'captureWidth',
      `${key} must capture at ${expected.width} pixels wide.`,
    ));
  }
  if (!Number.isInteger(draft.captureHeight) || draft.captureHeight <= 0) {
    errors.push(validationError('capture-height', 'captureHeight', 'captureHeight must be a positive integer.'));
  } else if (expected && draft.captureHeight !== expected.height) {
    errors.push(validationError(
      'capture-height-profile',
      'captureHeight',
      `${key} must capture at ${expected.height} pixels high.`,
    ));
  }
  if (draft.roiSpace !== 'read-region') {
    errors.push(validationError(
      'roi-space',
      'roiSpace',
      'roiSpace must be "read-region".',
    ));
  }

  const readErrors = regionErrors(draft.readRegion, 'readRegion');
  errors.push(...readErrors);
  const dimensionsUsable = Number.isInteger(draft.captureWidth)
    && draft.captureWidth > 0
    && Number.isInteger(draft.captureHeight)
    && draft.captureHeight > 0;
  if (!readErrors.length && dimensionsUsable) {
    const readPixels = regionToPixels(draft.readRegion, draft.captureWidth, draft.captureHeight);
    pixelGeometry.readRegion = readPixels;
    if (readPixels.width < MINIMUM_READ_PIXELS.width || readPixels.height < MINIMUM_READ_PIXELS.height) {
      errors.push(validationError(
        'read-region-minimum',
        'readRegion',
        `Read region must be at least ${MINIMUM_READ_PIXELS.width}×${MINIMUM_READ_PIXELS.height} pixels; got ${readPixels.width}×${readPixels.height}.`,
      ));
    }
  }

  if (!isPlainObject(draft.rois)) {
    errors.push(validationError('rois-object', 'rois', 'rois must be an object.'));
  } else {
    for (const binding of REQUIRED_ROI_KEYS) {
      if (!Object.prototype.hasOwnProperty.call(draft.rois, binding)) {
        errors.push(validationError(
          'roi-required',
          `rois.${binding}`,
          `Missing required reader region: ${binding}.`,
        ));
        continue;
      }
      const roi = draft.rois[binding];
      const roiValidationErrors = regionErrors(roi, `rois.${binding}`);
      errors.push(...roiValidationErrors);
      if (!roiValidationErrors.length && pixelGeometry.readRegion) {
        const roiPixels = regionToPixels(
          roi,
          pixelGeometry.readRegion.width,
          pixelGeometry.readRegion.height,
        );
        pixelGeometry.rois[binding] = roiPixels;
        if (roiPixels.width < MINIMUM_ROI_PIXELS.width || roiPixels.height < MINIMUM_ROI_PIXELS.height) {
          errors.push(validationError(
            'roi-minimum',
            `rois.${binding}`,
            `${binding} must be at least ${MINIMUM_ROI_PIXELS.width}×${MINIMUM_ROI_PIXELS.height} pixels; got ${roiPixels.width}×${roiPixels.height}.`,
          ));
        }
      }
    }
  }

  return deepFreeze({
    ok: errors.length === 0,
    key,
    errors,
    pixelGeometry,
  });
}

function validateReaderProfileCatalog(catalog) {
  const errors = [];
  if (!isPlainObject(catalog)) {
    errors.push(validationError('catalog-object', 'catalog', 'Reader-profile catalog must be an object.'));
    return deepFreeze({ ok: false, errors });
  }
  if (catalog.schema !== CATALOG_SCHEMA) {
    errors.push(validationError(
      'catalog-schema',
      'schema',
      `Reader-profile catalog schema must be ${CATALOG_SCHEMA}.`,
    ));
  }
  if (!Number.isInteger(catalog.catalogVersion) || catalog.catalogVersion < 1) {
    errors.push(validationError(
      'catalog-version',
      'catalogVersion',
      'catalogVersion must be a positive integer.',
    ));
  }
  if (!isPlainObject(catalog.profiles)) {
    errors.push(validationError('catalog-profiles', 'profiles', 'Catalog profiles must be an object.'));
  } else {
    for (const key of PROFILE_KEYS) {
      if (!Object.prototype.hasOwnProperty.call(catalog.profiles, key)) {
        errors.push(validationError(
          'catalog-profile-required',
          `profiles.${key}`,
          `Catalog is missing the required ${key} factory profile.`,
        ));
        continue;
      }
      const result = validateReaderProfileDraft(key, catalog.profiles[key]);
      for (const error of result.errors) {
        errors.push(validationError(
          error.code,
          `profiles.${key}.${error.path}`,
          `${key}: ${error.message}`,
        ));
      }
    }
  }
  return deepFreeze({ ok: errors.length === 0, errors });
}

function parseCatalogSource(source) {
  if (source === undefined || source === null) {
    return JSON.parse(fs.readFileSync(DEFAULT_CATALOG_PATH, 'utf8'));
  }
  if (typeof source === 'string') {
    return JSON.parse(fs.readFileSync(path.resolve(source), 'utf8'));
  }
  if (Buffer.isBuffer(source)) return JSON.parse(source.toString('utf8'));
  if (isPlainObject(source)) return cloneJson(source);
  throw new TypeError('Reader-profile catalog source must be a path, Buffer, or object.');
}

function loadReaderProfileCatalog(source) {
  const catalog = parseCatalogSource(source);
  const result = validateReaderProfileCatalog(catalog);
  if (!result.ok) {
    const error = new Error(`Invalid reader-profile catalog: ${result.errors.map((item) => item.message).join(' ')}`);
    error.code = 'ERR_READER_PROFILE_CATALOG';
    error.validationErrors = result.errors;
    throw error;
  }
  return deepFreeze(catalog);
}

function assertValidCatalog(catalog) {
  const validation = validateReaderProfileCatalog(catalog);
  if (!validation.ok) {
    const error = new Error(`Invalid reader-profile catalog: ${validation.errors.map((item) => item.message).join(' ')}`);
    error.code = 'ERR_READER_PROFILE_CATALOG';
    error.validationErrors = validation.errors;
    throw error;
  }
}

/**
 * Complete profiles from older releases with reader boxes introduced later.
 * Existing placement and calibrated boxes are preserved; only missing added
 * boxes are copied from the matching factory profile. An incomplete or
 * otherwise unknown draft is left untouched so the normal validator can fail
 * closed.
 */
function upgradeLegacyProfileDraft(catalog, profileKeyValue, draft) {
  const key = normalizeProfileKey(profileKeyValue, null);
  if (!key || !isPlainObject(draft) || !isPlainObject(draft.rois)) return cloneJson(draft);
  const hasLegacySet = LEGACY_REQUIRED_ROI_KEYS.every((binding) => (
    Object.prototype.hasOwnProperty.call(draft.rois, binding)
  ));
  const missingAdded = ADDED_ROI_KEYS.filter((binding) => (
    !Object.prototype.hasOwnProperty.call(draft.rois, binding)
  ));
  if (!hasLegacySet || !missingAdded.length) return cloneJson(draft);

  const upgraded = cloneJson(draft);
  const factoryRois = catalog.profiles?.[key]?.rois || {};
  const orderedRois = {};
  for (const binding of REQUIRED_ROI_KEYS) {
    const source = Object.prototype.hasOwnProperty.call(upgraded.rois, binding)
      ? upgraded.rois[binding]
      : factoryRois[binding];
    if (source !== undefined) orderedRois[binding] = cloneJson(source);
  }
  upgraded.rois = orderedRois;
  return upgraded;
}

function upgradeLegacyReaderProfileOverrides(catalog, overrides) {
  assertValidCatalog(catalog);
  const source = isPlainObject(overrides) ? overrides : {};
  const upgraded = {};
  const upgradedKeys = [];
  for (const [rawKey, draft] of Object.entries(source)) {
    const key = normalizeProfileKey(rawKey, null);
    const nextDraft = key ? upgradeLegacyProfileDraft(catalog, key, draft) : cloneJson(draft);
    upgraded[rawKey] = nextDraft;
    if (JSON.stringify(nextDraft) !== JSON.stringify(draft)) upgradedKeys.push(rawKey);
  }
  return deepFreeze({ overrides: upgraded, upgradedKeys });
}

function approximatelyEqual(left, right, epsilon = 1e-7) {
  return Math.abs(Number(left) - Number(right)) <= epsilon * Math.max(1, Math.abs(Number(left)), Math.abs(Number(right)));
}

/**
 * Remove saved profile overrides that predate or distort the protected
 * horizontal layout. A legitimate override may translate the factory box and
 * uniformly scale it, but it must keep the factory capture size and all ROI
 * coordinates exactly proportional.
 */
function removeIncompatibleReaderProfileOverrides(catalog, overrides) {
  assertValidCatalog(catalog);
  const source = isPlainObject(overrides) ? overrides : {};
  const cleaned = cloneJson(source);
  const removedKeys = [];

  for (const [rawKey, draft] of Object.entries(source)) {
    const key = normalizeProfileKey(rawKey, null);
    if (!key) continue;
    const factory = catalog.profiles[key];
    const validation = validateReaderProfileDraft(key, draft);
    const widthScale = Number(draft?.readRegion?.width) / factory.readRegion.width;
    const heightScale = Number(draft?.readRegion?.height) / factory.readRegion.height;
    const captureMatches = Number(draft?.captureWidth) === factory.captureWidth
      && Number(draft?.captureHeight) === factory.captureHeight;
    const scaleMatches = Number.isFinite(widthScale)
      && Number.isFinite(heightScale)
      && widthScale > 0
      && approximatelyEqual(widthScale, heightScale);
    const roiLayoutMatches = REQUIRED_ROI_KEYS.every((binding) => (
      ['x', 'y', 'width', 'height'].every((part) => (
        approximatelyEqual(draft?.rois?.[binding]?.[part], factory.rois[binding][part])
      ))
    ));

    if (!validation.ok || !captureMatches || !scaleMatches || !roiLayoutMatches) {
      delete cleaned[rawKey];
      removedKeys.push(rawKey);
    }
  }

  return deepFreeze({ overrides: cleaned, removedKeys });
}

function resolveEffectiveReaderProfile(catalog, overrides, profileKeyValue) {
  assertValidCatalog(catalog);
  const key = normalizeProfileKey(profileKeyValue);
  const overrideMap = isPlainObject(overrides) ? overrides : {};
  const hasOverride = Object.prototype.hasOwnProperty.call(overrideMap, key);
  const candidateOverride = hasOverride
    ? upgradeLegacyProfileDraft(catalog, key, overrideMap[key])
    : null;
  const overrideValidation = hasOverride
    ? validateReaderProfileDraft(key, candidateOverride)
    : null;
  const useOverride = Boolean(overrideValidation?.ok);
  const profile = cloneJson(useOverride ? candidateOverride : catalog.profiles[key]);
  return deepFreeze({
    key,
    origin: useOverride ? 'custom' : 'factory',
    profile,
    fallbackReason: hasOverride && !useOverride ? 'invalid-override' : null,
    errors: hasOverride && !useOverride ? cloneJson(overrideValidation.errors) : [],
  });
}

function migrateLegacyCaptureToProfile(catalog, legacyCapture, selectedKeyValue, overrides = {}) {
  assertValidCatalog(catalog);
  const key = normalizeProfileKey(selectedKeyValue);
  const nextOverrides = isPlainObject(overrides) ? cloneJson(overrides) : {};
  if (!isPlainObject(legacyCapture)
    || !Object.prototype.hasOwnProperty.call(legacyCapture, 'readRegion')
    || !Object.prototype.hasOwnProperty.call(legacyCapture, 'rois')) {
    return {
      key,
      migrated: false,
      reason: 'no-legacy-calibration',
      errors: [],
      overrides: nextOverrides,
    };
  }
  if (Object.prototype.hasOwnProperty.call(nextOverrides, key)) {
    return {
      key,
      migrated: false,
      reason: 'override-exists',
      errors: [],
      overrides: nextOverrides,
    };
  }

  const factory = catalog.profiles[key];
  const migratedProfile = upgradeLegacyProfileDraft(catalog, key, {
    captureWidth: factory.captureWidth,
    captureHeight: factory.captureHeight,
    readRegion: cloneJson(legacyCapture.readRegion),
    roiSpace: legacyCapture.roiSpace || factory.roiSpace,
    rois: cloneJson(legacyCapture.rois),
  });
  const validation = validateReaderProfileDraft(key, migratedProfile);
  if (!validation.ok) {
    return {
      key,
      migrated: false,
      reason: 'invalid-legacy-calibration',
      errors: cloneJson(validation.errors),
      overrides: nextOverrides,
    };
  }
  nextOverrides[key] = migratedProfile;
  return {
    key,
    migrated: true,
    reason: 'migrated',
    errors: [],
    overrides: nextOverrides,
  };
}

function resetReaderProfileOverride(overrides, profileKeyValue) {
  const nextOverrides = isPlainObject(overrides) ? cloneJson(overrides) : {};
  const key = normalizeProfileKey(profileKeyValue, null);
  if (key) delete nextOverrides[key];
  return nextOverrides;
}

function finitePosition(value, fallback) {
  try {
    const number = Number(value);
    return Number.isFinite(number) ? number : fallback;
  } catch {
    return fallback;
  }
}

function clamp(value, minimum, maximum) {
  return Math.max(minimum, Math.min(maximum, value));
}

function exactRegion(region) {
  return {
    x: region.x,
    y: region.y,
    width: region.width,
    height: region.height,
  };
}

/** Preserve a bottom-centered scorebug's reference pixel footprint when the
 * captured source uses a different width or aspect ratio. */
function adaptReadRegionToSource(regionValue, referenceWidthValue, referenceHeightValue, sourceWidthValue, sourceHeightValue) {
  const errors = regionErrors(regionValue, 'readRegion');
  if (errors.length) throw new TypeError(errors.map((error) => error.message).join(' '));
  const region = exactRegion(regionValue);
  const referenceWidth = finitePosition(referenceWidthValue, 0);
  const referenceHeight = finitePosition(referenceHeightValue, 0);
  const sourceWidth = finitePosition(sourceWidthValue, 0);
  const sourceHeight = finitePosition(sourceHeightValue, 0);
  if (!(referenceWidth > 0 && referenceHeight > 0 && sourceWidth > 0 && sourceHeight > 0)) {
    return deepFreeze(region);
  }
  const width = clamp((region.width * referenceWidth) / sourceWidth, 0.0001, 1);
  const height = clamp((region.height * referenceHeight) / sourceHeight, 0.0001, 1);
  const centerOffset = (((region.x + (region.width / 2)) - 0.5) * referenceWidth) / sourceWidth;
  const bottomMargin = Math.max(0, ((1 - region.y - region.height) * referenceHeight) / sourceHeight);
  return deepFreeze({
    x: clamp(0.5 + centerOffset - (width / 2), 0, 1 - width),
    y: clamp(1 - bottomMargin - height, 0, 1 - height),
    width,
    height,
  });
}

/**
 * Build a server-authoritative custom profile that changes only the factory
 * scorebug's position. Client-supplied dimensions, ROIs, and metadata are
 * intentionally ignored.
 */
function createFactoryPlacementProfile(catalog, profileKeyValue, position = {}) {
  assertValidCatalog(catalog);
  const key = normalizeProfileKey(profileKeyValue, null);
  if (!key) {
    const error = new Error(`Unsupported reader profile key: ${String(profileKeyValue ?? '')}.`);
    error.code = 'ERR_READER_PROFILE_KEY';
    throw error;
  }

  const factory = catalog.profiles[key];
  const factoryReadRegion = exactRegion(factory.readRegion);
  const x = clamp(
    finitePosition(position?.x, factoryReadRegion.x),
    0,
    1 - factoryReadRegion.width,
  );
  const y = clamp(
    finitePosition(position?.y, factoryReadRegion.y),
    0,
    1 - factoryReadRegion.height,
  );
  const rois = Object.fromEntries(REQUIRED_ROI_KEYS.map((binding) => [
    binding,
    exactRegion(factory.rois[binding]),
  ]));
  const profile = {
    captureWidth: factory.captureWidth,
    captureHeight: factory.captureHeight,
    readRegion: {
      x,
      y,
      width: factoryReadRegion.width,
      height: factoryReadRegion.height,
    },
    roiSpace: 'read-region',
    rois,
  };
  const validation = validateReaderProfileDraft(key, profile);
  if (!validation.ok) {
    const error = new Error(`Invalid factory placement profile: ${validation.errors.map((item) => item.message).join(' ')}`);
    error.code = 'ERR_READER_PROFILE_PLACEMENT';
    error.validationErrors = validation.errors;
    throw error;
  }
  return deepFreeze(cloneJson(profile));
}

/**
 * Build a server-authoritative custom profile whose outer scorebug rectangle
 * may be moved and uniformly scaled. Factory OCR fields remain relative to
 * that outer rectangle. Independent width/height stretching is rejected.
 */
function createFactoryResizablePlacementProfile(catalog, profileKeyValue, placement = {}) {
  assertValidCatalog(catalog);
  const key = normalizeProfileKey(profileKeyValue, null);
  if (!key) {
    const error = new Error(`Unsupported reader profile key: ${String(profileKeyValue ?? '')}.`);
    error.code = 'ERR_READER_PROFILE_KEY';
    throw error;
  }

  const factory = catalog.profiles[key];
  const factoryReadRegion = exactRegion(factory.readRegion);
  const rois = Object.fromEntries(REQUIRED_ROI_KEYS.map((binding) => [
    binding,
    exactRegion(factory.rois[binding]),
  ]));
  const minimumWidth = Math.max(
    MINIMUM_READ_PIXELS.width / factory.captureWidth,
    ...REQUIRED_ROI_KEYS.map((binding) => (
      MINIMUM_ROI_PIXELS.width / (factory.captureWidth * rois[binding].width)
    )),
  );
  const minimumHeight = Math.max(
    MINIMUM_READ_PIXELS.height / factory.captureHeight,
    ...REQUIRED_ROI_KEYS.map((binding) => (
      MINIMUM_ROI_PIXELS.height / (factory.captureHeight * rois[binding].height)
    )),
  );
  const requestedWidthScale = finitePosition(placement?.width, factoryReadRegion.width)
    / factoryReadRegion.width;
  const requestedHeightScale = finitePosition(placement?.height, factoryReadRegion.height)
    / factoryReadRegion.height;
  const scaleDelta = Math.abs(requestedWidthScale - requestedHeightScale);
  if (scaleDelta > 1e-7 * Math.max(1, requestedWidthScale, requestedHeightScale)) {
    const error = new Error('Invalid reader placement: width and height must use one uniform scale.');
    error.code = 'ERR_READER_PROFILE_PLACEMENT';
    error.validationErrors = [validationError(
      'layout-aspect-locked',
      'readRegion',
      'Reader placement cannot stretch the protected horizontal layout.',
    )];
    throw error;
  }
  const minimumScale = Math.max(
    minimumWidth / factoryReadRegion.width,
    minimumHeight / factoryReadRegion.height,
  );
  const maximumScale = Math.min(1 / factoryReadRegion.width, 1 / factoryReadRegion.height);
  const scale = clamp(requestedWidthScale, minimumScale, maximumScale);
  const width = factoryReadRegion.width * scale;
  const height = factoryReadRegion.height * scale;
  const profile = {
    captureWidth: factory.captureWidth,
    captureHeight: factory.captureHeight,
    readRegion: {
      x: clamp(finitePosition(placement?.x, factoryReadRegion.x), 0, 1 - width),
      y: clamp(finitePosition(placement?.y, factoryReadRegion.y), 0, 1 - height),
      width,
      height,
    },
    roiSpace: 'read-region',
    rois,
  };
  const validation = validateReaderProfileDraft(key, profile);
  if (!validation.ok) {
    const error = new Error(`Invalid resizable placement profile: ${validation.errors.map((item) => item.message).join(' ')}`);
    error.code = 'ERR_READER_PROFILE_PLACEMENT';
    error.validationErrors = validation.errors;
    throw error;
  }
  return deepFreeze(cloneJson(profile));
}

module.exports = Object.freeze({
  CATALOG_SCHEMA,
  DEFAULT_CATALOG_PATH,
  DEFAULT_PROFILE_KEY,
  MINIMUM_READ_PIXELS,
  MINIMUM_ROI_PIXELS,
  PROFILE_DIMENSIONS,
  PROFILE_KEYS,
  REQUIRED_ROI_KEYS,
  adaptReadRegionToSource,
  createFactoryPlacementProfile,
  createFactoryResizablePlacementProfile,
  loadReaderProfileCatalog,
  migrateLegacyCaptureToProfile,
  normalizeProfileKey,
  resetReaderProfileOverride,
  removeIncompatibleReaderProfileOverrides,
  resolveEffectiveReaderProfile,
  upgradeLegacyReaderProfileOverrides,
  validateReaderProfileCatalog,
  validateReaderProfileDraft,
});
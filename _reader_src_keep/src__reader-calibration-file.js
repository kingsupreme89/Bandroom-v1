
'use strict';

const {
  normalizeProfileKey,
  PROFILE_KEYS,
  REQUIRED_ROI_KEYS,
  validateReaderProfileCatalog,
  validateReaderProfileDraft,
} = require('./reader-profile');

const LEGACY_READER_CALIBRATION_SCHEMA = 'cfb27-reader-calibration/1';
const LEGACY_READER_CALIBRATION_SCHEMA_V2 = 'cfb27-reader-calibration/2';
const READER_CALIBRATION_SCHEMA = 'cfb27-reader-calibration/3';
const READER_CALIBRATION_COORDINATE_MODEL = 'source-height-v1';
const EXACT_OUTER_FIT_MODE = 'exact';
const LEGACY_OUTER_FIT_MODE = 'uniform';
const LOCKED_LAYOUT_EPSILON = 1e-7;
// Fallback box for a legacy 12-area calibration file written before
// away.possession existed. It must stay equal to the factory catalog's own
// away.possession, which is now derived from the donor's authored layout - a
// test asserts exactly that, so this cannot drift silently.
const DEFAULT_AWAY_POSSESSION_REGION = Object.freeze({
  x: 0.16701234280217758,
  y: 0.5784579800439515,
  width: 0.035872259995799134,
  height: 0.1436471974330067,
});
const LEGACY_REQUIRED_ROI_KEYS = Object.freeze(
  REQUIRED_ROI_KEYS.filter((binding) => binding !== 'away.possession'),
);
const MAX_READER_CALIBRATION_BYTES = 64 * 1024;
const MINIMUM_REFERENCE_ASPECT_RATIO = 0.5;
const MAXIMUM_REFERENCE_ASPECT_RATIO = 4;
const FILE_KEYS = Object.freeze([
  'schema',
  'coordinateModel',
  'referenceAspectRatio',
  'readRegion',
  'roiSpace',
  'rois',
  'outerFitMode',
]);
const LEGACY_FILE_KEYS = Object.freeze(FILE_KEYS.filter((key) => key !== 'outerFitMode'));
const CONSTRUCTOR_KEYS = Object.freeze(FILE_KEYS);
const REGION_KEYS = Object.freeze(['x', 'y', 'width', 'height']);
const FORBIDDEN_PROPERTY_KEYS = Object.freeze(['__proto__', 'constructor', 'prototype']);

function deepFreeze(value) {
  if (!value || typeof value !== 'object' || Object.isFrozen(value)) return value;
  for (const child of Object.values(value)) deepFreeze(child);
  return Object.freeze(value);
}

function cloneJson(value) {
  if (value === undefined) return undefined;
  return JSON.parse(JSON.stringify(value));
}

function isPlainObject(value) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const prototype = Object.getPrototypeOf(value);
  return prototype === Object.prototype || prototype === null;
}

function validationError(code, pathName, message) {
  return Object.freeze({ code, path: pathName, message });
}

function exactKeyErrors(value, expectedKeys, pathName) {
  if (!isPlainObject(value)) {
    return [validationError('object-required', pathName, `${pathName} must be an object.`)];
  }
  const expected = new Set(expectedKeys);
  const errors = [];
  for (const key of Reflect.ownKeys(value)) {
    if (typeof key !== 'string') {
      errors.push(validationError('property-symbol', pathName, `${pathName} cannot contain symbol properties.`));
      continue;
    }
    if (FORBIDDEN_PROPERTY_KEYS.includes(key)) {
      errors.push(validationError('property-forbidden', `${pathName}.${key}`, `${pathName} contains a forbidden property.`));
    } else if (!expected.has(key)) {
      errors.push(validationError('property-unexpected', `${pathName}.${key}`, `${pathName} contains an unsupported property: ${key}.`));
    }
  }
  for (const key of expectedKeys) {
    if (!Object.prototype.hasOwnProperty.call(value, key)) {
      errors.push(validationError('property-required', `${pathName}.${key}`, `${pathName} is missing ${key}.`));
    }
  }
  return errors;
}

function regionErrors(value, pathName) {
  const errors = exactKeyErrors(value, REGION_KEYS, pathName);
  if (!isPlainObject(value)) return errors;
  for (const part of REGION_KEYS) {
    if (!Object.prototype.hasOwnProperty.call(value, part)) continue;
    if (typeof value[part] !== 'number' || !Number.isFinite(value[part])) {
      errors.push(validationError('region-number', `${pathName}.${part}`, `${pathName}.${part} must be a finite number.`));
    }
  }
  if (errors.some((error) => error.code === 'region-number')) return errors;
  if (!REGION_KEYS.every((part) => Object.prototype.hasOwnProperty.call(value, part))) return errors;
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

function validateReaderCalibrationFile(value) {
  const legacySchema = value?.schema === LEGACY_READER_CALIBRATION_SCHEMA
    || value?.schema === LEGACY_READER_CALIBRATION_SCHEMA_V2;
  const errors = exactKeyErrors(
    value,
    legacySchema ? LEGACY_FILE_KEYS : FILE_KEYS,
    'calibration',
  );
  if (!isPlainObject(value)) return deepFreeze({ ok: false, errors });

  const supportedSchema = value.schema === READER_CALIBRATION_SCHEMA
    || legacySchema;
  if (!supportedSchema) {
    errors.push(validationError(
      'schema',
      'calibration.schema',
      `Calibration schema must be ${READER_CALIBRATION_SCHEMA}, ${LEGACY_READER_CALIBRATION_SCHEMA_V2}, or ${LEGACY_READER_CALIBRATION_SCHEMA}.`,
    ));
  }
  if (value.coordinateModel !== READER_CALIBRATION_COORDINATE_MODEL) {
    errors.push(validationError(
      'coordinate-model',
      'calibration.coordinateModel',
      `Calibration coordinateModel must be ${READER_CALIBRATION_COORDINATE_MODEL}.`,
    ));
  }
  if (typeof value.referenceAspectRatio !== 'number'
      || !Number.isFinite(value.referenceAspectRatio)
      || value.referenceAspectRatio < MINIMUM_REFERENCE_ASPECT_RATIO
      || value.referenceAspectRatio > MAXIMUM_REFERENCE_ASPECT_RATIO) {
    errors.push(validationError(
      'reference-aspect-ratio',
      'calibration.referenceAspectRatio',
      `referenceAspectRatio must be a finite number between ${MINIMUM_REFERENCE_ASPECT_RATIO} and ${MAXIMUM_REFERENCE_ASPECT_RATIO}.`,
    ));
  }
  errors.push(...regionErrors(value.readRegion, 'calibration.readRegion'));
  if (value.roiSpace !== 'read-region') {
    errors.push(validationError(
      'roi-space',
      'calibration.roiSpace',
      'Calibration roiSpace must be "read-region".',
    ));
  }
  if (!legacySchema
    && value.outerFitMode !== EXACT_OUTER_FIT_MODE
    && value.outerFitMode !== LEGACY_OUTER_FIT_MODE) {
    errors.push(validationError(
      'outer-fit-mode',
      'calibration.outerFitMode',
      `Calibration outerFitMode must be "${EXACT_OUTER_FIT_MODE}" or "${LEGACY_OUTER_FIT_MODE}".`,
    ));
  }

  const expectedRoiKeys = value.schema === LEGACY_READER_CALIBRATION_SCHEMA
    ? LEGACY_REQUIRED_ROI_KEYS
    : REQUIRED_ROI_KEYS;
  const roiObjectErrors = exactKeyErrors(value.rois, expectedRoiKeys, 'calibration.rois');
  errors.push(...roiObjectErrors);
  if (isPlainObject(value.rois)) {
    for (const binding of expectedRoiKeys) {
      if (Object.prototype.hasOwnProperty.call(value.rois, binding)) {
        errors.push(...regionErrors(value.rois[binding], `calibration.rois.${binding}`));
      }
    }
  }

  return deepFreeze({ ok: errors.length === 0, errors });
}

function canonicalNumber(value) {
  return Object.is(value, -0) ? 0 : value;
}

function canonicalRegion(value) {
  return {
    x: canonicalNumber(value.x),
    y: canonicalNumber(value.y),
    width: canonicalNumber(value.width),
    height: canonicalNumber(value.height),
  };
}

function canonicalReaderCalibrationFile(value) {
  const rois = {};
  for (const binding of REQUIRED_ROI_KEYS) {
    const region = value.rois[binding]
      || (binding === 'away.possession' ? DEFAULT_AWAY_POSSESSION_REGION : null);
    rois[binding] = canonicalRegion(region);
  }
  return {
    schema: READER_CALIBRATION_SCHEMA,
    coordinateModel: READER_CALIBRATION_COORDINATE_MODEL,
    outerFitMode: value.outerFitMode || LEGACY_OUTER_FIT_MODE,
    referenceAspectRatio: canonicalNumber(value.referenceAspectRatio),
    readRegion: canonicalRegion(value.readRegion),
    roiSpace: 'read-region',
    rois,
  };
}

function readerCalibrationError(code, message, validationErrors = []) {
  const error = new Error(message);
  error.code = code;
  error.validationErrors = cloneJson(validationErrors);
  return error;
}

function assertReaderCalibrationFile(value) {
  const validation = validateReaderCalibrationFile(value);
  if (!validation.ok) {
    throw readerCalibrationError(
      'ERR_READER_CALIBRATION_FILE',
      `Invalid reader calibration: ${validation.errors.map((item) => item.message).join(' ')}`,
      validation.errors,
    );
  }
  return deepFreeze(canonicalReaderCalibrationFile(value));
}

function approximatelyEqual(left, right, epsilon = LOCKED_LAYOUT_EPSILON) {
  const a = Number(left);
  const b = Number(right);
  if (!Number.isFinite(a) || !Number.isFinite(b)) return false;
  return Math.abs(a - b) <= epsilon * Math.max(1, Math.abs(a), Math.abs(b));
}

function lockedReferenceProfile(catalog) {
  return catalog.profiles[PROFILE_KEYS[PROFILE_KEYS.length - 1]];
}

function validateLockedReaderCalibration(catalog, value) {
  const catalogValidation = validateReaderProfileCatalog(catalog);
  if (!catalogValidation.ok) {
    return deepFreeze({ ok: false, errors: catalogValidation.errors.map((error) => validationError(
      error.code,
      `catalog.${error.path}`,
      error.message,
    )) });
  }

  let calibration;
  try {
    calibration = assertReaderCalibrationFile(value);
  } catch (error) {
    return deepFreeze({
      ok: false,
      errors: (error.validationErrors || []).map((item) => validationError(item.code, item.path, item.message)),
    });
  }

  const errors = [];
  if (calibration.outerFitMode === LEGACY_OUTER_FIT_MODE) {
    const reference = lockedReferenceProfile(catalog);
    const expectedPixelAspect = (reference.captureWidth * reference.readRegion.width)
      / (reference.captureHeight * reference.readRegion.height);
    const actualPixelAspect = calibration.referenceAspectRatio
      * calibration.readRegion.width
      / calibration.readRegion.height;
    if (!approximatelyEqual(actualPixelAspect, expectedPixelAspect)) {
      errors.push(validationError(
        'layout-aspect-locked',
        'calibration.readRegion',
        'This older reader file must keep its original horizontal layout aspect ratio. Capture a fresh game picture to create an exact-fit reader file.',
      ));
    }
  }

  // The thirteen reading areas are hand-adjustable: every scorebug renders
  // slightly differently, and the outer artwork can also vary in aspect.
  // The schema bounds every box and profile materialization rejects boxes too
  // small to read, so calibration may preserve the user's exact four edges.
  return deepFreeze({ ok: errors.length === 0, errors });
}

function assertLockedReaderCalibration(catalog, value) {
  const calibration = assertReaderCalibrationFile(value);
  const validation = validateLockedReaderCalibration(catalog, calibration);
  if (!validation.ok) {
    throw readerCalibrationError(
      'ERR_READER_CALIBRATION_LAYOUT',
      `Reader calibration does not match the protected horizontal layout: ${validation.errors.map((item) => item.message).join(' ')}`,
      validation.errors,
    );
  }
  return calibration;
}

function createReaderCalibrationFile(draft = {}) {
  const errors = [];
  if (!isPlainObject(draft)) {
    errors.push(validationError('object-required', 'draft', 'draft must be an object.'));
  }
  const requiredKeys = ['referenceAspectRatio', 'readRegion', 'rois'];
  const allowedKeys = new Set(CONSTRUCTOR_KEYS);
  if (isPlainObject(draft)) {
    for (const key of Reflect.ownKeys(draft)) {
      if (typeof key !== 'string') {
        errors.push(validationError('property-symbol', 'draft', 'draft cannot contain symbol properties.'));
      } else if (FORBIDDEN_PROPERTY_KEYS.includes(key)) {
        errors.push(validationError('property-forbidden', `draft.${key}`, 'draft contains a forbidden property.'));
      } else if (!allowedKeys.has(key)) {
        errors.push(validationError('property-unexpected', `draft.${key}`, `draft contains an unsupported property: ${key}.`));
      }
    }
    for (const key of requiredKeys) {
      if (!Object.prototype.hasOwnProperty.call(draft, key)) {
        errors.push(validationError('property-required', `draft.${key}`, `draft is missing ${key}.`));
      }
    }
  }
  if (errors.length) {
    throw readerCalibrationError(
      'ERR_READER_CALIBRATION_DRAFT',
      `Invalid reader calibration draft: ${errors.map((item) => item.message).join(' ')}`,
      errors,
    );
  }
  return assertReaderCalibrationFile({
    schema: draft.schema ?? READER_CALIBRATION_SCHEMA,
    coordinateModel: draft.coordinateModel ?? READER_CALIBRATION_COORDINATE_MODEL,
    outerFitMode: draft.outerFitMode ?? EXACT_OUTER_FIT_MODE,
    referenceAspectRatio: draft.referenceAspectRatio,
    readRegion: draft.readRegion,
    roiSpace: draft.roiSpace ?? 'read-region',
    rois: draft.rois,
  });
}

function sourceBytes(source) {
  if (typeof source === 'string') return Buffer.from(source, 'utf8');
  if (Buffer.isBuffer(source)) return Buffer.from(source);
  if (source instanceof ArrayBuffer) return Buffer.from(source);
  if (ArrayBuffer.isView(source)) {
    return Buffer.from(source.buffer, source.byteOffset, source.byteLength);
  }
  throw readerCalibrationError(
    'ERR_READER_CALIBRATION_SOURCE',
    'Reader calibration input must be UTF-8 text or bytes.',
  );
}

function parseReaderCalibrationFile(source) {
  const bytes = sourceBytes(source);
  if (bytes.byteLength > MAX_READER_CALIBRATION_BYTES) {
    throw readerCalibrationError(
      'ERR_READER_CALIBRATION_TOO_LARGE',
      `Reader calibration files cannot exceed ${MAX_READER_CALIBRATION_BYTES} bytes.`,
    );
  }
  let text;
  try {
    text = new TextDecoder('utf-8', { fatal: true }).decode(bytes);
  } catch {
    throw readerCalibrationError(
      'ERR_READER_CALIBRATION_ENCODING',
      'Reader calibration file must use valid UTF-8 text.',
    );
  }
  if (text.charCodeAt(0) === 0xFEFF) text = text.slice(1);
  let parsed;
  try {
    parsed = JSON.parse(text);
  } catch {
    throw readerCalibrationError(
      'ERR_READER_CALIBRATION_JSON',
      'Reader calibration file is not valid JSON.',
    );
  }
  return assertReaderCalibrationFile(parsed);
}

function serializeReaderCalibrationFile(value) {
  const canonical = assertReaderCalibrationFile(value);
  const output = `${JSON.stringify(canonical, null, 2)}\n`;
  if (Buffer.byteLength(output, 'utf8') > MAX_READER_CALIBRATION_BYTES) {
    throw readerCalibrationError(
      'ERR_READER_CALIBRATION_TOO_LARGE',
      `Reader calibration files cannot exceed ${MAX_READER_CALIBRATION_BYTES} bytes.`,
    );
  }
  return output;
}

function finitePositiveDimension(value, pathName) {
  if (typeof value !== 'number' || !Number.isFinite(value) || value <= 0 || value > 65_536) {
    throw readerCalibrationError(
      'ERR_READER_CALIBRATION_TARGET',
      `${pathName} must be a finite positive number no greater than 65536.`,
    );
  }
  return value;
}

function cleanBoundaryNumber(value) {
  if (Math.abs(value) < 1e-14) return 0;
  if (Math.abs(1 - value) < 1e-14) return 1;
  return value;
}

function adaptReaderCalibrationReadRegion(value, targetWidthValue, targetHeightValue) {
  const calibration = assertReaderCalibrationFile(value);
  const targetWidth = finitePositiveDimension(targetWidthValue, 'targetWidth');
  const targetHeight = finitePositiveDimension(targetHeightValue, 'targetHeight');
  const targetAspectRatio = targetWidth / targetHeight;
  const factor = calibration.referenceAspectRatio / targetAspectRatio;
  const source = calibration.readRegion;
  const width = source.width * factor;
  const centerX = 0.5 + (((source.x + (source.width / 2)) - 0.5) * factor);
  const unclampedX = centerX - (width / 2);
  const x = width <= 1
    ? Math.max(0, Math.min(1 - width, unclampedX))
    : unclampedX;
  const adapted = {
    x: cleanBoundaryNumber(x),
    y: cleanBoundaryNumber(source.y),
    width: cleanBoundaryNumber(width),
    height: cleanBoundaryNumber(source.height),
  };
  const errors = regionErrors(adapted, 'adaptedReadRegion');
  if (errors.length) {
    throw readerCalibrationError(
      'ERR_READER_CALIBRATION_TARGET_GEOMETRY',
      `Reader calibration does not fit the ${targetWidth}x${targetHeight} capture shape.`,
      errors,
    );
  }
  return deepFreeze(adapted);
}

function materializeReaderProfileOverrides(catalog, value, profileKeyValue = null) {
  const calibration = assertReaderCalibrationFile(value);
  const catalogValidation = validateReaderProfileCatalog(catalog);
  if (!catalogValidation.ok) {
    throw readerCalibrationError(
      'ERR_READER_CALIBRATION_CATALOG',
      `Cannot apply reader calibration to an invalid profile catalog: ${catalogValidation.errors.map((item) => item.message).join(' ')}`,
      catalogValidation.errors,
    );
  }
  assertLockedReaderCalibration(catalog, calibration);

  const profileKey = profileKeyValue === null
    ? null
    : normalizeProfileKey(profileKeyValue, null);
  if (profileKeyValue !== null && !profileKey) {
    throw readerCalibrationError(
      'ERR_READER_CALIBRATION_TARGET',
      `Unsupported reader calibration profile: ${String(profileKeyValue ?? '')}.`,
    );
  }
  const targetKeys = profileKey ? [profileKey] : PROFILE_KEYS;
  const profiles = {};
  const errors = [];
  for (const key of targetKeys) {
    const factory = catalog.profiles[key];
    const readRegion = adaptReaderCalibrationReadRegion(
      calibration,
      factory.captureWidth,
      factory.captureHeight,
    );
    const profile = {
      captureWidth: factory.captureWidth,
      captureHeight: factory.captureHeight,
      readRegion: cloneJson(readRegion),
      roiSpace: 'read-region',
      rois: cloneJson(calibration.rois),
      aspectAdaptive: true,
      referenceAspectRatio: calibration.referenceAspectRatio,
      referenceReadRegion: cloneJson(calibration.readRegion),
    };
    const validation = validateReaderProfileDraft(key, profile);
    if (!validation.ok) {
      for (const error of validation.errors) {
        errors.push(validationError(
          error.code,
          `profiles.${key}.${error.path}`,
          `${key}: ${error.message}`,
        ));
      }
    }
    profiles[key] = profile;
  }
  if (errors.length) {
    throw readerCalibrationError(
      'ERR_READER_CALIBRATION_PROFILES',
      `Reader calibration cannot be applied to the selected profile: ${errors.map((item) => item.message).join(' ')}`,
      errors,
    );
  }
  return deepFreeze(profiles);
}

module.exports = Object.freeze({
  DEFAULT_AWAY_POSSESSION_REGION,
  LEGACY_READER_CALIBRATION_SCHEMA,
  LOCKED_LAYOUT_EPSILON,
  MAX_READER_CALIBRATION_BYTES,
  READER_CALIBRATION_COORDINATE_MODEL,
  READER_CALIBRATION_SCHEMA,
  adaptReaderCalibrationReadRegion,
  assertLockedReaderCalibration,
  assertReaderCalibrationFile,
  createReaderCalibrationFile,
  materializeReaderProfileOverrides,
  parseReaderCalibrationFile,
  serializeReaderCalibrationFile,
  validateLockedReaderCalibration,
  validateReaderCalibrationFile,
});
eport };
(function exposeReadRegion(root, factory) {
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  if (root) root.Cfb27ReadRegion = api;
}(typeof globalThis !== 'undefined' ? globalThis : this, function readRegionFactory() {
  'use strict';

  const FULL_FRAME = Object.freeze({ x: 0, y: 0, width: 1, height: 1 });
  const READ_REGION_KEY = '__readRegion';
  const STOCK_SCORE_ROI = Object.freeze({
    x: 0.045,
    awayY: 0.2089041096,
    homeY: 0.5616438356,
    width: 0.36,
    height: 0.1712328767,
  });
  const LEGACY_SCORE_XS = Object.freeze([0.0542635659, STOCK_SCORE_ROI.x]);
  const LEGACY_SCORE_WIDTHS = Object.freeze([
    0.1666666667,
    0.3255813953,
    0.2868217054,
    0.289,
    0.28,
    STOCK_SCORE_ROI.width,
  ]);
  const LEGACY_SCORE_HEIGHTS = Object.freeze([0.1609589041, STOCK_SCORE_ROI.height]);
  const STOCK_DOWN_DISTANCE_ROI = Object.freeze({
    x: 0.0271317829,
    y: 0.8630136986,
    width: 0.82,
    height: 0.1267123288,
  });
  const LEGACY_DOWN_DISTANCE_WIDTHS = Object.freeze([0.6511627907, STOCK_DOWN_DISTANCE_ROI.width]);

  function finite(value, fallback) {
    const number = Number(value);
    return Number.isFinite(number) ? number : fallback;
  }

  function clamp(value, minimum, maximum) {
    return Math.max(minimum, Math.min(maximum, value));
  }

  function isRegion(value) {
    if (!value || typeof value !== 'object') return false;
    return [value.x, value.y, value.width, value.height].every((part) => Number.isFinite(Number(part)))
      && Number(value.width) > 0
      && Number(value.height) > 0;
  }

  /** Normalize and constrain a rectangle to its normalized 0..1 parent. */
  function normalizeRegion(value, fallback = FULL_FRAME, minimum = {}) {
    const source = isRegion(value) ? value : fallback;
    const minimumWidth = clamp(finite(minimum.width, 0.001), 0.0001, 1);
    const minimumHeight = clamp(finite(minimum.height, 0.001), 0.0001, 1);
    const x = clamp(finite(source.x, fallback.x), 0, 1 - minimumWidth);
    const y = clamp(finite(source.y, fallback.y), 0, 1 - minimumHeight);
    const width = clamp(finite(source.width, fallback.width), minimumWidth, 1 - x);
    const height = clamp(finite(source.height, fallback.height), minimumHeight, 1 - y);
    return { x, y, width, height };
  }

  function unionRegions(regions) {
    const valid = regions.filter(isRegion).map((region) => normalizeRegion(region));
    if (!valid.length) return null;
    const left = Math.min(...valid.map((region) => region.x));
    const top = Math.min(...valid.map((region) => region.y));
    const right = Math.max(...valid.map((region) => region.x + region.width));
    const bottom = Math.max(...valid.map((region) => region.y + region.height));
    return normalizeRegion({ x: left, y: top, width: right - left, height: bottom - top });
  }

  function containsRegion(parentValue, childValue, epsilon = 0.00001) {
    if (!isRegion(parentValue) || !isRegion(childValue)) return false;
    const parent = normalizeRegion(parentValue);
    const child = normalizeRegion(childValue);
    return child.x >= parent.x - epsilon
      && child.y >= parent.y - epsilon
      && child.x + child.width <= parent.x + parent.width + epsilon
      && child.y + child.height <= parent.y + parent.height + epsilon;
  }

  /**
   * Resolve the independent capture/read box with backward compatibility.
   * Older builds stored a full-frame game.anchor or only absolute field ROIs.
   */
  function resolveReadRegion(capture = {}) {
    if (isRegion(capture.readRegion)) return normalizeRegion(capture.readRegion);
    if (isRegion(capture.rois?.['game.anchor'])) return normalizeRegion(capture.rois['game.anchor']);
    const fieldUnion = unionRegions(Object.entries(capture.rois || {})
      .filter(([name]) => name !== 'game.anchor')
      .map(([, region]) => region));
    if (fieldUnion) return fieldUnion;
    if (isRegion(capture.donorRegion)) return normalizeRegion(capture.donorRegion);
    return { ...FULL_FRAME };
  }

  /** Convert a child rectangle within parent coordinates into full-frame coordinates. */
  function composeRegion(parentValue, childValue) {
    const parent = normalizeRegion(parentValue);
    const child = normalizeRegion(childValue);
    return normalizeRegion({
      x: parent.x + (child.x * parent.width),
      y: parent.y + (child.y * parent.height),
      width: child.width * parent.width,
      height: child.height * parent.height,
    });
  }

  /** Convert a full-frame rectangle to coordinates relative to the read region. */
  function relativeRegion(parentValue, absoluteValue) {
    const parent = normalizeRegion(parentValue);
    const absolute = normalizeRegion(absoluteValue);
    const left = Math.max(parent.x, absolute.x);
    const top = Math.max(parent.y, absolute.y);
    const right = Math.min(parent.x + parent.width, absolute.x + absolute.width);
    const bottom = Math.min(parent.y + parent.height, absolute.y + absolute.height);
    if (right <= left || bottom <= top) return null;
    return normalizeRegion({
      x: (left - parent.x) / parent.width,
      y: (top - parent.y) / parent.height,
      width: (right - left) / parent.width,
      height: (bottom - top) / parent.height,
    });
  }

  function usesRelativeFieldRois(capture = {}) {
    return capture.roiSpace === 'read-region' || capture.roiSpace === 'readRegion';
  }

  function approximatelyEqual(left, right, epsilon = 0.00001) {
    return Math.abs(Number(left) - Number(right)) <= epsilon;
  }

  function isStockScoreRoi(binding, region) {
    const expectedY = binding === 'away.score'
      ? STOCK_SCORE_ROI.awayY
      : (binding === 'home.score' ? STOCK_SCORE_ROI.homeY : null);
    if (expectedY === null || !isRegion(region)) return false;
    return LEGACY_SCORE_XS.some((x) => approximatelyEqual(region.x, x))
      && approximatelyEqual(region.y, expectedY)
      && LEGACY_SCORE_WIDTHS.some((width) => approximatelyEqual(region.width, width))
      && LEGACY_SCORE_HEIGHTS.some((height) => approximatelyEqual(region.height, height));
  }

  function isStockDownDistanceRoi(binding, region) {
    return binding === 'game.downDistance'
      && isRegion(region)
      && approximatelyEqual(region.x, STOCK_DOWN_DISTANCE_ROI.x)
      && approximatelyEqual(region.y, STOCK_DOWN_DISTANCE_ROI.y)
      && approximatelyEqual(region.height, STOCK_DOWN_DISTANCE_ROI.height)
      && LEGACY_DOWN_DISTANCE_WIDTHS.some((width) => approximatelyEqual(region.width, width));
  }

  function resolveStoredFieldRoi(capture = {}, binding) {
    const stored = capture.rois?.[binding];
    if (!isRegion(stored)) return null;
    if (!usesRelativeFieldRois(capture)) return stored;

    // The original stock down box ended at x=175 in the 258px read image.
    // That clips the tail of "2nd & Inches". Expand only the known stock box
    // to x=219, leaving the rightmost 39px (where ball-on text can appear) out.
    if (isStockDownDistanceRoi(binding, stored)) {
      return { ...stored, width: STOCK_DOWN_DISTANCE_ROI.width };
    }

    if (!isStockScoreRoi(binding, stored)) return stored;

    // Upgrade historical stock score boxes in memory to the wider calibration
    // proven by the working reader profile. This prevents the second digit from
    // being clipped when an existing installation still stores an older box.
    return {
      ...stored,
      x: STOCK_SCORE_ROI.x,
      width: STOCK_SCORE_ROI.width,
      height: STOCK_SCORE_ROI.height,
    };
  }

  /** Return a field ROI in read-region coordinates, regardless of stored schema. */
  function resolveFieldRoi(capture = {}, binding) {
    const stored = resolveStoredFieldRoi(capture, binding);
    if (!stored) return null;
    if (usesRelativeFieldRois(capture) || stored.relativeTo === 'readRegion') {
      return normalizeRegion(stored);
    }
    return relativeRegion(resolveReadRegion(capture), stored);
  }

  /** Return the same field ROI in full captured-window coordinates. */
  function resolveAbsoluteFieldRoi(capture = {}, binding) {
    const stored = resolveStoredFieldRoi(capture, binding);
    if (!stored) return null;
    if (usesRelativeFieldRois(capture) || stored.relativeTo === 'readRegion') {
      return composeRegion(resolveReadRegion(capture), stored);
    }
    return normalizeRegion(stored);
  }

  /** Upgrade old absolute per-field boxes without changing their screen pixels. */
  function migrateCaptureSettings(captureValue = {}) {
    const capture = { ...captureValue, rois: { ...(captureValue.rois || {}) } };
    const readRegion = resolveReadRegion(capture);
    if (!usesRelativeFieldRois(capture)) {
      const converted = {};
      for (const [binding, region] of Object.entries(capture.rois)) {
        if (binding === 'game.anchor') continue;
        const relative = relativeRegion(readRegion, region);
        if (relative) converted[binding] = relative;
      }
      capture.rois = converted;
    } else {
      delete capture.rois['game.anchor'];
    }
    capture.readRegion = readRegion;
    capture.roiSpace = 'read-region';
    return capture;
  }

  function regionToPixels(regionValue, widthValue, heightValue) {
    const region = normalizeRegion(regionValue);
    const width = Math.max(1, finite(widthValue, 1));
    const height = Math.max(1, finite(heightValue, 1));
    const left = clamp(Math.round(region.x * width), 0, Math.max(0, Math.floor(width) - 1));
    const top = clamp(Math.round(region.y * height), 0, Math.max(0, Math.floor(height) - 1));
    const right = clamp(Math.round((region.x + region.width) * width), left + 1, Math.floor(width));
    const bottom = clamp(Math.round((region.y + region.height) * height), top + 1, Math.floor(height));
    return { x: left, y: top, width: right - left, height: bottom - top };
  }

  function pixelsToRegion(rectangle, widthValue, heightValue) {
    const width = Math.max(1, finite(widthValue, 1));
    const height = Math.max(1, finite(heightValue, 1));
    return normalizeRegion({
      x: finite(rectangle?.x, 0) / width,
      y: finite(rectangle?.y, 0) / height,
      width: finite(rectangle?.width, width) / width,
      height: finite(rectangle?.height, height) / height,
    });
  }

  /** Crop an Electron NativeImage (or compatible test object) by normalized ROI. */
  function cropNativeImage(image, regionValue) {
    if (!image || typeof image.getSize !== 'function' || typeof image.crop !== 'function') return null;
    const size = image.getSize();
    if (!size?.width || !size?.height) return null;
    return image.crop(regionToPixels(regionValue, size.width, size.height));
  }

  /**
   * Produce the exact two-stage crop used by OCR: first the independent read
   * area, then the selected field inside that area.
   */
  function cropFieldFromCapture(image, capture, binding) {
    const readRegion = resolveReadRegion(capture);
    const fieldRegion = resolveFieldRoi(capture, binding);
    if (!fieldRegion) return null;
    const readImage = cropNativeImage(image, readRegion);
    if (!readImage) return null;
    return cropNativeImage(readImage, fieldRegion);
  }

  function regionToPercent(regionValue, precision = 2) {
    const region = normalizeRegion(regionValue);
    const round = (value) => Number((value * 100).toFixed(precision));
    return {
      x: round(region.x),
      y: round(region.y),
      width: round(region.width),
      height: round(region.height),
    };
  }

  function regionFromPercent(value, fallback = FULL_FRAME) {
    return normalizeRegion({
      x: finite(value?.x, fallback.x * 100) / 100,
      y: finite(value?.y, fallback.y * 100) / 100,
      width: finite(value?.width, fallback.width * 100) / 100,
      height: finite(value?.height, fallback.height * 100) / 100,
    }, fallback, { width: 0.005, height: 0.005 });
  }

  function pointToNormalized(point, viewport) {
    return {
      x: clamp(finite(point?.x, 0) / Math.max(1, finite(viewport?.width, 1)), 0, 1),
      y: clamp(finite(point?.y, 0) / Math.max(1, finite(viewport?.height, 1)), 0, 1),
    };
  }

  class ReadRegionEditor {
    constructor(region = FULL_FRAME, options = {}) {
      this.minimum = {
        width: clamp(finite(options.minimumWidth, 0.02), 0.001, 1),
        height: clamp(finite(options.minimumHeight, 0.02), 0.001, 1),
      };
      this.handleSize = clamp(finite(options.handleSize, 12), 4, 40);
      this.region = normalizeRegion(region, FULL_FRAME, this.minimum);
      this.gesture = null;
    }

    getRegion() {
      return { ...this.region };
    }

    setRegion(region) {
      this.region = normalizeRegion(region, this.region, this.minimum);
      return this.getRegion();
    }

    pixelRect(viewport) {
      return regionToPixels(this.region, viewport?.width, viewport?.height);
    }

    handles(viewport) {
      const rect = this.pixelRect(viewport);
      const left = rect.x;
      const centerX = rect.x + (rect.width / 2);
      const right = rect.x + rect.width;
      const top = rect.y;
      const centerY = rect.y + (rect.height / 2);
      const bottom = rect.y + rect.height;
      return {
        nw: { x: left, y: top }, n: { x: centerX, y: top }, ne: { x: right, y: top },
        e: { x: right, y: centerY }, se: { x: right, y: bottom },
        s: { x: centerX, y: bottom }, sw: { x: left, y: bottom }, w: { x: left, y: centerY },
      };
    }

    hitTest(point, viewport) {
      const radius = this.handleSize;
      for (const [handle, position] of Object.entries(this.handles(viewport))) {
        if (Math.abs(point.x - position.x) <= radius && Math.abs(point.y - position.y) <= radius) {
          return { action: 'resize', handle };
        }
      }
      const rect = this.pixelRect(viewport);
      if (point.x >= rect.x && point.x <= rect.x + rect.width
        && point.y >= rect.y && point.y <= rect.y + rect.height) {
        return { action: 'move', handle: null };
      }
      return { action: 'create', handle: 'se' };
    }

    begin(point, viewport, forcedAction) {
      const hit = forcedAction || this.hitTest(point, viewport);
      this.gesture = {
        action: hit.action,
        handle: hit.handle,
        startPoint: pointToNormalized(point, viewport),
        startRegion: this.getRegion(),
      };
      if (hit.action === 'create') {
        const start = this.gesture.startPoint;
        this.region = normalizeRegion({
          x: start.x,
          y: start.y,
          width: this.minimum.width,
          height: this.minimum.height,
        }, this.region, this.minimum);
        this.gesture.startRegion = this.getRegion();
      }
      return { ...hit, region: this.getRegion() };
    }

    update(point, viewport) {
      if (!this.gesture) return this.getRegion();
      const current = pointToNormalized(point, viewport);
      const start = this.gesture.startPoint;
      const original = this.gesture.startRegion;
      const deltaX = current.x - start.x;
      const deltaY = current.y - start.y;

      if (this.gesture.action === 'move') {
        this.region = normalizeRegion({
          ...original,
          x: clamp(original.x + deltaX, 0, 1 - original.width),
          y: clamp(original.y + deltaY, 0, 1 - original.height),
        }, original, this.minimum);
        return this.getRegion();
      }

      if (this.gesture.action === 'create') {
        const left = Math.min(start.x, current.x);
        const top = Math.min(start.y, current.y);
        const right = Math.max(start.x, current.x);
        const bottom = Math.max(start.y, current.y);
        this.region = normalizeRegion({
          x: left,
          y: top,
          width: Math.max(this.minimum.width, right - left),
          height: Math.max(this.minimum.height, bottom - top),
        }, original, this.minimum);
        return this.getRegion();
      }

      let left = original.x;
      let top = original.y;
      let right = original.x + original.width;
      let bottom = original.y + original.height;
      const handle = this.gesture.handle || 'se';
      if (handle.includes('w')) left = clamp(original.x + deltaX, 0, right - this.minimum.width);
      if (handle.includes('e')) right = clamp(right + deltaX, left + this.minimum.width, 1);
      if (handle.includes('n')) top = clamp(original.y + deltaY, 0, bottom - this.minimum.height);
      if (handle.includes('s')) bottom = clamp(bottom + deltaY, top + this.minimum.height, 1);
      this.region = normalizeRegion({ x: left, y: top, width: right - left, height: bottom - top }, original, this.minimum);
      return this.getRegion();
    }

    end(point, viewport) {
      if (point && viewport) this.update(point, viewport);
      this.gesture = null;
      return this.getRegion();
    }

    cancel() {
      if (this.gesture) this.region = this.gesture.startRegion;
      this.gesture = null;
      return this.getRegion();
    }
  }

  function drawReadRegion(context, region, viewport, options = {}) {
    if (!context) return;
    const editor = options.editor instanceof ReadRegionEditor ? options.editor : null;
    const rect = regionToPixels(region, viewport?.width, viewport?.height);
    const selected = options.selected !== false;
    const stroke = options.stroke || '#f8c35c';
    context.save();
    context.fillStyle = options.fill || 'rgba(248,195,92,.08)';
    context.strokeStyle = stroke;
    context.lineWidth = options.lineWidth || 4;
    context.setLineDash(options.dashed === false ? [] : [10, 6]);
    context.fillRect(rect.x, rect.y, rect.width, rect.height);
    context.strokeRect(rect.x, rect.y, rect.width, rect.height);
    context.setLineDash([]);
    if (options.showLabel !== false) {
      context.font = options.font || '600 16px Segoe UI';
      const label = options.label || 'READ / CAPTURE AREA';
      const labelWidth = context.measureText(label).width + 14;
      context.fillStyle = options.labelFill || 'rgba(10,13,18,.9)';
      context.fillRect(rect.x, Math.max(0, rect.y - 24), labelWidth, 24);
      context.fillStyle = stroke;
      context.fillText(label, rect.x + 7, Math.max(17, rect.y - 7));
    }
    if (selected) {
      const handles = editor ? editor.handles(viewport) : new ReadRegionEditor(region).handles(viewport);
      const handleSize = options.handleSize || 10;
      context.fillStyle = '#ffffff';
      context.strokeStyle = stroke;
      context.lineWidth = 2;
      for (const position of Object.values(handles)) {
        context.fillRect(position.x - (handleSize / 2), position.y - (handleSize / 2), handleSize, handleSize);
        context.strokeRect(position.x - (handleSize / 2), position.y - (handleSize / 2), handleSize, handleSize);
      }
    }
    context.restore();
  }

  return {
    FULL_FRAME,
    READ_REGION_KEY,
    ReadRegionEditor,
    composeRegion,
    containsRegion,
    cropFieldFromCapture,
    cropNativeImage,
    drawReadRegion,
    isRegion,
    migrateCaptureSettings,
    normalizeRegion,
    pixelsToRegion,
    regionFromPercent,
    regionToPercent,
    regionToPixels,
    relativeRegion,
    resolveAbsoluteFieldRoi,
    resolveFieldRoi,
    resolveReadRegion,
    unionRegions,
    usesRelativeFieldRois,
 
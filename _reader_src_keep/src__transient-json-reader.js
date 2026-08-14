ment,
};
'use strict';

const fs = require('node:fs');

const DEFAULT_TRANSIENT_JSON_GRACE_MS = 750;
const MAX_TRANSIENT_JSON_GRACE_MS = 1_000;
const DEFAULT_READ_ATTEMPTS = 3;

function normalizedGraceMs(value) {
  const number = Number(value);
  if (!Number.isFinite(number)) return DEFAULT_TRANSIENT_JSON_GRACE_MS;
  return Math.min(MAX_TRANSIENT_JSON_GRACE_MS, Math.max(0, Math.trunc(number)));
}

class TransientJsonReader {
  constructor({
    graceMs = DEFAULT_TRANSIENT_JSON_GRACE_MS,
    readAttempts = DEFAULT_READ_ATTEMPTS,
    now = Date.now,
    readFile = (filePath) => fs.readFileSync(filePath, 'utf8'),
    exists = fs.existsSync,
  } = {}) {
    this.graceMs = normalizedGraceMs(graceMs);
    this.readAttempts = Math.max(1, Math.trunc(Number(readAttempts)) || DEFAULT_READ_ATTEMPTS);
    this.now = now;
    this.readFile = readFile;
    this.exists = exists;
    this.cache = new Map();
  }

  read(filePath) {
    const observedAt = this.now();
    for (let attempt = 0; attempt < this.readAttempts; attempt += 1) {
      try {
        const document = JSON.parse(this.readFile(filePath));
        this.cache.set(filePath, { document, observedAt });
        return document;
      } catch { }
    }

    let pathStillExists = false;
    try {
      pathStillExists = Boolean(this.exists(filePath));
    } catch {
      pathStillExists = false;
    }

    const cached = this.cache.get(filePath);
    const ageMs = cached ? observedAt - cached.observedAt : Number.POSITIVE_INFINITY;
    if (pathStillExists && cached && ageMs >= 0 && ageMs <= this.graceMs) {
      return cached.document;
    }

    this.cache.delete(filePath);
    return null;
  }

  clear(filePath) {
    if (filePath === undefined) this.cache.clear();
    else this.cache.delete(filePath);
  }
}

module.exports = {
  DEFAULT_READ_ATTEMPTS,
  DEFAULT_TRANSIENT_JSON_GRACE_MS,
  MAX_TRANSIENT_JSON_GRACE_MS,
  TransientJsonReader,
  normalizedGra
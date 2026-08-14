ceMs,
};
'use strict';

const path = require('node:path');

const PORTABLE_MARKER_FILE = 'portable-data.json';
const FOLDER_LOCAL_USER_DATA_DIRECTORY = 'UserData';

function requiredText(value, name) {
  const text = String(value || '').trim();
  if (!text) throw new TypeError(`${name} is required.`);
  return text;
}

function normalizedDirectory(value) {
  const text = String(value || '').trim();
  return text ? path.resolve(text) : '';
}

function candidateDirectories(options) {
  const candidates = [];
  const portableDirectory = normalizedDirectory(options.portableExecutableDir);
  if (portableDirectory) candidates.push(portableDirectory);

  const executablePath = String(options.executablePath || '').trim();
  if (options.isPackaged && executablePath) {
    candidates.push(path.dirname(path.resolve(executablePath)));
  }

  return [...new Set(candidates)];
}

/**
 * Resolve user-data storage without importing Electron, reading process state,
 * creating folders, or mutating app paths. The caller supplies the filesystem
 * predicate so this function can run before Electron's app.setPath().
 */
function resolveUserDataLocation(options = {}) {
  const appDataPath = requiredText(options.appDataPath, 'appDataPath');
  const productName = requiredText(options.productName, 'productName');
  if (typeof options.markerExists !== 'function') {
    throw new TypeError('markerExists must be a function.');
  }

  for (const directory of candidateDirectories(options)) {
    const markerPath = path.join(directory, PORTABLE_MARKER_FILE);
    let exists = false;
    try {
      exists = Boolean(options.markerExists(markerPath));
    } catch {
      exists = false;
    }
    if (exists) {
      return {
        userDataPath: path.join(directory, FOLDER_LOCAL_USER_DATA_DIRECTORY),
        mode: 'folder-local',
        markerPath,
      };
    }
  }

  return {
    userDataPath: path.join(path.resolve(appDataPath), productName),
    mode: 'app-data',
    markerPath: '',
  };
}

module.exports = {
  FOLDER_LOCAL_USER_DATA_DIRECTORY,
  PORTABLE_MARKER_FILE,
  candidateDirectories,
  resolveUserDataLoca
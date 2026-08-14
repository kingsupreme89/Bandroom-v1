ADconst fs = require('fs');
const path = require('path');

const asarPath = 'c:/Bandroom/_scratch_reader/extracted/resources/app.asar';
const outDir = 'c:/Bandroom/_scratch_reader/extracted/_src';

const b = fs.readFileSync(asarPath);

// asar: u32 @4 = header size, JSON header starts @8
let headerSize = b.readUInt32LE(4);
let header;
try {
  header = JSON.parse(b.slice(8, 8 + headerSize).toString('utf8'));
} catch (e) {
  // fallback: some asars start JSON at 16
  headerSize = b.readUInt32LE(12);
  header = JSON.parse(b.slice(16, 16 + headerSize).toString('utf8'));
}

const files = header.files;
const wanted = [
  'src/claude-dc-bridge.js',
  'src/transient-json-reader.js',
  'src/automatic-data-extractor.js',
  'src/user-data-location.js',
  'src/scoreboard-data-source.js',
  'src/read-region.js',
  'package.json',
];

fs.mkdirSync(outDir, { recursive: true });

function get(node, prefix) {
  for (const [k, v] of Object.entries(node)) {
    const key = prefix + k;
    if (v && v.files) {
      get(v.files, key + '/');
    } else if (v && typeof v.offset !== 'undefined') {
      if (wanted.includes(key)) {
        const payloadOffset = 8 + headerSize + parseInt(v.offset, 10);
        const payload = b.slice(payloadOffset, payloadOffset + v.size);
        const flat = key.replace(/\//g, '__');
        const out = path.join(outDir, flat);
        fs.writeFileSync(out, payload);
        console.log('WROTE', key, '->', out, '(' + v.size + ' bytes)');
      }
    }
  }
}
get(files, '');
console.log('DONE');
// The comparison of the browser (wwwroot/js/textdiff.js) for a list of cases, as lines that the server's comparison (DocumentDiff) writes the same way:
// DiffParityTests makes the cases, asks both and compares the answers. Usage: node parity.mjs cases.json   (prints one JSON list of lists of strings)
import { createRequire } from 'node:module';
import fs from 'node:fs';
const require = createRequire(import.meta.url);
const D = require('../../src/KommasKorrektus/wwwroot/js/textdiff.js');
const cases = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const answer = cases.map(c => D.compare(c.old, c.new).map(row => {
  const line = `${row.kind}:${row.o ?? '-'}/${row.n ?? '-'}`;
  if (row.kind !== 'mod') { return line; }
  const w = D.words(c.old[row.o], c.new[row.n]);
  return `${line} del=${JSON.stringify(w.del)} ins=${JSON.stringify(w.ins)}`;
}));
console.log(JSON.stringify(answer));

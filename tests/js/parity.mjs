// The comparison of the browser (wwwroot/js/textdiff.js) for a list of cases, as lines that the server's comparison (TextDiff.cs) writes the same way:
// DiffParityTests makes the cases, asks both and compares the answers. Usage: node parity.mjs cases.json   (prints one JSON list of lists of strings)
import { createRequire } from 'node:module';
import fs from 'node:fs';
const require = createRequire(import.meta.url);
const D = require('../../src/KommasKorrektus/wwwroot/js/textdiff.js');
const cases = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const ranges = list => '[' + list.map(r => `[${r[0]},${r[1]}]`).join(',') + ']';
const at = p => `${p.p},${p.c}`;
const answer = cases.map(c => {
  const r = D.compare(c.old, c.new);
  return [
    ...r.rows.map(row => `${row.kind}:${row.o ?? '-'}/${row.n ?? '-'}:${row.mark}:${row.oldFrom}-${row.oldTo}/${row.newFrom}-${row.newTo} del=${ranges(row.del)} ins=${ranges(row.ins)}`),
    ...r.changes.map(x => `c:${at(x.oldFrom)}-${at(x.oldTo)}>${at(x.newFrom)}-${at(x.newTo)}`),
    'p:' + r.pairs.map(p => p[0] + '/' + p[1]).join(' '),
  ];
});
console.log(JSON.stringify(answer));

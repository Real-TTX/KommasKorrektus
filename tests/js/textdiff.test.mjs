import { createRequire } from 'node:module';
import assert from 'node:assert/strict';
const require = createRequire(import.meta.url);
const D = require('../../src/KommasKorrektus/wwwroot/js/textdiff.js');

let count = 0;
const test = (name, fn) => { fn(); count += 1; console.log('ok  ' + name); };
const kinds = rows => rows.map(r => `${r.kind}:${r.o ?? '-'}/${r.n ?? '-'}`).join(' ');

// ---- Myers against the longest common subsequence
const lcs = (a, b) => { const t = Array.from({ length: a.length + 1 }, () => new Array(b.length + 1).fill(0)); for (let i = 1; i <= a.length; i++) for (let j = 1; j <= b.length; j++) t[i][j] = a[i - 1] === b[j - 1] ? t[i - 1][j - 1] + 1 : Math.max(t[i - 1][j], t[i][j - 1]); return t[a.length][b.length]; };
test('Myers finds as many matches as the longest common subsequence, in order, all equal', () => {
  let seed = 7;
  const rnd = n => { seed = (seed * 1103515245 + 12345) & 0x7fffffff; return seed % n; };
  for (let round = 0; round < 400; round++) {
    const a = Array.from({ length: rnd(30) }, () => rnd(5));
    const b = Array.from({ length: rnd(30) }, () => rnd(5));
    const m = D.myers(a, b, 1000);
    assert.equal(m.length, lcs(a, b), `lengths for ${a} / ${b}`);
    for (let k = 0; k < m.length; k++) { assert.equal(a[m[k][0]], b[m[k][1]]); if (k) { assert.ok(m[k][0] > m[k - 1][0] && m[k][1] > m[k - 1][1]); } }
  }
});
test('Myers gives up (null) when there are more differences than allowed', () => {
  assert.equal(D.myers([1, 2, 3, 4, 5, 6], [7, 8, 9, 10, 11, 12], 3), null);
});

const p = (name, n) => `${name}: ` + Array.from({ length: n }, (_, i) => `${name.toLowerCase()}wort${i}`).join(' ');
const A = p('Alpha', 14), B = p('Beta', 14), C = p('Gamma', 14), Dd = p('Delta', 14), E = p('Epsilon', 14);

test('the same text is all the same', () => {
  const rows = D.compare([A, B, C], [A, B, C]);
  assert.equal(kinds(rows), 'same:0/0 same:1/1 same:2/2');
});
test('a new paragraph in the middle is new, the rest stays across from each other', () => {
  const rows = D.compare([A, B, C], [A, B, E, C]);
  assert.equal(kinds(rows), 'same:0/0 same:1/1 ins:-/2 same:2/3');
});
test('a paragraph taken away is taken away', () => {
  assert.equal(kinds(D.compare([A, B, C, Dd], [A, C, Dd])), 'same:0/0 del:1/- same:2/1 same:3/2');
});
test('Enter in the middle of a paragraph: the first half is the old paragraph changed, the second half is new', () => {
  const words = B.split(' ');
  const first = words.slice(0, 8).join(' '), second = words.slice(8).join(' ');
  const rows = D.compare([A, B, C], [A, first, second, C]);
  assert.equal(rows.length, 4);
  assert.equal(rows[0].kind, 'same');
  const middle = rows.slice(1, 3).map(r => r.kind).sort().join(',');
  assert.equal(middle, 'ins,mod');
  assert.equal(rows[3].kind, 'same');
  assert.deepEqual([rows[3].o, rows[3].n], [2, 3]);
});
test('Backspace joining two paragraphs: one is the old one changed, one is gone', () => {
  const joined = B + ' ' + C;
  const rows = D.compare([A, B, C, Dd], [A, joined, Dd]);
  assert.equal(rows[0].kind, 'same');
  assert.equal(rows.slice(1, 3).map(r => r.kind).sort().join(','), 'del,mod');
  assert.deepEqual([rows[3].o, rows[3].n], [3, 2]);
});
test('a paragraph written anew is the same paragraph, changed: the two stand across from each other', () => {
  const rows = D.compare([A, B, C], [A, 'Ganz etwas anderes, das nichts mit dem Alten zu tun hat.', C]);
  assert.equal(kinds(rows), 'same:0/0 mod:1/1 same:2/2');
});
test('a changed word makes the paragraph a changed one (and the words are found)', () => {
  const changed = B.replace('betawort3', 'etwas anderes');
  const rows = D.compare([A, B, C], [A, changed, C]);
  assert.equal(kinds(rows), 'same:0/0 mod:1/1 same:2/2');
  const w = D.words(B, changed);
  assert.equal(B.slice(w.del[0][0], w.del[0][1]), 'betawort3');
  assert.equal(changed.slice(w.ins[0][0], w.ins[0][1]), 'etwas anderes');
});
test('empty paragraphs do not confuse the pairing', () => {
  const rows = D.compare([A, '', B, '', C], [A, '', B, '', 'Neu eingefügt.', '', C]);
  assert.equal(rows.filter(r => r.kind === 'ins').length >= 1, true);
  assert.deepEqual(rows.filter(r => r.o !== null).map(r => r.o), [0, 1, 2, 3, 4]);
  assert.deepEqual(rows.filter(r => r.n !== null).map(r => r.n), [0, 1, 2, 3, 4, 5, 6]);
});
test('the rows keep the order of both texts', () => {
  let seed = 3;
  const rnd = n => { seed = (seed * 1103515245 + 12345) & 0x7fffffff; return seed % n; };
  const pool = [A, B, C, Dd, E, '', 'Kurz.', 'Noch kürzer.'];
  for (let round = 0; round < 200; round++) {
    const a = Array.from({ length: rnd(12) }, () => pool[rnd(pool.length)]);
    const b = Array.from({ length: rnd(12) }, () => pool[rnd(pool.length)]);
    const rows = D.compare(a, b);
    const os = rows.filter(r => r.o !== null).map(r => r.o), ns = rows.filter(r => r.n !== null).map(r => r.n);
    assert.deepEqual(os, a.map((_, i) => i), 'every old paragraph once, in order');
    assert.deepEqual(ns, b.map((_, i) => i), 'every new paragraph once, in order');
  }
});
test('words: an insertion, a deletion, punctuation', () => {
  const w = D.words('Der Hund lief schnell, sehr schnell.', 'Der große Hund lief, sehr schnell.');
  assert.deepEqual(w.ins.map(r => 'Der große Hund lief, sehr schnell.'.slice(...r)), ['große ']);
  assert.equal(w.del.length >= 1, true);
});
test('words: nothing differs', () => {
  assert.deepEqual(D.words('Gleich.', 'Gleich.'), { del: [], ins: [] });
});
test('a big text with one change is fast', () => {
  const old = Array.from({ length: 3000 }, (_, i) => `Absatz ${i} ` + 'wort '.repeat(30) + i);
  const next = old.slice(); next.splice(1500, 0, 'Ein neuer Absatz mitten drin, wie ihn jemand eben tippt.'); next[2000] += ' und ein Zusatz';
  const t0 = Date.now();
  const rows = D.compare(old, next);
  const ms = Date.now() - t0;
  assert.equal(rows.filter(r => r.kind === 'ins').length, 1);
  assert.equal(rows.filter(r => r.kind === 'mod').length, 1);
  console.log('     3000 paragraphs compared in', ms, 'ms');
  assert.ok(ms < 500);
});
console.log(count + ' tests passed');

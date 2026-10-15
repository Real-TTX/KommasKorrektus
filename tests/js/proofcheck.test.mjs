import { createRequire } from 'node:module';
import assert from 'node:assert/strict';
const require = createRequire(import.meta.url);
const P = require('../../src/KommasKorrektus/wwwroot/js/proofcheck.js');

let count = 0;
const test = (name, fn) => { fn(); count += 1; console.log('ok  ' + name); };
const NBSP = String.fromCharCode(0xa0);
const by = (found, rule) => found.filter(f => f.rule === rule);
const show = list => list.map(f => `${f.rule}:${f.n}:${f.from}-${f.to}[${f.found}]>[${f.fix}]`).join(' ');
// the text with every fix of the findings put in, from the end (what "Alle korrigieren" does)
const fixed = (paragraphs, findings) => {
  const texts = paragraphs.slice();
  const removes = [];
  for (const f of findings.slice().reverse()) {
    if (f.remove) { removes.push(f.n); continue; }
    if (f.fix === null) { continue; }
    texts[f.n] = texts[f.n].slice(0, f.from) + f.fix + texts[f.n].slice(f.to);
  }
  return texts.filter((_, i) => !removes.includes(i));
};

test('spaces twice are found (not at the edges, which is another rule)', () => {
  const found = P.check(['Das  ist   doppelt.']);
  assert.deepEqual(by(found, 'spaces').map(f => [f.from, f.to, f.fix]), [[3, 5, ' '], [8, 11, ' ']]);
  assert.equal(by(P.check(['  nur am Rand']), 'spaces').length, 0);
});
test('spaces at the edges of a paragraph', () => {
  const found = by(P.check([' Anfang und Ende  ']), 'edges');
  assert.deepEqual(found.map(f => [f.from, f.to, f.fix]), [[0, 1, ''], [16, 18, '']]);
  assert.equal(by(P.check(['   ']), 'edges').length, 0, 'a paragraph of spaces only is a matter of the empty paragraphs');
});
test('a space before a punctuation mark', () => {
  const found = by(P.check(['Hallo , Welt !']), 'spaceBefore');
  assert.deepEqual(found.map(f => [f.from, f.to, f.fix]), [[5, 6, ''], [12, 13, '']]);
  assert.equal(by(P.check(['Und dann ... nichts mehr.']), 'spaceBefore').length, 0, 'the ellipsis may have a space before it');
  assert.equal(by(P.check(['Ein Satz mit Ende .']), 'spaceBefore').length, 1);
});
test('no space after a punctuation mark, but not in numbers, abbreviations and addresses', () => {
  assert.deepEqual(by(P.check(['Hallo,Welt']), 'spaceAfter').map(f => [f.from, f.fix]), [[5, ', ']]);
  assert.deepEqual(by(P.check(['Das Ende.Der Hund']), 'spaceAfter').map(f => [f.from, f.fix]), [[8, '. ']]);
  assert.equal(by(P.check(['Um 3,5 Uhr, also z.B. um 10:30, siehe www.beispiel.de/a,b und a@b.de.']), 'spaceAfter').length, 0);
});
test('the quotation marks: straight, English, apostrophe; the German ones are right', () => {
  const lo = String.fromCharCode(0x201e), hi = String.fromCharCode(0x201c), hi9 = String.fromCharCode(0x201d), apo = String.fromCharCode(0x2019);
  assert.deepEqual(by(P.check(['"Hallo", sagte sie.']), 'quotes').map(f => f.fix), [lo, hi]);
  assert.deepEqual(by(P.check([hi + 'Hallo' + hi9 + ' sagte sie.']), 'quotes').map(f => f.fix), [lo, hi]);
  assert.equal(by(P.check([lo + 'Hallo' + hi + ' sagte sie, es geht' + apo + 's.']), 'quotes').length, 0);
  assert.deepEqual(by(P.check(["Wie geht's dir?"]), 'quotes').map(f => f.fix), [apo]);
  assert.equal(by(P.check(['Ein 5" Bildschirm']), 'quotes').length, 0, 'a measure');
});
test('three dots are one sign', () => {
  const ellipsis = String.fromCharCode(0x2026);
  assert.deepEqual(by(P.check(['Und dann...']), 'ellipsis').map(f => [f.from, f.fix]), [[8, ellipsis]]);
  assert.equal(by(P.check(['Und dann....']), 'ellipsis').length, 0);
});
test('a hyphen between blanks is a dash, a list dash at the beginning is not', () => {
  const dash = String.fromCharCode(0x2013);
  assert.deepEqual(by(P.check(['Das - und das']), 'dash').map(f => [f.from, f.fix]), [[4, dash]]);
  assert.equal(by(P.check(['- Aufzählung', 'Die Süd-Ost-Route']), 'dash').length, 0);
});
test('protected spaces in an abbreviation and before a unit', () => {
  assert.deepEqual(by(P.check(['also z. B. so, und u. a. m.']), 'nbsp').map(f => f.from), [7, 21, 24]);
  assert.deepEqual(by(P.check(['um 8 Uhr sind 5 % und 10 km']), 'nbsp').map(f => f.from), [4, 15, 24]);
  assert.equal(by(P.check(['10 Meter und 5 Stück']), 'nbsp').length, 0);
});
test('a word twice in a row (not an interjection)', () => {
  const found = by(P.check(['Der der Hund lief und und lief.', 'Ja ja, sagte sie.']), 'repeat');
  assert.deepEqual(found.map(f => [f.n, f.found]), [[0, ' der'], [0, ' und']]);
  assert.equal(by(P.check(['Es war sehr sehr kalt.']), 'repeat').length, 0);
});
test('several empty paragraphs in a row: the second and the following ones', () => {
  const found = by(P.check(['a', '', '', '', 'b', '', 'c']), 'empty');
  assert.deepEqual(found.map(f => [f.n, f.remove]), [[2, true], [3, true]]);
});
test('a name that differs a letter from the style sheet', () => {
  const terms = [{ term: 'Rabenwald', kind: 'Place' }, { term: 'Anna', kind: 'Character' }, { term: 'Besen', kind: 'Term' }];
  const found = by(P.check(['Im Rabenwalt war es still, im Rabenwald auch, und Rabenwalds Bäume.', 'Anne und rabenwald, Annas Hut.'], { terms }), 'names');
  assert.deepEqual(found.map(f => [f.n, f.found, f.fix]), [[0, 'Rabenwalt', 'Rabenwald'], [1, 'Anne', 'Anna'], [1, 'rabenwald', 'Rabenwald']]);
});
test('findings come in the order of the text', () => {
  const found = P.check(['Hallo , Welt', '', 'Das  ist...']);
  const keys = found.map(f => [f.n, f.from]);
  assert.deepEqual(keys, keys.slice().sort((a, b) => a[0] - b[0] || a[1] - b[1]));
});
test('taking every fix back in once leaves nothing to find (the fixes are the fixes)', () => {
  const lo = String.fromCharCode(0x201e);
  const text = ['Das  ist  ein "Test" , wirklich !', ' Und dann...Ende.Der Hund - ja , z. B. 5 % ', '', '', 'Der der Hund ' + lo + 'ok' + String.fromCharCode(0x201c) + ' geht\'s ?'];
  const once = fixed(text, P.check(text));
  const again = P.check(once);
  assert.deepEqual(again, [], 'left over: ' + show(again) + ' in ' + JSON.stringify(once));
  assert.equal(once.length, 4, 'the second empty paragraph is gone (five paragraphs, one empty one too many)');
});
test('a long text is checked fast', () => {
  const text = Array.from({ length: 5000 }, (_, i) => `Absatz ${i}: der Hund lief  schnell, und dann... kam der Rabe , sagte "er" z. B. um 5 Uhr.`);
  const t0 = Date.now();
  const found = P.check(text, { terms: [{ term: 'Rabenwald', kind: 'Place' }] });
  const ms = Date.now() - t0;
  console.log('     5000 paragraphs checked in', ms, 'ms,', found.length, 'findings');
  assert.ok(ms < 3000);
});
test('Levenshtein with a limit', () => {
  assert.equal(P.levenshtein('rabenwald', 'rabenwalt', 2), 1);
  assert.equal(P.levenshtein('haus', 'maus', 1), 1);
  assert.equal(P.levenshtein('haus', 'baumhaus', 2) > 2, true);
});
console.log(count + ' tests passed');

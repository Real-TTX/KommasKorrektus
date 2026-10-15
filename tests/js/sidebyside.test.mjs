import { createRequire } from 'node:module';
import { readFileSync } from 'node:fs';
import assert from 'node:assert/strict';
const require = createRequire(import.meta.url);

// sidebyside.js is written for the browser (it takes `window`); with only the texts of the earlier version it needs no page at all, so the places it finds and the classes it
// gives the paragraphs can be tested here (the lining up needs a page and is left alone without one).
const root = { KkTextDiff: require('../../src/KommasKorrektus/wwwroot/js/textdiff.js') };
new Function('window', readFileSync(new URL('../../src/KommasKorrektus/wwwroot/js/sidebyside.js', import.meta.url), 'utf8'))(root);
const SEP = root.KkSideBySide.SEP;

let count = 0;
const test = (name, fn) => { fn(); count += 1; console.log('ok  ' + name); };
const el = () => { const classes = new Set(); return { classes, classList: { add: c => classes.add(c), remove: c => classes.delete(c), contains: c => classes.has(c) } }; };
const compare = (oldTexts, newTexts) => {
  const side = root.KkSideBySide.create({ editable: true });
  side.setOldTexts(oldTexts);
  const els = newTexts.map(el);
  side.refresh(els, newTexts, { mode: 'all' });
  return { places: side.hunks(), els, side };
};
const placesOf = (oldTexts, newTexts) => compare(oldTexts, newTexts).places;
const A = 'Die Hexe wohnte am Rand des Waldes und braute jeden Abend einen Tee.';
const B = 'Der Rabe brachte einen Brief, den sie zweimal las und dann ins Feuer warf.';
const C = 'Am Ende wurde alles gut, und niemand erinnerte sich an den Streit.';
const E = 'Dann flog sie davon, ohne sich noch einmal umzusehen.';

test('nothing changed, nothing found', () => {
  assert.deepEqual(placesOf([A, B, C], [A, B, C]), []);
});
test('four empty paragraphs taken away one after the other are one place', () => {
  const found = placesOf([A, '', '', '', '', B], [A, B]);
  assert.equal(found.length, 1);
  assert.equal(found[0].level, 'paragraph');
  assert.equal(found[0].kind, 'delete');
  assert.deepEqual(found[0].oldTexts, ['', '', '', '']);
  assert.deepEqual(found[0].newTexts, []);
  assert.deepEqual(found[0].newFrom, { p: 1, c: 0 }, 'they stood before the paragraph that now follows A');
});
test('paragraphs with words, taken away together, keep their order and the place they stood at', () => {
  const found = placesOf([A, B, C, E], [A, E]);
  assert.equal(found.length, 1);
  assert.deepEqual(found[0].oldTexts, [B, C]);
  assert.deepEqual(found[0].newFrom, { p: 1, c: 0 });
});
test('paragraphs taken away at two places are two places', () => {
  const found = placesOf([A, B, C, E], [B, E]);
  assert.deepEqual(found.map(f => f.kind), ['delete', 'delete']);
  assert.deepEqual(found.map(f => f.newFrom.p), [0, 1]);
});
test('paragraphs put in one after the other are one place with their texts', () => {
  const found = placesOf([A, C], [A, '', '', '', B, C]);
  assert.equal(found.length, 1);
  assert.equal(found[0].kind, 'insert');
  assert.equal(found[0].level, 'paragraph');
  assert.deepEqual(found[0].newTexts, ['', '', '', B]);
  assert.equal(found[0].n, 1);
});
test('a paragraph put in and one taken away at another place are two places, and the paragraphs get their classes', () => {
  const { places, els } = compare([A, B, C], [A, C, E]);
  assert.deepEqual(places.map(f => f.kind), ['delete', 'insert']);
  assert.deepEqual(els.map(e => [...e.classes].join(' ')), ['', '', 'kk-new']);
});
test('a paragraph written anew is the same paragraph, changed, not one taken away and one put in', () => {
  const { places, els } = compare([A, B, C], [A, 'Ein ganz neuer Absatz, von dem vorher keine Rede war.', C]);
  assert.ok(places.every(f => f.level === 'words'));
  assert.deepEqual(els.map(e => [...e.classes].join(' ')), ['', 'kk-changed', '']);
});
test('a word changed inside a paragraph is a place of its own', () => {
  const found = placesOf([A, B], [A.replace('Tee', 'Kaffee'), B]);
  assert.equal(found.length, 1);
  assert.equal(found[0].level, 'words');
  assert.equal(found[0].kind, 'replace');
  assert.equal(found[0].oldText, 'Tee');
  assert.equal(found[0].newText, 'Kaffee');
});
test('a paragraph split in two is a break put in; the rest of it is no new paragraph', () => {
  const { places, els } = compare([A + ' ' + B, C], [A, B, C]);
  assert.equal(places.length, 1);
  assert.equal(places[0].oldText, ' ');
  assert.equal(places[0].newText, SEP);
  assert.deepEqual(els.map(e => [...e.classes].join(' ')), ['kk-changed', 'kk-changed', '']);
});
test('a paragraph whose words were all taken away stays, empty: its words are the place', () => {
  const found = placesOf([A, B, C], [A, '', C]);
  assert.equal(found.length, 1);
  assert.equal(found[0].kind, 'delete');
  assert.equal(found[0].oldText, B);
});
test('the mode "off" leaves the new page as it is, the classes go when the comparison is cleared', () => {
  const side = root.KkSideBySide.create({ editable: true });
  side.setOldTexts([A, B]);
  const els = [A, B, C].map(el);
  side.refresh(els, [A, B, C], { mode: 'off' });
  assert.deepEqual(els.map(e => e.classes.size), [0, 0, 0]);
  side.refresh(els, [A, B, C], { mode: 'simple' });
  assert.deepEqual(els.map(e => [...e.classes].join(' ')), ['', '', 'kk-new']);
  side.clear();
  assert.deepEqual(els.map(e => e.classes.size), [0, 0, 0]);
});

console.log(count + ' tests passed');

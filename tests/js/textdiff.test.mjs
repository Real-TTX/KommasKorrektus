import { createRequire } from 'node:module';
import assert from 'node:assert/strict';
const require = createRequire(import.meta.url);
const D = require('../../src/KommasKorrektus/wwwroot/js/textdiff.js');

let count = 0;
const test = (name, fn) => { fn(); count += 1; console.log('ok  ' + name); };
const SEP = D.SEP;
// a row as kind:old/new, with the mark at its end when the break did not stay (I put in, D taken away): the same as DiffPairingTests.cs
const kinds = r => r.rows.map(x => `${x.kind}:${x.o ?? '-'}/${x.n ?? '-'}${x.mark === 'E' ? '' : x.mark}`).join(' ');
const places = r => r.changes.map(c => `${c.oldText.split(SEP).join('¶')}>${c.newText.split(SEP).join('¶')}`);
// the old text of a row with what was taken away in [ ] and the new one with what was put in in [ ]
const marked = (text, list, from, to) => { let out = ''; let at = from; for (const [x, y] of list) { out += text.slice(at, x) + '[' + text.slice(x, y) + ']'; at = y; } return out + text.slice(at, to); };
const seq = list => ({ length: list.length, get: i => list[i] });

// ---- the algorithms: Myers and the scoring subsequence find a shortest script
const lcsLength = (a, b) => { const t = Array.from({ length: a.length + 1 }, () => new Array(b.length + 1).fill(0)); for (let i = 1; i <= a.length; i++) for (let j = 1; j <= b.length; j++) t[i][j] = a[i - 1] === b[j - 1] ? t[i - 1][j - 1] + 1 : Math.max(t[i - 1][j], t[i][j - 1]); return t[a.length][b.length]; };
const apply = (a, b, diffs) => { let out = []; let at = 0; for (const d of diffs) { out = out.concat(a.slice(at, d.s1), b.slice(d.s2, d.e2)); at = d.e1; } return out.concat(a.slice(at)); };
test('Myers and the subsequence: as few changes as there can be, and the script makes the new sequence out of the old one', () => {
  let seed = 7;
  const rnd = n => { seed = (seed * 1103515245 + 12345) & 0x7fffffff; return seed % n; };
  for (let round = 0; round < 400; round++) {
    const a = Array.from({ length: rnd(30) }, () => rnd(5));
    const b = Array.from({ length: rnd(30) }, () => rnd(5));
    for (const diffs of [D.myers(seq(a), seq(b)), D.lcs(seq(a), seq(b), null, 1)]) {
      assert.deepEqual(apply(a, b, diffs), b, `script for ${a} / ${b}`);
      const changed = diffs.reduce((n, d) => n + (d.e1 - d.s1) + (d.e2 - d.s2), 0);
      assert.ok(changed >= a.length + b.length - 2 * lcsLength(a, b));
    }
    const m = D.myers(seq(a), seq(b));
    assert.equal(m.reduce((n, d) => n + (d.e1 - d.s1) + (d.e2 - d.s2), 0), a.length + b.length - 2 * lcsLength(a, b), 'Myers is shortest');
  }
});

// ---- letters and words, the way VS Code marks them
const one = (a, b) => { const r = D.compare([a], [b]); const row = r.rows[0]; return marked(a, row.del, 0, a.length) + ' → ' + marked(b, row.ins, 0, b.length); };
test('a corrected letter is a letter', () => {
  assert.equal(one('Das ist ein Fehler im Satz.', 'Das ist ein Fahler im Satz.'), 'Das ist ein F[e]hler im Satz. → Das ist ein F[a]hler im Satz.');
  assert.equal(one('Sie hate einen Besen.', 'Sie hatte einen Besen.'), 'Sie hate einen Besen. → Sie ha[t]te einen Besen.');
  assert.equal(one('Ich weiß, dass er kam.', 'Ich weiß, das er kam.'), 'Ich weiß, das[s] er kam. → Ich weiß, das er kam.');
});
test('a word that is mostly changed is changed as a whole', () => {
  assert.equal(one('Ich habe ein Buch gelesen.', 'Ich hatte ein Roman gelesen.'), 'Ich ha[b]e ein [Buch] gelesen. → Ich ha[tt]e ein [Roman] gelesen.');
});
test('commas and quotation marks are their own changes', () => {
  assert.equal(one('Darin stand: Komm schnell wir brauchen deine Hilfe der Wald brennt.', 'Darin stand: „Komm schnell, wir brauchen deine Hilfe, der Wald brennt!“'),
    'Darin stand: Komm schnell wir brauchen deine Hilfe der Wald brennt[.] → Darin stand: [„]Komm schnell[,] wir brauchen deine Hilfe[,] der Wald brennt[!“]');
});
test('the same text is all the same, nothing to show', () => {
  const r = D.compare(['Eins.', '', 'Zwei.'], ['Eins.', '', 'Zwei.']);
  assert.equal(kinds(r), 'same:0/0 same:1/1 same:2/2');
  assert.deepEqual(r.changes, []);
  assert.deepEqual(r.pairs, [[0, 0], [1, 1], [2, 2]]);
  assert.deepEqual(D.compare([], []).rows, []);
});
test('a line break inside a paragraph comes in as \\n and is a character like the others', () => {
  const r = D.compare(['Name: Müller\nOrt: Berlin'], ['Name: Müller\nOrt: Hamburg']);
  assert.equal(places(r).join(' | '), 'Berlin>Hamburg');
});

// ---- which old paragraph is which new one (the same cases as DiffPairingTests.cs)
test('a paragraph split with a new paragraph in between goes on after it', () => {
  const old = ['Kapitel 1', 'Der Rabe kam zurück und krächzte laut. Dann flog er davon und die Hexe blieb allein im Haus.', 'Ende.'];
  const now = ['Kapitel 1', 'Der Rabe kam zurück und krächzte laut.', 'Ein ganz neuer Absatz, der die Stimmung beschreibt und vom Wetter erzählt.', 'Dann flog er davon und die Hexe blieb allein im Haus.', 'Ende.'];
  const r = D.compare(old, now);
  assert.equal(kinds(r), 'same:0/0 mod:1/1I ins:-/2I mod:1/3 same:2/4');
  assert.deepEqual(places(r), [' >¶Ein ganz neuer Absatz, der die Stimmung beschreibt und vom Wetter erzählt.¶']);
});
test('a paragraph split in two is a break put in and the words changed after it', () => {
  const r = D.compare(['Der Rabe kam zurück und krächzte laut. Dann flog er davon und die Hexe blieb allein.', 'Ende.'], ['Der Rabe kam zurück und krächzte laut.', 'Dann flog er fort, und die Hexe blieb allein.', 'Ende.']);
  assert.equal(kinds(r), 'mod:0/0I mod:0/1 same:1/2');
  assert.deepEqual(places(r), [' >¶', 'davon>fort,']);
});
test('paragraphs that were joined are a break taken away', () => {
  const r = D.compare(['Der Rabe kam zurück.', 'Er krächzte laut.', 'Ende.'], ['Der Rabe kam zurück. Er krächzte sehr laut.', 'Ende.']);
  assert.equal(kinds(r), 'mod:0/0D mod:1/0 same:2/1');
  assert.deepEqual(places(r), ['¶> ', '> sehr']);
});
test('text put in at the end of a paragraph and a new paragraph after it are two places', () => {
  const longer = ' Er hatte eine lange Reise hinter sich, über die Berge und durch die Täler, vorbei an den Dörfern und den Flüssen, und er erzählte der Hexe alles, was er gesehen hatte, bis es dunkel wurde.';
  const r = D.compare(['Eins.', 'Der Rabe kam zurück.', 'Ende.'], ['Eins.', 'Der Rabe kam zurück.' + longer, 'Und noch ein ganz neuer Absatz über den Wind.', 'Ende.']);
  assert.equal(kinds(r), 'same:0/0 mod:1/1 ins:-/2I same:2/3');
  assert.deepEqual(places(r), ['>' + longer, '>Und noch ein ganz neuer Absatz über den Wind.¶']);
});
test('the full stop stays with its paragraph when text went in before it and a paragraph after it', () => {
  const r = D.compare(['Eins.', 'Er kam zurück und schwieg lange.', 'Ende.'], ['Eins.', 'Er kam zurück und schwieg lange vor dem Feuer.', 'Ein anderer Absatz.', 'Ende.']);
  assert.equal(kinds(r), 'same:0/0 mod:1/1 ins:-/2I same:2/3');
  assert.deepEqual(places(r), ['> vor dem Feuer', '>Ein anderer Absatz.¶']);
});
test('a sentence put in the middle of a long paragraph is one place in it', () => {
  const old = ['Kapitel 1', 'Der Rabe kam zurück und krächzte laut. Dann flog er davon und die Hexe blieb allein im Haus, lange nachdem die Sonne untergegangen war.', 'Ende.'];
  const now = ['Kapitel 1', 'Der Rabe kam zurück und krächzte laut. Er war müde. Dann flog er davon und die Hexe blieb allein im Haus, lange nachdem die Sonne untergegangen war.', 'Ende.'];
  const r = D.compare(old, now);
  assert.equal(kinds(r), 'same:0/0 mod:1/1 same:2/2');
  assert.deepEqual(places(r), ['> Er war müde.']);
});
test('a paragraph that was moved is taken away at one place and put in at the other', () => {
  const r = D.compare(['Erster Absatz über den Wald.', 'Zweiter Absatz über die Hexe.', 'Dritter Absatz über den Raben.'], ['Zweiter Absatz über die Hexe.', 'Dritter Absatz über den Raben.', 'Erster Absatz über den Wald.']);
  assert.equal(kinds(r), 'del:0/-D same:1/0 same:2/1 ins:-/2I');
});
test('empty paragraphs are no anchors for the paragraphs with text', () => {
  const r = D.compare(['A', '', 'B', '', 'C'], ['A', '', 'B', '', 'Neu eingefügt.', '', 'C']);
  assert.equal(kinds(r), 'same:0/0 same:1/1 same:2/2 same:3/3 ins:-/4I ins:-/5I same:4/6');
});
test('a paragraph split with many empty paragraphs goes on with its text after them', () => {
  const old = ['Am nächsten Morgen klopfte es.', '„Ein Komma kann Leben retten“ sagte sie, und sie öffnete den Brief noch einmal, langsam, als könnte er sich verändert haben.', 'Darin stand: Komm schnell.'];
  const now = ['Am nächsten Morgen klopfte es.', '„Ein Komma kann Leben retten“ sagte sie, und sie öffnete den Brief noch einmal, lan,', '', '', 'ööä', '', 'gsam, als könnte er sich verändert haben.', 'Darin stand: Komm schnell.'];
  const r = D.compare(old, now);
  assert.deepEqual(places(r), ['>,¶¶¶ööä¶¶']);
  const rest = r.rows.find(x => x.n === 6);
  assert.equal(rest.o, 1);
  assert.deepEqual([rest.del, rest.ins], [[], []]);
  // the old paragraph stands across from its first part, the next paragraph across from the next one
  assert.deepEqual(r.pairs, [[0, 0], [1, 1], [2, 7]]);
});

// ---- whatever the texts: the rows make both texts, the places make the new text out of the old one, the pairs keep the order
test('random texts: rows, places and pairs agree with the texts', () => {
  let seed = 5;
  const rnd = n => { seed = (seed * 1103515245 + 12345) & 0x7fffffff; return seed % n; };
  const pool = ['der', 'die', 'das', 'und', 'Haus', 'Garten', 'über', 'Brücke', 'wurde', 'schnell', 'Ost-West', "gibt's", '3,5', '1.200', ',', '.', ';', '„Hallo“', '…', 'Kommata', 'Fehler', 'korrekt', 'Käuzchen', 'hate', 'hatte', ''];
  const para = () => Array.from({ length: rnd(12) }, () => pool[rnd(pool.length)]).join(rnd(8) ? ' ' : '  ');
  const joined = list => (list.length ? list.join(SEP) + SEP : '');
  const starts = list => { const s = [0]; list.forEach(t => s.push(s[s.length - 1] + t.length + 1)); return s; };
  for (let round = 0; round < 1500; round++) {
    const old = Array.from({ length: rnd(14) }, () => (rnd(5) ? para() : ''));
    const now = old.slice();
    for (let step = rnd(7); step > 0; step--) {
      const at = now.length ? rnd(now.length) : 0;
      switch (rnd(7)) {
        case 0: if (now.length) { now.splice(at, 1); } break;
        case 1: now.splice(rnd(now.length + 1), 0, rnd(3) ? para() : ''); break;
        case 2: if (now.length) { const w = now[at].split(' '); w.splice(rnd(w.length + 1), rnd(2), pool[rnd(pool.length)]); now[at] = w.join(' '); } break;
        case 3: if (now.length) { const t = now[at]; const c = rnd(t.length + 1); now.splice(at, 1, t.slice(0, c), t.slice(c)); } break;
        case 4: if (at + 1 < now.length) { now.splice(at, 2, now[at] + (rnd(2) ? ' ' : '') + now[at + 1]); } break;
        case 5: if (now.length > 1) { const o = rnd(now.length); [now[at], now[o]] = [now[o], now[at]]; } break;
        default: if (now.length) { now[at] = now[at] + '\n' + para(); } break;
      }
    }
    const r = D.compare(old, now);
    const ctx = JSON.stringify(old) + ' / ' + JSON.stringify(now);
    const keep = (text, from, to, list) => { let out = ''; let at = from; for (const [x, y] of list) { out += text.slice(at, x); at = y; } return out + text.slice(at, to); };
    const oldParts = old.map(() => ''), newParts = now.map(() => '');
    for (const row of r.rows) {
      if (row.o !== null) { oldParts[row.o] += old[row.o].slice(row.oldFrom, row.oldTo); }
      if (row.n !== null) { newParts[row.n] += now[row.n].slice(row.newFrom, row.newTo); }
      if (row.o !== null && row.n !== null) { assert.equal(keep(old[row.o], row.oldFrom, row.oldTo, row.del), keep(now[row.n], row.newFrom, row.newTo, row.ins), 'kept text ' + ctx); }
      if (row.kind === 'same') { assert.equal(old[row.o], now[row.n]); }
    }
    assert.deepEqual(oldParts, old, 'old paragraphs ' + ctx);
    assert.deepEqual(newParts, now, 'new paragraphs ' + ctx);
    let text = joined(old);
    const so = starts(old);
    for (let k = r.changes.length - 1; k >= 0; k--) {
      const c = r.changes[k];
      const o1 = so[c.oldFrom.p] + c.oldFrom.c, o2 = so[c.oldTo.p] + c.oldTo.c;
      assert.equal(text.slice(o1, o2), c.oldText);
      text = text.slice(0, o1) + c.newText + text.slice(o2);
    }
    assert.equal(text, joined(now), 'places ' + ctx);
    for (let k = 1; k < r.pairs.length; k++) { assert.ok(r.pairs[k][0] > r.pairs[k - 1][0] && r.pairs[k][1] > r.pairs[k - 1][1], 'pairs ' + ctx); }
  }
});

// ---- long texts
test('a big text with one change is fast, and a second comparison after a keystroke is faster', () => {
  const old = Array.from({ length: 3000 }, (_, i) => `Absatz ${i} ` + 'wort '.repeat(30) + i);
  const next = old.slice(); next.splice(1500, 0, 'Ein neuer Absatz mitten drin, wie ihn jemand eben tippt.'); next[2000] += ' und ein Zusatz';
  let t0 = Date.now();
  const r = D.compare(old, next);
  const first = Date.now() - t0;
  assert.equal(r.rows.filter(x => x.kind === 'ins').length, 1);
  assert.equal(r.rows.filter(x => x.kind === 'mod').length, 1);
  next[2000] += 'x';
  t0 = Date.now();
  D.compare(old, next);
  const again = Date.now() - t0;
  console.log('     3000 paragraphs compared in', first, 'ms, after a keystroke in', again, 'ms');
  assert.ok(first < 500);
});
test('a long text with a change in every third paragraph', () => {
  let seed = 9;
  const rnd = n => { seed = (seed * 1103515245 + 12345) & 0x7fffffff; return seed % n; };
  const words = ['der', 'die', 'das', 'und', 'Haus', 'Garten', 'über', 'Brücke', 'wurde', 'schnell', 'Wald', 'Rabe', 'Hexe'];
  const old = Array.from({ length: 4000 }, (_, i) => `Absatz ${i}: ` + Array.from({ length: 40 }, () => words[rnd(words.length)]).join(' '));
  const pass = old.map((t, i) => (i % 3 === 0 ? t.replace('der', 'Der').replace(' und ', ', und ') : t));
  const t0 = Date.now();
  const r = D.compare(old, pass);
  console.log('     4000 paragraphs, every third changed, in', Date.now() - t0, 'ms');
  assert.equal(r.rows.filter(x => x.kind === 'same').length + r.rows.filter(x => x.kind === 'mod').length, 4000);
  assert.ok(Date.now() - t0 < 3000);
});
console.log(count + ' tests passed');

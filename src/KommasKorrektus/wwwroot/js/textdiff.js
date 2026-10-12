/* The comparison of two texts in the browser, paragraph by paragraph and word by word: what the editor shows beside the text while somebody writes (what differs from an
   earlier version, as it is now, not as it was saved), and what the comparison page shows of two versions. It is the same comparison the server makes (DocumentDiff.cs in
   KommasKorrektus.Core: same steps, same limits, same way of pairing, the same tokens), so that a text looks the same wherever it is compared; a test asks both
   for the same answer (tests/js/parity.mjs, DiffParityTests). It follows the way a diff is made for a pull request:

   1. the paragraphs that are the same (Myers' algorithm, on the paragraphs as whole strings) are the anchors;
   2. between two anchors the paragraphs that are left are paired by what they have in common (a changed paragraph is a changed paragraph, not one taken away and one
      new), and what has no partner is taken away or new;
   3. inside a changed paragraph the words are compared (Myers again, on the words).

   Pure functions, no page needed: window.KkTextDiff in the browser, module.exports for a test. */
(function (root) {
  'use strict';

  var SIMILAR = 0.35;          // how much two paragraphs have in common (shared words / all words) to be the same paragraph, changed
  var PAIRING_LIMIT = 150;     // blocks of leftovers that are bigger than this in both directions are not paired (taken away and new)
  var PAIRING_CELLS = 250000;  // the same for a block that is huge on one side: old x new, a limit for the browser (the server has none)
  var MAX_DISTANCE = 3000;     // how many differences Myers is allowed to look at: more, and the middle is one deletion and one insertion

  /**
   * Myers' O(ND) algorithm on two arrays of numbers: the pairs [i, j] of equal elements that stay, in order, or null when there are more than
   * <paramref name="limit"/> differences (the caller then takes the whole middle as taken away and new).
   */
  function myers(a, b, limit) {
    var n = a.length;
    var m = b.length;
    if (n === 0 || m === 0) { return []; }
    var max = Math.min(n + m, limit);
    var offset = max + 1;
    var v = new Int32Array(2 * max + 3);
    var trace = [];
    var found = -1;
    for (var d = 0; d <= max && found < 0; d += 1) {
      trace.push(v.slice());
      for (var k = -d; k <= d; k += 2) {
        var x;
        if (k === -d || (k !== d && v[offset + k - 1] < v[offset + k + 1])) { x = v[offset + k + 1]; } else { x = v[offset + k - 1] + 1; }
        var y = x - k;
        while (x < n && y < m && a[x] === b[y]) { x += 1; y += 1; }
        v[offset + k] = x;
        if (x >= n && y >= m) { found = d; break; }
      }
    }
    if (found < 0) { return null; }
    var matches = [];
    var px = n;
    var py = m;
    for (var step = found; step > 0; step -= 1) {
      var before = trace[step];
      var diagonal = px - py;
      var from = (diagonal === -step || (diagonal !== step && before[offset + diagonal - 1] < before[offset + diagonal + 1])) ? diagonal + 1 : diagonal - 1;
      var fromX = before[offset + from];
      var fromY = fromX - from;
      while (px > fromX && py > fromY) { px -= 1; py -= 1; matches.push([px, py]); }
      px = fromX;
      py = fromY;
    }
    while (px > 0 && py > 0) { px -= 1; py -= 1; matches.push([px, py]); }
    matches.reverse();
    return matches;
  }

  /** The strings of the two lists as numbers (the same string, the same number). */
  function idsOf(listA, listB) {
    var seen = new Map();
    var number = function (s) { var id = seen.get(s); if (id === undefined) { id = seen.size; seen.set(s, id); } return id; };
    return [listA.map(number), listB.map(number)];
  }

  // ------------------------------------------------------------------------------------------------ the words
  // Words, numbers, runs of blanks and single other characters: what the word-level comparison works on (the same pattern as DocumentDiff.TokenRegex).
  var TOKEN = /[\p{L}\p{M}][\p{L}\p{M}\p{N}'’\-]*|\p{N}+(?:[.,]\p{N}+)*|[ \t ]+|\p{Zl}|[\s\S]/gu;
  var WORD_CHARACTER = /[\p{L}\p{N}]/u;

  function tokens(text) {
    var list = [];
    var found;
    TOKEN.lastIndex = 0;
    while ((found = TOKEN.exec(text)) !== null) { list.push({ t: found[0], s: found.index, e: found.index + found[0].length }); }
    return list;
  }

  function wordSet(text) {
    var set = new Set();
    tokens(text).forEach(function (token) { if (WORD_CHARACTER.test(token.t)) { set.add(token.t); } });
    return set;
  }

  /** How much two sets of words have in common, from 0 to 1 (Dice's coefficient). */
  function dice(a, b) {
    if (a.size === 0 && b.size === 0) { return 1; }
    if (a.size === 0 || b.size === 0) { return 0; }
    var shared = 0;
    a.forEach(function (word) { if (b.has(word)) { shared += 1; } });
    return (2 * shared) / (a.size + b.size);
  }

  function similarity(x, y) { return x === y ? 1 : dice(wordSet(x), wordSet(y)); }

  // ------------------------------------------------------------------------------------------------ the paragraphs
  /** What is left of a stretch of paragraphs between two anchors: changed ones (paired by what they have in common), taken away, new, in the order of the pages. */
  function hunk(oldText, newText, O, N, out) {
    var n = O.length;
    var m = N.length;
    var del = function (i) { out.push({ o: O[i], n: null, kind: 'del' }); };
    var ins = function (j) { out.push({ o: null, n: N[j], kind: 'ins' }); };
    var mod = function (i, j) { out.push({ o: O[i], n: N[j], kind: 'mod' }); };
    var x;
    var y;
    if (n === 0) { for (y = 0; y < m; y += 1) { ins(y); } return; }
    if (m === 0) { for (x = 0; x < n; x += 1) { del(x); } return; }
    if ((n > PAIRING_LIMIT && m > PAIRING_LIMIT) || n * m > PAIRING_CELLS) {
      for (x = 0; x < n; x += 1) { del(x); }
      for (y = 0; y < m; y += 1) { ins(y); }
      return;
    }

    var oldSets = O.map(function (i) { return wordSet(oldText[i]); });
    var newSets = N.map(function (j) { return wordSet(newText[j]); });
    var sim = [];
    var score = [];
    for (x = 0; x <= n; x += 1) { score.push(new Float64Array(m + 1)); }
    for (x = 0; x < n; x += 1) {
      sim.push(new Float64Array(m));
      for (y = 0; y < m; y += 1) { sim[x][y] = dice(oldSets[x], newSets[y]); }
    }
    // the best order-keeping pairing (like the longest common subsequence, with the similarity as the weight)
    for (x = n - 1; x >= 0; x -= 1) {
      for (y = m - 1; y >= 0; y -= 1) {
        var best = Math.max(score[x + 1][y], score[x][y + 1]);
        if (sim[x][y] >= SIMILAR) { best = Math.max(best, score[x + 1][y + 1] + sim[x][y]); }
        score[x][y] = best;
      }
    }

    // Nothing is alike, but as many paragraphs went as came: a paragraph that was written over completely is still the same paragraph, in the same place.
    var alike = false;
    for (x = 0; x < n && !alike; x += 1) { for (y = 0; y < m; y += 1) { if (sim[x][y] >= SIMILAR) { alike = true; break; } } }
    if (!alike && n === m) { for (x = 0; x < n; x += 1) { mod(x, x); } return; }

    var a = 0;
    var b = 0;
    while (a < n || b < m) {
      if (a < n && b < m && sim[a][b] >= SIMILAR && Math.abs(score[a][b] - (score[a + 1][b + 1] + sim[a][b])) < 1e-9) { mod(a, b); a += 1; b += 1; }
      else if (a < n && (b >= m || Math.abs(score[a][b] - score[a + 1][b]) < 1e-9)) { del(a); a += 1; }
      else { ins(b); b += 1; }
    }
  }

  /**
   * Two lists of paragraphs (texts) as rows, in the order of the page: { o, n, kind } with the index in the old list (or null), the index in the new list (or null) and
   * the kind: "same", "mod" (the same paragraph, changed), "del" (taken away), "ins" (new).
   */
  function compare(oldText, newText) {
    var rows = [];
    var n = oldText.length;
    var m = newText.length;
    var start = 0;
    while (start < n && start < m && oldText[start] === newText[start]) { rows.push({ o: start, n: start, kind: 'same' }); start += 1; }
    var tail = 0;
    while (tail < n - start && tail < m - start && oldText[n - 1 - tail] === newText[m - 1 - tail]) { tail += 1; }
    var oldMid = oldText.slice(start, n - tail);
    var newMid = newText.slice(start, m - tail);
    var ids = idsOf(oldMid, newMid);
    var matches = myers(ids[0], ids[1], MAX_DISTANCE) || [];
    var io = 0;
    var jn = 0;
    var range = function (from, to) { var list = []; for (var x = from; x < to; x += 1) { list.push(x + start); } return list; };
    matches.forEach(function (pair) {
      hunk(oldText, newText, range(io, pair[0]), range(jn, pair[1]), rows);
      rows.push({ o: pair[0] + start, n: pair[1] + start, kind: 'same' });
      io = pair[0] + 1;
      jn = pair[1] + 1;
    });
    hunk(oldText, newText, range(io, oldMid.length), range(jn, newMid.length), rows);
    for (var k = 0; k < tail; k += 1) { rows.push({ o: n - tail + k, n: m - tail + k, kind: 'same' }); }
    return rows;
  }

  // ------------------------------------------------------------------------------------------------ the words of a changed paragraph
  /** Ranges [start, end) of the tokens that are not in the matches, merged where they touch. */
  function gaps(list, matched, side) {
    var keep = new Set(matched.map(function (pair) { return pair[side]; }));
    var out = [];
    list.forEach(function (token, i) {
      if (keep.has(i)) { return; }
      var last = out[out.length - 1];
      if (last && last[1] === token.s) { last[1] = token.e; } else { out.push([token.s, token.e]); }
    });
    return out;
  }

  /** The words that differ between an old and a new text of one paragraph: { del: [[start, end], ...] in the old text, ins: [...] in the new one }. */
  function words(oldText, newText) {
    var a = tokens(oldText);
    var b = tokens(newText);
    var start = 0;
    while (start < a.length && start < b.length && a[start].t === b[start].t) { start += 1; }
    var tail = 0;
    while (tail < a.length - start && tail < b.length - start && a[a.length - 1 - tail].t === b[b.length - 1 - tail].t) { tail += 1; }
    var midA = a.slice(start, a.length - tail);
    var midB = b.slice(start, b.length - tail);
    var ids = idsOf(midA.map(function (t) { return t.t; }), midB.map(function (t) { return t.t; }));
    var matches = myers(ids[0], ids[1], MAX_DISTANCE);
    if (matches === null) { matches = []; }
    return { del: gaps(midA, matches, 0), ins: gaps(midB, matches, 1) };
  }

  var api = { compare: compare, words: words, similarity: similarity, myers: myers };
  if (typeof module !== 'undefined' && module.exports) { module.exports = api; } else { root.KkTextDiff = api; }
})(typeof window !== 'undefined' ? window : this);

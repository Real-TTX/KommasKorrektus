/* The comparison of two texts in the browser, paragraph by paragraph and word by word: what the editor shows beside the text while somebody writes (what is different
   from an earlier version, as it is now, not as it was saved). It follows the way a diff is made for a pull request:

   1. the paragraphs that are the same (Myers' algorithm, on the paragraphs as whole strings) are the anchors;
   2. between two anchors the paragraphs that are left are paired by what they have in common (a changed paragraph is a changed paragraph, not one taken away and one
      new), the rest that stands opposite each other is a replacement, and what has no opposite is taken away or new;
   3. inside a changed paragraph the words are compared (Myers again, on the words).

   Pure functions, no page needed: window.KkTextDiff in the browser, module.exports for a test. */
(function (root) {
  'use strict';

  var SIMILAR = 0.45;          // how much two paragraphs have in common to be the same paragraph, changed
  var HUNK_PAIRING = 3600;     // the biggest block of leftovers that is paired by similarity (old x new): a bigger one is paired in order
  var PARAGRAPH_STEPS = 1500;  // how many differences Myers is allowed to look at before the paragraphs count as different from one another
  var WORD_STEPS = 3000;

  /**
   * Myers' O(ND) algorithm on two arrays of numbers: the pairs [i, j] of equal elements that stay, in order, or null when there are more than
   * <paramref name="limit"/> differences (the caller then gives up on the finer comparison).
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

  // ------------------------------------------------------------------------------------------------ similarity of two paragraphs
  var WORD = /[\p{L}\p{N}_]+/gu;

  function wordsOf(text) { return (text.toLowerCase().match(WORD) || []); }

  function profile(text, mode) {
    var set = new Set();
    if (mode === 'words') {
      wordsOf(text).forEach(function (w) { set.add(w); });
    } else {
      var flat = text.toLowerCase().replace(/\s+/g, ' ');
      for (var i = 0; i + 1 < flat.length; i += 1) { set.add(flat.substr(i, 2)); }
      if (flat.length === 1) { set.add(flat); }
    }
    return set;
  }

  /** How much two texts have in common, from 0 to 1 (Dice's coefficient on their words, or on pairs of letters when they are short). */
  function similarity(x, y) {
    if (x === y) { return 1; }
    if (x.length === 0 || y.length === 0) { return 0; }
    var mode = Math.min(wordsOf(x).length, wordsOf(y).length) >= 4 ? 'words' : 'letters';
    var a = profile(x, mode);
    var b = profile(y, mode);
    if (a.size === 0 || b.size === 0) { return 0; }
    var common = 0;
    a.forEach(function (g) { if (b.has(g)) { common += 1; } });
    return (2 * common) / (a.size + b.size);
  }

  // ------------------------------------------------------------------------------------------------ the paragraphs
  /** The leftovers between two anchors: [pairs of indexes] of paragraphs that are the same one, changed (in order, as much as they have in common). */
  function pairBySimilarity(oldText, newText, O, N) {
    var rows = O.length;
    var cols = N.length;
    var sim = [];
    for (var i = 0; i < rows; i += 1) {
      sim.push([]);
      for (var j = 0; j < cols; j += 1) { sim[i].push(similarity(oldText[O[i]], newText[N[j]])); }
    }
    // the best order-keeping pairing (like the longest common subsequence, with the similarity as the weight)
    var best = [];
    for (var a = 0; a <= rows; a += 1) { best.push(new Float64Array(cols + 1)); }
    for (var r = 1; r <= rows; r += 1) {
      for (var c = 1; c <= cols; c += 1) {
        var take = sim[r - 1][c - 1] >= SIMILAR ? best[r - 1][c - 1] + sim[r - 1][c - 1] : -1;
        best[r][c] = Math.max(best[r - 1][c], best[r][c - 1], take);
      }
    }
    var pairs = [];
    var ri = rows;
    var ci = cols;
    while (ri > 0 && ci > 0) {
      var here = best[ri][ci];
      if (sim[ri - 1][ci - 1] >= SIMILAR && here === best[ri - 1][ci - 1] + sim[ri - 1][ci - 1] && here > best[ri - 1][ci] && here > best[ri][ci - 1]) {
        pairs.push([ri - 1, ci - 1]);
        ri -= 1;
        ci -= 1;
      } else if (best[ri - 1][ci] >= best[ri][ci - 1]) {
        ri -= 1;
      } else {
        ci -= 1;
      }
    }
    return pairs.reverse();
  }

  /** What is left of a stretch of paragraphs between two anchors: changed ones, replacements (standing opposite each other), taken away, new. */
  function hunk(oldText, newText, O, N, out) {
    if (O.length === 0 || N.length === 0) {
      O.forEach(function (o) { out.push({ o: o, n: null, kind: 'del' }); });
      N.forEach(function (n) { out.push({ o: null, n: n, kind: 'ins' }); });
      return;
    }
    var pairs = O.length * N.length <= HUNK_PAIRING ? pairBySimilarity(oldText, newText, O, N) : [];
    var po = 0;
    var pn = 0;
    var rest = function (toO, toN) {
      var count = Math.min(toO - po, toN - pn);
      for (var k = 0; k < count; k += 1) { out.push({ o: O[po + k], n: N[pn + k], kind: 'repl' }); }
      for (var x = po + count; x < toO; x += 1) { out.push({ o: O[x], n: null, kind: 'del' }); }
      for (var y = pn + count; y < toN; y += 1) { out.push({ o: null, n: N[y], kind: 'ins' }); }
    };
    pairs.forEach(function (pair) {
      rest(pair[0], pair[1]);
      out.push({ o: O[pair[0]], n: N[pair[1]], kind: 'mod' });
      po = pair[0] + 1;
      pn = pair[1] + 1;
    });
    rest(O.length, N.length);
  }

  /**
   * Two lists of paragraphs (texts) as rows, in the order of the page: { o, n, kind } with the index in the old list (or null), the index in the new list (or null) and
   * the kind: "same", "mod" (the same paragraph, changed), "repl" (a replacement: they stand opposite each other, no more), "del" (taken away), "ins" (new).
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
    var matches = myers(ids[0], ids[1], PARAGRAPH_STEPS) || [];
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
  var TOKEN = /\s+|[\p{L}\p{N}_]+(?:['’-][\p{L}\p{N}_]+)*|[^\s\p{L}\p{N}_]/gu;

  function tokens(text) {
    var list = [];
    var found;
    TOKEN.lastIndex = 0;
    while ((found = TOKEN.exec(text)) !== null) { list.push({ t: found[0], s: found.index, e: found.index + found[0].length }); }
    return list;
  }

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
    var matches = myers(ids[0], ids[1], WORD_STEPS);
    if (matches === null) { matches = []; }
    return { del: gaps(midA, matches, 0), ins: gaps(midB, matches, 1) };
  }

  var api = { compare: compare, words: words, similarity: similarity, myers: myers };
  if (typeof module !== 'undefined' && module.exports) { module.exports = api; } else { root.KkTextDiff = api; }
})(typeof window !== 'undefined' ? window : this);

/* The comparison of two texts in the browser, the way Visual Studio Code compares two files (its diff, MIT licence: src/vs/editor/common/diff/defaultLinesDiffComputer),
   with paragraphs where VS Code has lines: what the editor shows beside the text while somebody writes (what differs from an earlier version, as it is now, not as it
   was saved), and what the comparison page shows of two versions. The server makes the same comparison (DocumentDiff.cs in KommasKorrektus.Core: the same steps, limits
   and characters), so that a text looks the same wherever it is compared; a test asks both for the same answer (tests/js/parity.mjs, DiffParityTests).

   1. The paragraphs are compared as whole strings (blanks at their ends aside): a scoring longest common subsequence for up to 1700 of them, that values a long paragraph
      that stayed over an empty one (empty paragraphs are no good anchors), Myers above that. The places are moved to good boundaries and joined when only a few
      characters of unchanged text lie between them.
   2. Every stretch of paragraphs that differs is compared character by character, the paragraph breaks being characters too: a paragraph that was split, two that were
      joined, a sentence put in between are seen for what they are. The result is cleaned up the way a person reads it: a word that is mostly changed is marked whole,
      changes only a character or two apart are one change, a short unchanged scrap between two long changes goes with them.
   3. What came out is cut at the paragraph breaks into rows: a row is a paragraph as Word keeps it with tracked changes (a split paragraph is two rows of one old
      paragraph, joined paragraphs are two rows of one new paragraph, the break that was put in or taken away is the mark at the end of a row), and the paragraphs that
      stand across from each other are found (VS Code's alignment inside a change).

   Deviations from VS Code, the same on the server: letters and digits of any script are word characters; Myers has a limit of work instead of a time out (then the
   paragraphs that stayed are found by the ones that occur once, and the characters by the words); a word is not marked whole across a paragraph break.

   Pure functions, no page needed: window.KkTextDiff in the browser, module.exports for a test. */
(function (root) {
  'use strict';

  var SEP = 0x2029;               // the paragraph break, as a character of a stretch of paragraphs
  var SOFT = 0x2028;              // a line break inside a paragraph (the browser hands it in as \n)
  var SEP_TEXT = String.fromCharCode(SEP);
  var LINE_DP_LIMIT = 1700;       // paragraphs, old and new together, up to which the scoring subsequence is used (VS Code: the same)
  var CHAR_DP_LIMIT = 500;        // characters of a stretch, old and new together, up to which the subsequence is used, Myers above it (VS Code: the same)
  var WORK_LIMIT = 4000000;       // steps Myers may take before it gives up (VS Code has a time out)
  var MAX_SHIFT = 100;            // how far a change is moved to a better boundary (VS Code: the same)

  // ================================================================================================ characters
  /** Whitespace, as a fixed list (the string functions of the browser and of .NET do not agree on all of it). */
  function isWhite(c) {
    return (c >= 0x09 && c <= 0x0d) || c === 0x20 || c === 0xa0 || c === 0x1680 || (c >= 0x2000 && c <= 0x200a) || c === 0x2028 || c === 0x2029 ||
      c === 0x202f || c === 0x205f || c === 0x3000 || c === 0xfeff;
  }

  function trimmed(text) {
    var a = 0;
    var b = text.length;
    while (a < b && isWhite(text.charCodeAt(a))) { a += 1; }
    while (b > a && isWhite(text.charCodeAt(b - 1))) { b -= 1; }
    return a === 0 && b === text.length ? text : text.slice(a, b);
  }

  var LOWER = 0;   // the kinds of characters VS Code tells apart for a boundary (a letter that is no capital, a capital, a digit ...)
  var UPPER = 1;
  var NUMBER = 2;
  var END = 3;
  var OTHER = 4;
  var SEPARATOR = 5;
  var SPACE = 6;
  var BREAK = 8;
  var KIND_SCORE = [0, 0, 0, 10, 2, 30, 3, 10, 10];
  var kinds = new Uint8Array(65536);   // 0: not looked up yet, else the kind + 1
  var CAPITAL = /[\p{Lu}\p{Lt}]/u;
  var LETTER = /[\p{Ll}\p{Lm}\p{Lo}\p{M}]/u;
  var DIGIT = /\p{N}/u;

  function kindOf(c) {
    if (c < 0) { return END; }
    var known = kinds[c];
    if (known) { return known - 1; }
    var k;
    if (c === SEP) { k = BREAK; }
    else if (c === 0x20 || c === 0x09 || c === 0xa0 || c === 0x202f || c === SOFT) { k = SPACE; }
    else if (c === 0x2c || c === 0x3b) { k = SEPARATOR; }
    else {
      var s = String.fromCharCode(c);
      k = CAPITAL.test(s) ? UPPER : LETTER.test(s) ? LOWER : DIGIT.test(s) ? NUMBER : OTHER;
    }
    kinds[c] = k + 1;
    return k;
  }

  function isWordChar(c) { var k = kindOf(c); return k === LOWER || k === UPPER || k === NUMBER; }

  function floorLog2(x) { return 31 - Math.clz32(x); }

  // ================================================================================================ changes: a range of the old sequence for a range of the new one
  function D(s1, e1, s2, e2) { return { s1: s1, e1: e1, s2: s2, e2: e2 }; }
  function moved(d, by) { return by === 0 ? d : D(d.s1 + by, d.e1 + by, d.s2 + by, d.e2 + by); }
  function swapped(d) { return D(d.s2, d.e2, d.s1, d.e1); }
  function joined(a, b) { return D(Math.min(a.s1, b.s1), Math.max(a.e1, b.e1), Math.min(a.s2, b.s2), Math.max(a.e2, b.e2)); }
  function size(d) { return (d.e1 - d.s1) + (d.e2 - d.s2); }
  function intersection(a, b) {
    var s1 = Math.max(a.s1, b.s1);
    var e1 = Math.min(a.e1, b.e1);
    var s2 = Math.max(a.s2, b.s2);
    var e2 = Math.min(a.e2, b.e2);
    return s1 <= e1 && s2 <= e2 ? D(s1, e1, s2, e2) : null;
  }
  function overlap(s, e, t, f) { return Math.max(s, t) < Math.min(e, f); }

  /** The unchanged stretches between changes (VS Code: SequenceDiff.invert). */
  function invert(diffs, length1) {
    var out = [];
    for (var i = 0; i <= diffs.length; i += 1) {
      var a = i === 0 ? null : diffs[i - 1];
      var b = i === diffs.length ? null : diffs[i];
      var s1 = a ? a.e1 : 0;
      var s2 = a ? a.e2 : 0;
      if (b) { out.push(D(s1, b.s1, s2, b.s2)); } else { out.push(D(s1, length1, s2, (a ? a.e2 - a.e1 : 0) + length1)); }
    }
    return out;
  }

  // ================================================================================================ sequences
  /** The paragraphs as numbers (the same text without the blanks at its ends, the same number). */
  function paragraphSequence(lines, hashes) {
    return {
      length: lines.length,
      get: function (i) { return hashes[i]; },
      strongly: function (i, j) { return lines[i] === lines[j]; },
      boundary: function (i) { return 1000 - (indent(i === 0 ? '' : lines[i - 1]) + indent(i === lines.length ? '' : lines[i])); },
      /** How many characters of the paragraphs from..to are not whitespace, counted up to one more than limit (the breaks between them are whitespace). */
      visible: function (from, to, limit) {
        var count = 0;
        for (var p = from; p < to && count <= limit; p += 1) {
          var t = lines[p];
          for (var i = 0; i < t.length && count <= limit; i += 1) { if (!isWhite(t.charCodeAt(i))) { count += 1; } }
        }
        return count;
      },
    };
  }

  function indent(text) {
    var i = 0;
    while (i < text.length && (text.charCodeAt(i) === 0x20 || text.charCodeAt(i) === 0x09)) { i += 1; }
    return i;
  }

  /** The characters of a stretch of paragraphs, every paragraph followed by its break. */
  function charSequence(codes) {
    var starts = [0];   // where the lines (paragraphs) of the stretch begin; after the last break a line of nothing begins
    for (var i = 0; i < codes.length; i += 1) { if (codes[i] === SEP) { starts.push(i + 1); } }
    var seq = {
      length: codes.length,
      codes: codes,
      get: function (i) { return codes[i]; },
      strongly: function (i, j) { return codes[i] === codes[j]; },
      boundary: function (i) {
        var prev = kindOf(i > 0 ? codes[i - 1] : -1);
        var next = kindOf(i < codes.length ? codes[i] : -1);
        if (prev === BREAK) { return 150; }   // the break before a change is the place for it
        var score = 0;
        if (prev !== next) { score += 10; if (prev === LOWER && next === UPPER) { score += 1; } }
        return score + KIND_SCORE[prev] + KIND_SCORE[next];
      },
      /** The word the character at i belongs to, [start, end), or null. */
      word: function (i) {
        if (i < 0 || i >= codes.length || !isWordChar(codes[i])) { return null; }
        var s = i;
        while (s > 0 && isWordChar(codes[s - 1])) { s -= 1; }
        var e = i;
        while (e < codes.length && isWordChar(codes[e])) { e += 1; }
        return [s, e];
      },
      lineOf: function (offset) {
        var lo = 0;
        var hi = starts.length - 1;
        while (lo < hi) { var mid = (lo + hi + 1) >> 1; if (starts[mid] <= offset) { lo = mid; } else { hi = mid - 1; } }
        return lo;
      },
      linesIn: function (from, to) { return seq.lineOf(to) - seq.lineOf(from); },
      fullLines: function (from, to) {
        var s = starts[seq.lineOf(from)];
        var e = codes.length;
        for (var k = seq.lineOf(to); k < starts.length; k += 1) { if (starts[k] >= to) { e = starts[k]; break; } }
        return [s, e];
      },
      text: function (from, to) {
        var out = '';
        for (var k = from; k < to; k += 8192) { out += String.fromCharCode.apply(null, codes.subarray(k, Math.min(to, k + 8192))); }
        return out;
      },
    };
    return seq;
  }

  /** A part of a sequence, for the algorithms (they only ask for the length and the elements). */
  function view(seq, from, to) { return { length: to - from, get: function (i) { return seq.get(from + i); } }; }

  // ================================================================================================ the algorithms
  /**
   * The longest common subsequence with a score for every pair of equal elements (VS Code: DynamicProgrammingDiffing): O(n·m), only for short sequences. A run of equal
   * elements is worth more the longer it gets (bonus for every step of it).
   */
  var table = { size: 0, best: null, dir: null, run: null };   // the tables of lcs, kept for the next call (every cell is written before it is read)

  function elementsOf(seq) {
    var out = new Int32Array(seq.length);
    for (var i = 0; i < seq.length; i += 1) { out[i] = seq.get(i); }
    return out;
  }

  function lcs(seq1, seq2, score, bonus) {
    var n = seq1.length;
    var m = seq2.length;
    if (n === 0 || m === 0) { return n === m ? [] : [D(0, n, 0, m)]; }
    if (table.size < n * m) { table.size = n * m; table.best = new Float64Array(n * m); table.dir = new Uint8Array(n * m); table.run = new Int32Array(n * m); }
    var best = table.best;
    var dir = table.dir;
    var run = table.run;
    var a = elementsOf(seq1);
    var b = elementsOf(seq2);
    for (var s1 = 0; s1 < n; s1 += 1) {
      var e1 = a[s1];
      for (var s2 = 0; s2 < m; s2 += 1) {
        var at = s1 + s2 * n;
        var horizontal = s1 === 0 ? 0 : best[at - 1];
        var vertical = s2 === 0 ? 0 : best[at - n];
        var extended;
        if (e1 === b[s2]) {
          extended = s1 === 0 || s2 === 0 ? 0 : best[at - 1 - n];
          if (s1 > 0 && s2 > 0 && dir[at - 1 - n] === 3) { extended += run[at - 1 - n] * bonus; }
          extended += score ? score(s1, s2) : 1;
        } else {
          extended = -1;
        }
        var value = Math.max(horizontal, vertical, extended);
        if (value === extended) { run[at] = (s1 > 0 && s2 > 0 ? run[at - 1 - n] : 0) + 1; dir[at] = 3; }
        else if (value === horizontal) { run[at] = 0; dir[at] = 1; }
        else if (value === vertical) { run[at] = 0; dir[at] = 2; }
        best[at] = value;
      }
    }

    var out = [];
    var last1 = n;
    var last2 = m;
    var report = function (a, b) {
      if (a + 1 !== last1 || b + 1 !== last2) { out.push(D(a + 1, last1, b + 1, last2)); }
      last1 = a;
      last2 = b;
    };
    var i = n - 1;
    var j = m - 1;
    while (i >= 0 && j >= 0) {
      var d = dir[i + j * n];
      if (d === 3) { report(i, j); i -= 1; j -= 1; } else if (d === 1) { i -= 1; } else { j -= 1; }
    }
    report(-1, -1);
    out.reverse();
    return out;
  }

  /** Myers' O(ND) algorithm, the way VS Code walks it (MyersDiffAlgorithm), or null when it would take more than the limit of work. */
  function myers(seq1, seq2) {
    var n = seq1.length;
    var m = seq2.length;
    if (n === 0 || m === 0) { return n === m ? [] : [D(0, n, 0, m)]; }
    var a = elementsOf(seq1);
    var b = elementsOf(seq2);
    var work = 0;
    var snake = function (x, y) {
      while (x < n && y < m && a[x] === b[y]) { x += 1; y += 1; }
      work += 1;
      return x;
    };
    var off = m + 2;
    var V = new Int32Array(n + m + 5);
    var P = new Int32Array(n + m + 5).fill(-1);
    // the snakes of the paths: before, x, y, length
    var cap = 1024;
    var prev = new Int32Array(cap);
    var sx = new Int32Array(cap);
    var sy = new Int32Array(cap);
    var sl = new Int32Array(cap);
    var count = 0;
    var add = function (before, x, y, length) {
      if (count === cap) {
        cap *= 2;
        var grow = function (a) { var b = new Int32Array(cap); b.set(a); return b; };
        prev = grow(prev); sx = grow(sx); sy = grow(sy); sl = grow(sl);
      }
      prev[count] = before; sx[count] = x; sy[count] = y; sl[count] = length;
      count += 1;
      return count - 1;
    };

    V[off] = snake(0, 0);
    work += V[off];
    P[off] = V[off] === 0 ? -1 : add(-1, 0, 0, V[off]);
    var k = 0;
    var d = 0;
    loop:
    while (true) {
      d += 1;
      if (work > WORK_LIMIT) { return null; }
      var lower = -Math.min(d, m + (d % 2));
      var upper = Math.min(d, n + (d % 2));
      for (k = lower; k <= upper; k += 2) {
        var top = k === upper ? -1 : V[off + k + 1];
        var left = k === lower ? -1 : V[off + k - 1] + 1;
        var x = Math.min(Math.max(top, left), n);
        var y = x - k;
        if (x > n || y > m) { continue; }
        var reached = snake(x, y);
        work += reached - x;
        V[off + k] = reached;
        var before = x === top ? P[off + k + 1] : P[off + k - 1];
        P[off + k] = reached !== x ? add(before, x, y, reached - x) : before;
        if (V[off + k] === n && V[off + k] - k === m) { break loop; }
      }
    }

    var path = P[off + k];
    var out = [];
    var last1 = n;
    var last2 = m;
    while (true) {
      var endX = path >= 0 ? sx[path] + sl[path] : 0;
      var endY = path >= 0 ? sy[path] + sl[path] : 0;
      if (endX !== last1 || endY !== last2) { out.push(D(endX, last1, endY, last2)); }
      if (path < 0) { break; }
      last1 = sx[path];
      last2 = sy[path];
      path = prev[path];
    }
    out.reverse();
    return out;
  }

  /** The longest chain of pairs [i, j] (sorted by i) in which j grows too (patience sorting). */
  function increasing(pairs) {
    var tails = [];
    var back = new Int32Array(pairs.length);
    for (var k = 0; k < pairs.length; k += 1) {
      var j = pairs[k][1];
      var lo = 0;
      var hi = tails.length;
      while (lo < hi) { var mid = (lo + hi) >> 1; if (pairs[tails[mid]][1] < j) { lo = mid + 1; } else { hi = mid; } }
      back[k] = lo > 0 ? tails[lo - 1] : -1;
      tails[lo] = k;
    }
    var out = [];
    for (var at = tails.length > 0 ? tails[tails.length - 1] : -1; at >= 0; at = back[at]) { out.push(pairs[at]); }
    return out.reverse();
  }

  /**
   * When Myers gives up (two long sequences with many differences): the elements that occur exactly once in each are the anchors (in the order both keep), and between
   * two anchors Myers runs again, on far less. What is still too much between two anchors is one change.
   */
  function anchored(seq1, seq2) {
    var n = seq1.length;
    var m = seq2.length;
    var count1 = new Map();
    var count2 = new Map();
    var at2 = new Map();
    var i;
    for (i = 0; i < n; i += 1) { count1.set(seq1.get(i), (count1.get(seq1.get(i)) || 0) + 1); }
    for (i = 0; i < m; i += 1) { count2.set(seq2.get(i), (count2.get(seq2.get(i)) || 0) + 1); at2.set(seq2.get(i), i); }
    var candidates = [];
    for (i = 0; i < n; i += 1) {
      var e = seq1.get(i);
      if (count1.get(e) === 1 && count2.get(e) === 1) { candidates.push([i, at2.get(e)]); }
    }
    var anchors = increasing(candidates);
    var out = [];
    var gap = function (x1, x2, y1, y2) {
      while (x1 < x2 && y1 < y2 && seq1.get(x1) === seq2.get(y1)) { x1 += 1; y1 += 1; }
      while (x2 > x1 && y2 > y1 && seq1.get(x2 - 1) === seq2.get(y2 - 1)) { x2 -= 1; y2 -= 1; }
      if (x1 === x2 && y1 === y2) { return; }
      // nothing of the one is in the other: one change, and Myers need not find that out the long way
      var there = new Set();
      var k;
      for (k = y1; k < y2; k += 1) { there.add(seq2.get(k)); }
      var common = false;
      for (k = x1; k < x2 && !common; k += 1) { common = there.has(seq1.get(k)); }
      var inner = (common && myers(view(seq1, x1, x2), view(seq2, y1, y2))) || [D(0, x2 - x1, 0, y2 - y1)];
      inner.forEach(function (d) { out.push(D(d.s1 + x1, d.e1 + x1, d.s2 + y1, d.e2 + y1)); });
    };
    var a = 0;
    var b = 0;
    anchors.forEach(function (pair) { gap(a, pair[0], b, pair[1]); a = pair[0] + 1; b = pair[1] + 1; });
    gap(a, n, b, m);
    return out;
  }

  // ================================================================================================ cleaning up (VS Code: heuristicSequenceOptimizations)
  function optimize(seq1, seq2, diffs) {
    diffs = joinByShifting(seq1, seq2, diffs);
    diffs = joinByShifting(seq1, seq2, diffs);
    return shiftToBoundaries(seq1, seq2, diffs);
  }

  /** Changes that only put in or only take away are moved left, then right, as far as the text allows, and joined with their neighbour when they meet. */
  function joinByShifting(seq1, seq2, diffs) {
    if (diffs.length === 0) { return diffs; }
    var result = [diffs[0]];
    var i;
    for (i = 1; i < diffs.length; i += 1) {
      var prev = result[result.length - 1];
      var cur = diffs[i];
      if (cur.s1 === cur.e1 || cur.s2 === cur.e2) {
        var length = cur.s1 - prev.e1;
        var d;
        for (d = 1; d <= length; d += 1) {
          if (seq1.get(cur.s1 - d) !== seq1.get(cur.e1 - d) || seq2.get(cur.s2 - d) !== seq2.get(cur.e2 - d)) { break; }
        }
        d -= 1;
        if (d === length) {
          result[result.length - 1] = D(prev.s1, cur.e1 - length, prev.s2, cur.e2 - length);
          continue;
        }
        cur = moved(cur, -d);
      }
      result.push(cur);
    }

    var result2 = [];
    for (i = 0; i < result.length - 1; i += 1) {
      var next = result[i + 1];
      var c = result[i];
      if (c.s1 === c.e1 || c.s2 === c.e2) {
        var len = next.s1 - c.e1;
        var k;
        for (k = 0; k < len; k += 1) {
          if (!seq1.strongly(c.s1 + k, c.e1 + k) || !seq2.strongly(c.s2 + k, c.e2 + k)) { break; }
        }
        if (k === len) {
          result[i + 1] = D(c.s1 + len, next.e1, c.s2 + len, next.e2);
          continue;
        }
        if (k > 0) { c = moved(c, k); }
      }
      result2.push(c);
    }
    if (result.length > 0) { result2.push(result[result.length - 1]); }
    return result2;
  }

  function shiftToBoundaries(seq1, seq2, diffs) {
    for (var i = 0; i < diffs.length; i += 1) {
      var prev = i > 0 ? diffs[i - 1] : null;
      var diff = diffs[i];
      var next = i + 1 < diffs.length ? diffs[i + 1] : null;
      var from1 = prev ? prev.e1 + 1 : 0;
      var to1 = next ? next.s1 - 1 : seq1.length;
      var from2 = prev ? prev.e2 + 1 : 0;
      var to2 = next ? next.s2 - 1 : seq2.length;
      if (diff.s1 === diff.e1) { diffs[i] = toBetterPosition(diff, seq1, seq2, from1, to1, from2, to2); }
      else if (diff.s2 === diff.e2) { diffs[i] = swapped(toBetterPosition(swapped(diff), seq2, seq1, from2, to2, from1, to1)); }
    }
    return diffs;
  }

  /** An insertion (nothing of seq1) moved to where it begins and ends at the best boundaries. */
  function toBetterPosition(diff, seq1, seq2, from1, to1, from2, to2) {
    var before = 1;
    while (diff.s1 - before >= from1 && diff.s2 - before >= from2 && seq2.strongly(diff.s2 - before, diff.e2 - before) && before < MAX_SHIFT) { before += 1; }
    before -= 1;
    var after = 0;
    while (diff.s1 + after < to1 && diff.e2 + after < to2 && seq2.strongly(diff.s2 + after, diff.e2 + after) && after < MAX_SHIFT) { after += 1; }
    if (before === 0 && after === 0) { return diff; }
    var bestDelta = 0;
    var bestScore = -1;
    for (var delta = -before; delta <= after; delta += 1) {
      var score = seq1.boundary(diff.s1 + delta) + seq2.boundary(diff.s2 + delta) + seq2.boundary(diff.e2 + delta);
      if (score > bestScore) { bestScore = score; bestDelta = delta; }
    }
    return moved(diff, bestDelta);
  }

  /** Two changes with no more than two characters between them are one (but not across a paragraph break: those are changes of two paragraphs). */
  function removeShortMatches(seq1, diffs) {
    var result = [];
    diffs.forEach(function (s) {
      var last = result[result.length - 1];
      if (!last) { result.push(s); return; }
      if ((s.s1 - last.e1 <= 2 || s.s2 - last.e2 <= 2) && seq1.linesIn(last.e1, s.s1) === 0) { result[result.length - 1] = joined(last, s); } else { result.push(s); }
    });
    return result;
  }

  /** A word that is mostly changed (less than two thirds of it stayed) is changed as a whole. */
  function extendToWords(seq1, seq2, diffs) {
    var equal = invert(diffs, seq1.length);
    var next = 0;   // the first unchanged stretch not looked at yet
    var additional = [];
    var last1 = 0;
    var last2 = 0;

    var scanWord = function (p1, p2, mapping) {
      if (p1 < last1 || p2 < last2) { return; }
      var w1 = seq1.word(p1);
      var w2 = seq2.word(p2);
      if (!w1 || !w2) { return; }
      var w = D(w1[0], w1[1], w2[0], w2[1]);
      var part = intersection(w, mapping);
      var equal1 = part.e1 - part.s1;
      var equal2 = part.e2 - part.s2;
      while (next < equal.length) {
        var following = equal[next];
        if (!overlap(following.s1, following.e1, w.s1, w.e1) && !overlap(following.s2, following.e2, w.s2, w.e2)) { break; }
        var v1 = seq1.word(following.s1);
        var v2 = seq2.word(following.s2);
        if (!v1 || !v2) { break; }
        var v = D(v1[0], v1[1], v2[0], v2[1]);
        var inner = intersection(v, following);
        equal1 += inner.e1 - inner.s1;
        equal2 += inner.e2 - inner.s2;
        w = joined(w, v);
        if (w.e1 >= following.e1) { next += 1; } else { break; }
      }
      if (3 * (equal1 + equal2) < 2 * size(w) && seq1.linesIn(w.s1, w.e1) === 0 && seq2.linesIn(w.s2, w.e2) === 0) { additional.push(w); }
      last1 = w.e1;
      last2 = w.e2;
    };

    while (next < equal.length) {
      var stretch = equal[next];
      next += 1;
      if (stretch.s1 === stretch.e1) { continue; }
      scanWord(stretch.s1, stretch.s2, stretch);
      scanWord(stretch.e1 - 1, stretch.e2 - 1, stretch);
    }
    return merge(diffs, additional);
  }

  function merge(a, b) {
    var result = [];
    var i = 0;
    var j = 0;
    while (i < a.length || j < b.length) {
      var take = i < a.length && (j >= b.length || a[i].s1 < b[j].s1) ? a[i++] : b[j++];
      if (result.length > 0 && result[result.length - 1].e1 >= take.s1) { result[result.length - 1] = joined(result[result.length - 1], take); } else { result.push(take); }
    }
    return result;
  }

  /** Paragraphs that stayed between two changes are part of them when they hold no more than four visible characters and one of the changes is big. */
  function joinAcrossShortParagraphs(lines, diffs) {
    if (diffs.length === 0) { return diffs; }
    var counter = 0;
    var repeat;
    do {
      repeat = false;
      var result = [diffs[0]];
      for (var i = 1; i < diffs.length; i += 1) {
        var cur = diffs[i];
        var last = result[result.length - 1];
        if (lines.visible(last.e1, cur.s1, 4) <= 4 && (size(last) > 5 || size(cur) > 5)) {
          repeat = true;
          result[result.length - 1] = joined(last, cur);
        } else {
          result.push(cur);
        }
      }
      diffs = result;
    } while (counter++ < 10 && repeat);
    return diffs;
  }

  var CAP = 2 * 40 + 50;
  var JOIN_LIMIT = (function () { var m = CAP * Math.sqrt(CAP); return m * Math.sqrt(m) * 1.3; })();
  function pow15(v) { return v * Math.sqrt(v); }   // v^1.5, the same on every machine (a square root is exact, a power is not everywhere)

  /** Short unchanged text between two long changes goes with them; a short rest of the paragraph before or after a long change too. */
  function removeShortTextBetweenLongDiffs(seq1, seq2, diffs) {
    if (diffs.length === 0) { return diffs; }
    var counter = 0;
    var repeat;
    var weight = function (lines, length) { return pow15(Math.min(lines * 40 + length, CAP)); };
    do {
      repeat = false;
      var result = [diffs[0]];
      for (var i = 1; i < diffs.length; i += 1) {
        var cur = diffs[i];
        var last = result[result.length - 1];
        var join = false;
        var lines = seq1.linesIn(last.e1, cur.s1);
        // (VS Code joins across one line break, the break being blanks when trimmed; here two paragraphs are two places)
        if (lines === 0 && cur.s1 - last.e1 <= 500) {
          var unchanged = trimmed(seq1.text(last.e1, cur.s1));
          if (unchanged.length <= 20 && unchanged.indexOf(SEP_TEXT) < 0) {
            var before = pow15(weight(seq1.linesIn(last.s1, last.e1), last.e1 - last.s1) + weight(seq2.linesIn(last.s2, last.e2), last.e2 - last.s2));
            var after = pow15(weight(seq1.linesIn(cur.s1, cur.e1), cur.e1 - cur.s1) + weight(seq2.linesIn(cur.s2, cur.e2), cur.e2 - cur.s2));
            join = before + after > JOIN_LIMIT;
          }
        }
        if (join) {
          repeat = true;
          result[result.length - 1] = joined(last, cur);
        } else {
          result.push(cur);
        }
      }
      diffs = result;
    } while (counter++ < 10 && repeat);

    var out = [];
    var MAX = Number.MAX_SAFE_INTEGER;
    for (var k = 0; k < diffs.length; k += 1) {
      var prev = k > 0 ? diffs[k - 1] : null;
      var current = diffs[k];
      var following = k + 1 < diffs.length ? diffs[k + 1] : null;
      var changed = current;
      var mark = function (text) { return text.length > 0 && trimmed(text).length <= 3 && size(current) > 100; };
      var full = seq1.fullLines(current.s1, current.e1);
      // the rest of the paragraph, without its break (VS Code counts the line break in; here it would make a change that puts in text at the end of a paragraph take its break)
      if (full[1] > current.e1 && seq1.codes[full[1] - 1] === SEP) { full[1] -= 1; }
      var prefix = full[0] < current.s1 ? current.s1 - full[0] : 0;
      if (prefix > 0 && mark(seq1.text(full[0], current.s1))) { changed = D(changed.s1 - prefix, changed.e1, changed.s2 - prefix, changed.e2); }
      var suffix = full[1] > current.e1 ? full[1] - current.e1 : 0;
      if (suffix > 0 && mark(seq1.text(current.e1, full[1]))) { changed = D(changed.s1, changed.e1 + suffix, changed.s2, changed.e2 + suffix); }
      var fitting = intersection(changed, D(prev ? prev.e1 : 0, following ? following.s1 : MAX, prev ? prev.e2 : 0, following ? following.s2 : MAX));
      var tail = out[out.length - 1];
      if (tail && fitting.s1 === tail.e1 && fitting.s2 === tail.e2) { out[out.length - 1] = joined(tail, fitting); } else { out.push(fitting); }
    }
    return out;
  }

  // ================================================================================================ a stretch of paragraphs, character by character
  /** Where the words, the runs of blanks and the other characters (one each) of a stretch begin, and the end. */
  function tokenStarts(seq) {
    var codes = seq.codes;
    var starts = [];
    var i = 0;
    while (i < codes.length) {
      starts.push(i);
      var k = kindOf(codes[i]);
      var word = k === LOWER || k === UPPER || k === NUMBER;
      i += 1;
      if (word) { while (i < codes.length && isWordChar(codes[i])) { i += 1; } }
      else if (k === SPACE) { while (i < codes.length && kindOf(codes[i]) === SPACE) { i += 1; } }
    }
    starts.push(codes.length);
    return starts;
  }

  /** When the characters are too many for Myers: the words first, then the characters inside every change of words. */
  function byWords(seq1, seq2) {
    var a = { starts: tokenStarts(seq1), seq: seq1 };
    var b = { starts: tokenStarts(seq2), seq: seq2 };
    var ids = new Map();
    var number = function (s) { var id = ids.get(s); if (id === undefined) { id = ids.size; ids.set(s, id); } return id; };
    var tokensOf = function (t) { var out = []; for (var i = 0; i + 1 < t.starts.length; i += 1) { out.push(number(t.seq.text(t.starts[i], t.starts[i + 1]))); } return out; };
    var ta = tokensOf(a);
    var tb = tokensOf(b);
    var words1 = { length: ta.length, get: function (i) { return ta[i]; } };
    var words2 = { length: tb.length, get: function (i) { return tb[i]; } };
    var coarse = myers(words1, words2) || anchored(words1, words2);
    var out = [];
    coarse.forEach(function (d) {
      var x1 = a.starts[d.s1];
      var x2 = a.starts[d.e1];
      var y1 = b.starts[d.s2];
      var y2 = b.starts[d.e2];
      var v1 = view(seq1, x1, x2);
      var v2 = view(seq2, y1, y2);
      var fine = v1.length + v2.length < CHAR_DP_LIMIT ? lcs(v1, v2, null, 1) : (myers(v1, v2) || [D(0, v1.length, 0, v2.length)]);
      fine.forEach(function (f) { out.push(D(f.s1 + x1, f.e1 + x1, f.s2 + y1, f.e2 + y1)); });
    });
    return out;
  }

  /** The changes between two stretches of paragraphs (character codes, every paragraph followed by its break): VS Code's refineDiff. */
  function refine(codes1, codes2) {
    var seq1 = charSequence(codes1);
    var seq2 = charSequence(codes2);
    var diffs = seq1.length + seq2.length < CHAR_DP_LIMIT ? lcs(seq1, seq2, null, 1) : (myers(seq1, seq2) || byWords(seq1, seq2));
    diffs = optimize(seq1, seq2, diffs);
    diffs = extendToWords(seq1, seq2, diffs);
    diffs = removeShortMatches(seq1, diffs);
    return removeShortTextBetweenLongDiffs(seq1, seq2, diffs);
  }

  // the stretches compared last, by their text: a keystroke changes one of them, the others need not be compared again
  var refined = new Map();
  var REFINED_LIMIT = 4000;

  function refineCached(codes1, codes2, key) {
    var hit = refined.get(key);
    if (hit) { return hit; }
    var result = refine(codes1, codes2);
    if (refined.size >= REFINED_LIMIT) { refined.clear(); }
    refined.set(key, result);
    return result;
  }

  // ================================================================================================ the comparison
  /** Where a place of the joined text (every paragraph followed by its break) is: { p, c }, the paragraph and the place in it (p is the count at the very end). */
  function locator(starts) {
    return function (offset) {
      var lo = 0;
      var hi = starts.length - 1;
      while (lo < hi) { var mid = (lo + hi + 1) >> 1; if (starts[mid] <= offset) { lo = mid; } else { hi = mid - 1; } }
      return { p: lo, c: offset - starts[lo] };
    };
  }

  function codesOf(texts, from, to) {
    var length = 0;
    var i;
    for (i = from; i < to; i += 1) { length += texts[i].length + 1; }
    var codes = new Uint16Array(length);
    var at = 0;
    for (i = from; i < to; i += 1) {
      var t = texts[i];
      for (var k = 0; k < t.length; k += 1) { codes[at++] = t.charCodeAt(k); }
      codes[at++] = SEP;
    }
    return codes;
  }

  /**
   * Two lists of paragraphs compared. The answer:
   * - rows: the paragraphs as Word keeps them with tracked changes, in the order of the text: { o, n, kind, mark, oldFrom, oldTo, newFrom, newTo, del, ins }. o and n are the
   *   old and the new paragraph (null where a side has none), kind "same" | "mod" | "ins" | "del", mark what became of the break at the end of the row ("E" it stayed, "D" taken
   *   away: the paragraph is joined with the next, "I" put in: the paragraph was split there). oldFrom..oldTo is the part of the old paragraph the row holds (all of it,
   *   unless the paragraph was split), newFrom..newTo of the new one; del and ins are the stretches [from, to) taken away from the old paragraph and put into the new one
   *   (places in the paragraph, not in the row).
   * - changes: what changed, place by place: { oldFrom, oldTo, newFrom, newTo } ({ p, c }: paragraph and place in it), oldText, newText (a paragraph break is U+2029).
   * - pairs: [o, n] of the paragraphs that stand across from each other (unchanged ones, and the first ones of the parts of a change that belong together).
   */
  function compare(oldInput, newInput) {
    var soft = new RegExp(String.fromCharCode(10), 'g');
    var softText = String.fromCharCode(SOFT);
    var oldTexts = oldInput.map(function (t) { return t.indexOf('\n') < 0 ? t : t.replace(soft, softText); });
    var newTexts = newInput.map(function (t) { return t.indexOf('\n') < 0 ? t : t.replace(soft, softText); });
    var n = oldTexts.length;
    var m = newTexts.length;

    var hashes = new Map();
    var hash = function (text) { var key = trimmed(text); var id = hashes.get(key); if (id === undefined) { id = hashes.size; hashes.set(key, id); } return id; };
    var oldHashes = oldTexts.map(hash);
    var newHashes = newTexts.map(hash);
    var lines1 = paragraphSequence(oldTexts, oldHashes);
    var lines2 = paragraphSequence(newTexts, newHashes);

    // 1. the paragraphs (a long text: the paragraphs that occur once are the anchors, Myers between them; VS Code takes Myers for all of it)
    var lineDiffs;
    if (n + m < LINE_DP_LIMIT) {
      lineDiffs = lcs(lines1, lines2, function (i, j) {
        if (oldTexts[i] !== newTexts[j]) { return 99; }
        return newTexts[j].length === 0 ? 10 : 100 + 69 * floorLog2(1 + newTexts[j].length);
      }, 100);
    } else {
      lineDiffs = anchored(lines1, lines2);
    }
    lineDiffs = optimize(lines1, lines2, lineDiffs);
    lineDiffs = joinAcrossShortParagraphs(lines1, lineDiffs);

    // 2. every stretch that differs, character by character (and paragraphs that differ only in blanks at their ends)
    var oldStarts = new Array(n + 1);
    var newStarts = new Array(m + 1);
    oldStarts[0] = 0;
    newStarts[0] = 0;
    var i;
    for (i = 0; i < n; i += 1) { oldStarts[i + 1] = oldStarts[i] + oldTexts[i].length + 1; }
    for (i = 0; i < m; i += 1) { newStarts[i + 1] = newStarts[i] + newTexts[i].length + 1; }
    var inner = [];
    var region = function (o1, o2, n1, n2) {
      var key = oldTexts.slice(o1, o2).join(SEP_TEXT) + String.fromCharCode(0) + newTexts.slice(n1, n2).join(SEP_TEXT) + String.fromCharCode(0) + (o2 - o1) + ':' + (n2 - n1);
      var base1 = oldStarts[o1];
      var base2 = newStarts[n1];
      refineCached(codesOf(oldTexts, o1, o2), codesOf(newTexts, n1, n2), key).forEach(function (d) {
        inner.push({ o1: base1 + d.s1, o2: base1 + d.e1, n1: base2 + d.s2, n2: base2 + d.e2 });
      });
    };
    var last1 = 0;
    var last2 = 0;
    var blanks = function (count) {
      for (var k = 0; k < count; k += 1) { if (oldTexts[last1 + k] !== newTexts[last2 + k]) { region(last1 + k, last1 + k + 1, last2 + k, last2 + k + 1); } }
    };
    lineDiffs.forEach(function (d) {
      blanks(d.s1 - last1);
      last1 = d.e1;
      last2 = d.e2;
      region(d.s1, d.e1, d.s2, d.e2);
    });
    blanks(n - last1);

    // 3. rows, places, pairs
    inner = keepBreaks(inner, oldTexts, newTexts, oldStarts, newStarts);
    var oldAt = locator(oldStarts);
    var newAt = locator(newStarts);
    var rows = buildRows(inner, oldTexts, newTexts, oldStarts, newStarts);
    var changes = [];
    if (inner.length > 0) {
      var oldJoined = oldInput.join(SEP_TEXT) + SEP_TEXT;
      var newJoined = newInput.join(SEP_TEXT) + SEP_TEXT;
      inner.forEach(function (c) {
        changes.push({
          oldFrom: oldAt(c.o1), oldTo: oldAt(c.o2), newFrom: newAt(c.n1), newTo: newAt(c.n2),
          oldText: oldJoined.slice(c.o1, c.o2), newText: newJoined.slice(c.n1, c.n2),
        });
      });
    }
    var pairs = alignment(changes, oldTexts, newTexts);
    return { rows: rows, changes: changes, pairs: pairs };
  }

  /** The character of a joined text (every paragraph followed by its break) at g. */
  function charOf(starts, texts, at, g) {
    var p = at(g);
    return p.c === texts[p.p].length ? SEP : texts[p.p].charCodeAt(p.c);
  }

  /**
   * Text put in that holds a paragraph break, followed by the rest of the old paragraph: the rest and its break may as well be the ones in the text that was put in, where the
   * new paragraph begins. Then the old paragraph keeps its break, and what follows it is whole paragraphs put in (a sentence written before the full stop and a paragraph after
   * the paragraph are two places, not one place that runs from the middle of a paragraph into the next). The same for text taken away. The text both ways is the same.
   */
  function keepBreaks(inner, oldTexts, newTexts, oldStarts, newStarts) {
    var oldAt = locator(oldStarts);
    var newAt = locator(newStarts);
    var oldTotal = oldStarts[oldTexts.length];
    var newTotal = newStarts[newTexts.length];
    // one side: the place a, the range [b1, b2) of the other side that holds a break; the paragraph a is in ends at its break, which must stay before the next change (limit)
    var rebreak = function (aStarts, aTexts, aAt, aTotal, a, bStarts, bTexts, bAt, b1, b2, limit) {
      if (a >= aTotal) { return null; }
      var q = aAt(a).p;
      var end = aStarts[q] + aTexts[q].length;   // the break of the paragraph the place is in
      if (end + 1 >= limit) { return null; }
      var p = bAt(b1).p;
      var sep = bStarts[p] + bTexts[p].length;   // the first break of the range
      if (sep >= b2) { return null; }
      var length = end - a;
      if (sep - b1 < length) { return null; }
      for (var k = 0; k < length; k += 1) {
        if (charOf(aStarts, aTexts, aAt, a + k) !== charOf(bStarts, bTexts, bAt, sep - length + k)) { return null; }
      }
      return { end: end, sep: sep, length: length };
    };
    var out = [];
    for (var i = 0; i < inner.length; i += 1) {
      var c = inner[i];
      var next = inner[i + 1];
      var r;
      if (c.o1 === c.o2 && c.n2 > c.n1 && (r = rebreak(oldStarts, oldTexts, oldAt, oldTotal, c.o1, newStarts, newTexts, newAt, c.n1, c.n2, next ? next.o1 : oldTotal + 1))) {
        if (r.sep - r.length > c.n1) { out.push({ o1: c.o1, o2: c.o1, n1: c.n1, n2: r.sep - r.length }); }
        out.push({ o1: r.end + 1, o2: r.end + 1, n1: r.sep + 1, n2: c.n2 + r.length + 1 });
      } else if (c.n1 === c.n2 && c.o2 > c.o1 && (r = rebreak(newStarts, newTexts, newAt, newTotal, c.n1, oldStarts, oldTexts, oldAt, c.o1, c.o2, next ? next.n1 : newTotal + 1))) {
        if (r.sep - r.length > c.o1) { out.push({ o1: c.o1, o2: r.sep - r.length, n1: c.n1, n2: c.n1 }); }
        out.push({ o1: r.sep + 1, o2: c.o2 + r.length + 1, n1: r.end + 1, n2: r.end + 1 });
      } else {
        out.push(c);
      }
    }
    return out;
  }

  /** The changes cut at the paragraph breaks into rows (see compare). Inside a change, what was taken away comes before what was put in. */
  function buildRows(inner, oldTexts, newTexts, oldStarts, newStarts) {
    var rows = [];
    var po = 0;   // the paragraphs the next old and new character belong to
    var pn = 0;
    var go = 0;   // and where that is in the joined texts
    var gn = 0;
    var row = null;
    var open = function () {
      if (!row) { row = { o: null, n: null, kind: '', mark: '', oldFrom: go - oldStarts[po], oldTo: 0, newFrom: gn - newStarts[pn], newTo: 0, del: [], ins: [] }; }
      return row;
    };
    var close = function (mark, old, now) {
      var r = open();
      if (old) { r.o = po; }
      if (now) { r.n = pn; }
      r.mark = mark;
      r.oldTo = go - oldStarts[po];
      r.newTo = gn - newStarts[pn];
      rows.push(r);
      row = null;
    };
    var equal = function (count) {
      while (count > 0) {
        var take = Math.min(count, oldStarts[po] + oldTexts[po].length - go);
        if (take > 0) { var r = open(); r.o = po; r.n = pn; go += take; gn += take; count -= take; }
        if (count > 0) { close('E', true, true); go += 1; gn += 1; po += 1; pn += 1; count -= 1; }
      }
    };
    var removed = function (count) {
      while (count > 0) {
        var take = Math.min(count, oldStarts[po] + oldTexts[po].length - go);
        if (take > 0) { var r = open(); r.o = po; var from = go - oldStarts[po]; r.del.push([from, from + take]); go += take; count -= take; }
        if (count > 0) { close('D', true, false); go += 1; po += 1; count -= 1; }
      }
    };
    var added = function (count) {
      while (count > 0) {
        var take = Math.min(count, newStarts[pn] + newTexts[pn].length - gn);
        if (take > 0) { var r = open(); r.n = pn; var from = gn - newStarts[pn]; r.ins.push([from, from + take]); gn += take; count -= take; }
        if (count > 0) { close('I', false, true); gn += 1; pn += 1; count -= 1; }
      }
    };
    inner.forEach(function (c) {
      equal(c.o1 - go);
      removed(c.o2 - c.o1);
      added(c.n2 - c.n1);
    });
    equal(oldStarts[oldTexts.length] - go);

    var perOld = new Int32Array(oldTexts.length);
    var perNew = new Int32Array(newTexts.length);
    rows.forEach(function (r) { if (r.o !== null) { perOld[r.o] += 1; } if (r.n !== null) { perNew[r.n] += 1; } });
    rows.forEach(function (r) {
      if (r.o === null) { r.kind = 'ins'; }
      else if (r.n === null) { r.kind = 'del'; }
      else { r.kind = r.mark === 'E' && r.del.length === 0 && r.ins.length === 0 && perOld[r.o] === 1 && perNew[r.n] === 1 ? 'same' : 'mod'; }
    });
    return rows;
  }

  /**
   * The paragraphs that stand across from each other: the unchanged ones, and inside a change the first paragraphs of its parts (VS Code's alignment inside a change: where a
   * change begins after some text of a paragraph, or ends before some, the paragraphs on both sides belong together from there on).
   */
  function alignment(changes, oldTexts, newTexts) {
    var n = oldTexts.length;
    var m = newTexts.length;
    var oldLength = function (p) { return p < n ? oldTexts[p].length : 0; };
    var newLength = function (p) { return p < m ? newTexts[p].length : 0; };

    // the changes by paragraphs (VS Code: getLineRangeMapping), neighbours that touch grouped (lineRangeMappingFromRangeMappings)
    var groups = [];
    var previous = null;
    changes.forEach(function (c) {
      var startDelta = 0;
      var endDelta = 0;
      if (c.newTo.c === 0 && c.oldTo.c === 0 && c.oldFrom.p <= c.oldTo.p && c.newFrom.p <= c.newTo.p) { endDelta = -1; }
      if (c.newFrom.c >= newLength(c.newFrom.p) && c.oldFrom.c >= oldLength(c.oldFrom.p) && c.oldFrom.p <= c.oldTo.p + endDelta && c.newFrom.p <= c.newTo.p + endDelta) { startDelta = 1; }
      var g = { o1: c.oldFrom.p + startDelta, o2: c.oldTo.p + 1 + endDelta, n1: c.newFrom.p + startDelta, n2: c.newTo.p + 1 + endDelta, inner: [c] };
      var last = groups[groups.length - 1];
      if (previous && ((previous.o1 <= g.o2 && g.o1 <= previous.o2) || (previous.n1 <= g.n2 && g.n1 <= previous.n2))) {
        last.o1 = Math.min(last.o1, g.o1); last.o2 = Math.max(last.o2, g.o2);
        last.n1 = Math.min(last.n1, g.n1); last.n2 = Math.max(last.n2, g.n2);
        last.inner.push(c);
      } else {
        groups.push({ o1: g.o1, o2: g.o2, n1: g.n1, n2: g.n2, inner: [c] });
      }
      previous = g;
    });

    var pairs = [];
    var lastOld = 0;
    var lastNew = 0;
    groups.forEach(function (g) {
      for (var k = 0; lastOld + k < g.o1 && lastNew + k < g.n1; k += 1) { pairs.push([lastOld + k, lastNew + k]); }
      var first = true;
      var lo = g.o1;
      var ln = g.n1;
      var emit = function (oldEnd, newEnd, force) {
        if (oldEnd < lo || newEnd < ln) { return; }
        if (first) { first = false; } else if (!force && (oldEnd === lo || newEnd === ln)) { return; }
        if (oldEnd === lo && newEnd === ln) { return; }
        if (oldEnd > lo && newEnd > ln) { pairs.push([lo, ln]); }
        lo = oldEnd;
        ln = newEnd;
      };
      g.inner.forEach(function (c) {
        if (c.oldFrom.c > 0 && c.newFrom.c > 0) { emit(c.oldFrom.p, c.newFrom.p, false); }
        if (c.oldTo.c < oldLength(c.oldTo.p)) { emit(c.oldTo.p, c.newTo.p, false); }
      });
      emit(g.o2, g.n2, true);
      lastOld = g.o2;
      lastNew = g.n2;
    });
    for (var k = 0; lastOld + k < n && lastNew + k < m; k += 1) { pairs.push([lastOld + k, lastNew + k]); }
    return pairs;
  }

  var api = { compare: compare, lcs: lcs, myers: myers, SEP: SEP_TEXT };
  if (typeof module !== 'undefined' && module.exports) { module.exports = api; } else { root.KkTextDiff = api; }
})(typeof window !== 'undefined' ? window : this);

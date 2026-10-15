/* Two pages beside each other, compared and lined up: the editor with an earlier version beside the text, and the comparison of two versions. Both look the same
   because both are this: the two texts are compared (textdiff.js, the way VS Code compares two files, and the comparison the server makes too), what is gone is marked on
   the old page (red, struck through), what is new on the new page (green), and the pages are lined up so that the paragraphs that belong together stand across from each
   other (VS Code's alignment), however much a side has more at a place: the side that has less there gets room before its next paragraph.

   What is marked is not put into the text: the words are highlights of the page (CSS.highlights, nothing of the markup changes) and a whole paragraph gets a class. The room
   is a margin, a rule of a constructable stylesheet keyed by the number of the paragraph (data-n), on both pages: the page that is written in must not get elements it did
   not make (native undo), and no page is allowed a style attribute (the policy of the site).

   The lining up is incremental: it measures where the paragraphs stand now and changes only the room that is not right, so that lining up what is lined up writes
   nothing (no flicker, no loop with a ResizeObserver), and it never takes all room away to measure from scratch (that would move the page under the reader for a moment).

   The earlier version does not have to be on the screen: with only its text (setOldTexts) the new page is marked and the list of the places that changed is made all the same,
   which is how the editor shows the changes in the text itself (the earlier page beside it is then only one more thing to look at).

   Usage: var side = KkSideBySide.create({ editable: true });  side.setOld(sheetElement);  side.refresh(elements, texts, { mode: 'all' });  side.hunks();  side.clear(); */
(function (root) {
  'use strict';

  var canHighlight = typeof CSS !== 'undefined' && !!CSS.highlights && typeof Highlight !== 'undefined';
  var canSheet = typeof CSSStyleSheet !== 'undefined' && !!CSSStyleSheet.prototype.replaceSync && typeof document !== 'undefined' && 'adoptedStyleSheets' in document;
  var SEP = String.fromCharCode(0x2029);
  var TOLERANCE = 1;   // pixels two paragraphs may stand apart before they are lined up again (less is rounding of the screen, and correcting it could go on for ever)
  var instances = 0;

  function zoomOf(element) { var z = parseFloat(element.style.zoom); return z > 0 ? z : 1; }

  /** The paragraphs of a page, as the editor numbers them (notes have no number), and their texts. */
  function paragraphsOf(sheet) {
    var els = Array.prototype.slice.call(sheet.querySelectorAll('p.kk-lp[data-n]'));
    return { els: els, texts: els.map(function (p) { return root.KkPageText.displayOf(p); }) };
  }

  function rangeIn(paragraph, from, to) {
    var a = root.KkPageText.locate(paragraph, from);
    var b = root.KkPageText.locate(paragraph, to);
    var range = document.createRange();
    range.setStart(a.node, a.offset);
    range.setEnd(b.node, b.offset);
    return range;
  }

  /** The paragraphs of a side of a change when the change is whole paragraphs there: [from, to) of them, or null (an empty side is whole paragraphs too, none). */
  function wholeParagraphs(from, to, text) {
    if (from.c !== 0 || to.c !== 0) { return null; }
    if (text.length > 0 && text.charAt(text.length - 1) !== SEP) { return null; }
    return [from.p, to.p];
  }

  /**
   * A change of textdiff.js as a place the editor lists, marks and takes back: { kind: "insert" | "delete" | "replace", level: "paragraph" (whole paragraphs on both sides)
   * | "words", oldFrom, oldTo, newFrom, newTo ({ p, c }), oldText, newText (U+2029 for a paragraph break), o, n (the paragraphs where it begins), and for whole paragraphs
   * oldTexts / newTexts (the paragraphs) and count (how many went or came).
   */
  function placeOf(change, oldTexts, newTexts) {
    var gone = change.oldText.length > 0;
    var came = change.newText.length > 0;
    var place = {
      kind: gone ? (came ? 'replace' : 'delete') : 'insert', level: 'words',
      oldFrom: change.oldFrom, oldTo: change.oldTo, newFrom: change.newFrom, newTo: change.newTo,
      oldText: change.oldText, newText: change.newText, o: change.oldFrom.p, n: change.newFrom.p,
      oldTexts: null, newTexts: null, count: 0,
    };
    var oldSpan = wholeParagraphs(change.oldFrom, change.oldTo, change.oldText);
    var newSpan = wholeParagraphs(change.newFrom, change.newTo, change.newText);
    if (oldSpan && newSpan) {
      place.level = 'paragraph';
      place.oldTexts = oldTexts.slice(oldSpan[0], oldSpan[1]);
      place.newTexts = newTexts.slice(newSpan[0], newSpan[1]);
      place.count = Math.max(place.oldTexts.length, place.newTexts.length);
    }
    return place;
  }

  /**
   * options.editable: the new page is written in (only its room is set; nothing else of it is touched).
   */
  function create(options) {
    var editable = !!(options && options.editable);
    var id = 'a' + (instances += 1);
    var oldSheet = null;
    var hasOld = false;
    var old = { els: [], texts: [] };
    var marked = new Map();   // element -> the class it was given (kk-gone, kk-new, kk-changed)
    var rows = [];
    var places = [];
    var pairs = [];           // { theirs, mine }: the paragraphs that stand across from each other
    var room = null;          // the rules of the room (a constructable stylesheet of its own)
    var roomText = '';
    var roomMine = new Map(); // paragraph of the new page -> { room, margin }: the room before it and the margin that makes it, in pixels of its page (before the zoom)
    var roomTheirs = new Map();
    var mineEls = [];

    /** The page that shows what was there before; its paragraphs and their texts are read once. */
    function setOld(sheet) {
      clear();
      oldSheet = sheet;
      hasOld = !!sheet;
      if (sheet) { sheet.setAttribute('data-kk-align', id + '-old'); }
      old = sheet ? paragraphsOf(sheet) : { els: [], texts: [] };
    }

    /** Only the text of the earlier version (a paragraph each, a line break inside one as \n): nothing to draw on its side, but the new page is marked and the places are found. */
    function setOldTexts(texts) {
      clear();
      oldSheet = null;
      hasOld = !!texts;
      old = { els: [], texts: texts ? texts.slice() : [] };
    }

    /** Everything that was drawn is taken away again: the room, the marks of the paragraphs and of the words. */
    function clear() {
      roomMine.clear();
      roomTheirs.clear();
      writeRoom();
      pairs = [];
      if (canHighlight) { CSS.highlights.delete('kk-removed'); CSS.highlights.delete('kk-added'); }
      marked.forEach(function (cls, element) { element.classList.remove(cls); });
      marked = new Map();
      rows = [];
      places = [];
      mineEls = [];
    }

    /** The classes of the paragraphs as they should be now; a paragraph whose class stays is not touched. */
    function setClasses(wanted) {
      marked.forEach(function (cls, element) {
        if (wanted.get(element) !== cls) { element.classList.remove(cls); }
      });
      wanted.forEach(function (cls, element) {
        if (marked.get(element) !== cls) { element.classList.add(cls); }
      });
      marked = wanted;
    }

    /**
     * The new page as it is now (its paragraphs and their texts, in the order of the page): compared with the old one, marked and lined up. It is meant to run in the frame
     * in which the text changed (before the page is drawn): it costs a comparison and a few measurements.
     * settings.mode says what is drawn on the new page: "all" (the words that are new, the bar of a changed paragraph, the tint of a new one), "simple" (only the bars and
     * tints) or "off" (nothing); the page of the earlier version, when there is one, is always marked.
     */
    function refresh(els, texts, settings) {
      if (!hasOld || !root.KkTextDiff) { clear(); return; }
      var mode = settings && settings.mode ? settings.mode : 'all';
      var result = root.KkTextDiff.compare(old.texts, texts);
      rows = result.rows;
      mineEls = els;

      // what a paragraph is: gone (its only row is taken away) / new (its only row is new) / changed (a row of it is not the same)
      var oldRows = new Array(old.texts.length);
      var newRows = new Array(texts.length);
      rows.forEach(function (row) {
        row.theirs = row.o === null ? null : old.els[row.o] || null;
        row.mine = row.n === null ? null : els[row.n] || null;
        if (row.o !== null) { (oldRows[row.o] = oldRows[row.o] || []).push(row); }
        if (row.n !== null) { (newRows[row.n] = newRows[row.n] || []).push(row); }
      });
      var stateOf = function (list, whole) {
        if (!list) { return null; }
        if (list.length === 1 && list[0].kind === whole) { return whole; }
        return list.some(function (row) { return row.kind !== 'same'; }) ? 'mod' : null;
      };
      var wanted = new Map();
      old.els.forEach(function (p, i) {
        var state = stateOf(oldRows[i], 'del');
        if (state) { wanted.set(p, state === 'del' ? 'kk-gone' : 'kk-changed'); }
      });
      if (mode !== 'off') {
        els.forEach(function (p, j) {
          var state = stateOf(newRows[j], 'ins');
          if (state) { wanted.set(p, state === 'ins' ? 'kk-new' : 'kk-changed'); }
        });
      }
      setClasses(wanted);

      // the words: taken away on the old page, put in on the new one (a paragraph that is gone or new as a whole has its class, not every word marked)
      if (canHighlight) {
        var removed = [];
        var added = [];
        rows.forEach(function (row) {
          if (row.theirs && row.kind !== 'del') { row.del.forEach(function (r) { removed.push(rangeIn(row.theirs, r[0], r[1])); }); }
          if (row.mine && row.kind !== 'ins' && mode === 'all') { row.ins.forEach(function (r) { added.push(rangeIn(row.mine, r[0], r[1])); }); }
        });
        if (removed.length > 0) { CSS.highlights.set('kk-removed', new Highlight(...removed)); } else { CSS.highlights.delete('kk-removed'); }
        if (added.length > 0) { CSS.highlights.set('kk-added', new Highlight(...added)); } else { CSS.highlights.delete('kk-added'); }
      }

      places = result.changes.map(function (change) { return placeOf(change, old.texts, texts); });
      pairs = [];
      result.pairs.forEach(function (pair) {
        var theirs = old.els[pair[0]];
        var mine = els[pair[1]];
        if (theirs && mine) { pairs.push({ theirs: theirs, mine: mine }); }
      });
      align();
    }

    /** The pages changed their size (the window, the zoom, a font that came late, the text): the room is made right again, the comparison stays. */
    function realign() { align(); }

    function writeRoom() {
      if (!canSheet) { return; }
      var rules = [];
      roomTheirs.forEach(function (value, p) {
        rules.push('.kk-sheet[data-kk-align="' + id + '-old"] p.kk-lp[data-n="' + p.getAttribute('data-n') + '"] { margin-top: ' + value.margin + 'px; }');
      });
      roomMine.forEach(function (value, p) {
        var scope = editable ? '.kk-sheet[contenteditable]' : '.kk-sheet[data-kk-align="' + id + '-new"]';
        rules.push(scope + ' p.kk-lp[data-n="' + p.getAttribute('data-n') + '"] { margin-top: ' + value.margin + 'px; }');
      });
      var text = rules.join('\n');
      if (text === roomText) { return; }
      roomText = text;
      if (!room) {
        if (text === '') { return; }
        room = new CSSStyleSheet();
        document.adoptedStyleSheets = document.adoptedStyleSheets.concat([room]);
      }
      room.replaceSync(text);
    }

    /** The margin a paragraph has before it without any room of ours (the space to the one before it, which takes the margins of both into account), in pixels of its page. */
    function naturalGap(p, zoom, map) {
      var own = map.get(p);
      var before = p.previousElementSibling;
      var measured = before && before.offsetParent !== null
        ? (p.getBoundingClientRect().top - before.getBoundingClientRect().bottom) / zoom
        : parseFloat(getComputedStyle(p).marginTop) || 0;
      return measured - (own ? own.room : 0);
    }

    /**
     * Lines the paragraphs that belong together up: where a paragraph of the new page stands lower than its counterpart, it first gets less of the room it has, then its
     * counterpart gets more, and the other way round; what follows moves along. Twice more when a margin did not do exactly what was asked (margins of neighbours collapse).
     */
    function align() {
      if (!canSheet) { return; }
      var list = pairs.filter(function (pair) {
        return pair.mine.isConnected && pair.theirs.isConnected && !pair.mine.classList.contains('kk-folded') && !pair.theirs.classList.contains('kk-folded');
      });
      var mineInList = new Set(list.map(function (pair) { return pair.mine; }));
      var theirsInList = new Set(list.map(function (pair) { return pair.theirs; }));
      roomMine.forEach(function (value, p) { if (!mineInList.has(p) || !p.isConnected) { roomMine.delete(p); } });
      roomTheirs.forEach(function (value, p) { if (!theirsInList.has(p) || !p.isConnected) { roomTheirs.delete(p); } });
      // the rules go by the numbers of the paragraphs, which change when one is put in or taken away: the room is where it belongs before anything is measured
      writeRoom();
      if (list.length === 0) { return; }
      var theirSheet = list[0].theirs.closest('.kk-sheet');
      var mySheet = list[0].mine.closest('.kk-sheet');
      if (!theirSheet || !mySheet) { return; }
      if (!editable) { mySheet.setAttribute('data-kk-align', id + '-new'); }
      if (mySheet.getBoundingClientRect().left === theirSheet.getBoundingClientRect().left) {
        // stacked (a narrow window), not side by side: no room at all
        if (roomMine.size > 0 || roomTheirs.size > 0) { roomMine.clear(); roomTheirs.clear(); writeRoom(); }
        return;
      }
      var zt = zoomOf(theirSheet);
      var zm = zoomOf(mySheet);

      for (var pass = 0; pass < 3; pass += 1) {
        var count = list.length;
        var offsets = new Array(count);
        for (var j = 0; j < count; j += 1) { offsets[j] = list[j].mine.getBoundingClientRect().top - list[j].theirs.getBoundingClientRect().top; }
        var shift = 0;
        var changes = [];
        for (var k = 0; k < count; k += 1) {
          var r = offsets[k] + shift;
          if (Math.abs(r) <= TOLERANCE) { continue; }
          var pair = list[k];
          var m = roomMine.has(pair.mine) ? roomMine.get(pair.mine).room * zm : 0;
          var t = roomTheirs.has(pair.theirs) ? roomTheirs.get(pair.theirs).room * zt : 0;
          if (r > 0) { var less = Math.min(m, r); m -= less; t += r - less; } else { var fewer = Math.min(t, -r); t -= fewer; m += -r - fewer; }
          changes.push({ pair: pair, mine: m / zm, theirs: t / zt });
          shift -= r;
        }
        if (changes.length === 0) { break; }
        // the margins before the paragraphs whose room changes, measured before anything is written
        var gaps = changes.map(function (c) { return { mine: naturalGap(c.pair.mine, zm, roomMine), theirs: naturalGap(c.pair.theirs, zt, roomTheirs) }; });
        changes.forEach(function (c, i) {
          if (c.mine > 0.01) { roomMine.set(c.pair.mine, { room: c.mine, margin: gaps[i].mine + c.mine }); } else { roomMine.delete(c.pair.mine); }
          if (c.theirs > 0.01) { roomTheirs.set(c.pair.theirs, { room: c.theirs, margin: gaps[i].theirs + c.theirs }); } else { roomTheirs.delete(c.pair.theirs); }
        });
        writeRoom();
      }
    }

    /** Every row of the comparison, in the order of the pages: { o, n, kind, mark, theirs, mine, ... } (the elements are null where a side has none). */
    function allRows() { return rows; }

    /** The places that changed, in the order of the pages, with the paragraph to show for each: the new page's where something came, else the old page's. */
    function changes() {
      return places.map(function (h) {
        var mine = h.newText.length > 0 ? mineEls[h.newFrom.p] || null : null;
        var theirs = old.els[h.oldFrom.p] || old.els[old.els.length - 1] || null;
        return { kind: h.kind, mine: mine, theirs: theirs, element: mine || theirs || mineEls[Math.min(h.newFrom.p, mineEls.length - 1)] || null };
      });
    }

    /** What changed, place by place, in the order of the text (as of the last refresh): see placeOf. */
    function hunks() { return places; }

    return { setOld: setOld, setOldTexts: setOldTexts, refresh: refresh, realign: realign, clear: clear, changes: changes, hunks: hunks, rows: allRows, old: function () { return old; } };
  }

  root.KkSideBySide = { create: create, paragraphsOf: paragraphsOf, canHighlight: canHighlight, SEP: SEP };
})(window);

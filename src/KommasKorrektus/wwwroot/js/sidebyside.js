/* Two pages beside each other, compared and lined up: the editor with an earlier version beside the text, and the comparison of two versions. Both look the same
   because both are this: the two texts are compared paragraph by paragraph and word by word (textdiff.js, the comparison the server makes too), what is gone is marked on
   the old page (red, struck through), what is new on the new page (green), and the pages are lined up so that the paragraphs that belong together stand across from each
   other, however much a side has more at a place (a paragraph taken away, one more, a longer one): the side that has less there gets room before its next paragraph.

   What is marked is not put into the text: the words are highlights of the page (CSS.highlights, nothing of the markup changes) and a whole paragraph gets a class. The room
   is a spacer on a page that is only read; on the page that is written in (the editor) it is a rule of a constructable stylesheet, because the page that is written in must
   not get elements it did not make (native undo) and its markup is not allowed a style attribute (the policy of the site). Nothing here is a style attribute: the height
   of a spacer is set from script, which the policy allows.

   Usage: var side = KkSideBySide.create({ editable: true });  side.setOld(sheetElement);  side.refresh(elements, texts);  side.clear(); */
(function (root) {
  'use strict';

  var canHighlight = typeof CSS !== 'undefined' && !!CSS.highlights && typeof Highlight !== 'undefined';
  var canSheet = typeof CSSStyleSheet !== 'undefined' && !!CSSStyleSheet.prototype.replaceSync && 'adoptedStyleSheets' in document;

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

  /**
   * options.editable: the new page is written in (its room is a rule of a stylesheet, found by the number of the paragraph, data-n), not a spacer.
   */
  function create(options) {
    var editable = !!(options && options.editable);
    var oldSheet = null;
    var old = { els: [], texts: [] };
    var marked = { gone: [], fresh: [], changed: [] };
    var spacers = [];
    var room = null;     // the rules for the page that is written in
    var rows = [];
    var lastPairs = [];

    /** The page that shows what was there before; its paragraphs and their texts are read once. */
    function setOld(sheet) {
      clear();
      oldSheet = sheet;
      old = sheet ? paragraphsOf(sheet) : { els: [], texts: [] };
    }

    function unalign() {
      spacers.forEach(function (node) { node.remove(); });
      spacers = [];
      if (room) { room.replaceSync(''); }
    }

    /** Everything that was drawn is taken away again: the room, the marks of the paragraphs and of the words. */
    function clear() {
      unalign();
      lastPairs = [];
      if (canHighlight) { CSS.highlights.delete('kk-removed'); CSS.highlights.delete('kk-added'); }
      marked.gone.forEach(function (p) { p.classList.remove('kk-gone'); });
      marked.fresh.forEach(function (p) { p.classList.remove('kk-new'); });
      marked.changed.forEach(function (p) { p.classList.remove('kk-changed'); });
      marked = { gone: [], fresh: [], changed: [] };
      rows = [];
    }

    /**
     * The new page as it is now (its paragraphs and their texts, in the order of the page): compared with the old one, marked and lined up. It is meant to run on every
     * change of the text: it costs a comparison and a few measurements.
     */
    function refresh(els, texts) {
      clear();
      if (!oldSheet || !root.KkTextDiff) { return; }
      rows = root.KkTextDiff.compare(old.texts, texts);
      var removed = [];
      var added = [];
      var pairs = [];
      rows.forEach(function (row) {
        var theirs = row.o === null ? null : old.els[row.o];
        var mine = row.n === null ? null : els[row.n];
        row.theirs = theirs;
        row.mine = mine;
        if (row.kind === 'mod') {
          if (theirs) { theirs.classList.add('kk-changed'); marked.changed.push(theirs); }
          if (mine) { mine.classList.add('kk-changed'); marked.changed.push(mine); }
          if (canHighlight && theirs && mine) {
            var found = root.KkTextDiff.words(old.texts[row.o], texts[row.n]);
            found.del.forEach(function (r) { removed.push(rangeIn(theirs, r[0], r[1])); });
            found.ins.forEach(function (r) { added.push(rangeIn(mine, r[0], r[1])); });
          }
        } else if (row.kind === 'del') {
          if (theirs) { theirs.classList.add('kk-gone'); marked.gone.push(theirs); }
        } else if (row.kind === 'ins') {
          if (mine) { mine.classList.add('kk-new'); marked.fresh.push(mine); }
        }
        if (theirs && mine) { pairs.push({ theirs: theirs, mine: mine }); }
      });
      if (canHighlight) {
        if (removed.length > 0) { CSS.highlights.set('kk-removed', new Highlight(...removed)); }
        if (added.length > 0) { CSS.highlights.set('kk-added', new Highlight(...added)); }
      }
      lastPairs = pairs;
      align(pairs);
    }

    /** The pages changed their size (the window, the zoom, a font that came late): the room is made anew, the comparison stays. */
    function realign() {
      unalign();
      align(lastPairs);
    }

    function spacerBefore(paragraph, height) {
      var spacer = document.createElement('div');
      spacer.className = 'kk-aligner';
      spacer.style.height = height + 'px';
      paragraph.parentNode.insertBefore(spacer, paragraph);
      spacers.push(spacer);
    }

    /**
     * Lines the paragraphs that belong together up. From the first look at the pairs and twice more should a margin not have done exactly what was asked: where the new
     * page lies lower than its counterpart by more than it did at the pair before, the old page gets room there, and the other way round.
     */
    function align(all) {
      // what is folded away has no place on the page to line up
      var pairs = all.filter(function (pair) { return !pair.mine.classList.contains('kk-folded') && !pair.theirs.classList.contains('kk-folded'); });
      if (pairs.length === 0) { return; }
      var theirSheet = pairs[0].theirs.closest('.kk-sheet');
      var mySheet = pairs[0].mine.closest('.kk-sheet');
      if (!theirSheet || !mySheet || mySheet.getBoundingClientRect().left === theirSheet.getBoundingClientRect().left) { return; }   // stacked, not side by side
      if (editable && !canSheet) { return; }
      var zt = zoomOf(theirSheet);
      var zm = zoomOf(mySheet);
      var count = pairs.length;
      if (editable && !room) { room = new CSSStyleSheet(); document.adoptedStyleSheets = document.adoptedStyleSheets.concat([room]); }

      // the room each paragraph of the page that is written in has before it now (a margin that is bigger than this one stands in for the next paragraph's own)
      var gaps = editable ? pairs.map(function (pair) {
        var before = pair.mine.previousElementSibling;
        return before ? (pair.mine.getBoundingClientRect().top - before.getBoundingClientRect().bottom) / zm : parseFloat(getComputedStyle(pair.mine).marginTop) || 0;
      }) : null;
      var roomTheirs = new Array(count).fill(0);
      var roomMine = new Array(count).fill(0);

      function apply() {
        spacers.forEach(function (node) { node.remove(); });
        spacers = [];
        var rules = [];
        for (var k = 0; k < count; k += 1) {
          if (roomTheirs[k] > 0.4) { spacerBefore(pairs[k].theirs, roomTheirs[k] / zt); }
          if (roomMine[k] > 0.4) {
            if (editable) { rules.push('.kk-sheet[contenteditable] p.kk-lp[data-n="' + pairs[k].mine.getAttribute('data-n') + '"] { margin-top: ' + (gaps[k] + roomMine[k] / zm) + 'px; }'); }
            else { spacerBefore(pairs[k].mine, roomMine[k] / zm); }
          }
        }
        if (editable) { room.replaceSync(rules.join('\n')); }
      }

      for (var pass = 0; pass < 3; pass += 1) {
        var before = 0;
        var changed = false;
        var offsets = [];
        for (var j = 0; j < count; j += 1) { offsets.push(pairs[j].mine.getBoundingClientRect().top - pairs[j].theirs.getBoundingClientRect().top); }
        for (var i = 0; i < count; i += 1) {
          var step = offsets[i] - before;
          before = offsets[i];
          if (step > 0.5) { roomTheirs[i] += step; changed = true; } else if (step < -0.5) { roomMine[i] += -step; changed = true; }
        }
        if (!changed) { break; }
        apply();
      }
    }

    /** Every row of the comparison, in the order of the pages: { o, n, kind, theirs, mine } (the elements are null where a side has none). */
    function allRows() { return rows; }

    /** The places that changed, in the order of the pages: the new page's paragraph where there is one, else the old page's. */
    function changes() {
      return rows.filter(function (row) { return row.kind !== 'same'; }).map(function (row) { return { kind: row.kind, mine: row.mine, theirs: row.theirs, element: row.mine || row.theirs }; });
    }

    return { setOld: setOld, refresh: refresh, realign: realign, clear: clear, changes: changes, rows: allRows, old: function () { return old; } };
  }

  root.KkSideBySide = { create: create, paragraphsOf: paragraphsOf, canHighlight: canHighlight };
})(window);

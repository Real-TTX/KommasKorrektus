/* The page view of the editor: the text on a page with the formatting of the original Word file (headings, emphasis, lists, tables, pictures, notes),
   written in directly. The text is the only thing that changes: formatting is never edited here, it stays in the original and comes back in the export.

   The whole page is one editable field, so that the browser does what it does best: selection across paragraphs, cut and paste, drag, undo and redo, spelling.
   What this file adds on top:
   - the text of a paragraph is read from the page the way the server wrote it (words, line breaks; pictures, notes and list numbers are not text);
   - formatting commands (Ctrl+B ...), dropping text and deleting across table cells or over a picture are refused: they would not reach the export;
   - a paragraph made with Enter gets the look the export will give it (the server says which in data-nx);
   - paragraphs are numbered again after every change, so that remarks and the word count keep their places.
   No library, no build step. */
(function () {
  'use strict';

  var core = window.KkEditor;
  if (!core) { return; }

  var LS = core.LS;
  var NL = String.fromCharCode(10);
  var BREAK = String.fromCharCode(0x2029);   // a paragraph break in the text of a change (textdiff.js)
  var parasElement = core.parasElement;
  var wrap = null;        // the desk the page lies on
  var sheet = null;       // the page: one editable field
  var tools = null;       // the number of the paragraph with the cursor and its comment button
  var toolsNumber = null;
  var cssLink = null;
  var observer = null;
  var width = 0;
  var cache = null;       // the paragraphs of the page, in order; null when they have to be looked up again
  var housekeeping = null;
  var toolsFrame = 0;
  var marks = null;       // the layer of the comment marks in the margin of the page
  var ghosts = null;      // the layer of the marks of what was taken away since the earlier version (editor-changes.js draws on it)
  var marked = new Set(); // the paragraphs with an open comment
  var markedDone = new Set(); // the paragraphs with a comment that is ticked off, when those are shown
  var marksFrame = 0;
  var composing = false;

  function el(tag, cls) { return core.el(tag, cls); }

  // ------------------------------------------------------------------------------------------------ reading the text of a paragraph
  // The text of a paragraph is read from the page the way the server wrote it (words, line breaks; pictures, notes and list numbers are not text): pagetext.js, which the pages
  // that only read the text (the earlier version beside it, the comparison) use too, so that all of them count the same characters.
  var text = window.KkPageText;
  var isOpaque = text.isOpaque;
  var textOf = text.textOf;
  var displayOf = text.displayOf;
  var segments = text.segments;
  var locate = text.locate;

  /** The position in the text of a paragraph of a place on the page. */
  function offsetOf(p, node, offset) {
    var list = segments(p);
    if (node.nodeType === 3) {
      for (var i = 0; i < list.length; i += 1) { if (list[i].node === node) { return list[i].start + Math.min(offset, list[i].length); } }
    }
    var probe = document.createRange();
    probe.setStart(node, offset);
    probe.collapse(true);
    var total = 0;
    for (var j = 0; j < list.length; j += 1) {
      if (probe.comparePoint(list[j].node, 0) >= 0) { break; }
      total = list[j].start + list[j].length;
    }
    return total;
  }

  function paragraphOf(node) {
    var e = node && node.nodeType === 3 ? node.parentNode : node;
    return e && e.closest ? e.closest('p.kk-lp[data-n]') : null;
  }

  function cellOf(node) {
    var e = node && node.nodeType === 3 ? node.parentNode : node;
    return e && e.closest ? e.closest('td') : null;
  }

  function list() {
    if (!cache) { cache = Array.prototype.slice.call(sheet.querySelectorAll('p.kk-lp[data-n]')); }
    return cache;
  }

  function invalidate() { cache = null; }

  function indexOfParagraph(p) { return parseInt(p.getAttribute('data-n'), 10) - 1; }

  function setRange(p, start, end) {
    var a = locate(p, start);
    var b = end === start ? a : locate(p, end);
    var range = document.createRange();
    range.setStart(a.node, a.offset);
    range.setEnd(b.node, b.offset);
    var selection = window.getSelection();
    selection.removeAllRanges();
    selection.addRange(range);
  }

  // ------------------------------------------------------------------------------------------------ keeping the numbers right
  function renumber() {
    list().forEach(function (p, i) {
      if (p.getAttribute('data-n') !== String(i + 1)) {
        p.setAttribute('data-n', String(i + 1));
        p.id = 'p' + (i + 1);
      }
    });
  }

  /**
   * After Enter: the paragraph the browser made has the look of the one it was cut from, but the export gives a new paragraph the properties of its neighbour
   * without the style, the list and the page break. The server wrote that look into the neighbour (data-nx); here it is put on the new paragraph.
   */
  function dressNew() {
    var all = list();
    all.forEach(function (p, i) {
      if (p.__kk) { return; }
      p.__kk = true;
      var source = all[i - 1] || all[i + 1];
      var nx = source ? source.getAttribute('data-nx') : null;
      if (nx) { p.className = 'kk-lp ' + nx; } else { p.classList.remove('kk-pbb', 'has-comment'); }
      p.removeAttribute('data-nx');
      // The number of a list stays with the paragraph it belongs to: a new paragraph is not a list item in the export. It is hidden, not removed, so that undo finds it.
      Array.prototype.forEach.call(p.querySelectorAll('.kk-lm'), function (m) { m.classList.add('kk-lm--off'); });
    });
  }

  /** Every paragraph the browser knows now is old; a list number that ended up in the middle of a paragraph (two were joined) is hidden, and shows again when it is first. */
  function settle() {
    list().forEach(function (p) { p.__kk = true; });
    Array.prototype.forEach.call(sheet.querySelectorAll('.kk-lm'), function (m) { m.classList.toggle('kk-lm--stray', m.previousSibling !== null); });
  }

  function recountAll() {
    var words = 0;
    var characters = 0;
    list().forEach(function (p) {
      var text = textOf(p);
      words += core.countWords(text);
      characters += text.length;
      p.classList.toggle('is-changed', !core.base.has(text));
    });
    core.showWordCount(words, characters);
  }

  function scheduleHousekeeping() {
    clearTimeout(housekeeping);
    housekeeping = setTimeout(function () { housekeeping = null; recountAll(); }, 300);
  }

  // ------------------------------------------------------------------------------------------------ what the page takes and what it refuses
  function say(message) { core.toast(message); }

  /** Whether a range takes away a picture, a note mark or a list number of a paragraph that stays. */
  function lostOpaque(rangeLike) {
    var range = document.createRange();
    range.setStart(rangeLike.startContainer, rangeLike.startOffset);
    range.setEnd(rangeLike.endContainer, rangeLike.endOffset);
    if (range.collapsed) { return false; }
    var nodes = sheet.querySelectorAll('[data-opaque]:not(.kk-notes)');
    var startParagraph = paragraphOf(rangeLike.startContainer);
    var endParagraph = paragraphOf(rangeLike.endContainer);
    for (var i = 0; i < nodes.length; i += 1) {
      var node = nodes[i];
      if (!range.intersectsNode(node) || node.closest('.kk-notes') || node.classList.contains('kk-lm--stray') || node.classList.contains('kk-lm--off')) { continue; }
      var p = paragraphOf(node);
      var whole = p && range.comparePoint(p, 0) === 0 && range.comparePoint(p, p.childNodes.length) === 0;
      if (whole) { continue; }
      // A list number goes when two paragraphs are joined; inside one paragraph it stays (the export keeps the list).
      if (node.classList.contains('kk-lm') && startParagraph !== endParagraph) { continue; }
      return true;
    }
    return false;
  }

  function onBeforeInput(event) {
    var type = event.inputType || '';
    if (composing) { return; }
    if (type.indexOf('format') === 0) {
      event.preventDefault();
      say('Die Formatierung wird hier nicht geändert: Du bearbeitest den Text, die Formatierung des Originals bleibt und kommt beim Export wieder heraus.');
      return;
    }
    if (type === 'insertFromDrop' || type === 'deleteByDrag' || type === 'insertFromYank' || type === 'insertTranspose') {
      event.preventDefault();
      return;
    }
    if (type !== 'insertText' && type !== 'insertCompositionText') { invalidate(); }
    var ranges = event.getTargetRanges ? event.getTargetRanges() : [];
    for (var i = 0; i < ranges.length; i += 1) {
      var r = ranges[i];
      if (cellOf(r.startContainer) !== cellOf(r.endContainer)) {
        event.preventDefault();
        say('Über mehrere Tabellenzellen hinweg lässt sich nichts löschen, ersetzen oder zusammenfügen.');
        return;
      }
      if (type !== 'insertText' && type !== 'insertCompositionText' && lostOpaque(r)) {
        event.preventDefault();
        say('Bilder, Fußnotenzeichen und Aufzählungen bleiben, wie sie sind: Sie sind nicht Teil des Textes.');
        return;
      }
    }
  }

  function onInput(event) {
    core.touch();
    core.markDirty();
    if (event.isComposing || composing) { return; }
    var type = event.inputType || '';
    if (type === 'insertText' || type === 'insertCompositionText') {
      scheduleHousekeeping();
      return;
    }
    invalidate();
    if (type === 'insertParagraph') { dressNew(); }
    settle();
    renumber();
    core.renderComments();
    scheduleHousekeeping();
    onSelection();   // the number beside the paragraph with the cursor moves in this frame (the browser tells of the new selection only after it)
  }

  /** A text from the clipboard is put in as text: lines become paragraphs (as in the text view), and nothing of the formatting it came with. */
  function onPaste(event) {
    event.preventDefault();
    var text = (event.clipboardData || window.clipboardData).getData('text/plain');
    if (!text) { return; }
    var parts = text.replace(/\r\n?/g, '\n').split(/\n+/).filter(function (part, i, all) { return part.length > 0 || i === 0 || i === all.length - 1; });
    if (parts.length === 0) { return; }
    document.execCommand('insertText', false, parts[0]);
    for (var i = 1; i < parts.length; i += 1) {
      document.execCommand('insertParagraph');
      if (parts[i]) { document.execCommand('insertText', false, parts[i]); }
    }
  }

  // ------------------------------------------------------------------------------------------------ the number and the comment button beside the paragraph with the cursor
  function placeTools() {
    toolsFrame = 0;
    if (!sheet) { return; }
    var selection = window.getSelection();
    if (!selection || selection.rangeCount === 0 || !sheet.contains(selection.anchorNode)) { tools.hidden = true; return; }
    var p = paragraphOf(selection.anchorNode);
    if (!p) { tools.hidden = true; return; }
    var box = p.getBoundingClientRect();
    var desk = wrap.getBoundingClientRect();
    var page = sheet.getBoundingClientRect();
    tools.hidden = false;
    tools.style.top = Math.max(0, box.top - desk.top) + 'px';
    tools.style.left = Math.max(2, page.left - desk.left + 4) + 'px';
    toolsNumber.textContent = p.getAttribute('data-n');
  }

  // ------------------------------------------------------------------------------------------------ the comments in the margin
  // A small bubble beside every paragraph with an open comment, at the right edge of the page (inside it when the page fills the desk); a click opens the comments at that one.
  function placeMarks() {
    marksFrame = 0;
    if (!sheet || !marks) { return; }
    marks.textContent = '';
    var desk = wrap.getBoundingClientRect();
    var page = sheet.getBoundingClientRect();
    var left = Math.min(page.right - desk.left + 6, wrap.clientWidth - 30);
    var paragraphs = list();
    function bubbleFor(index, done) {
      var p = paragraphs[index];
      if (!p) { return; }
      var bubble = el('button', done ? 'kk-pmark kk-pmark--done' : 'kk-pmark');
      bubble.type = 'button';
      bubble.setAttribute('aria-label', (done ? 'Erledigten Kommentar zu Absatz ' : 'Kommentar zu Absatz ') + (index + 1) + ' zeigen');
      bubble.setAttribute('data-tip', done ? 'Erledigten Kommentar zeigen' : 'Kommentar zeigen');
      bubble.innerHTML = core.icon(done ? 'check' : 'message-square');
      bubble.style.top = Math.max(0, p.getBoundingClientRect().top - desk.top) + 'px';
      bubble.style.left = left + 'px';
      bubble.addEventListener('mousedown', function (e) { e.preventDefault(); });
      bubble.addEventListener('click', function () { core.showComments(index); });
      marks.appendChild(bubble);
    }
    marked.forEach(function (index) { bubbleFor(index, false); });
    markedDone.forEach(function (index) { if (!marked.has(index)) { bubbleFor(index, true); } });
  }

  function scheduleMarks() {
    if (!marksFrame) { marksFrame = requestAnimationFrame(placeMarks); }
  }

  function onSelection() {
    if (!toolsFrame) { toolsFrame = requestAnimationFrame(placeTools); }
  }

  function commentHere() {
    var active = activePosition();
    if (!active) { return; }
    var p = list()[active.index];
    var text = displayOf(p);
    var quote = active.end > active.start ? text.slice(active.start, active.end) : text.slice(0, 60);
    core.openComment(active.index, quote);
  }

  // ------------------------------------------------------------------------------------------------ the view
  /** The width the page can have in its desk. */
  function availableWidth() { return wrap ? wrap.clientWidth - 16 : 0; }

  function fit() {
    if (!sheet) { return; }
    // the earlier version beside the page is fitted to the same width, so that both are the same size (editor-changes.js sets the limit)
    var available = core.fitLimit ? Math.min(availableWidth(), core.fitLimit) : availableWidth();
    // 100 % is the page fitted to the width of the window; the size of the writing the person chose is on top of that (a bigger one may be wider than the window: it scrolls)
    var zoom = (available < width ? Math.max(0.3, available / width) : 1) * (core.scale ? core.scale() : 1);
    sheet.style.zoom = Math.abs(zoom - 1) < 0.001 ? '' : String(zoom);
  }

  /**
   * Where a stretch of a paragraph is on the screen, relative to the desk the page lies on (the layers of marks are drawn there): { left, top, width, height } of its first
   * line, or of the paragraph when the stretch has no box of its own (a place between two words, an empty paragraph).
   */
  function rectOf(index, from, to) {
    var p = list()[index];
    if (!p || !wrap) { return null; }
    var a = locate(p, from);
    var b = to === from ? a : locate(p, to);
    var range = document.createRange();
    range.setStart(a.node, a.offset);
    range.setEnd(b.node, b.offset);
    var rects = range.getClientRects();
    var r = rects.length > 0 ? rects[0] : range.getBoundingClientRect();
    if (!r || (r.width === 0 && r.height === 0)) { r = p.getBoundingClientRect(); }
    var desk = wrap.getBoundingClientRect();
    return { left: r.left - desk.left + wrap.scrollLeft, top: r.top - desk.top, width: r.width, height: r.height };
  }

  /** Types a text (a line break inside a paragraph is a line break, not a new paragraph) where the cursor is; nothing at all takes the marked words away. */
  function typeText(value) {
    if (!value) { return document.execCommand('delete'); }
    var parts = value.split(NL);
    var done = true;
    parts.forEach(function (part, k) {
      if (k > 0) { done = document.execCommand('insertLineBreak') && done; }
      if (part) { done = document.execCommand('insertText', false, part) && done; }
    });
    return done;
  }

  function activePosition() {
    var selection = window.getSelection();
    if (!sheet || !selection || selection.rangeCount === 0 || !sheet.contains(selection.anchorNode)) { return null; }
    var range = selection.getRangeAt(0);
    var p = paragraphOf(range.startContainer);
    if (!p) { return null; }
    var start = offsetOf(p, range.startContainer, range.startOffset);
    var endParagraph = paragraphOf(range.endContainer);
    var end = endParagraph === p ? offsetOf(p, range.endContainer, range.endOffset) : textOf(p).length;
    return { index: indexOfParagraph(p), start: start, end: end };
  }

  function loadStyles(url) {
    return new Promise(function (resolve, reject) {
      var link = document.createElement('link');
      link.rel = 'stylesheet';
      link.href = url;
      link.onload = function () { resolve(link); };
      link.onerror = function () { link.remove(); reject(new Error('Die Seite lässt sich nicht aufbauen: Das Stylesheet fehlt.')); };
      document.head.appendChild(link);
    });
  }

  function sourceNote(result) {
    if (result.formatted) { return null; }
    var kind = core.data.kind;
    var note = el('p', 'kk-desk__note mat-hint');
    note.textContent = kind === 'Pdf'
      ? 'Das Original ist ein PDF: Die Seite zeigt den Text auf einer einfachen Seite, das Layout des PDFs wird nicht nachgebaut. Mit dem Auge-Symbol oben siehst du das PDF daneben.'
      : kind === 'Text'
        ? 'Das Original ist eine Textdatei und hat keine Formatierung: Die Seite zeigt den Text auf einer einfachen Seite.'
        : 'Der Text weicht vom Import ab, darum zeigt die Seite ihn ohne die Formatierung des Originals.';
    return note;
  }

  var page = {
    name: 'page',

    /** The size of the writing changed: the page is fitted again. */
    refit: function () { fit(); },

    // the numbers of the paragraphs are right again (the earlier version beside the text lines up by them)
    tools: {
      renumber: function () { cache = null; renumber(); },
    },

    prepare: function (texts) {
      return core.post('Layout', { paragraphs: texts }).then(function (result) {
        if (!result.ok) { throw new Error(result.reason || 'Die Seite lässt sich nicht aufbauen.'); }
        return loadStyles(result.css).then(function (link) { result.link = link; return result; });
      });
    },

    mount: function (result, anchor) {
      parasElement.className = 'kk-editor__paper is-page';
      parasElement.textContent = '';
      var note = sourceNote(result);
      if (note) { parasElement.appendChild(note); }
      wrap = el('div', 'kk-desk');
      sheet = el('div', 'kk-sheet ' + result.pageClass);
      sheet.setAttribute('contenteditable', 'true');
      sheet.setAttribute('spellcheck', 'true');
      sheet.setAttribute('lang', 'de');
      sheet.setAttribute('role', 'textbox');
      sheet.setAttribute('aria-multiline', 'true');
      sheet.setAttribute('aria-label', 'Text des Dokuments auf der Seite');
      // The markup comes from the server, which encodes every word of the text; it has no scripts and no style attributes (the policy of the site would refuse them).
      sheet.innerHTML = result.html;
      tools = el('div', 'kk-ptools');
      tools.hidden = true;
      toolsNumber = el('span', 'kk-ptools__no');
      var button = el('button', 'kk-ptools__comment');
      button.type = 'button';
      button.setAttribute('aria-label', 'Kommentar zu diesem Absatz');
      button.setAttribute('data-tip', 'Kommentar');
      button.innerHTML = core.icon('message-square');
      button.addEventListener('mousedown', function (e) { e.preventDefault(); });
      button.addEventListener('click', commentHere);
      tools.appendChild(toolsNumber);
      tools.appendChild(button);
      marks = el('div', 'kk-pmarks');
      ghosts = el('div', 'kk-pghosts');
      wrap.appendChild(sheet);
      wrap.appendChild(tools);
      wrap.appendChild(marks);
      wrap.appendChild(ghosts);
      parasElement.appendChild(wrap);

      width = result.width;
      cssLink = result.link;
      cache = null;
      list().forEach(function (p) { p.__kk = true; });

      sheet.addEventListener('beforeinput', onBeforeInput);
      sheet.addEventListener('input', onInput);
      sheet.addEventListener('paste', onPaste);
      sheet.addEventListener('drop', function (e) { e.preventDefault(); });
      sheet.addEventListener('compositionstart', function () { composing = true; });
      sheet.addEventListener('compositionend', function () { composing = false; invalidate(); settle(); renumber(); scheduleHousekeeping(); });
      document.addEventListener('selectionchange', onSelection);
      document.execCommand('defaultParagraphSeparator', false, 'p');

      if (typeof ResizeObserver !== 'undefined') {
        // the marks follow at once, in the frame the size changed in (a frame later they would be seen jumping after the text); they lie on a layer of their own, so moving
        // them changes no size that is observed here
        observer = new ResizeObserver(function () {
          fit();
          if (marksFrame) { cancelAnimationFrame(marksFrame); }
          placeMarks();
          if (toolsFrame) { cancelAnimationFrame(toolsFrame); }
          placeTools();
        });
        observer.observe(wrap);
        // the text grows and shrinks while somebody writes: the marks follow their paragraphs
        observer.observe(sheet);
      }
      fit();
      recountAll();

      if (anchor) {
        var all = list();
        var target = all[Math.min(anchor.index, all.length - 1)];
        if (target) {
          if (anchor.focus) { sheet.focus({ preventScroll: true }); setRange(target, Math.min(anchor.offset || 0, textOf(target).length), Math.min(anchor.offset || 0, textOf(target).length)); }
          if (anchor.focus || anchor.index > 0) { target.scrollIntoView({ block: anchor.focus ? 'center' : 'start' }); }
        }
      }
    },

    unmount: function () {
      document.removeEventListener('selectionchange', onSelection);
      clearTimeout(housekeeping);
      if (observer) { observer.disconnect(); observer = null; }
      if (cssLink) { cssLink.remove(); cssLink = null; }
      parasElement.textContent = '';
      sheet = null;
      wrap = null;
      tools = null;
      marks = null;
      ghosts = null;
      cache = null;
    },

    /** The layer on the desk for marks of what was taken away, and where a stretch of text is on it (see rectOf). */
    layer: function () { return ghosts; },
    desk: function () { return wrap; },
    sheet: function () { return sheet; },
    rectOf: rectOf,
    /** The stretch from..to of a paragraph as a range of the page (for marking it with a highlight). */
    rangeOf: function (i, from, to) {
      var p = list()[i];
      if (!p) { return null; }
      var a = locate(p, from);
      var b = to === from ? a : locate(p, to);
      var range = document.createRange();
      range.setStart(a.node, a.offset);
      range.setEnd(b.node, b.offset);
      return range;
    },
    availableWidth: availableWidth,
    pageWidth: function () { return width; },

    texts: function () { return list().map(textOf); },
    count: function () { return list().length; },
    textAt: function (i) { return list()[i] ? displayOf(list()[i]) : ''; },
    element: function (i) { return list()[i]; },

    focusParagraph: function (i, position) {
      var p = list()[i];
      if (!p) { return; }
      sheet.focus({ preventScroll: true });
      var at = position === 'end' ? textOf(p).length : position;
      setRange(p, at, at);
    },

    anchor: function () {
      var active = activePosition();
      if (active) { return { index: active.index, offset: active.start, focus: true }; }
      var all = list();
      for (var i = 0; i < all.length; i += 1) { if (all[i].getBoundingClientRect().bottom > 140) { return { index: i, offset: 0, focus: false }; } }
      return { index: 0, offset: 0, focus: false };
    },

    activePosition: activePosition,

    select: function (i, start, end) {
      var p = list()[i];
      if (!p) { return; }
      sheet.focus({ preventScroll: true });
      setRange(p, start, end);
      p.scrollIntoView({ block: 'center' });
    },

    selectedText: function () {
      var active = activePosition();
      return active && active.end > active.start ? displayOf(list()[active.index]).slice(active.start, active.end) : '';
    },

    replaceSelection: function (text) {
      sheet.focus({ preventScroll: true });
      if (text) { document.execCommand('insertText', false, text); } else { document.execCommand('delete'); }
    },

    // ---- taking a change back (editor-changes.js): every one is a step of the browser's own undo

    /** The words from..to of a paragraph are replaced by a text (put in at that place when there are none): the old words of a change put back. Whether it worked. */
    replaceRange: function (i, from, to, value) {
      var p = list()[i];
      if (!p) { return false; }
      sheet.focus({ preventScroll: true });
      setRange(p, from, to);
      return typeText(value);
    },

    /**
     * A stretch of the text that may go over paragraph breaks (from paragraph p1 at c1 to paragraph p2 at c2) replaced by a text in which U+2029 is a paragraph break: a
     * paragraph that was split joined again, two that were joined split again, and the words with them. The browser's own steps (delete, insertParagraph, insertText), so that
     * Ctrl+Z puts it forward again. The very end (after the break of the last paragraph) is the end of the last paragraph. Whether it worked.
     */
    replaceSpan: function (p1, c1, p2, c2, value) {
      var all = list();
      if (all.length === 0) { return false; }
      var text = value;
      var last = all.length - 1;
      if (p1 > last) {
        // put in after the last break: before it instead (the break moves to the front of the text)
        p1 = last;
        c1 = textOf(all[last]).length;
        if (text.charAt(text.length - 1) === BREAK) { text = BREAK + text.slice(0, -1); }
      }
      if (p2 > last) {
        p2 = last;
        c2 = textOf(all[last]).length;
        if (text.charAt(text.length - 1) === BREAK) { text = text.slice(0, -1); }
      }
      sheet.focus({ preventScroll: true });
      var a = locate(all[p1], c1);
      var b = locate(all[p2], c2);
      var range = document.createRange();
      range.setStart(a.node, a.offset);
      range.setEnd(b.node, b.offset);
      var selection = window.getSelection();
      selection.removeAllRanges();
      selection.addRange(range);
      var done = true;
      if (!range.collapsed) { done = document.execCommand('delete'); }
      text.split(BREAK).forEach(function (part, k) {
        if (k > 0) { done = document.execCommand('insertParagraph') && done; }
        if (part) { done = typeText(part) && done; }
      });
      return done;
    },

    /**
     * Takes a paragraph away: the break before it and its words (the first paragraph: its words and the break after it). It is a deletion across two paragraphs, so a paragraph
     * in a table cell, whose neighbour is not in the same cell, stays (the page refuses that). Whether it worked.
     */
    removeParagraph: function (i) {
      var all = list();
      var p = all[i];
      if (!p) { return false; }
      sheet.focus({ preventScroll: true });
      var range = document.createRange();
      if (i > 0) {
        var before = locate(all[i - 1], textOf(all[i - 1]).length);
        var end = locate(p, textOf(p).length);
        range.setStart(before.node, before.offset);
        range.setEnd(end.node, end.offset);
      } else if (all.length > 1) {
        var start = locate(p, 0);
        var next = locate(all[1], 0);
        range.setStart(start.node, start.offset);
        range.setEnd(next.node, next.offset);
      } else {
        setRange(p, 0, textOf(p).length);
        return document.execCommand('delete');
      }
      var selection = window.getSelection();
      selection.removeAllRanges();
      selection.addRange(range);
      return document.execCommand('delete');
    },

    /** A new paragraph with these words after paragraph i (-1: at the very beginning), looking as the export makes it. Whether it worked. */
    insertParagraphAfter: function (i, value) {
      var all = list();
      sheet.focus({ preventScroll: true });
      if (i < 0) {
        setRange(all[0], 0, 0);
        if (!document.execCommand('insertParagraph')) { return false; }
        setRange(list()[0], 0, 0);
      } else {
        var p = all[Math.min(i, all.length - 1)];
        setRange(p, textOf(p).length, textOf(p).length);
        if (!document.execCommand('insertParagraph')) { return false; }
      }
      // an empty paragraph is the paragraph and nothing more (typeText of nothing would take the marked words away: it would delete the paragraph that was just made)
      return value ? typeText(value) : true;
    },

    /** Every match in every paragraph, from the end so that places that were found stay where they are; each replacement is a step the browser can undo. */
    replaceAll: function (re, text) {
      var count = 0;
      var all = list().slice();
      sheet.focus({ preventScroll: true });
      for (var i = all.length - 1; i >= 0; i -= 1) {
        var found = [];
        var content = displayOf(all[i]);
        re.lastIndex = 0;
        var m;
        while ((m = re.exec(content)) !== null) {
          found.push([m.index, m[0].length, m]);
          if (m[0].length === 0) { re.lastIndex += 1; }
        }
        for (var k = found.length - 1; k >= 0; k -= 1) {
          setRange(all[i], found[k][0], found[k][0] + found[k][1]);
          // what goes in is a text, or a function of the hit (the groups of an expression)
          var value = typeof text === 'function' ? text(found[k][2]) : text;
          var done = value ? document.execCommand('insertText', false, value) : document.execCommand('delete');
          if (done) { count += 1; }
        }
      }
      return count;
    },

    setCommentMarks: function (paragraphs, done) {
      marked = paragraphs;
      markedDone = done || new Set();
      list().forEach(function (p, i) { p.classList.toggle('has-comment', paragraphs.has(i)); });
      scheduleMarks();
    },

    undo: function () { sheet.focus({ preventScroll: true }); document.execCommand('undo'); },
    redo: function () { sheet.focus({ preventScroll: true }); document.execCommand('redo'); },
    keys: false
  };

  core.registerView(page);
})();

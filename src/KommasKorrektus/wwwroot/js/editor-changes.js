/* The changes in the text of the editor, as an editor knows them from Word's review: what differs from an earlier version is marked in the text itself while it is written, a
   place where words were taken away says so and shows what stood there, the places can be walked through and listed, and each of them can be taken back.

   - The text is compared with the earlier version in the browser on every change (sidebyside.js and textdiff.js, the same comparison the comparison page makes). Which version
     it is compared with is chosen in the bar ("the version before", "the beginning of this round", "the original", or a number) and kept with the account, like how it is marked:
     all of it (the words that are new, a bar at the margin of a changed paragraph, a mark where words were taken away), only the bars, or nothing (Alt+M).
   - A mark where words were taken away is a small red pin in the text; a hover, a click or Alt+N / Alt+P opens a card with the old words and the button to put them back.
   - The list of the changes is in the side panel (Alt+L), with a button to take each one back. Taking back is a step of the browser's own undo, so Ctrl+Z puts it forward again.
   - The page of the earlier version can stand beside the text (Alt+R), lined up paragraph by paragraph: it is the same comparison, drawn on the other page too.
   It needs the page view (the text view has no page to mark). No library, no build step. */
(function () {
  'use strict';

  var core = window.KkEditor;
  var chctl = document.getElementById('editor-chctl');
  if (!core || !chctl || !window.KkSideBySide || !window.KkTextDiff) { return; }

  var host = core.host;
  var data = core.data;
  var el = core.el;
  var NL = String.fromCharCode(10);
  var BLANKS = new RegExp('^[ \\t' + String.fromCharCode(0xa0) + ']+$');   // words that are only blanks
  var BLANKS_ANYWHERE = new RegExp('[ \\t' + String.fromCharCode(0xa0) + ']', 'g');
  var SEP = window.KkSideBySide.SEP;   // a paragraph break inside the text of a change
  var MARKUPS = ['all', 'simple', 'off'];
  var LIST_LIMIT = 200;
  var side = window.KkSideBySide.create({ editable: true });   // compares, marks and lines up (shared with the comparison page)

  var pane = document.getElementById('editor-reference');
  var paneBody = pane ? pane.querySelector('.kk-editor__reference-body') : null;
  var bar = document.getElementById('editor-refbar');
  var select = document.getElementById('reference-from');
  var note = document.getElementById('reference-note');
  var paneSwitch = document.getElementById('reference-pane');
  var paneMenu = document.getElementById('editor-reference-toggle');
  var markupMenu = document.getElementById('editor-markup-menu');
  var markupButtons = Array.prototype.slice.call(chctl.querySelectorAll('[data-markup]'));
  var prevButton = document.getElementById('changes-prev');
  var nextButton = document.getElementById('changes-next');
  var countButton = document.getElementById('changes-count');
  var section = document.getElementById('changes-section');
  var listElement = document.getElementById('changes-list');
  var totalElement = document.getElementById('changes-total');
  var summaryElement = document.getElementById('changes-summary');

  var markup = cleanMarkup(data.options && data.options.markup);
  var from = cleanChoice(data.options && data.options.referenceFrom);   // "previous", "round", "original" or the number of a version
  var paneWanted = !!(data.options && data.options.reference);          // what the person asked for; it is shown when the page view is
  var active = false;     // the page view is shown: there is a page to mark
  var paneShown = false;
  var loaded = null;      // the earlier version: { from, to, self, texts, withPage }
  var ticket = 0;
  var link = null;        // the stylesheet of the earlier page
  var places = [];        // what changed, place by place (sidebyside.js: hunks)
  var current = -1;       // the place that was walked to
  var nav = null;         // where the cursor was put by that
  var frame = 0;
  var paragraphCount = 0;  // how many paragraphs the text had at the last comparison
  var lastCost = 0;
  var reloadTimer = null;
  var last = { loaded: null, markup: '', pane: false, els: [], texts: [] };   // what the last comparison was made of (see refresh)
  var burst = [];         // when the size of something changed lately (see layoutChanged)
  var quietUntil = 0;
  var fixedFrom = null;   // the number of the version that the rule "previous" meant when it was loaded: it stays that version while the person works (a save does not move it)
  var listKey = '';
  var pop = null;
  var popPinned = false;
  var popTimer = null;

  function cleanMarkup(value) { return MARKUPS.indexOf(value) >= 0 ? value : 'all'; }
  function cleanChoice(value) { return value === 'round' || value === 'original' || (value && /^\d{1,6}$/.test(String(value))) ? String(value) : 'previous'; }
  function page() { var view = core.view(); return view && view.name === 'page' ? view : null; }
  function remember(body) { core.post('View', body).catch(function () { /* it is only not remembered */ }); }

  /** A text for a line: the line breaks inside a paragraph and the paragraph breaks shown as signs, cut after max characters. */
  function short(text, max) {
    var flat = text.split(NL).join(' ↵ ').split(SEP).join(' ¶ ');
    return flat.length > max ? flat.slice(0, max - 1) + '…' : flat;
  }

  /** What is shown of words in a list or on a card: blanks and breaks (which show nothing) as what they are, anything else cut after max characters. */
  function shownText(text, max) {
    if (BLANKS.test(text)) { return text.length === 1 ? 'ein Leerzeichen' : text.length + ' Leerzeichen'; }
    if (onlyBreaks(text)) { var n = text.split(SEP).length - 1; return n === 1 ? 'ein Absatzwechsel' : n + ' Absatzwechsel'; }
    return short(text, max);
  }

  /** Whether a text is nothing but paragraph breaks (and blanks around them). */
  function onlyBreaks(text) { return text.indexOf(SEP) >= 0 && text.replace(BLANKS_ANYWHERE, '').split(SEP).join('') === ''; }

  // ================================================================================================ what is shown
  function showMarkup() {
    markupButtons.forEach(function (button) { button.setAttribute('aria-pressed', button.getAttribute('data-markup') === markup ? 'true' : 'false'); });
    host.setAttribute('data-markup', markup);
  }

  function setMarkup(next) {
    if (MARKUPS.indexOf(next) < 0 || next === markup) { return; }
    markup = next;
    showMarkup();
    remember({ markup: markup });
    scheduleRefresh();
  }

  function cycleMarkup() {
    if (!page()) { core.toast('Die Änderungen im Text zeigt die Seitenansicht (Alt+W).'); return; }
    setMarkup(MARKUPS[(MARKUPS.indexOf(markup) + 1) % MARKUPS.length]);
    core.toast(markup === 'all' ? 'Änderungen: alle markiert.' : markup === 'simple' ? 'Änderungen: nur ein Balken am Rand.' : 'Änderungen: nicht markiert.');
  }

  /** The earlier page beside the text, as it should be now. */
  function applyPane() {
    if (pane) { pane.hidden = !paneShown; }
    host.classList.toggle('has-reference', paneShown);
    if (paneSwitch) { paneSwitch.checked = paneWanted; }
    if (paneMenu) { paneMenu.setAttribute('aria-pressed', paneWanted ? 'true' : 'false'); }
    if (!paneShown) {
      if (paneBody) { paneBody.textContent = ''; }
      if (link) { link.remove(); link = null; }
      if (loaded) { loaded.withPage = false; }   // the page of the earlier version is gone: showing the pane again has to fetch it
    }
    fit();
  }

  /** The page and the earlier page next to it are the same size: both are fitted to the narrower of the two desks. */
  function fit() {
    var view = page();
    if (!view) { core.fitLimit = null; return; }
    var sheet = paneShown && paneBody ? paneBody.querySelector('.kk-sheet') : null;
    if (!sheet) {
      if (core.fitLimit) { core.fitLimit = null; view.refit(); }
      return;
    }
    var wanted = view.pageWidth() || sheet.offsetWidth;   // the width of the page, the same number the page that is written in is fitted by: both get the very same size
    var own = sheet.parentNode.clientWidth - 16;   // as the page that is written in: its desk, less a little room
    var mine = view.availableWidth();
    var limit = mine > 0 ? Math.min(own, mine) : own;
    var changed = core.fitLimit !== limit;
    core.fitLimit = limit;
    if (changed) { view.refit(); }
    var zoom = (wanted > limit && limit > 0 ? Math.max(0.3, limit / wanted) : 1) * core.scale();   // the size of the writing the person chose is on top
    sheet.style.zoom = Math.abs(zoom - 1) < 0.001 ? '' : String(zoom);
  }

  function setPane(on) {
    paneWanted = on;
    remember({ reference: on });
    if (on && markup === 'off') { setMarkup('all'); }   // the page beside the text is there to show the changes
    if (on && !page()) {
      var button = host.querySelector('[data-view="page"]');
      if (!button || button.disabled) { paneWanted = false; applyPane(); core.toast('Die frühere Fassung braucht die Seitenansicht, und die gibt es für dieses Dokument nicht.'); return; }
      core.setView('page');   // tells us, and sync() does the rest
      return;
    }
    sync();
  }

  // ================================================================================================ loading the earlier version
  function fillSelect(r) {
    select.textContent = '';
    var fill = function (parent, list) {
      list.forEach(function (o) {
        var option = el('option');
        option.value = o.value;
        option.textContent = o.text;
        if (o.value === r.selected) { option.selected = true; }
        parent.appendChild(option);
      });
    };
    fill(select, r.rules);
    if (r.versions.length > 0) {
      var group = el('optgroup');
      group.label = 'Eine bestimmte Fassung';
      fill(group, r.versions);
      select.appendChild(group);
    }
  }

  function drawPane(r) {
    paneBody.textContent = '';
    if (link) { link.remove(); }
    link = el('link');
    link.rel = 'stylesheet';
    link.href = r.link;
    document.head.appendChild(link);
    var desk = el('div', 'kk-desk kk-desk--read');
    var sheet = el('div', 'kk-sheet kk-sheet--read ' + r.pageClass);
    sheet.innerHTML = r.html;
    desk.appendChild(sheet);
    paneBody.appendChild(desk);
    // the ids of the paragraphs would be there twice on the page
    Array.prototype.forEach.call(sheet.querySelectorAll('p.kk-lp[id]'), function (p) { p.removeAttribute('id'); });
    fit();
    last.loaded = null;   // setOld / setOldTexts / clear take every mark away: the next refresh has to make them again
    side.setOld(sheet);
    // the stylesheet of the page has to be there before anything is measured
    var ready = function () { fit(); scheduleRefresh(); };
    if (link.sheet) { ready(); } else { link.addEventListener('load', ready); link.addEventListener('error', ready); }
  }

  function load() {
    var mine = ticket + 1;
    ticket = mine;
    var url = location.pathname + '?id=' + data.documentId + '&handler=Reference&from=' + encodeURIComponent(fixedFrom !== null ? String(fixedFrom) : from) + '&withPage=' + (paneShown ? 'true' : 'false');
    fetch(url, { credentials: 'same-origin' })
      .then(function (response) { if (!response.ok) { throw new Error('HTTP ' + response.status); } return response.json(); })
      .then(function (r) {
        if (mine !== ticket || !active) { return; }
        fillSelect(r);
        if (from === 'previous' && fixedFrom === null) { fixedFrom = r.from; }   // from now on this version, not whatever the rule will mean after the next save
        loaded = { from: r.from, to: r.to, self: !!r.self, texts: r.texts.map(core.toDisplay), withPage: false };
        if (paneShown && r.html) {
          loaded.withPage = true;
          drawPane(r);
        } else {
          applyPane();
          last.loaded = null;
          side.setOldTexts(loaded.texts);
          scheduleRefresh();
        }
      })
      .catch(function () { if (mine === ticket) { note.textContent = 'Die frühere Fassung lässt sich gerade nicht laden.'; } });
  }

  /** The list of choices anew (the names of the versions, what "the version before" is now), without touching the comparison that is on the screen. */
  function refreshChoices() {
    var mine = ticket;
    fetch(location.pathname + '?id=' + data.documentId + '&handler=Reference&withPage=false&from=' + encodeURIComponent(loaded ? String(loaded.from) : from), { credentials: 'same-origin' })
      .then(function (response) { return response.ok ? response.json() : null; })
      .then(function (r) { if (r && mine === ticket && active) { fillSelect(r); } })
      .catch(function () { /* the list stays as it was */ });
  }

  /** The page view is shown, or it is not: what is drawn follows. */
  function sync() {
    var view = page();
    active = !!view;
    chctl.hidden = !active;
    bar.hidden = !active;
    if (!active) { teardown(); return; }
    showMarkup();
    paneShown = paneWanted;
    applyPane();
    if (!loaded || (paneShown && !loaded.withPage)) { load(); return; }
    if (!paneShown) { last.loaded = null; side.setOldTexts(loaded.texts); }
    scheduleRefresh();
  }

  function teardown() {
    last.loaded = null;
    side.clear();
    closePop();
    places = [];
    current = -1;
    nav = null;
    paneShown = false;
    if (loaded) { loaded.withPage = false; }
    applyPane();
    renderCounts();
    renderList();
  }

  // ================================================================================================ comparing and marking
  // When it happens matters as much as what: everything a change of the text makes different (the marks, the room between the two pages, the pins) is done in the frame in
  // which the text changed, before the browser draws it. Done a frame or a moment later, the page is drawn twice, once as the text left it and once lined up and marked: that is
  // seen as a flicker, worst after Enter (everything below jumps down and back up). Only in a text so long that comparing it holds up typing is the comparison put off to a
  // pause; the room is still made right at once (the paragraph that came with Enter is then simply one more on its side until the comparison follows).
  var COMPARE_BUDGET = 16;   // milliseconds a comparison may cost to be made on every keystroke
  var wantCompare = false;
  var compareTimer = null;

  /** The text as it is now, compared with the earlier version, marked, lined up, and the places drawn anew. */
  function refresh() {
    frame = 0;
    var view = page();
    if (!active || !view || !loaded) { return; }
    if (!wantCompare && last.loaded === loaded) {
      // only the size of something changed, or the text in a long document between two comparisons: the room is made right, the marks stay
      side.realign();
      draw(view);
      return;
    }

    wantCompare = false;
    var started = performance.now();
    view.tools.renumber();
    var els = [];
    var texts = [];
    for (var i = 0; i < view.count(); i += 1) { els.push(view.element(i)); texts.push(view.textAt(i)); }
    // Only a change of the text (or of what it is compared with, or how it is marked) is compared and marked anew; else the marks stay as they are.
    var unchanged = last.loaded === loaded && last.markup === markup && last.pane === paneShown && sameList(last.els, els) && sameList(last.texts, texts);
    if (unchanged) {
      side.realign();
    } else {
      side.refresh(els, texts, { mode: markup });
      places = side.hunks();
      paragraphCount = texts.length;
      last = { loaded: loaded, markup: markup, pane: paneShown, els: els, texts: texts };
      if (current >= places.length) { current = -1; }
    }

    draw(view);
    if (!unchanged) {
      renderCounts();
      renderList();
      lastCost = performance.now() - started;
    }
  }

  function sameList(a, b) {
    if (a.length !== b.length) { return false; }
    for (var i = 0; i < a.length; i += 1) { if (a[i] !== b[i]) { return false; } }
    return true;
  }

  /**
   * The size of something changed (the window, the zoom, a column): the places are drawn again. This is what a loop could feed on (a change of size makes a change of size), so
   * it has a brake: more than a dozen in a second, and it waits a few seconds, and says so in the console.
   */
  function layoutChanged(source) {
    if (!active) { return; }
    var now = performance.now();
    if (now < quietUntil) { return; }
    burst = burst.filter(function (t) { return now - t < 1000; });
    burst.push(now);
    if (burst.length > 12) {
      quietUntil = now + 4000;
      burst = [];
      if (window.console && console.warn) { console.warn('KommasKorrektus: the size of the page does not settle (' + source + '); the marks wait a moment.'); }
      setTimeout(scheduleRealign, 4100);
      return;
    }

    fit();
    scheduleRealign();
  }

  /** Compared, marked and lined up in the next frame (a request from an event of this frame runs before the frame is drawn). */
  function scheduleRefresh() {
    wantCompare = true;
    if (!frame) { frame = requestAnimationFrame(refresh); }
  }

  /** Only lined up (and the pins put where their places are now) in the next frame. */
  function scheduleRealign() {
    if (!frame) { frame = requestAnimationFrame(refresh); }
  }

  /** The text changed (a letter, Enter, Backspace ...): see "When it happens" above. */
  function textChanged() {
    if (!active) { return; }
    closePop();
    clearTimeout(compareTimer);
    if (lastCost <= COMPARE_BUDGET) {
      scheduleRefresh();
      return;
    }

    scheduleRealign();
    compareTimer = setTimeout(scheduleRefresh, Math.min(1500, Math.max(250, lastCost * 4)));
  }

  function refreshNow() {
    clearTimeout(compareTimer);
    if (frame) { cancelAnimationFrame(frame); frame = 0; }
    wantCompare = true;
    refresh();
  }

  // ================================================================================================ the marks of what was taken away
  /** A place of the text that exists on the page: the very end (after the break of the last paragraph) is the end of the last paragraph. */
  function clamped(view, at) {
    var count = view.count();
    if (at.p < count) { return at; }
    return count > 0 ? { p: count - 1, c: view.textAt(count - 1).length } : { p: 0, c: 0 };
  }

  /** A pin where words were taken away, a line across the page where whole paragraphs were. */
  function pin(layer, index, left, top, height, width) {
    var h = places[index];
    var button = el('button', width ? 'kk-pghost kk-pghost--paragraph' : 'kk-pghost');
    button.type = 'button';
    button.tabIndex = -1;
    button.setAttribute('data-change', String(index));
    button.setAttribute('aria-label', kindText(h) + (h.kind === 'replace' ? ', vorher: ' : ': ') + shownText(wordsOf(h, 'old'), 60));
    button.style.left = left + 'px';
    button.style.top = top + 'px';
    button.style.height = height + 'px';
    if (width) { button.style.width = width + 'px'; }
    button.addEventListener('mousedown', function (event) { event.preventDefault(); });
    button.addEventListener('click', function () { openPop(index, button.getBoundingClientRect(), true); });
    button.addEventListener('mouseenter', function () { if (!popPinned) { clearTimeout(popTimer); openPop(index, button.getBoundingClientRect(), false); } });
    button.addEventListener('mouseleave', leavePop);
    layer.appendChild(button);
  }

  function draw(view) {
    var layer = view.layer();
    if (!layer) { return; }
    var pins = [];
    if (markup !== 'off') {
      var sheet = view.sheet();
      var desk = view.desk();
      var box = sheet.getBoundingClientRect();
      var deskBox = desk.getBoundingClientRect();
      var count = view.count();
      places.forEach(function (h, index) {
        if (!hasOld(h) || count === 0) { return; }
        if (h.level === 'paragraph') {
          // whole paragraphs that went: a line across the page at the top of the paragraph that stands there now, at the bottom of the last one when nothing follows
          var paragraph = view.element(Math.min(h.newFrom.p, count - 1));
          if (!paragraph) { return; }
          var pb = paragraph.getBoundingClientRect();
          var top = (h.newFrom.p < count ? pb.top : pb.bottom) - deskBox.top - 5;
          pins.push([index, box.left - deskBox.left + desk.scrollLeft, top, 10, box.width]);
          return;
        }
        if (markup !== 'all') { return; }
        var at = clamped(view, h.newFrom);
        var r = view.rectOf(at.p, at.c, at.c);
        if (r) { pins.push([index, r.left, r.top, Math.max(r.height, 14), 0]); }
      });
    }

    // The pins are only made again when there is a different set of them (or one of them has moved by more than a pixel): the same pins in the same places are left alone, so that
    // nothing is taken away and put back for nothing.
    var signature = pins.map(function (p) { return p[0] + ':' + Math.round(p[1]) + ',' + Math.round(p[2]) + ',' + Math.round(p[3]) + ',' + Math.round(p[4]); }).join(';');
    if (layer.getAttribute('data-signature') === signature) { return; }
    layer.textContent = '';
    pins.forEach(function (p) { pin(layer, p[0], p[1], p[2], p[3], p[4]); });
    layer.setAttribute('data-signature', signature);
  }

  // ================================================================================================ the card of a change
  function ensurePop() {
    if (pop) { return pop; }
    pop = el('div', 'kk-chpop');
    pop.setAttribute('role', 'group');
    pop.hidden = true;
    pop.addEventListener('mouseenter', function () { clearTimeout(popTimer); });
    pop.addEventListener('mouseleave', leavePop);
    pop.addEventListener('mousedown', function (event) { event.preventDefault(); });   // the cursor stays in the text
    document.body.appendChild(pop);
    return pop;
  }

  function leavePop() {
    clearTimeout(popTimer);
    if (!popPinned) { popTimer = setTimeout(closePop, 350); }
  }

  function closePop() {
    clearTimeout(popTimer);
    popPinned = false;
    if (pop) { pop.hidden = true; }
  }

  function kindText(h) {
    if (h.level === 'paragraph') {
      var gone = h.oldTexts.length;
      var came = h.newTexts.length;
      if (gone === 0) { return came > 1 ? came + ' neue Absätze' : 'Neuer Absatz'; }
      if (came === 0) { return gone > 1 ? gone + ' Absätze entfernt' : 'Absatz entfernt'; }
      return gone > 1 || came > 1 ? 'Absätze ersetzt' : 'Absatz ersetzt';
    }
    // a break put in or taken away, and nothing but blanks besides: a paragraph split, two joined
    if (onlyBreaks(h.newText) && h.oldText.replace(BLANKS_ANYWHERE, '') === '') { return 'Absatz geteilt'; }
    if (onlyBreaks(h.oldText) && h.newText.replace(BLANKS_ANYWHERE, '') === '') { return 'Absätze verbunden'; }
    return h.kind === 'insert' ? 'Eingefügt' : h.kind === 'delete' ? 'Gestrichen' : 'Ersetzt';
  }

  /** Whether something stood there / stands there now (an empty paragraph that went or came counts: its break). */
  function hasOld(h) { return h.oldText.length > 0; }
  function hasNew(h) { return h.newText.length > 0; }

  /** The words of a place for a list or a card; paragraphs that have no words at all are said to be empty (otherwise there would be nothing to read). */
  function wordsOf(h, which) {
    if (h.level !== 'paragraph') { return which === 'old' ? h.oldText : h.newText; }
    var list = which === 'old' ? h.oldTexts : h.newTexts;
    var texts = list.filter(function (text) { return text !== ''; });
    if (texts.length === 0) { return list.length > 1 ? list.length + ' leere Absätze' : 'leerer Absatz'; }
    return texts.join(SEP);
  }

  /** The number of the paragraph a place is in (or before which it stood), as the page shows it. */
  function numberOf(h) { return Math.max(1, Math.min(h.newFrom.p + 1, paragraphCount)); }

  function row(label, tag, text) {
    var line = el('div', 'kk-chpop__row');
    var small = el('small');
    small.textContent = label;
    var mark = el(tag);
    mark.textContent = shownText(text, 600);
    line.appendChild(small);
    line.appendChild(mark);
    return line;
  }

  /** The card of a place: what stood there and what stands there now, and the button that puts it back. rect is where it is on the screen. */
  function openPop(index, rect, pinned) {
    var h = places[index];
    if (!h) { return; }
    var card = ensurePop();
    popPinned = !!pinned;
    card.textContent = '';
    var title = el('div', 'kk-chpop__title');
    title.textContent = kindText(h) + ' · Absatz ' + numberOf(h);
    card.appendChild(title);
    if (hasOld(h)) { card.appendChild(row(h.kind === 'replace' ? 'Vorher' : 'Stand da', 'del', wordsOf(h, 'old'))); }
    if (hasNew(h)) { card.appendChild(row(h.kind === 'replace' ? 'Jetzt' : 'Neu', 'ins', wordsOf(h, 'new'))); }
    var actions = el('div', 'kk-chpop__actions');
    var back = el('button', 'mat-btn mat-btn--secondary mat-btn--sm');
    back.type = 'button';
    back.innerHTML = core.icon('undo') + '<span>Zurücksetzen</span>';
    back.addEventListener('click', function () { revert(index); });
    actions.appendChild(back);
    card.appendChild(actions);
    card.hidden = false;
    var width = card.offsetWidth;
    var height = card.offsetHeight;
    var left = Math.max(8, Math.min(rect.left, window.innerWidth - width - 8));
    var top = rect.bottom + 6;
    if (top + height > window.innerHeight - 8) { top = Math.max(8, rect.top - height - 6); }
    card.style.left = left + 'px';
    card.style.top = top + 'px';
  }

  // ================================================================================================ taking a change back
  /** One place put back as it was. Whether it worked. */
  function revertPlace(h) {
    var view = page();
    if (!view) { return false; }
    if (h.level === 'words') { return view.replaceSpan(h.newFrom.p, h.newFrom.c, h.newTo.p, h.newTo.c, h.oldText); }
    // Whole paragraphs: the ones that were taken away come back in their order, each after the one before (so that each gets the look of a paragraph of its own), then the ones
    // that were put in go, from the last one (they now stand after the ones that came back).
    var ok = true;
    var at = h.newFrom.p;
    for (var j = 0; j < h.oldTexts.length; j += 1) { ok = view.insertParagraphAfter(at - 1 + j, h.oldTexts[j]) && ok; }
    for (var k = h.newTexts.length - 1; k >= 0; k -= 1) { ok = view.removeParagraph(at + h.oldTexts.length + k) && ok; }
    return ok;
  }

  function revert(index) {
    var h = places[index];
    if (!h) { return; }
    closePop();
    var ok = revertPlace(h);
    current = -1;
    nav = null;
    refreshNow();
    if (!ok) { core.toast('Das lässt sich hier nicht zurücksetzen. Ändere die Stelle von Hand.', 'error'); }
  }

  function revertAll() {
    var count = places.length;
    if (count === 0 || !page()) { return; }
    var from = loaded ? againstWhat() : 'der früheren Fassung';
    window.MatUi.confirm('Alle ' + count + ' Änderungen zurücksetzen? Der Text kehrt zu ' + from + ' zurück. Mit Strg+Z lässt sich das Stück für Stück wieder zurücknehmen.', { title: 'Alle Änderungen zurücksetzen', ok: 'Zurücksetzen', danger: true }).then(function (yes) {
      if (!yes) { return; }
      // from the end: a place that is put back does not move the ones before it; the window stays where it is (every step puts the cursor at its place, which would scroll there)
      var all = places.slice();
      var failed = 0;
      var left = window.scrollX;
      var top = window.scrollY;
      for (var i = all.length - 1; i >= 0; i -= 1) { if (!revertPlace(all[i])) { failed += 1; } }
      window.scrollTo(left, top);
      current = -1;
      nav = null;
      refreshNow();
      if (failed > 0) { core.toast(failed + ' von ' + count + ' Stellen ließen sich nicht zurücksetzen (zum Beispiel über eine Tabellenzelle hinweg).', 'error'); }
    });
  }

  // ================================================================================================ walking through the changes
  /** Where a place is in the text, to compare with the cursor: [paragraph, position]; paragraphs that were taken away stand before the one after them. */
  function keyOf(h) {
    return [h.newFrom.p, h.level === 'paragraph' && h.newTexts.length === 0 ? -1 : h.newFrom.c];
  }

  function before(a, b) { return a[0] < b[0] || (a[0] === b[0] && a[1] < b[1]); }

  /** Where the person is in the text: the cursor, or the first paragraph on the screen. */
  function caret(view) {
    var position = view.activePosition();
    if (position) { return { index: position.index, start: position.start }; }
    return { index: view.anchor().index, start: 0 };
  }

  function rectOfPlace(view, h) {
    var at = clamped(view, h.newFrom);
    var r = view.rectOf(at.p, at.c, at.c);
    if (!r) { return null; }
    var desk = view.desk().getBoundingClientRect();
    return { left: r.left + desk.left - view.desk().scrollLeft, top: r.top + desk.top, bottom: r.top + desk.top + Math.max(r.height, 16) };
  }

  /** Puts the cursor at a place (on the new words when there are some, as far as the paragraph it begins in) and opens its card. */
  function goTo(index, open) {
    var view = page();
    var h = places[index];
    if (!view || !h || view.count() === 0) { return; }
    current = index;
    var from = clamped(view, h.newFrom);
    var to = clamped(view, h.newTo);
    if (h.level === 'paragraph') { view.select(from.p, 0, 0); }
    else if (to.p === from.p) { view.select(from.p, from.c, to.c); }
    else { view.select(from.p, from.c, view.textAt(from.p).length); }
    var at = view.activePosition();
    nav = at ? { index: at.index, start: at.start } : null;
    markCurrent();
    if (open) { requestAnimationFrame(function () { var r = rectOfPlace(view, h); if (r) { openPop(index, r, true); } }); }
  }

  function step(direction) {
    var view = page();
    if (!view) { core.toast('Die Änderungen zeigt die Seitenansicht (Alt+W).'); return; }
    if (places.length === 0) { core.toast('Keine Änderungen gegenüber ' + (loaded ? (loaded.self ? 'dem gespeicherten Text.' : 'Fassung ' + loaded.from + '.') : 'der früheren Fassung.')); return; }
    var here = caret(view);
    var target = -1;
    if (nav && current >= 0 && current < places.length && here.index === nav.index && here.start === nav.start) {
      target = (current + direction + places.length) % places.length;   // the cursor is where the last jump put it: on to the next
    } else if (direction > 0) {
      for (var i = 0; i < places.length; i += 1) { if (before([here.index, here.start], keyOf(places[i]))) { target = i; break; } }
      if (target < 0) { target = 0; }
    } else {
      for (var j = places.length - 1; j >= 0; j -= 1) { if (before(keyOf(places[j]), [here.index, here.start])) { target = j; break; } }
      if (target < 0) { target = places.length - 1; }
    }
    goTo(target, true);
  }

  // ================================================================================================ the numbers and the list
  /** What the changes are against, in words: the saved text of a version, or the version. */
  function againstWhat() { return loaded.self ? 'dem gespeicherten Text (Fassung ' + loaded.from + ')' : 'Fassung ' + loaded.from; }

  function renderCounts() {
    var n = places.length;
    var text = String(n);
    countButton.textContent = text;
    countButton.setAttribute('data-tip', n === 0 ? 'Keine Änderungen gegenüber der früheren Fassung' : n + ' Änderungen auflisten');
    prevButton.disabled = n === 0;
    nextButton.disabled = n === 0;
    if (totalElement) { totalElement.textContent = text; }
    var against = loaded ? againstWhat() : '';
    if (summaryElement) { summaryElement.textContent = loaded ? 'Gegenüber ' + against + (n === 0 ? ': nichts geändert.' : '.') : ''; }
    note.textContent = loaded ? (paneShown ? 'Gegenüber ' + against + ': rot ist, was dort weggefallen ist, grün, was bei dir neu ist.' : 'Gegenüber ' + against + '.') : '';
  }

  function itemFor(h, index) {
    var item = el('li', 'kk-comment kk-change');
    item.setAttribute('data-change', String(index));
    var head = el('div', 'kk-comment__head');
    var where = el('a');
    where.href = '#p' + numberOf(h);
    where.textContent = 'Absatz ' + numberOf(h);
    where.addEventListener('click', function (event) { event.preventDefault(); goTo(index, false); });
    var kind = el('small', 'kk-change__kind kk-change__kind--' + (h.kind === 'insert' ? 'new' : h.kind === 'delete' ? 'gone' : 'both'));
    kind.textContent = kindText(h);
    var back = el('button', 'mat-btn mat-btn--ghost mat-btn--sm');
    back.type = 'button';
    back.innerHTML = core.icon('undo') + '<span>Zurücksetzen</span>';
    back.addEventListener('click', function (event) { event.stopPropagation(); revert(index); });
    head.appendChild(where);
    head.appendChild(kind);
    head.appendChild(back);
    var body = el('p', 'kk-change__text');
    if (hasOld(h)) { var gone = el('del'); gone.textContent = shownText(wordsOf(h, 'old'), 90); body.appendChild(gone); }
    if (hasOld(h) && hasNew(h)) { body.appendChild(document.createTextNode(' ')); }
    if (hasNew(h)) { var came = el('ins'); came.textContent = shownText(wordsOf(h, 'new'), 90); body.appendChild(came); }
    item.appendChild(head);
    item.appendChild(body);
    item.addEventListener('click', function () { goTo(index, false); });
    return item;
  }

  function markCurrent() {
    Array.prototype.forEach.call(listElement.children, function (li) {
      var isCurrent = li.getAttribute('data-change') === String(current);
      li.classList.toggle('is-current', isCurrent);
      if (isCurrent) { core.revealInSide(li); }
    });
  }

  function renderList() {
    if (!section || !listElement) { return; }
    section.hidden = !active;
    // the list is only built again when it is another list (typing inside a word that stays changed does not touch it)
    var key = places.map(function (h) { return h.level + h.kind + numberOf(h) + 'x' + (h.count || 1) + '|' + h.oldText + '|' + h.newText; }).join('\n');
    if (key === listKey) { return; }
    listKey = key;
    listElement.textContent = '';
    if (!active) { return; }
    if (places.length === 0) {
      var empty = el('p', 'mat-muted');
      empty.textContent = loaded ? 'Nichts hat sich geändert.' : '';
      listElement.appendChild(empty);
      return;
    }
    places.slice(0, LIST_LIMIT).forEach(function (h, index) { listElement.appendChild(itemFor(h, index)); });
    if (places.length > LIST_LIMIT) {
      var more = el('p', 'mat-muted');
      more.textContent = '… und ' + (places.length - LIST_LIMIT) + ' weitere (Alt+N geht sie der Reihe nach durch).';
      listElement.appendChild(more);
    }
    var all = el('button', 'mat-btn mat-btn--ghost mat-btn--sm kk-changes__all');
    all.type = 'button';
    all.innerHTML = core.icon('undo') + '<span>Alle zurücksetzen</span>';
    all.addEventListener('click', revertAll);
    listElement.appendChild(all);
    markCurrent();
  }

  // ================================================================================================ wiring
  markupButtons.forEach(function (button) { button.addEventListener('click', function () { setMarkup(button.getAttribute('data-markup')); }); });
  prevButton.addEventListener('click', function () { step(-1); });
  nextButton.addEventListener('click', function () { step(1); });
  countButton.addEventListener('click', function () {
    core.showSide(true, true);
    if (section && !section.hidden) { core.revealInSide(section, true); }
  });
  if (select) {
    select.addEventListener('change', function () {
      from = cleanChoice(select.value);
      fixedFrom = null;
      remember({ referenceFrom: from });
      load();
    });
  }
  if (paneSwitch) { paneSwitch.addEventListener('change', function () { setPane(paneSwitch.checked); }); }
  if (paneMenu) { paneMenu.addEventListener('click', function () { setPane(!paneWanted); }); }
  if (markupMenu) { markupMenu.addEventListener('click', cycleMarkup); }

  core.hotkeys.KeyR = function () { setPane(!paneWanted); };
  core.hotkeys.KeyM = cycleMarkup;
  core.hotkeys.KeyN = function () { step(1); };
  core.hotkeys.KeyP = function () { step(-1); };

  core.on('view', sync);
  core.on('ready', sync);
  core.on('change', textChanged);
  core.on('zoom', function () { fit(); scheduleRealign(); });
  core.on('saved', function (event) {
    // Nothing is compared anew: the version that is compared with stays, so the marks do not jump while somebody works. Only the list of choices is brought up to date when a
    // save made a version or closed the pass (the names, and what "the version before" is for the next pass).
    if (!active || !loaded) { return; }
    var result = event.result;
    if (event.kind === 'close' && from === 'previous') {
      // The pass is closed (the person did that): the next pass is changed against this version, so the comparison begins anew, clean.
      fixedFrom = null;
      clearTimeout(reloadTimer);
      reloadTimer = setTimeout(load, 400);
      return;
    }

    if (result.created || event.kind === 'close' || result.versionNo !== loaded.to) {
      loaded.to = result.versionNo;
      clearTimeout(reloadTimer);
      reloadTimer = setTimeout(refreshChoices, 400);
    }
  });

  document.addEventListener('keydown', function (event) { if (event.key === 'Escape' && pop && !pop.hidden) { closePop(); } });
  document.addEventListener('mousedown', function (event) { if (pop && !pop.hidden && !pop.contains(event.target) && !(event.target.closest && event.target.closest('.kk-pghost'))) { closePop(); } }, true);
  window.addEventListener('scroll', function () { if (pop && !pop.hidden) { closePop(); } }, true);
  window.addEventListener('resize', function () { fit(); scheduleRealign(); });

  if (typeof ResizeObserver !== 'undefined') {
    // Whatever changes the height of one column (a word that wraps, the zoom of the page, the window) is lined up again.
    // (the one place a loop could start: the lining up changes a height, which is seen here; the window and the zoom are a person's doing and are not braked)
    var resizes = new ResizeObserver(function () { layoutChanged('height'); });
    if (paneBody) { resizes.observe(paneBody); }
    resizes.observe(core.parasElement);
  }
})();

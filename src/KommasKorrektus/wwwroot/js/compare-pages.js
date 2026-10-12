/* The comparison as two pages next to each other: the page of the old version and the page of the new one, compared in the browser, marked and lined up the same way as the
   editor does it with an earlier version beside the text (sidebyside.js). It also steps from change to change (the buttons, or n and p on the keyboard). */
(function () {
  'use strict';

  var oldSheet = document.querySelector('.kk-desk--old .kk-sheet');
  var newSheet = document.querySelector('.kk-desk--new .kk-sheet');
  if (!oldSheet || !newSheet || !window.KkSideBySide) { return; }

  var side = window.KkSideBySide.create({ editable: false });
  var frame = 0;
  var current = null;

  function compare() {
    side.setOld(oldSheet);
    var now = window.KkSideBySide.paragraphsOf(newSheet);
    side.refresh(now.els, now.texts);
  }

  // "Nur Änderungen": runs of paragraphs that are the same are folded away on both pages at once, one paragraph of context stays around a change; a click on the place
  // opens that run on both sides. Paragraphs in a table stay (the table would be left with empty cells).
  var changesOnly = (document.getElementById('pages') || document.body).getAttribute('data-changes') === 'true';
  var CONTEXT = 1;

  function fold() {
    if (!changesOnly) { return; }
    var rows = side.rows();
    var i = 0;
    while (i < rows.length) {
      if (rows[i].kind !== 'same') { i += 1; continue; }
      var j = i;
      while (j < rows.length && rows[j].kind === 'same') { j += 1; }
      var from = i === 0 ? i : i + CONTEXT;
      var to = j === rows.length ? j : j - CONTEXT;
      var run = rows.slice(from, to).filter(function (row) { return row.theirs && row.mine && !row.theirs.closest('td') && !row.mine.closest('td'); });
      if (run.length >= 2) { foldRun(run); }
      i = j;
    }
  }

  function foldRun(run) {
    var text = run.length + ' unveränderte Absätze';
    var markers = [];
    var open = function () {
      run.forEach(function (row) { row.theirs.classList.remove('kk-folded'); row.mine.classList.remove('kk-folded'); });
      markers.forEach(function (marker) { marker.remove(); });
      side.realign();
    };
    [run[0].theirs, run[0].mine].forEach(function (first) {
      var marker = document.createElement('button');
      marker.type = 'button';
      marker.className = 'kk-fold';
      marker.textContent = text;
      marker.setAttribute('aria-label', text + ' zeigen');
      marker.addEventListener('click', open);
      first.parentNode.insertBefore(marker, first);
      markers.push(marker);
    });
    run.forEach(function (row) { row.theirs.classList.add('kk-folded'); row.mine.classList.add('kk-folded'); });
  }

  function later() {
    if (frame) { return; }
    frame = requestAnimationFrame(function () { frame = 0; side.realign(); });
  }

  compare();
  fold();
  side.realign();
  window.addEventListener('resize', later);
  if (document.fonts && document.fonts.ready) { document.fonts.ready.then(later); }
  if (typeof ResizeObserver !== 'undefined') {
    // The zoom of the pages (sheet.js) follows the width of the desk: the paragraphs change their height when it changes.
    var observer = new ResizeObserver(later);
    Array.prototype.forEach.call(document.querySelectorAll('.kk-desk--old, .kk-desk--new'), function (desk) { observer.observe(desk); });
  }

  // The size of the writing: the same choice as in the editor, kept with the account.
  var zoomBox = document.querySelector('[data-zoom]');
  if (zoomBox && window.KkZoom) {
    var zoom = window.KkZoom.clean(zoomBox.getAttribute('data-zoom'));
    var value = zoomBox.querySelector('.kk-zoom__value');
    var down = zoomBox.querySelector('[data-zoom-step="-1"]');
    var up = zoomBox.querySelector('[data-zoom-step="1"]');

    var show = function () {
      value.textContent = zoom + ' %';
      down.disabled = !window.KkZoom.next(zoom, -1);
      up.disabled = !window.KkZoom.next(zoom, 1);
    };
    var choose = function (next) {
      if (!next || next === zoom) { return; }
      zoom = next;
      Array.prototype.forEach.call(document.querySelectorAll('.kk-desk--old, .kk-desk--new'), function (desk) { desk.setAttribute('data-scale', String(zoom / 100)); });
      show();
      if (window.KkSheet) { window.KkSheet.fit(); }
      later();
      var token = document.querySelector('input[name=__RequestVerificationToken]');
      fetch(location.pathname + '?handler=Zoom', {
        method: 'POST', credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json', RequestVerificationToken: token ? token.value : '' },
        body: JSON.stringify({ zoom: zoom }),
      }).catch(function () { /* it is only not remembered */ });
    };
    down.addEventListener('click', function () { choose(window.KkZoom.next(zoom, -1)); });
    up.addEventListener('click', function () { choose(window.KkZoom.next(zoom, 1)); });
    value.addEventListener('click', function () { choose(100); });
    show();
  }

  function go(step) {
    var list = side.changes();
    if (list.length === 0) { return; }
    var at = current ? list.findIndex(function (c) { return c.element === current; }) : -1;
    if (current) { current.classList.remove('is-current'); }
    at = at < 0 ? (step > 0 ? 0 : list.length - 1) : (at + step + list.length) % list.length;
    current = list[at].element;
    current.classList.add('is-current');
    current.scrollIntoView({ block: 'center', behavior: 'smooth' });
  }

  Array.prototype.forEach.call(document.querySelectorAll('[data-diff-step]'), function (button) {
    button.addEventListener('click', function () { go(parseInt(button.getAttribute('data-diff-step'), 10)); });
  });

  document.addEventListener('keydown', function (event) {
    var target = event.target;
    if (event.ctrlKey || event.metaKey || event.altKey || (target && /^(input|select|textarea)$/i.test(target.tagName))) { return; }
    if (event.key === 'n') { go(1); } else if (event.key === 'p') { go(-1); }
  });
})();

/* The check of the editor: what a proofreader looks for by rule (proofcheck.js: spaces, quotation marks, ellipsis, dashes, protected spaces, a word twice, empty paragraphs, names that
   differ a letter from the style sheet) shown in the side panel and in the text, each place with the one click that corrects it, each rule with a click that corrects all of its places.
   It is asked for (Alt+T or the button) and then follows the text: a moment after the last key it looks again, so the list is always about the text as it is. A correction is a step of
   the editor's own undo. Nothing is changed on its own. No library, no build step. */
(function () {
  'use strict';

  var core = window.KkEditor;
  var section = document.getElementById('check-section');
  if (!core || !section || !window.KkProofCheck) { return; }

  var el = core.el;
  var data = core.data;
  var NL = String.fromCharCode(10);
  var NBSP = String.fromCharCode(0xa0);
  var SHOWN_PER_RULE = 30;
  var AGAIN_AFTER_MS = 1200;

  var list = document.getElementById('check-list');
  var totalElement = document.getElementById('check-total');
  var summary = document.getElementById('check-summary');
  var runButton = document.getElementById('check-run');
  var clearButton = document.getElementById('check-clear');
  var menuItem = document.getElementById('editor-check-menu');
  var canHighlight = typeof CSS !== 'undefined' && !!CSS.highlights && typeof Highlight !== 'undefined';

  var findings = [];
  var shown = false;     // the person asked for the check: it follows the text
  var again = null;

  function terms() { return (data.styles || []).map(function (s) { return { term: s.term, kind: s.kind }; }); }

  function texts(view) {
    var out = [];
    for (var i = 0; i < view.count(); i += 1) { out.push(view.textAt(i)); }
    return out;
  }

  /** A stretch of text as it is read in a list: line breaks as a sign, blanks as what they are. */
  function describe(text) {
    if (text === '') { return 'weg'; }
    if (/^ +$/.test(text)) { return text.length === 1 ? 'ein Leerzeichen' : text.length + ' Leerzeichen'; }
    if (text === NBSP) { return 'geschütztes Leerzeichen'; }
    if (/^[ \t]+$/.test(text)) { return 'Leerraum'; }
    return text.split(NL).join('↵');
  }

  function around(text, from, to) {
    var before = text.slice(Math.max(0, from - 24), from);
    var after = text.slice(to, to + 24);
    return { before: (from > 24 ? '…' : '') + before.split(NL).join('↵'), after: after.split(NL).join('↵') + (to + 24 < text.length ? '…' : '') };
  }

  function clearMarks() { if (canHighlight) { CSS.highlights.delete('kk-check'); } }

  /** The places in the text of the page, marked (the text view has no page to mark). */
  function paint(view) {
    clearMarks();
    if (!canHighlight || !view || !view.rangeOf) { return; }
    var ranges = [];
    findings.slice(0, 1500).forEach(function (f) {
      if (f.to > f.from) { var r = view.rangeOf(f.n, f.from, f.to); if (r) { ranges.push(r); } }
      else if (f.remove) { var p = view.element(f.n); if (p) { p.classList.add('kk-check-empty'); } }
    });
    if (ranges.length > 0) { CSS.highlights.set('kk-check', new Highlight(...ranges)); }
  }

  function unpaintEmpty() {
    Array.prototype.forEach.call(document.querySelectorAll('.kk-check-empty'), function (p) { p.classList.remove('kk-check-empty'); });
  }

  // ================================================================================================ taking a place right
  /** Whether the place is still what it was found as (the text may have changed since): a correction is only made where it is. */
  function stillThere(view, f) {
    if (f.n >= view.count()) { return false; }
    var text = view.textAt(f.n);
    return f.remove ? text === '' : text.slice(f.from, f.to) === f.found;
  }

  function correct(view, f) {
    if (!stillThere(view, f)) { return false; }
    if (f.remove) { return !!view.removeParagraph && view.removeParagraph(f.n); }
    return !!view.replaceRange && view.replaceRange(f.n, f.from, f.to, f.fix);
  }

  function fixable(f) { return f.remove || f.fix !== null; }

  function fixOne(index) {
    var view = core.view();
    var f = findings[index];
    if (!view || !f) { return; }
    if (!correct(view, f)) { core.toast('Die Stelle hat sich geändert. Ich prüfe noch einmal.'); }
    run();
  }

  function fixRule(rule) {
    var view = core.view();
    if (!view) { return; }
    var mine = findings.filter(function (f) { return f.rule === rule && fixable(f); });
    var done = 0;
    // from the end: a correction does not move the places before it
    for (var i = mine.length - 1; i >= 0; i -= 1) { if (correct(view, mine[i])) { done += 1; } }
    run();
    core.toast(done === 1 ? 'Eine Stelle korrigiert (Strg+Z nimmt sie zurück).' : done + ' Stellen korrigiert (Strg+Z nimmt sie nacheinander zurück).');
  }

  function goTo(index) {
    var view = core.view();
    var f = findings[index];
    if (!view || !f) { return; }
    if (!stillThere(view, f)) { run(); return; }
    view.select(f.n, f.from, f.to);
  }

  // ================================================================================================ the list
  function item(f, index) {
    var row = el('li', 'kk-check__item');
    row.setAttribute('data-finding', String(index));
    var view = core.view();
    var text = view ? view.textAt(f.n) : '';
    var context = el('div', 'kk-check__context');
    var near = around(text, f.from, f.to);
    var link = el('a');
    link.href = '#p' + (f.n + 1);
    link.className = 'kk-check__where';
    link.textContent = 'Absatz ' + (f.n + 1);
    link.addEventListener('click', function (event) { event.preventDefault(); goTo(index); });
    var mark = el('mark');
    mark.textContent = f.remove ? 'leerer Absatz' : describe(f.found);
    context.appendChild(link);
    context.appendChild(document.createTextNode(' '));
    context.appendChild(document.createTextNode(near.before));
    context.appendChild(mark);
    context.appendChild(document.createTextNode(near.after));
    row.appendChild(context);
    var actions = el('div', 'kk-check__actions');
    if (fixable(f)) {
      var result = el('span', 'kk-check__result');
      result.appendChild(document.createTextNode(f.remove ? 'entfernen' : '→ '));
      if (!f.remove) { var ins = el('ins'); ins.textContent = describe(f.fix); result.appendChild(ins); }
      actions.appendChild(result);
      var button = el('button', 'mat-btn mat-btn--ghost mat-btn--sm');
      button.type = 'button';
      button.innerHTML = core.icon('check') + '<span>Korrigieren</span>';
      button.addEventListener('click', function (event) { event.stopPropagation(); fixOne(index); });
      actions.appendChild(button);
    }
    row.appendChild(actions);
    row.addEventListener('click', function () { goTo(index); });
    return row;
  }

  function render() {
    var keep = list.scrollTop;
    list.textContent = '';
    totalElement.textContent = String(findings.length);
    clearButton.hidden = !shown;
    if (!shown) { summary.textContent = 'Leerzeichen, Anführungszeichen, Gedankenstriche und die Namen des Stilblatts.'; return; }
    if (findings.length === 0) {
      summary.textContent = 'Nichts gefunden. Der Text ist, soweit diese Regeln es sehen, in Ordnung.';
      return;
    }
    summary.textContent = findings.length === 1 ? 'Eine Stelle.' : findings.length + ' Stellen. Ein Klick auf eine Stelle springt dorthin, ein zweiter korrigiert sie.';
    window.KkProofCheck.RULES.forEach(function (rule) {
      var mine = [];
      findings.forEach(function (f, index) { if (f.rule === rule.id) { mine.push(index); } });
      if (mine.length === 0) { return; }
      var group = el('div', 'kk-check__group');
      var head = el('div', 'kk-check__head');
      var title = el('h3');
      title.textContent = rule.title + ' ';
      var count = el('span', 'mat-badge mat-badge--soft');
      count.textContent = String(mine.length);
      title.appendChild(count);
      head.appendChild(title);
      if (mine.every(function (index) { return fixable(findings[index]); })) {
        var all = el('button', 'mat-btn mat-btn--ghost mat-btn--sm');
        all.type = 'button';
        all.innerHTML = core.icon('check') + '<span>Alle korrigieren</span>';
        all.addEventListener('click', function () { fixRule(rule.id); });
        head.appendChild(all);
      }
      group.appendChild(head);
      var hint = el('p', 'mat-hint');
      hint.textContent = rule.hint;
      group.appendChild(hint);
      var ul = el('ul', 'kk-check__items');
      mine.slice(0, SHOWN_PER_RULE).forEach(function (index) { ul.appendChild(item(findings[index], index)); });
      group.appendChild(ul);
      if (mine.length > SHOWN_PER_RULE) {
        var more = el('p', 'mat-muted');
        more.textContent = '… und ' + (mine.length - SHOWN_PER_RULE) + ' weitere (nach dem Korrigieren dieser kommen sie an die Reihe).';
        group.appendChild(more);
      }
      list.appendChild(group);
    });
    list.scrollTop = keep;
  }

  function run() {
    var view = core.view();
    clearTimeout(again);
    if (!view) { return; }
    unpaintEmpty();
    shown = true;
    findings = window.KkProofCheck.check(texts(view), { terms: terms() });
    render();
    paint(view);
  }

  function close() {
    shown = false;
    findings = [];
    clearTimeout(again);
    clearMarks();
    unpaintEmpty();
    render();
  }

  function open() {
    core.showSide(true, false);
    run();
    core.revealInSide(section, true);
  }

  // ================================================================================================ wiring
  runButton.addEventListener('click', run);
  clearButton.addEventListener('click', close);
  if (menuItem) { menuItem.addEventListener('click', open); }
  core.hotkeys.KeyT = open;
  // it follows the text: a moment after the last key it looks again (what is shown is always about the text as it is)
  core.on('change', function () {
    if (!shown) { return; }
    clearMarks();
    clearTimeout(again);
    again = setTimeout(run, AGAIN_AFTER_MS);
  });
  core.on('view', function () { if (shown) { setTimeout(run, 0); } });
})();

/* The editor of KommaKorrektus: the text of a document as paragraphs, each a field of its own.
   - Enter splits a paragraph, Backspace at the start joins it with the one before, Shift+Enter is a line break inside a paragraph.
   - Everything is saved as versions: an autosave while somebody types, a version when asked (Ctrl+S).
   - The editor reports how long it was used (seconds with activity, keys pressed); a long pause is a pause, the server cuts the time there.
   - Undo and redo work across paragraphs (the fields of the browser only know their own text).
   No library, no build step; the data comes as JSON in #editor-data. */
(function () {
  'use strict';

  var dataElement = document.getElementById('editor-data');
  var host = document.getElementById('kk-editor');
  if (!dataElement || !host) { return; }

  var data = JSON.parse(dataElement.textContent);
  var LS = String.fromCharCode(0x2028);   // a line break inside a paragraph (Word's soft return)
  var tokenInput = host.querySelector('input[name="__RequestVerificationToken"]');
  var token = tokenInput ? tokenInput.value : '';
  var parasElement = document.getElementById('paras');
  var statusElement = document.getElementById('editor-status');
  var wordsElement = document.getElementById('editor-words');
  var sessionElement = document.getElementById('editor-session');
  var base = new Set(data.paragraphs);
  var baseVersion = data.versionNo;
  var sizeSupported = window.CSS && CSS.supports && CSS.supports('field-sizing', 'content');

  // ------------------------------------------------------------------------------------------------ helpers
  function el(tag, cls) { var e = document.createElement(tag); if (cls) { e.className = cls; } return e; }
  function toDisplay(text) { return text.split(LS).join('\n'); }
  function fromDisplay(value) { return value.replace(/\r\n?/g, '\n').split('\n').join(LS); }
  function countWords(value) {
    var tokens = value.match(/\S+/g);
    if (!tokens) { return 0; }
    var n = 0;
    for (var i = 0; i < tokens.length; i += 1) { if (/[\p{L}\p{N}]/u.test(tokens[i])) { n += 1; } }
    return n;
  }
  function textareaOf(p) { return p.firstElementChild.nextElementSibling; }
  function paragraphs() { return Array.prototype.slice.call(parasElement.children); }
  function snapshot() { return paragraphs().map(function (p) { return fromDisplay(textareaOf(p).value); }); }
  function icon(name) { return '<svg class="mat-icon" aria-hidden="true" focusable="false"><use href="#mat-i-' + name + '"></use></svg>'; }
  function toast(message, tone) { if (window.MatUi) { window.MatUi.toast(message, { tone: tone }); } }

  function autosize(ta) {
    if (sizeSupported) { return; }
    ta.style.height = 'auto';
    ta.style.height = ta.scrollHeight + 'px';
  }

  // ------------------------------------------------------------------------------------------------ the paragraphs
  var totalWords = 0;
  var wordsOf = new WeakMap();

  function createParagraph(text) {
    var wrap = el('div', 'kk-p');
    var no = el('span', 'kk-p__no');
    no.setAttribute('aria-hidden', 'true');
    var ta = el('textarea', 'kk-p__text');
    ta.rows = 1;
    ta.value = toDisplay(text);
    ta.spellcheck = true;
    ta.lang = 'de';
    ta.setAttribute('autocomplete', 'off');
    var button = el('button', 'kk-p__comment');
    button.type = 'button';
    button.tabIndex = -1;
    button.setAttribute('aria-label', 'Kommentar zu diesem Absatz');
    button.setAttribute('data-tip', 'Kommentar');
    button.innerHTML = icon('message-square');
    wrap.appendChild(no);
    wrap.appendChild(ta);
    wrap.appendChild(button);
    wordsOf.set(ta, 0);
    markChanged(wrap, text);
    return wrap;
  }

  function markChanged(wrap, text) {
    wrap.classList.toggle('is-changed', !base.has(text));
  }

  function renumber() {
    paragraphs().forEach(function (p, i) {
      p.firstElementChild.textContent = String(i + 1);
      p.id = 'p' + (i + 1);
      textareaOf(p).setAttribute('aria-label', 'Absatz ' + (i + 1));
      p.dataset.n = String(i + 1);
    });
  }

  function recount(ta) {
    var n = countWords(ta.value);
    totalWords += n - (wordsOf.get(ta) || 0);
    wordsOf.set(ta, n);
  }

  function showWords() { wordsElement.textContent = totalWords.toLocaleString('de-DE'); }

  function build(list) {
    var fragment = document.createDocumentFragment();
    totalWords = 0;
    list.forEach(function (text) {
      var wrap = createParagraph(text);
      fragment.appendChild(wrap);
      recount(textareaOf(wrap));
    });
    parasElement.textContent = '';
    parasElement.appendChild(fragment);
    renumber();
    if (!sizeSupported) { paragraphs().forEach(function (p) { autosize(textareaOf(p)); }); }
    showWords();
  }

  function focusParagraph(wrap, position) {
    var ta = textareaOf(wrap);
    ta.focus();
    var at = position === 'end' ? ta.value.length : position;
    ta.setSelectionRange(at, at);
  }

  // ------------------------------------------------------------------------------------------------ undo and redo
  var undoStack = [];
  var redoStack = [];
  var lastSnapshot = null;
  var burstTimer = null;

  function same(a, b) {
    if (a.length !== b.length) { return false; }
    for (var i = 0; i < a.length; i += 1) { if (a[i] !== b[i]) { return false; } }
    return true;
  }

  /** Ends a burst of typing: what was there before becomes one step of the undo list. */
  function checkpoint() {
    clearTimeout(burstTimer);
    var now = snapshot();
    if (lastSnapshot && !same(lastSnapshot, now)) {
      undoStack.push(lastSnapshot);
      if (undoStack.length > 100) { undoStack.shift(); }
      redoStack = [];
    }
    lastSnapshot = now;
    updateUndoButtons();
  }

  function scheduleCheckpoint() {
    clearTimeout(burstTimer);
    burstTimer = setTimeout(checkpoint, 700);
  }

  function restore(list) {
    var current = snapshot();
    var first = 0;
    while (first < list.length && first < current.length && list[first] === current[first]) { first += 1; }
    build(list);
    lastSnapshot = list;
    markDirty();
    var wraps = paragraphs();
    if (wraps.length) { focusParagraph(wraps[Math.min(first, wraps.length - 1)], 'end'); }
    updateUndoButtons();
  }

  function undo() {
    checkpoint();
    if (undoStack.length === 0) { return; }
    redoStack.push(snapshot());
    restore(undoStack.pop());
  }

  function redo() {
    checkpoint();
    if (redoStack.length === 0) { return; }
    undoStack.push(snapshot());
    restore(redoStack.pop());
  }

  function updateUndoButtons() {
    var u = document.getElementById('editor-undo');
    var r = document.getElementById('editor-redo');
    if (u) { u.disabled = undoStack.length === 0 && !burstTimer; }
    if (r) { r.disabled = redoStack.length === 0; }
  }

  // ------------------------------------------------------------------------------------------------ saving
  var dirty = false;
  var saving = false;
  var saveTimer = null;
  var maxTimer = null;
  var pendingKind = null;
  var session = { added: 0, removed: 0, replaced: 0 };

  function setStatus(kind, text) {
    statusElement.className = 'kk-editor__status is-' + kind;
    statusElement.textContent = text;
  }

  function markDirty() {
    dirty = true;
    setStatus('dirty', 'Nicht gespeichert');
    if (data.autosaveSeconds > 0) {
      clearTimeout(saveTimer);
      saveTimer = setTimeout(function () { save('auto'); }, data.autosaveSeconds * 1000);
      if (!maxTimer) { maxTimer = setTimeout(function () { save('auto'); }, Math.max(60, data.autosaveSeconds * 4) * 1000); }
    }
  }

  function post(handler, body) {
    return fetch(location.pathname + '?id=' + data.documentId + '&handler=' + handler, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
      credentials: 'same-origin',
      body: JSON.stringify(body)
    }).then(function (response) {
      if (!response.ok) { throw new Error('HTTP ' + response.status); }
      return response.json();
    });
  }

  function save(kind, options) {
    var o = options || {};
    if (saving) { pendingKind = pendingKind === 'manual' ? 'manual' : kind; return Promise.resolve(); }
    if (kind === 'auto' && !dirty) { return Promise.resolve(); }
    checkpoint();
    clearTimeout(saveTimer);
    clearTimeout(maxTimer);
    maxTimer = null;
    saving = true;
    setStatus('saving', 'Speichert ...');
    var text = snapshot().join('\n');
    return post('Save', {
      text: text, kind: kind, label: o.label || null, comment: o.comment || null,
      activeSeconds: takeActive(), keystrokes: takeKeys(), baseVersion: baseVersion, force: !!o.force
    }).then(function (result) {
      saving = false;
      if (result.conflict) {
        setStatus('error', 'Konflikt: neuere Fassung von jemand anderem');
        return window.MatUi.confirm(result.message, { title: 'Neuere Fassung vorhanden', ok: 'Trotzdem speichern', danger: true }).then(function (yes) {
          if (yes) { return save(kind, { force: true, label: o.label, comment: o.comment }); }
          return null;
        });
      }
      baseVersion = result.versionNo;
      dirty = snapshot().join('\n') !== text;
      session.added += result.added; session.removed += result.removed; session.replaced += result.replaced;
      showSession();
      setStatus(dirty ? 'dirty' : 'saved', dirty ? 'Nicht gespeichert' : 'Gespeichert um ' + result.savedAt + ' (Fassung ' + result.versionNo + ')');
      if (kind === 'manual') { toast('Fassung ' + result.versionNo + ' ist gespeichert.'); }
      var versionLabel = document.getElementById('editor-version');
      if (versionLabel) { versionLabel.textContent = 'Fassung ' + result.versionNo; }
      if (dirty) { markDirty(); }
      return result;
    }).catch(function (error) {
      saving = false;
      setStatus('error', 'Speichern fehlgeschlagen (' + error.message + ')');
      if (kind === 'manual') { toast('Speichern fehlgeschlagen. Der Text bleibt in diesem Fenster.', 'error'); }
      // Time that was not reported goes into the next report.
      activeSeconds += lostActive; keys += lostKeys;
      return null;
    }).then(function (result) {
      if (pendingKind) { var next = pendingKind; pendingKind = null; return save(next); }
      return result;
    });
  }

  function showSession() {
    sessionElement.textContent = '+' + session.added + ' neu, ' + session.replaced + ' ersetzt, −' + session.removed + ' entfernt';
  }

  // ------------------------------------------------------------------------------------------------ working time
  var lastActivity = 0;
  var activeSeconds = 0;
  var keys = 0;
  var lostActive = 0;
  var lostKeys = 0;

  function touch() { lastActivity = Date.now(); }

  function takeActive() { lostActive = activeSeconds; activeSeconds = 0; return lostActive; }
  function takeKeys() { lostKeys = keys; keys = 0; return lostKeys; }

  setInterval(function () {
    if (document.visibilityState === 'visible' && Date.now() - lastActivity < 30000) { activeSeconds += 1; }
  }, 1000);

  setInterval(function () {
    if (!dirty && !saving && activeSeconds > 0) {
      post('Track', { activeSeconds: takeActive(), keystrokes: takeKeys() }).catch(function () { activeSeconds += lostActive; keys += lostKeys; });
    }
  }, 30000);

  ['keydown', 'pointerdown', 'wheel', 'touchstart'].forEach(function (name) {
    document.addEventListener(name, function () { touch(); }, { passive: true, capture: true });
  });
  document.addEventListener('keydown', function () { keys += 1; }, true);

  // ------------------------------------------------------------------------------------------------ editing
  parasElement.addEventListener('input', function (event) {
    var ta = event.target;
    if (!ta || ta.tagName !== 'TEXTAREA') { return; }
    touch();
    autosize(ta);
    recount(ta);
    showWords();
    markChanged(ta.parentElement, fromDisplay(ta.value));
    markDirty();
    scheduleCheckpoint();
  });

  function split(wrap, ta) {
    checkpoint();
    var start = ta.selectionStart;
    var end = ta.selectionEnd;
    var before = ta.value.slice(0, start);
    var after = ta.value.slice(end);
    ta.value = before;
    var next = createParagraph(fromDisplay(after));
    wrap.after(next);
    textareaOf(next).value = after;
    recount(ta);
    recount(textareaOf(next));
    markChanged(wrap, fromDisplay(before));
    markChanged(next, fromDisplay(after));
    autosize(ta); autosize(textareaOf(next));
    renumber();
    showWords();
    markDirty();
    focusParagraph(next, 0);
    checkpoint();
  }

  function joinWithPrevious(wrap, ta) {
    var previous = wrap.previousElementSibling;
    if (!previous) { return; }
    checkpoint();
    var pta = textareaOf(previous);
    var at = pta.value.length;
    pta.value += ta.value;
    totalWords -= wordsOf.get(ta) || 0;
    wordsOf.set(ta, 0);
    recount(pta);
    wrap.remove();
    markChanged(previous, fromDisplay(pta.value));
    autosize(pta);
    renumber();
    showWords();
    markDirty();
    focusParagraph(previous, at);
    checkpoint();
  }

  function joinWithNext(wrap, ta) {
    var next = wrap.nextElementSibling;
    if (!next) { return; }
    checkpoint();
    var at = ta.value.length;
    var nta = textareaOf(next);
    ta.value += nta.value;
    totalWords -= wordsOf.get(nta) || 0;
    wordsOf.set(nta, 0);
    recount(ta);
    next.remove();
    markChanged(wrap, fromDisplay(ta.value));
    autosize(ta);
    renumber();
    showWords();
    markDirty();
    focusParagraph(wrap, at);
    checkpoint();
  }

  parasElement.addEventListener('keydown', function (event) {
    var ta = event.target;
    if (!ta || ta.tagName !== 'TEXTAREA') { return; }
    var wrap = ta.parentElement;
    var plain = !event.ctrlKey && !event.metaKey && !event.altKey;

    if (event.key === 'Enter' && plain && !event.shiftKey && !event.isComposing) {
      event.preventDefault();
      split(wrap, ta);
    } else if (event.key === 'Backspace' && plain && ta.selectionStart === 0 && ta.selectionEnd === 0 && wrap.previousElementSibling) {
      event.preventDefault();
      joinWithPrevious(wrap, ta);
    } else if (event.key === 'Delete' && plain && ta.selectionStart === ta.value.length && ta.selectionEnd === ta.value.length && wrap.nextElementSibling) {
      event.preventDefault();
      joinWithNext(wrap, ta);
    } else if (event.key === 'ArrowUp' && plain && !event.shiftKey && ta.selectionStart === 0 && wrap.previousElementSibling) {
      event.preventDefault();
      focusParagraph(wrap.previousElementSibling, 'end');
    } else if (event.key === 'ArrowDown' && plain && !event.shiftKey && ta.selectionStart === ta.value.length && wrap.nextElementSibling) {
      event.preventDefault();
      focusParagraph(wrap.nextElementSibling, 0);
    }
  });

  // A pasted text with line breaks becomes paragraphs, as in a word processor.
  parasElement.addEventListener('paste', function (event) {
    var ta = event.target;
    if (!ta || ta.tagName !== 'TEXTAREA') { return; }
    var text = (event.clipboardData || window.clipboardData).getData('text');
    if (!text || !/[\r\n]/.test(text)) { return; }
    event.preventDefault();
    checkpoint();
    var parts = text.replace(/\r\n?/g, '\n').split(/\n+/).filter(function (p, i, all) { return p.length > 0 || i === 0 || i === all.length - 1; });
    var wrap = ta.parentElement;
    var before = ta.value.slice(0, ta.selectionStart);
    var after = ta.value.slice(ta.selectionEnd);
    ta.value = before + parts[0];
    recount(ta);
    var last = wrap;
    for (var i = 1; i < parts.length; i += 1) {
      var tail = i === parts.length - 1 ? after : '';
      var next = createParagraph(parts[i] + tail);
      last.after(next);
      recount(textareaOf(next));
      markChanged(next, fromDisplay(parts[i] + tail));
      last = next;
    }
    if (parts.length === 1) { ta.value += after; recount(ta); }
    markChanged(wrap, fromDisplay(ta.value));
    paragraphs().forEach(function (p) { autosize(textareaOf(p)); });
    renumber();
    showWords();
    markDirty();
    focusParagraph(last, parts.length === 1 ? before.length + parts[0].length : parts[parts.length - 1].length);
    checkpoint();
  });

  document.addEventListener('keydown', function (event) {
    var mod = event.ctrlKey || event.metaKey;
    if (!mod) { return; }
    var key = event.key.toLowerCase();
    if (key === 's') { event.preventDefault(); save('manual'); }
    else if (key === 'z' && !event.shiftKey) { event.preventDefault(); undo(); }
    else if (key === 'y' || (key === 'z' && event.shiftKey)) { event.preventDefault(); redo(); }
    else if (key === 'h') { event.preventDefault(); openReplace(); }
  });

  // ------------------------------------------------------------------------------------------------ search and replace
  var replaceDialog = document.getElementById('editor-replace');
  var findInput = document.getElementById('replace-find');
  var withInput = document.getElementById('replace-with');
  var caseInput = document.getElementById('replace-case');
  var wholeInput = document.getElementById('replace-whole');
  var replaceInfo = document.getElementById('replace-info');

  function pattern() {
    var term = findInput.value;
    if (!term) { return null; }
    var escaped = term.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    if (wholeInput.checked) { escaped = '(?<![\\p{L}\\p{N}])' + escaped + '(?![\\p{L}\\p{N}])'; }
    return new RegExp(escaped, 'gu' + (caseInput.checked ? '' : 'i'));
  }

  function countMatches() {
    var re = pattern();
    if (!re) { replaceInfo.textContent = ''; return 0; }
    var n = 0;
    paragraphs().forEach(function (p) { var m = textareaOf(p).value.match(re); if (m) { n += m.length; } });
    replaceInfo.textContent = n === 0 ? 'Keine Treffer' : n + (n === 1 ? ' Treffer' : ' Treffer');
    return n;
  }

  function openReplace() {
    var selection = window.getSelection ? String(window.getSelection()) : '';
    var active = document.activeElement;
    if (active && active.tagName === 'TEXTAREA' && active.selectionEnd > active.selectionStart) { selection = active.value.slice(active.selectionStart, active.selectionEnd); }
    if (selection && selection.length < 80) { findInput.value = selection; }
    window.MatUi.dialog.open(replaceDialog);
    findInput.focus();
    findInput.select();
    countMatches();
  }

  function findNext(fromCurrent) {
    var re = pattern();
    if (!re) { return false; }
    var list = paragraphs();
    var active = document.activeElement;
    var startIndex = active && active.tagName === 'TEXTAREA' ? list.indexOf(active.parentElement) : 0;
    var startAt = active && active.tagName === 'TEXTAREA' ? (fromCurrent ? active.selectionEnd : active.selectionStart) : 0;
    for (var step = 0; step <= list.length; step += 1) {
      var i = (startIndex + step) % list.length;
      var ta = textareaOf(list[i]);
      re.lastIndex = step === 0 ? startAt : 0;
      var m = re.exec(ta.value);
      if (m) {
        ta.focus();
        ta.setSelectionRange(m.index, m.index + m[0].length);
        list[i].scrollIntoView({ block: 'center' });
        return true;
      }
    }
    return false;
  }

  function replaceCurrent() {
    var ta = document.activeElement;
    var re = pattern();
    if (re && ta && ta.tagName === 'TEXTAREA' && ta.selectionEnd > ta.selectionStart) {
      var selected = ta.value.slice(ta.selectionStart, ta.selectionEnd);
      re.lastIndex = 0;
      var m = re.exec(selected);
      if (m && m[0].length === selected.length) {
        checkpoint();
        ta.setRangeText(withInput.value, ta.selectionStart, ta.selectionEnd, 'end');
        ta.dispatchEvent(new Event('input', { bubbles: true }));
        checkpoint();
      }
    }
    if (!findNext(true)) { replaceInfo.textContent = 'Keine weiteren Treffer'; }
  }

  function replaceAll() {
    var re = pattern();
    if (!re) { return; }
    checkpoint();
    var count = 0;
    paragraphs().forEach(function (p) {
      var ta = textareaOf(p);
      var replaced = ta.value.replace(re, function () { count += 1; return withInput.value; });
      if (replaced !== ta.value) {
        ta.value = replaced;
        recount(ta);
        markChanged(p, fromDisplay(replaced));
        autosize(ta);
      }
    });
    showWords();
    if (count > 0) { markDirty(); checkpoint(); }
    replaceInfo.textContent = count + (count === 1 ? ' Ersetzung' : ' Ersetzungen');
  }

  if (replaceDialog) {
    findInput.addEventListener('input', countMatches);
    caseInput.addEventListener('change', countMatches);
    wholeInput.addEventListener('change', countMatches);
    document.getElementById('replace-next').addEventListener('click', function () { if (!findNext(true)) { replaceInfo.textContent = 'Keine Treffer'; } });
    document.getElementById('replace-one').addEventListener('click', replaceCurrent);
    document.getElementById('replace-all').addEventListener('click', replaceAll);
  }

  // ------------------------------------------------------------------------------------------------ comments and the style sheet
  var side = document.getElementById('editor-side');
  var commentList = document.getElementById('comment-list');
  var commentDialog = document.getElementById('editor-comment');
  var commentText = document.getElementById('comment-text');
  var commentFor = document.getElementById('comment-for');
  var commentTarget = null;

  function relocate(c) {
    var list = paragraphs();
    var i = Math.min(c.paragraphIndex, list.length - 1);
    if (c.quote && list[i] && textareaOf(list[i]).value.indexOf(c.quote) < 0) {
      for (var j = 0; j < list.length; j += 1) { if (textareaOf(list[j]).value.indexOf(c.quote) >= 0) { return j; } }
    }
    return i;
  }

  function renderComments() {
    if (!commentList) { return; }
    commentList.textContent = '';
    var open = data.comments.filter(function (c) { return !c.isResolved; });
    document.getElementById('comment-count').textContent = String(open.length);
    paragraphs().forEach(function (p) { p.classList.remove('has-comment'); });
    data.comments.forEach(function (c) {
      var index = relocate(c);
      c.at = index;
      var wrap = paragraphs()[index];
      if (wrap && !c.isResolved) { wrap.classList.add('has-comment'); }
    });
    if (open.length === 0) {
      var empty = el('p', 'mat-muted');
      empty.textContent = 'Keine offenen Kommentare.';
      commentList.appendChild(empty);
    }
    open.forEach(function (c) {
      var item = el('li', 'kk-comment');
      var head = el('div', 'kk-comment__head');
      var link = el('a');
      link.href = '#p' + (c.at + 1);
      link.textContent = 'Absatz ' + (c.at + 1);
      link.addEventListener('click', function (e) { e.preventDefault(); var w = paragraphs()[c.at]; if (w) { w.scrollIntoView({ block: 'center' }); focusParagraph(w, 'end'); } });
      var done = el('button', 'mat-btn mat-btn--ghost mat-btn--sm');
      done.type = 'button';
      done.innerHTML = icon('check') + '<span>Erledigt</span>';
      done.addEventListener('click', function () {
        fetch(location.pathname + '?id=' + data.documentId + '&handler=Resolve&commentId=' + c.id + '&resolved=true', { method: 'POST', headers: { 'RequestVerificationToken': token }, credentials: 'same-origin' })
          .then(function (r) { if (r.ok) { c.isResolved = true; renderComments(); } });
      });
      head.appendChild(link);
      head.appendChild(done);
      var body = el('p');
      body.textContent = c.text;
      var meta = el('small', 'mat-muted');
      meta.textContent = (c.quote ? '„' + c.quote + '“ · ' : '') + c.author + ' · ' + c.created;
      item.appendChild(head); item.appendChild(body); item.appendChild(meta);
      commentList.appendChild(item);
    });
  }

  parasElement.addEventListener('click', function (event) {
    var button = event.target.closest ? event.target.closest('.kk-p__comment') : null;
    if (!button) { return; }
    commentTarget = button.parentElement;
    var ta = textareaOf(commentTarget);
    var quote = ta.selectionEnd > ta.selectionStart ? ta.value.slice(ta.selectionStart, ta.selectionEnd) : ta.value.slice(0, 60);
    commentDialog.setAttribute('data-quote', quote);
    commentFor.textContent = 'Absatz ' + commentTarget.dataset.n + ': „' + quote.slice(0, 60) + (quote.length > 60 ? '…' : '') + '“';
    commentText.value = '';
    window.MatUi.dialog.open(commentDialog);
    commentText.focus();
  });

  if (commentDialog) {
    document.getElementById('comment-save').addEventListener('click', function () {
      var text = commentText.value.trim();
      if (!text || !commentTarget) { return; }
      var index = paragraphs().indexOf(commentTarget);
      post('Comment', { paragraphIndex: index, quote: commentDialog.getAttribute('data-quote'), text: text }).then(function (c) {
        data.comments.push({ id: c.id, paragraphIndex: c.paragraphIndex, quote: c.quote, text: c.text, author: c.author, isResolved: false, created: c.created });
        renderComments();
        window.MatUi.dialog.close(commentDialog);
        toast('Der Kommentar ist gespeichert.');
        if (side) { side.hidden = false; host.classList.add('has-side'); }
      }).catch(function () { toast('Der Kommentar konnte nicht gespeichert werden.', 'error'); });
    });
  }

  var toggle = document.getElementById('editor-side-toggle');
  if (toggle && side) {
    toggle.addEventListener('click', function () {
      side.hidden = !side.hidden;
      host.classList.toggle('has-side', !side.hidden);
      toggle.setAttribute('aria-pressed', side.hidden ? 'false' : 'true');
    });
  }

  var styleList = document.getElementById('style-list');
  if (styleList) {
    if (data.styles.length === 0) {
      var none = el('p', 'mat-muted');
      none.textContent = 'Das Stilblatt dieses Projekts ist noch leer.';
      styleList.appendChild(none);
    }
    data.styles.forEach(function (s) {
      var item = el('li', 'kk-comment');
      var strong = el('strong');
      strong.textContent = s.term;
      var small = el('small', 'mat-muted');
      small.textContent = s.category + (s.decision ? ' · ' + s.decision : '');
      item.appendChild(strong);
      item.appendChild(el('br'));
      item.appendChild(small);
      styleList.appendChild(item);
    });
  }

  // ------------------------------------------------------------------------------------------------ the bar
  function wire(id, handler) { var b = document.getElementById(id); if (b) { b.addEventListener('click', handler); } }
  wire('editor-save', function () { save('manual'); });
  wire('editor-name', function () {
    window.MatUi.prompt('Name der Fassung (zum Beispiel „Nach dem ersten Durchgang“)', { title: 'Fassung benennen', ok: 'Speichern' }).then(function (label) {
      if (label !== null) { save('manual', { label: label }); }
    });
  });
  wire('editor-undo', undo);
  wire('editor-redo', redo);
  wire('editor-find', openReplace);

  window.addEventListener('beforeunload', function (event) {
    if (dirty) { event.preventDefault(); event.returnValue = ''; }
  });

  document.addEventListener('visibilitychange', function () {
    if (document.visibilityState === 'hidden' && dirty) { save('auto'); }
  });

  // ------------------------------------------------------------------------------------------------ start
  build(data.paragraphs);
  lastSnapshot = snapshot();
  renderComments();
  setStatus('saved', 'Fassung ' + data.versionNo + ' geöffnet');
  showSession();
  updateUndoButtons();
  if (location.hash && /^#p\d+$/.test(location.hash)) {
    var target = document.getElementById(location.hash.slice(1));
    if (target) { target.scrollIntoView({ block: 'center' }); focusParagraph(target, 0); }
  }
})();

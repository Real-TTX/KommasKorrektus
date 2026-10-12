/* The editor of KommasKorrektus: the text of a document, written in one of two views.
   - Text: one field per paragraph, nothing but the words (this file).
   - Seite: the page with the formatting of the original Word file, written in directly (editor-page.js).
   Both work on the same text; the formatting is never changed, it stays in the original and comes back in the export.
   Everything else is shared and lives here:
   - A version is a pass over the text, not a click: saving (Ctrl+S, or the autosave while somebody types) always writes into the version being
     worked on; "Durchgang abschließen" closes it, and the next change then begins a new version.
   - The editor reports how long it was used (seconds with activity, keys pressed); a long pause is a pause, the server cuts the time there.
   - Search and replace, remarks on paragraphs, the style sheet.
   A view is an object with the same few functions (see "the views"); editor-page.js registers the second one.
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
  var views = {};       // name -> view
  var view = null;      // the view that is shown

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
  function icon(name) { return '<svg class="mat-icon" aria-hidden="true" focusable="false"><use href="#mat-i-' + name + '"></use></svg>'; }
  function toast(message, tone) { if (window.MatUi) { window.MatUi.toast(message, { tone: tone }); } }
  function showWordCount(n) { wordsElement.textContent = n.toLocaleString('de-DE'); }

  /** The text of the document as it is now: the paragraphs of the view that is shown. */
  function snapshot() { return view.texts(); }

  // ================================================================================================ the text view
  // One field per paragraph. Enter splits a paragraph, Backspace at the start joins it with the one before, Shift+Enter is a line break inside a paragraph.
  // Undo and redo work across paragraphs (the fields of the browser only know their own text).

  function textareaOf(p) { return p.firstElementChild.nextElementSibling; }
  function paragraphs() { return Array.prototype.slice.call(parasElement.children); }
  function textSnapshot() { return paragraphs().map(function (p) { return fromDisplay(textareaOf(p).value); }); }

  function autosize(ta) {
    if (sizeSupported) { return; }
    ta.style.height = 'auto';
    ta.style.height = ta.scrollHeight + 'px';
  }

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

  function showWords() { showWordCount(totalWords); }

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

  // ---- undo and redo
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
    var now = textSnapshot();
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
    var current = textSnapshot();
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
    redoStack.push(textSnapshot());
    restore(undoStack.pop());
  }

  function redo() {
    checkpoint();
    if (redoStack.length === 0) { return; }
    undoStack.push(textSnapshot());
    restore(redoStack.pop());
  }

  function updateUndoButtons() {
    var u = document.getElementById('editor-undo');
    var r = document.getElementById('editor-redo');
    if (!view || view.name !== 'text') { if (u) { u.disabled = false; } if (r) { r.disabled = false; } return; }
    if (u) { u.disabled = undoStack.length === 0 && !burstTimer; }
    if (r) { r.disabled = redoStack.length === 0; }
  }

  // ---- editing
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

  parasElement.addEventListener('click', function (event) {
    var button = event.target.closest ? event.target.closest('.kk-p__comment') : null;
    if (!button) { return; }
    var wrap = button.parentElement;
    var ta = textareaOf(wrap);
    var quote = ta.selectionEnd > ta.selectionStart ? ta.value.slice(ta.selectionStart, ta.selectionEnd) : ta.value.slice(0, 60);
    openComment(paragraphs().indexOf(wrap), quote);
  });

  // The field the person was last in: a button of the find panel takes the focus, but the selection in the field stays.
  var lastField = null;
  parasElement.addEventListener('focusin', function (event) { if (event.target.tagName === 'TEXTAREA') { lastField = event.target; } });

  function currentField() {
    var active = document.activeElement;
    if (active && active.tagName === 'TEXTAREA' && parasElement.contains(active)) { return active; }
    return lastField && parasElement.contains(lastField) ? lastField : null;
  }

  var textView = {
    name: 'text',
    prepare: function (texts) { return texts; },
    mount: function (texts, anchor) {
      parasElement.className = 'kk-editor__paper';
      build(texts);
      undoStack = [];
      redoStack = [];
      lastSnapshot = textSnapshot();
      if (anchor) {
        var wraps = paragraphs();
        var target = wraps[Math.min(anchor.index, wraps.length - 1)];
        if (target) {
          if (anchor.focus) { focusParagraph(target, Math.min(anchor.offset || 0, textareaOf(target).value.length)); }
          target.scrollIntoView({ block: anchor.focus ? 'center' : 'start' });
        }
      }
    },
    unmount: function () { clearTimeout(burstTimer); burstTimer = null; parasElement.textContent = ''; },
    texts: textSnapshot,
    count: function () { return parasElement.children.length; },
    textAt: function (i) { var p = parasElement.children[i]; return p ? textareaOf(p).value : ''; },
    element: function (i) { return parasElement.children[i]; },
    focusParagraph: function (i, position) { var p = parasElement.children[i]; if (p) { focusParagraph(p, position); } },
    /** Where the person is: the paragraph, or the first one on the screen when nothing has the focus. */
    anchor: function () {
      var active = document.activeElement;
      var list = paragraphs();
      if (active && active.tagName === 'TEXTAREA' && parasElement.contains(active)) {
        return { index: list.indexOf(active.parentElement), offset: active.selectionStart, focus: true };
      }
      for (var i = 0; i < list.length; i += 1) { if (list[i].getBoundingClientRect().bottom > 140) { return { index: i, offset: 0, focus: false }; } }
      return { index: 0, offset: 0, focus: false };
    },
    activePosition: function () {
      var field = currentField();
      if (!field) { return null; }
      return { index: paragraphs().indexOf(field.parentElement), start: field.selectionStart, end: field.selectionEnd };
    },
    select: function (i, start, end) {
      var p = parasElement.children[i];
      if (!p) { return; }
      var ta = textareaOf(p);
      ta.focus();
      ta.setSelectionRange(start, end);
      p.scrollIntoView({ block: 'center' });
    },
    selectedText: function () {
      var field = currentField();
      return field && field.selectionEnd > field.selectionStart ? field.value.slice(field.selectionStart, field.selectionEnd) : '';
    },
    replaceSelection: function (text) {
      var ta = currentField();
      if (!ta) { return; }
      ta.focus();
      checkpoint();
      ta.setRangeText(text, ta.selectionStart, ta.selectionEnd, 'end');
      ta.dispatchEvent(new Event('input', { bubbles: true }));
      checkpoint();
    },
    replaceAll: function (re, text) {
      checkpoint();
      var count = 0;
      paragraphs().forEach(function (p) {
        var ta = textareaOf(p);
        var replaced = ta.value.replace(re, function () { count += 1; return text; });
        if (replaced !== ta.value) {
          ta.value = replaced;
          recount(ta);
          markChanged(p, fromDisplay(replaced));
          autosize(ta);
        }
      });
      showWords();
      if (count > 0) { checkpoint(); }
      return count;
    },
    setCommentMarks: function (marked) {
      paragraphs().forEach(function (p, i) { p.classList.toggle('has-comment', marked.has(i)); });
    },
    undo: undo,
    redo: redo,
    keys: true
  };

  // ================================================================================================ the views
  // A view has: name; prepare(texts) (may be asynchronous: gets what it needs before anything on the screen changes); mount(prepared, anchor); unmount();
  // texts() (the paragraphs as they are now, with U+2028 for a line break inside one); count(); textAt(i) (the paragraph for searching: line break as \n);
  // element(i); focusParagraph(i, 'end' | number); anchor() ({index, offset, focus}); activePosition() ({index, start, end} or null); select(i, start, end);
  // selectedText(); replaceSelection(text); replaceAll(regexp, text) (the number of replacements); setCommentMarks(set of indexes); undo(); redo();
  // keys (true when the view takes Ctrl+Z / Ctrl+Y itself; false when the browser does).
  views.text = textView;

  var VIEW_KEY = 'kk.editor.view';

  // The view is kept with the account (the server says which, so another browser opens the same way); the browser's own memory is the fallback.
  var rememberedView = data.view || null;

  function storedView() {
    if (data.view) { return data.view; }
    try { return window.localStorage.getItem(VIEW_KEY); } catch (e) { return null; }
  }

  function rememberView(name) {
    try { window.localStorage.setItem(VIEW_KEY, name); } catch (e) { /* private mode: the view is simply not remembered */ }
    if (name === rememberedView) { return; }
    rememberedView = name;
    post('View', { view: name }).catch(function () { rememberedView = null; });
  }

  function showView(name) {
    Array.prototype.forEach.call(host.querySelectorAll('[data-view]'), function (button) {
      button.setAttribute('aria-pressed', button.getAttribute('data-view') === name ? 'true' : 'false');
    });
    host.setAttribute('data-view', name);
    host.classList.toggle('is-page', name === 'page');
  }

  /** Changes to another view. The text goes along: what is typed and not yet saved is on the other page too. */
  function setView(name, texts) {
    var next = views[name];
    if (!next || next === view) { return Promise.resolve(true); }
    parasElement.setAttribute('aria-busy', 'true');
    host.classList.add('is-busy');
    return Promise.resolve(next.prepare(texts || snapshot())).then(function (prepared) {
      var anchor = view ? view.anchor() : null;
      if (view) { view.unmount(); }
      view = next;
      next.mount(prepared, anchor);
      showView(name);
      rememberView(name);
      renderComments();
      updateUndoButtons();
      return true;
    }).catch(function (error) {
      toast(error && error.message ? error.message : 'Die Ansicht lässt sich nicht aufbauen.', 'error');
      return false;
    }).then(function (ok) {
      parasElement.removeAttribute('aria-busy');
      host.classList.remove('is-busy');
      return ok;
    });
  }

  // ================================================================================================ saving
  var dirty = false;
  var saving = false;
  var saveTimer = null;
  var maxTimer = null;
  var pendingKind = null;
  var pendingOptions = null;
  var RANK = { auto: 0, manual: 1, close: 2 };
  var session = { added: 0, removed: 0, replaced: 0 };

  // "working": saves go into this version; "closed": the next change begins a new one; "fixed": the original or a restored one.
  function stateWord(state) { return state === 'working' ? 'in Arbeit' : state === 'closed' ? 'abgeschlossen' : ''; }

  function setStatus(kind, text) {
    statusElement.className = 'kk-editor__status is-' + kind;
    statusElement.textContent = text;
  }

  var autosaves = data.autosaveSeconds > 0;
  function dirtyText() { return autosaves ? 'Wird automatisch gespeichert …' : 'Nicht gespeichert'; }

  // The text as the server has it, and since when the window holds something it has not: the threshold of the autosave measures against them.
  var savedText = data.paragraphs.join('\n');
  var dirtySince = 0;
  var PATIENCE_MS = 60000;      // a change below the threshold is saved anyway after this long
  var BIG_DELETION = 150;       // an autosave does not take away more than this many characters ...
  var BIG_SHARE = 0.3;          // ... and more than this share of the text without being asked: that is saved by hand

  /** How much differs from the saved text: what lies between the common beginning and the common end, as characters taken away and put in. */
  function difference(now) {
    var a = savedText;
    var n = Math.min(a.length, now.length);
    var start = 0;
    while (start < n && a.charCodeAt(start) === now.charCodeAt(start)) { start += 1; }
    var end = 0;
    while (end < n - start && a.charCodeAt(a.length - 1 - end) === now.charCodeAt(now.length - 1 - end)) { end += 1; }
    return { removed: a.length - start - end, added: now.length - start - end };
  }

  // Saving goes into the version being worked on, so it can be done often: a few seconds after the last keystroke, and at the latest
  // every few seconds' worth more while somebody types without a pause. What is saved on its own has a threshold (see autoTick).
  function markDirty() {
    if (!dirty) { dirtySince = Date.now(); }
    dirty = true;
    setStatus('dirty', dirtyText());
    if (autosaves) {
      clearTimeout(saveTimer);
      saveTimer = setTimeout(autoTick, data.autosaveSeconds * 1000);
      if (!maxTimer) { maxTimer = setTimeout(autoTick, Math.max(10, data.autosaveSeconds * 5) * 1000); }
    }
  }

  /**
   * The autosave is due. It saves when enough has changed (the threshold), or when a small change has waited long enough. It does not take a large part
   * of the text away on its own (somebody who selected everything and pressed Delete would find it saved): that waits for the save by hand, or for
   * leaving the page, which always saves.
   */
  function autoTick() {
    maxTimer = null;
    if (!dirty || saving) { return; }
    var diff = difference(snapshot().join('\n'));
    if (diff.removed > BIG_DELETION && diff.removed > savedText.length * BIG_SHARE) {
      setStatus('dirty', 'Viel Text gelöscht: nicht automatisch gespeichert (Strg+S speichert)');
      return;
    }
    if (Math.max(diff.removed, diff.added) >= (data.autosaveMinChars || 0) || Date.now() - dirtySince >= PATIENCE_MS) { save('auto'); return; }
    setStatus('dirty', 'Noch nicht gespeichert: erst bei mehr Änderungen (oder nach einer Minute)');
    clearTimeout(saveTimer);
    saveTimer = setTimeout(autoTick, 10000);
  }

  // keepalive lets a save that is sent while the page is closing finish (the browser allows 64 KB for it).
  function post(handler, body, keepalive) {
    var json = JSON.stringify(body);
    return fetch(location.pathname + '?id=' + data.documentId + '&handler=' + handler, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
      credentials: 'same-origin',
      keepalive: !!keepalive && json.length < 60000,
      body: json
    }).then(function (response) {
      if (!response.ok) { throw new Error('HTTP ' + response.status); }
      return response.json();
    });
  }

  function save(kind, options) {
    var o = options || {};
    if (saving) {
      // What is asked while a save runs follows it; closing the pass wins over saving, saving over the autosave.
      if (!pendingKind || RANK[kind] >= RANK[pendingKind]) { pendingKind = kind; pendingOptions = o; }
      return Promise.resolve();
    }
    if (kind === 'auto' && !dirty) { return Promise.resolve(); }
    if (view.checkpoint) { view.checkpoint(); } else if (view.name === 'text') { checkpoint(); }
    clearTimeout(saveTimer);
    clearTimeout(maxTimer);
    maxTimer = null;
    saving = true;
    setStatus('saving', 'Speichert ...');
    var text = snapshot().join('\n');
    return post('Save', {
      text: text, kind: kind, label: o.label || null, comment: o.comment || null,
      activeSeconds: takeActive(), keystrokes: takeKeys(), baseVersion: baseVersion, force: !!o.force
    }, o.leaving).then(function (result) {
      saving = false;
      if (result.conflict) {
        setStatus('error', 'Konflikt: neuere Fassung von jemand anderem');
        return window.MatUi.confirm(result.message, { title: 'Neuere Fassung vorhanden', ok: 'Trotzdem speichern', danger: true }).then(function (yes) {
          if (yes) { return save(kind, { force: true, label: o.label, comment: o.comment }); }
          return null;
        });
      }
      baseVersion = result.versionNo;
      savedText = text;
      dirty = snapshot().join('\n') !== text;
      if (dirty) { dirtySince = Date.now(); }
      session.added += result.added; session.removed += result.removed; session.replaced += result.replaced;
      showSession();
      var word = stateWord(result.state);
      setStatus(dirty ? 'dirty' : 'saved', dirty ? dirtyText() : (kind === 'auto' ? 'Automatisch gespeichert um ' : 'Gespeichert um ') + result.savedAt + ' (Fassung ' + result.versionNo + (word ? ', ' + word : '') + ')');
      if (kind === 'close') { toast('Fassung ' + result.versionNo + ' ist abgeschlossen. Die nächste Änderung beginnt Fassung ' + (result.versionNo + 1) + '.'); }
      else if (kind === 'manual') { toast('Gespeichert. Das bleibt Fassung ' + result.versionNo + (word ? ' (' + word + ')' : '') + '.'); }
      if (refPane && !refPane.hidden) { clearTimeout(refTimer); refTimer = setTimeout(refLoad, 600); }
      var versionLabel = document.getElementById('editor-version');
      if (versionLabel) { versionLabel.textContent = 'Fassung ' + result.versionNo + (result.name ? ' · ' + result.name : '') + (word ? ' · ' + word : ''); }
      if (dirty) { markDirty(); }
      return result;
    }).catch(function (error) {
      saving = false;
      setStatus('error', 'Speichern fehlgeschlagen (' + error.message + ')');
      if (kind !== 'auto') { toast('Speichern fehlgeschlagen. Der Text bleibt in diesem Fenster.', 'error'); }
      // Time that was not reported goes into the next report.
      activeSeconds += lostActive; keys += lostKeys;
      // A save that did not get through (the connection was gone for a moment) is tried again; the text stays here meanwhile.
      if (autosaves) { clearTimeout(saveTimer); saveTimer = setTimeout(autoTick, 10000); }
      return null;
    }).then(function (result) {
      if (pendingKind) { var next = pendingKind, nextOptions = pendingOptions; pendingKind = null; pendingOptions = null; return save(next, nextOptions); }
      return result;
    });
  }

  function showSession() {
    sessionElement.textContent = '+' + session.added + ' neu, ' + session.replaced + ' ersetzt, −' + session.removed + ' entfernt';
  }

  // ================================================================================================ working time
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

  document.addEventListener('keydown', function (event) {
    var mod = event.ctrlKey || event.metaKey;
    if (!mod) { return; }
    var key = event.key.toLowerCase();
    if (key === 's') { event.preventDefault(); save('manual'); }
    else if (key === 'h') { event.preventDefault(); openReplace(); }
    else if (view && view.keys && key === 'z' && !event.shiftKey) { event.preventDefault(); view.undo(); }
    else if (view && view.keys && (key === 'y' || (key === 'z' && event.shiftKey))) { event.preventDefault(); view.redo(); }
  });

  // The shortcuts of the editor are Alt + a letter: Ctrl + Alt is AltGr on a German keyboard (it types @, €, { ...), and Alt alone is free in the
  // browsers. They go by the position of the key (code), so that they work with any layout. The list is in the dialog "Tastenkürzel".
  var HOTKEYS = {
    KeyK: function () { commentHere(); },
    KeyL: function () { toggleSide(); },
    KeyA: function () { closePass(); },
    KeyW: function () { switchView(); },
    KeyX: function () { var b = document.getElementById('editor-export'); if (b) { b.click(); } },
    KeyV: function () { toggleMaximize(); },
    KeyB: function () { toggleFullscreen(); },
    KeyR: function () { toggleReference(); },
    KeyS: function () { openStyle(); },
    KeyH: function () { openKeys(); },
    ArrowDown: function () { jumpComment(1); },
    ArrowUp: function () { jumpComment(-1); }
  };

  function modalOpen() {
    return Array.prototype.some.call(document.querySelectorAll('dialog[open]'), function (d) { return d.matches(':modal'); });
  }

  document.addEventListener('keydown', function (event) {
    if (!view || event.isComposing || modalOpen()) { return; }
    if (event.altKey && !event.ctrlKey && !event.metaKey && !event.shiftKey && HOTKEYS[event.code]) {
      event.preventDefault();
      HOTKEYS[event.code]();
    } else if (event.key === 'F3' && !event.altKey && !event.ctrlKey && !event.metaKey) {
      event.preventDefault();
      findAgain(event.shiftKey);
    } else if (event.key === 'Escape' && !event.defaultPrevented && document.documentElement.classList.contains('kk-focus') && !(findBar && !findBar.hidden)) {
      // Esc ends the maximizing (the search bar and the dialogs have their own Esc; the browser's full screen ends on its own).
      setMaximized(false);
    }
  });

  // ================================================================================================ search and replace
  var findBar = document.getElementById('editor-find-bar');
  var findButton = document.getElementById('editor-find');
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
    for (var i = 0; i < view.count(); i += 1) { var m = view.textAt(i).match(re); if (m) { n += m.length; } }
    replaceInfo.textContent = n === 0 ? 'Keine Treffer' : n + ' Treffer';
    return n;
  }

  // The search is a bar above the text, not a dialog: the text stays in reach, and replacing in the page view needs the focus on the page.
  function openReplace() {
    var selection = view.selectedText() || (window.getSelection ? String(window.getSelection()) : '');
    if (selection && selection.length < 80 && selection.indexOf('\n') < 0) { findInput.value = selection; }
    findBar.hidden = false;
    if (findButton) { findButton.setAttribute('aria-pressed', 'true'); }
    findInput.focus();
    findInput.select();
    countMatches();
  }

  /** Closes the bar and puts the cursor back where it was in the text. */
  function closeFind() {
    var active = view.activePosition();
    findBar.hidden = true;
    if (findButton) { findButton.setAttribute('aria-pressed', 'false'); }
    if (active) { view.select(active.index, active.start, active.end); }
  }

  function toggleFind() { if (findBar.hidden) { openReplace(); } else { closeFind(); } }

  function findNext(fromCurrent) {
    var re = pattern();
    if (!re) { return false; }
    var n = view.count();
    var active = view.activePosition();
    var startIndex = active ? active.index : 0;
    var startAt = active ? (fromCurrent ? active.end : active.start) : 0;
    for (var step = 0; step <= n; step += 1) {
      var i = (startIndex + step) % n;
      re.lastIndex = step === 0 ? startAt : 0;
      var m = re.exec(view.textAt(i));
      if (m) {
        view.select(i, m.index, m.index + m[0].length);
        return true;
      }
    }
    return false;
  }

  function replaceCurrent() {
    var re = pattern();
    var position = view.activePosition();
    if (re && position && position.end > position.start) {
      var selected = view.textAt(position.index).slice(position.start, position.end);
      re.lastIndex = 0;
      var m = re.exec(selected);
      if (m && m[0].length === selected.length) { view.replaceSelection(withInput.value); }
    }
    if (!findNext(true)) { replaceInfo.textContent = 'Keine weiteren Treffer'; }
  }

  function findPrevious() {
    var re = pattern();
    if (!re) { return false; }
    var n = view.count();
    var active = view.activePosition();
    var startIndex = active ? active.index : n - 1;
    var limit = active ? active.start : Infinity;
    for (var step = 0; step <= n; step += 1) {
      var i = (((startIndex - step) % n) + n) % n;
      var text = view.textAt(i);
      var last = null;
      var m;
      re.lastIndex = 0;
      while ((m = re.exec(text))) {
        if (m[0].length === 0) { re.lastIndex += 1; continue; }
        // In the paragraph the search starts in, only what lies before the cursor counts (the last round of the loop comes back to it whole).
        if (step === 0 && m.index >= limit) { break; }
        last = m;
      }
      if (last) { view.select(i, last.index, last.index + last[0].length); return true; }
    }
    return false;
  }

  /** F3: the next hit (Shift+F3 the one before). Nothing to look for yet: the dialog opens. */
  function findAgain(backwards) {
    if (!pattern()) { openReplace(); return; }
    if (!(backwards ? findPrevious() : findNext(true))) { toast('Keine Treffer.'); }
  }

  function replaceAll() {
    var re = pattern();
    if (!re) { return; }
    var count = view.replaceAll(re, withInput.value);
    if (count > 0) { markDirty(); }
    replaceInfo.textContent = count + (count === 1 ? ' Ersetzung' : ' Ersetzungen');
  }

  if (findBar) {
    findBar.addEventListener('keydown', function (event) { if (event.key === 'Escape') { event.preventDefault(); closeFind(); } });
    // Enter in the field of the search goes to the hit (Shift+Enter to the one before); Enter in the field of the replacement replaces this hit.
    findInput.addEventListener('keydown', function (event) {
      if (event.key !== 'Enter' || event.isComposing) { return; }
      event.preventDefault();
      if (!(event.shiftKey ? findPrevious() : findNext(true))) { replaceInfo.textContent = 'Keine Treffer'; }
    });
    withInput.addEventListener('keydown', function (event) {
      if (event.key !== 'Enter' || event.isComposing) { return; }
      event.preventDefault();
      replaceCurrent();
    });
    document.getElementById('replace-prev').addEventListener('click', function () { if (!findPrevious()) { replaceInfo.textContent = 'Keine Treffer'; } });
    document.getElementById('replace-close').addEventListener('click', closeFind);
    findInput.addEventListener('input', countMatches);
    caseInput.addEventListener('change', countMatches);
    wholeInput.addEventListener('change', countMatches);
    document.getElementById('replace-next').addEventListener('click', function () { if (!findNext(true)) { replaceInfo.textContent = 'Keine Treffer'; } });
    document.getElementById('replace-one').addEventListener('click', replaceCurrent);
    document.getElementById('replace-all').addEventListener('click', replaceAll);
  }

  // ================================================================================================ comments and the style sheet
  var side = document.getElementById('editor-side');
  var commentList = document.getElementById('comment-list');
  var commentDialog = document.getElementById('editor-comment');
  var commentText = document.getElementById('comment-text');
  var commentFor = document.getElementById('comment-for');
  var commentIndex = 0;
  var commentQuote = '';

  /** Where a remark is now: the paragraph it was written on, or the one that has the words it quotes when the text has moved. */
  function relocate(c) {
    var n = view.count();
    var i = Math.min(c.paragraphIndex, n - 1);
    if (c.quote && n > 0 && view.textAt(i).indexOf(c.quote) < 0) {
      for (var j = 0; j < n; j += 1) { if (view.textAt(j).indexOf(c.quote) >= 0) { return j; } }
    }
    return i;
  }

  function renderComments() {
    if (!commentList || !view) { return; }
    commentList.textContent = '';
    var open = data.comments.filter(function (c) { return !c.isResolved; });
    document.getElementById('comment-count').textContent = String(open.length);
    var marked = new Set();
    data.comments.forEach(function (c) {
      c.at = relocate(c);
      if (!c.isResolved) { marked.add(c.at); }
    });
    view.setCommentMarks(marked);
    if (open.length === 0) {
      var empty = el('p', 'mat-muted');
      empty.textContent = 'Keine offenen Kommentare.';
      commentList.appendChild(empty);
    }
    open.forEach(function (c) {
      var item = el('li', 'kk-comment');
      item.setAttribute('data-comment', String(c.id));
      var head = el('div', 'kk-comment__head');
      var link = el('a');
      link.href = '#p' + (c.at + 1);
      link.textContent = 'Absatz ' + (c.at + 1);
      link.addEventListener('click', function (e) {
        e.preventDefault();
        var target = view.element(c.at);
        if (target) { target.scrollIntoView({ block: 'center' }); view.focusParagraph(c.at, 'end'); }
      });
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

  /** The dialog for a remark on a paragraph; the views call it from their own comment buttons. */
  function openComment(index, quote) {
    commentIndex = index;
    commentQuote = quote;
    commentFor.textContent = 'Absatz ' + (index + 1) + ': „' + quote.slice(0, 60) + (quote.length > 60 ? '…' : '') + '“';
    commentText.value = '';
    window.MatUi.dialog.open(commentDialog);
    commentText.focus();
  }

  if (commentDialog) {
    document.getElementById('comment-save').addEventListener('click', function () {
      var text = commentText.value.trim();
      if (!text) { return; }
      post('Comment', { paragraphIndex: commentIndex, quote: commentQuote, text: text }).then(function (c) {
        data.comments.push({ id: c.id, paragraphIndex: c.paragraphIndex, quote: c.quote, text: c.text, author: c.author, isResolved: false, created: c.created });
        renderComments();
        window.MatUi.dialog.close(commentDialog);
        toast('Der Kommentar ist gespeichert.');
        showSide(true);
      }).catch(function () { toast('Der Kommentar konnte nicht gespeichert werden.', 'error'); });
    });
  }

  var toggle = document.getElementById('editor-side-toggle');
  function showSide(on) {
    if (!side) { return; }
    side.hidden = !on;
    host.classList.toggle('has-side', on);
    if (toggle) { toggle.setAttribute('aria-pressed', on ? 'true' : 'false'); }
  }
  function toggleSide() { showSide(!!(side && side.hidden)); }
  if (toggle && side) { toggle.addEventListener('click', toggleSide); }

  /** Alt+K: a remark on the marked words, or on the paragraph the cursor is in. */
  function commentHere() {
    var p = view.activePosition();
    if (!p) { toast('Setze den Cursor in den Absatz, den du kommentieren möchtest.'); return; }
    var text = view.textAt(p.index);
    openComment(p.index, p.end > p.start ? text.slice(p.start, p.end) : text.slice(0, 60));
  }

  /** The comment of the list that is meant is marked, and the list shows it. */
  function flashComment(id) {
    Array.prototype.forEach.call(commentList.children, function (li) {
      var current = li.getAttribute('data-comment') === String(id);
      li.classList.toggle('is-current', current);
      if (current) { li.scrollIntoView({ block: 'nearest' }); }
    });
  }

  /** A mark in the margin of the page was clicked: the panel opens at the first open comment of that paragraph. */
  function showComments(index) {
    showSide(true);
    var first = data.comments.filter(function (c) { return !c.isResolved && c.at === index; })[0];
    if (first) { flashComment(first.id); }
  }

  /** Alt+↓ / Alt+↑: the next / the previous open remark, counted from where the cursor is (round and round). */
  function jumpComment(step) {
    var open = data.comments.filter(function (c) { return !c.isResolved; }).sort(function (a, b) { return a.at - b.at || a.id - b.id; });
    if (open.length === 0) { toast('Keine offenen Kommentare.'); return; }
    var p = view.activePosition();
    var from = p ? p.index : view.anchor().index;
    var target = null;
    if (step > 0) {
      target = open.find(function (c) { return c.at > from; }) || open[0];
    } else {
      for (var i = open.length - 1; i >= 0; i -= 1) { if (open[i].at < from) { target = open[i]; break; } }
      target = target || open[open.length - 1];
    }
    var element = view.element(target.at);
    if (element) { element.scrollIntoView({ block: 'center' }); view.focusParagraph(target.at, 'end'); }
    showSide(true);
    flashComment(target.id);
  }

  var styleList = document.getElementById('style-list');
  function renderStyles() {
    if (!styleList) { return; }
    styleList.textContent = '';
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
  renderStyles();

  // An entry for the style sheet while writing: the marked word is the term, the person says what is decided about it.
  var styleDialog = document.getElementById('editor-style');
  var styleTerm = document.getElementById('style-term');
  var styleDecision = document.getElementById('style-decision');
  var styleCategory = document.getElementById('style-category');
  var styleError = document.getElementById('style-error');

  function openStyle() {
    if (!styleDialog) { return; }
    var marked = view.selectedText() || (window.getSelection ? String(window.getSelection()) : '');
    styleTerm.value = marked && marked.length <= 100 && marked.indexOf('\n') < 0 ? marked.trim() : '';
    styleDecision.value = '';
    styleError.textContent = '';
    window.MatUi.dialog.open(styleDialog);
    (styleTerm.value ? styleDecision : styleTerm).focus();
  }

  if (styleDialog) {
    document.getElementById('style-save').addEventListener('click', function () {
      var term = styleTerm.value.trim();
      if (!term) { styleError.textContent = 'Bitte gib den Eintrag ein.'; styleTerm.focus(); return; }
      post('Style', { category: styleCategory.value, term: term, decision: styleDecision.value, note: null }).then(function (r) {
        var existing = data.styles.filter(function (s) { return s.term.toLowerCase() === r.term.toLowerCase(); })[0];
        if (existing) { existing.category = r.category; existing.decision = r.decision; } else { data.styles.push({ term: r.term, category: r.category, decision: r.decision }); }
        renderStyles();
        window.MatUi.dialog.close(styleDialog);
        showSide(true);
        toast(r.created ? 'Ins Stilblatt eingetragen.' : 'Der Eintrag im Stilblatt ist geändert.');
      }).catch(function () { styleError.textContent = 'Der Eintrag konnte nicht gespeichert werden.'; });
    });
    styleDialog.addEventListener('keydown', function (event) { if (event.key === 'Enter' && event.target !== document.getElementById('style-save') && event.target.tagName === 'INPUT') { event.preventDefault(); document.getElementById('style-save').click(); } });
  }

  // ================================================================================================ the bar
  function wire(id, handler) { var b = document.getElementById(id); if (b) { b.addEventListener('click', handler); } }
  function closePass() {
    window.MatUi.prompt('Name des Durchgangs (zum Beispiel „Erster Durchgang“). Danach beginnt die nächste Änderung eine neue Fassung.', { title: 'Durchgang abschließen', ok: 'Abschließen' }).then(function (label) {
      if (label !== null) { save('close', { label: label }); }
    });
  }

  function switchView() {
    var next = view.name === 'text' ? 'page' : 'text';
    var button = host.querySelector('[data-view="' + next + '"]');
    if (!button || button.disabled) { toast('Die Seitenansicht gibt es für dieses Dokument nicht.'); return; }
    setView(next);
  }

  // Two ways to get room: "maximize" makes the editor big inside the browser window (the menu, the breadcrumb and the footer step aside: a class on the page),
  // "full screen" is the browser's own (the whole screen; it maximizes too, because a menu in the way is the point). Esc ends the one that is on. Maximizing is
  // remembered, full screen cannot be (a browser wants a click for it).
  var MAXIMIZE_KEY = 'kk.editor.maximize';
  var maximizeButton = document.getElementById('editor-maximize');
  var fullscreenButton = document.getElementById('editor-fullscreen');
  var maximized = false;
  var maximizedBefore = false;   // what the maximizing was before full screen turned it on

  function showState(button, on) {
    if (!button) { return; }
    button.setAttribute('aria-pressed', on ? 'true' : 'false');
    Array.prototype.forEach.call(button.querySelectorAll('[data-icon-state]'), function (icon) { icon.hidden = (icon.getAttribute('data-icon-state') === 'on') !== on; });
  }

  function setMaximized(on) {
    maximized = on;
    document.documentElement.classList.toggle('kk-focus', on);
    showState(maximizeButton, on && !document.fullscreenElement);
    try { window.localStorage.setItem(MAXIMIZE_KEY, on && !document.fullscreenElement ? '1' : '0'); } catch (e) { /* not remembered */ }
  }

  function toggleMaximize() {
    if (document.fullscreenElement) { document.exitFullscreen().catch(function () { /* already out */ }); return; }
    setMaximized(!maximized);
  }

  function toggleFullscreen() {
    if (!document.documentElement.requestFullscreen) { toast('Dieser Browser kann keinen Vollbildmodus. Mit F11 geht es vielleicht.'); return; }
    if (document.fullscreenElement) { document.exitFullscreen().catch(function () { /* already out */ }); return; }
    maximizedBefore = maximized;
    document.documentElement.requestFullscreen().then(function () { setMaximized(true); showState(fullscreenButton, true); }).catch(function () { toast('Der Browser erlaubt den Vollbildmodus hier nicht.'); });
  }

  document.addEventListener('fullscreenchange', function () {
    var on = !!document.fullscreenElement;
    showState(fullscreenButton, on);
    if (!on) { setMaximized(maximizedBefore); }
  });

  try { if (window.localStorage.getItem(MAXIMIZE_KEY) === '1') { setMaximized(true); } } catch (e) { /* not remembered */ }

  // ================================================================================================ the earlier version beside the text
  // The "Änderungsmodus": the page of an earlier version next to the text, with what was taken away marked in it. It is made from the saved text and drawn again
  // after every save, so it follows the work with the delay of the autosave.
  var refPane = document.getElementById('editor-reference');
  var refBody = refPane ? refPane.querySelector('.kk-editor__reference-body') : null;
  var refSelect = document.getElementById('reference-from');
  var refNote = document.getElementById('reference-note');
  var refToggle = document.getElementById('editor-reference-toggle');
  var refLink = null;
  var refTimer = null;
  var refFrom = 0;
  var refTicket = 0;

  function refFit() {
    var sheet = refBody ? refBody.querySelector('.kk-sheet') : null;
    if (!sheet) { return; }
    sheet.style.zoom = '';
    var wanted = sheet.offsetWidth;
    var available = refBody.clientWidth - 24;
    sheet.style.zoom = wanted > available && available > 0 ? String(Math.max(0.3, available / wanted)) : '';
  }

  function refLoad() {
    if (!refPane || refPane.hidden) { return; }
    var ticket = refTicket + 1;
    refTicket = ticket;
    fetch(location.pathname + '?id=' + data.documentId + '&handler=Reference&from=' + refFrom, { credentials: 'same-origin' })
      .then(function (response) { if (!response.ok) { throw new Error('HTTP ' + response.status); } return response.json(); })
      .then(function (r) {
        if (ticket !== refTicket) { return; }
        refSelect.textContent = '';
        r.options.forEach(function (o) {
          var option = el('option');
          option.value = String(o.no);
          option.textContent = o.text;
          if (o.no === r.from) { option.selected = true; }
          refSelect.appendChild(option);
        });
        var top = refBody.scrollTop;
        refBody.textContent = '';
        if (r.empty) {
          refNote.textContent = '';
          var none = el('p', 'mat-muted kk-editor__reference-empty');
          none.textContent = 'Es gibt noch keine frühere Fassung. Sobald der Text gespeichert ist und eine zweite Fassung besteht, steht sie hier.';
          refBody.appendChild(none);
          return;
        }
        refFrom = r.from;
        if (refLink) { refLink.remove(); }
        refLink = el('link');
        refLink.rel = 'stylesheet';
        refLink.href = r.link;
        document.head.appendChild(refLink);
        var desk = el('div', 'kk-desk kk-desk--read kk-desk--old');
        var sheet = el('div', 'kk-sheet kk-sheet--read ' + r.pageClass);
        sheet.innerHTML = r.html;
        desk.appendChild(sheet);
        refBody.appendChild(desk);
        refFit();
        refBody.scrollTop = top;
        refNote.textContent = 'Fassung ' + r.from + ', was bis zur gespeicherten Fassung ' + r.to + ' wegfällt, durchgestrichen.';
      })
      .catch(function () { if (ticket === refTicket) { refNote.textContent = 'Die frühere Fassung lässt sich gerade nicht laden.'; } });
  }

  function toggleReference(on) {
    if (!refPane) { return; }
    var show = on === undefined ? refPane.hidden : on;
    refPane.hidden = !show;
    host.classList.toggle('has-reference', show);
    if (refToggle) { refToggle.setAttribute('aria-pressed', show ? 'true' : 'false'); }
    if (show) { refLoad(); }
  }

  if (refPane) {
    refSelect.addEventListener('change', function () { refFrom = parseInt(refSelect.value, 10) || 0; refLoad(); });
    document.getElementById('reference-close').addEventListener('click', function () { toggleReference(false); });
    if (typeof ResizeObserver !== 'undefined') { new ResizeObserver(refFit).observe(refBody); }
  }

  var keysDialog = document.getElementById('editor-keys');
  function openKeys() { if (keysDialog) { window.MatUi.dialog.open(keysDialog); } }

  wire('editor-save', function () { save('manual'); });
  wire('editor-close', closePass);
  wire('editor-close-menu', closePass);
  wire('editor-undo', function () { view.undo(); });
  wire('editor-redo', function () { view.redo(); });
  wire('editor-find', toggleFind);
  wire('editor-keys-open', openKeys);
  wire('editor-maximize', toggleMaximize);
  wire('editor-fullscreen', toggleFullscreen);
  wire('style-add', openStyle);
  wire('editor-reference-toggle', function () { toggleReference(); });

  // An export is made of the saved text: what is typed and not yet saved goes first, then the file comes.
  function afterSave() {
    return save('auto').then(function () {
      return saving || pendingKind ? new Promise(function (done) { setTimeout(done, 200); }).then(afterSave) : null;
    });
  }
  Array.prototype.forEach.call(document.querySelectorAll('[data-export]'), function (link) {
    link.addEventListener('click', function (event) {
      if (!dirty && !saving) { return; }
      event.preventDefault();
      afterSave().then(function () { location.href = link.href; });
    });
  });

  // The uploaded PDF next to the text (a PDF has no layout to rebuild, so the original itself is the page to look at).
  var originalButton = document.getElementById('editor-original');
  var originalPane = document.getElementById('editor-original-pane');
  if (originalButton && originalPane) {
    originalButton.addEventListener('click', function () {
      originalPane.hidden = !originalPane.hidden;
      host.classList.toggle('has-original', !originalPane.hidden);
      originalButton.setAttribute('aria-pressed', originalPane.hidden ? 'false' : 'true');
      var frame = originalPane.querySelector('iframe');
      if (!originalPane.hidden && frame && !frame.getAttribute('src')) { frame.setAttribute('src', frame.getAttribute('data-src')); }
    });
  }

  Array.prototype.forEach.call(host.querySelectorAll('[data-view]'), function (button) {
    button.addEventListener('click', function () { setView(button.getAttribute('data-view')); });
  });

  window.addEventListener('beforeunload', function (event) {
    if (dirty) { event.preventDefault(); event.returnValue = ''; }
  });

  // Switching to another tab or leaving the page: what is not saved yet goes out now, and may finish while the page closes.
  document.addEventListener('visibilitychange', function () {
    if (document.visibilityState === 'hidden' && dirty) { save('auto', { leaving: true }); }
  });
  window.addEventListener('pagehide', function () {
    if (dirty) { save('auto', { leaving: true }); }
  });

  // ================================================================================================ start
  /** What the other view needs from this one. */
  window.KkEditor = {
    data: data,
    host: host,
    parasElement: parasElement,
    LS: LS,
    base: base,
    el: el,
    icon: icon,
    toast: toast,
    post: post,
    touch: touch,
    markDirty: markDirty,
    countWords: countWords,
    showWordCount: showWordCount,
    openComment: openComment,
    showComments: showComments,
    toDisplay: toDisplay,
    renderComments: renderComments,
    updateUndoButtons: updateUndoButtons,
    texts: snapshot,
    registerView: function (page) {
      views[page.name] = page;
      var button = host.querySelector('[data-view="' + page.name + '"]');
      if (button) { button.disabled = false; }
    }
  };

  function start() {
    var wanted = storedView();
    var first = wanted && wanted !== 'text' && views[wanted] ? wanted : 'text';
    view = null;
    // The view the person used last; the text view when that cannot be drawn (the text is always there).
    setView(first, data.paragraphs).then(function (ok) {
      return ok || first === 'text' ? ok : setView('text', data.paragraphs);
    }).then(function () {
      var word = stateWord(data.state);
      setStatus('saved', 'Fassung ' + data.versionNo + (word ? ' (' + word + ')' : '') + ' geöffnet');
      showSession();
    }).then(function () {
      if (location.hash && /^#p\d+$/.test(location.hash)) {
        var target = document.getElementById(location.hash.slice(1));
        if (target) { target.scrollIntoView({ block: 'center' }); view.focusParagraph(parseInt(location.hash.slice(2), 10) - 1, 0); }
      }
    });
  }

  // The other view loads after this file: it registers itself first, then the editor starts (the page is parsed, so all scripts have run).
  if (document.readyState === 'loading') { document.addEventListener('DOMContentLoaded', start); } else { start(); }
})();

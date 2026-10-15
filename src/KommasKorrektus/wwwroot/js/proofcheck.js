/* What an editor or a proofreader looks for in a German manuscript by hand, or with a macro in Word, as rules that read the paragraphs of a text and say where something is not
   as it should be: spaces (twice, before a punctuation mark, missing after one, at the edges of a paragraph), the quotation marks (straight ones, English ones, apostrophes), the
   ellipsis and the dash, the protected space (z. B., 5 %), a word that stands twice in a row, empty paragraphs, and names that differ a letter from the spelling of the style
   sheet. A finding has the place (paragraph, from, to), what stands there and what it should be (fix; remove: true for an empty paragraph, no fix at all for a hint).

   Pure functions, no page needed: window.KkProofCheck in the browser, module.exports for a test. The rules are for German (the manuscripts are); they are careful on purpose:
   what is found is something a person decides with one click, not something that is changed on its own. */
(function (root) {
  'use strict';

  var NBSP = String.fromCharCode(0xa0);
  var LOW9 = String.fromCharCode(0x201e);      // „
  var HIGH6 = String.fromCharCode(0x201c);     // “
  var HIGH9 = String.fromCharCode(0x201d);     // ”
  var SLOW9 = String.fromCharCode(0x201a);     // ‚
  var SHIGH6 = String.fromCharCode(0x2018);    // ‘
  var APOSTROPHE = String.fromCharCode(0x2019);// ’
  var ELLIPSIS = String.fromCharCode(0x2026);  // …
  var ENDASH = String.fromCharCode(0x2013);    // –
  var MAX_PER_RULE = 2000;

  /** The rules, in the order they are shown: id, what it is called, and a sentence that says why. */
  var RULES = [
    { id: 'spaces', title: 'Mehrfache Leerzeichen', hint: 'Zwei oder mehr Leerzeichen hintereinander.' },
    { id: 'edges', title: 'Leerzeichen am Rand', hint: 'Leerzeichen am Anfang oder am Ende eines Absatzes.' },
    { id: 'spaceBefore', title: 'Leerzeichen vor Satzzeichen', hint: 'Vor Komma, Punkt, Semikolon, Doppelpunkt, Frage- und Ausrufezeichen steht kein Leerzeichen.' },
    { id: 'spaceAfter', title: 'Leerzeichen nach Satzzeichen fehlt', hint: 'Nach Komma, Semikolon, Ausrufe- und Fragezeichen und nach einem Satzende folgt ein Leerzeichen.' },
    { id: 'quotes', title: 'Anführungszeichen', hint: 'Deutsche Anführungszeichen sind „so“ und ‚so‘, der Apostroph ist ’.' },
    { id: 'ellipsis', title: 'Auslassungspunkte', hint: 'Drei Punkte sind ein Zeichen: …' },
    { id: 'dash', title: 'Gedankenstrich', hint: 'Zwischen Leerzeichen steht der Gedankenstrich –, kein Bindestrich.' },
    { id: 'nbsp', title: 'Geschütztes Leerzeichen', hint: 'In „z. B.“ und zwischen Zahl und Einheit (5 %, 10 km, 8 Uhr) darf die Zeile nicht umbrechen.' },
    { id: 'repeat', title: 'Wort doppelt', hint: 'Dasselbe Wort steht zweimal hintereinander.' },
    { id: 'empty', title: 'Leere Absätze', hint: 'Mehrere leere Absätze hintereinander (Abstand gehört in die Formatierung).' },
    { id: 'names', title: 'Schreibweise von Namen', hint: 'Ein Wort, das sich nur um einen Buchstaben von einem Namen des Stilblatts unterscheidet.' }
  ];

  function levenshtein(a, b, limit) {
    if (Math.abs(a.length - b.length) > limit) { return limit + 1; }
    var previous = [];
    for (var j = 0; j <= b.length; j += 1) { previous.push(j); }
    for (var i = 1; i <= a.length; i += 1) {
      var row = [i];
      var best = i;
      for (var k = 1; k <= b.length; k += 1) {
        var cost = a.charCodeAt(i - 1) === b.charCodeAt(k - 1) ? 0 : 1;
        var value = Math.min(previous[k] + 1, row[k - 1] + 1, previous[k - 1] + cost);
        row.push(value);
        if (value < best) { best = value; }
      }
      if (best > limit) { return limit + 1; }
      previous = row;
    }
    return previous[b.length];
  }

  /** Whether the stretch of text around an index is an address or a file name, where the rules about punctuation do not apply. */
  function inAddress(text, index) {
    var from = index;
    var to = index;
    while (from > 0 && !/\s/.test(text.charAt(from - 1))) { from -= 1; }
    while (to < text.length && !/\s/.test(text.charAt(to))) { to += 1; }
    var token = text.slice(from, to);
    return /[\/@\\]|^www\.|\.(?:de|com|org|net|at|ch|eu|docx?|pdf|txt|jpe?g|png)(?:\b|$)/i.test(token);
  }

  function isOpening(text, i) {
    return i === 0 || /[\s(\[{–—\-‚„«»…\/]/.test(text.charAt(i - 1));
  }

  var INTERJECTIONS = ['ja', 'nein', 'na', 'so', 'la', 'da', 'ha', 'ho', 'hm', 'nun', 'bla', 'tick', 'tack', 'hui', 'ach', 'oh', 'hey', 'ah', 'oha', 'pst', 'piep', 'tuck', 'wau', 'buh', 'mäh', 'muh', 'bumm', 'peng', 'klopf', 'tick', 'hopp', 'hui', 'brr', 'pfui', 'hurra', 'ja-ja', 'immer', 'sehr', 'ganz', 'noch', 'wieder', 'weit', 'tief', 'lang', 'laut', 'leise', 'schnell', 'langsam'];

  // ------------------------------------------------------------------------------------------------ the rules, one paragraph at a time
  function checkParagraph(text, n, add) {
    var m;
    var re;

    // spaces twice (not at the edges: those are another rule)
    re = / {2,}/gu;
    while ((m = re.exec(text)) !== null) {
      if (m.index === 0 || m.index + m[0].length === text.length) { continue; }
      add('spaces', n, m.index, m.index + m[0].length, m[0], ' ');
    }

    // spaces at the edges
    m = /^[ \t]+/u.exec(text);
    if (m && m[0].length < text.length) { add('edges', n, 0, m[0].length, m[0], ''); }
    m = /[ \t]+$/u.exec(text);
    if (m && m.index > 0) { add('edges', n, m.index, text.length, m[0], ''); }

    // a space before a punctuation mark
    re = /(?<=[\p{L}\p{N}"“”’)\]]) +(?=[,;:!?])/gu;
    while ((m = re.exec(text)) !== null) {
      if (!inAddress(text, m.index)) { add('spaceBefore', n, m.index, m.index + m[0].length, m[0], ''); }
    }
    re = /(?<=[\p{L}\p{N}"“”’)\]]) +(?=\.(?!\.)(?:\s|$|["“”’)»]))/gu;
    while ((m = re.exec(text)) !== null) {
      if (!inAddress(text, m.index)) { add('spaceBefore', n, m.index, m.index + m[0].length, m[0], ''); }
    }

    // no space after a punctuation mark
    re = /[,;!?](?=\p{L})|:(?=\p{Lu}\p{Ll})|(?<=\p{Ll}{2})\.(?=\p{Lu}\p{Ll})/gu;
    while ((m = re.exec(text)) !== null) {
      if (!inAddress(text, m.index)) { add('spaceAfter', n, m.index, m.index + 1, m[0], m[0] + ' '); }
    }

    // the quotation marks
    for (var i = 0; i < text.length; i += 1) {
      var c = text.charAt(i);
      var next = text.charAt(i + 1);
      var before = i > 0 ? text.charAt(i - 1) : '';
      if (c === '"') {
        if (/\d/.test(before) && !/\p{L}/u.test(next)) { continue; }   // 5" is a measure
        add('quotes', n, i, i + 1, c, isOpening(text, i) ? LOW9 : HIGH6);
      } else if (c === HIGH6 && isOpening(text, i) && next !== '' && !/\s/.test(next)) {
        add('quotes', n, i, i + 1, c, LOW9);                              // English opening
      } else if (c === HIGH9) {
        add('quotes', n, i, i + 1, c, isOpening(text, i) ? LOW9 : HIGH6); // English closing
      } else if (c === "'") {
        if (/\p{L}/u.test(before) && /\p{L}/u.test(next)) { add('quotes', n, i, i + 1, c, APOSTROPHE); }
        else if (isOpening(text, i) && /\p{L}/u.test(next)) { add('quotes', n, i, i + 1, c, SLOW9); }
        else if (/\p{L}/u.test(before) || /[.,!?]/.test(before)) { add('quotes', n, i, i + 1, c, SHIGH6); }
      } else if (c === SHIGH6 && isOpening(text, i) && /\p{L}/u.test(next)) {
        add('quotes', n, i, i + 1, c, SLOW9);                             // English opening, single
      }
    }

    // three dots are one sign
    re = /(?<!\.)\.{3}(?!\.)/gu;
    while ((m = re.exec(text)) !== null) { add('ellipsis', n, m.index, m.index + 3, m[0], ELLIPSIS); }

    // a hyphen with a blank on both sides is a dash
    re = /(?<=\S )-{1,2}(?= \S)/gu;
    while ((m = re.exec(text)) !== null) { add('dash', n, m.index, m.index + m[0].length, m[0], ENDASH); }

    // protected spaces: z. B., and a number and its unit
    re = /(?<=(?<![\p{L}])\p{L}\.) (?=\p{L}\.)/gu;
    while ((m = re.exec(text)) !== null) { add('nbsp', n, m.index, m.index + 1, m[0], NBSP); }
    re = /(?<=\d) (?=(?:%|‰|€|\$|°C|°|km|m|cm|mm|kg|g|mg|l|ml|h|min|s|Uhr|Euro|Prozent|Mio\.|Mrd\.)(?![\p{L}]))/gu;
    while ((m = re.exec(text)) !== null) { add('nbsp', n, m.index, m.index + 1, m[0], NBSP); }

    // a word twice in a row
    re = /(?<![\p{L}])(\p{L}{2,}) +\1(?![\p{L}])/giu;
    while ((m = re.exec(text)) !== null) {
      if (INTERJECTIONS.indexOf(m[1].toLowerCase()) >= 0) { continue; }
      add('repeat', n, m.index + m[1].length, m.index + m[0].length, m[0].slice(m[1].length), '');
    }
  }

  function checkNames(paragraphs, terms, add) {
    var names = (terms || []).filter(function (t) { return t && t.term && /^[\p{L}\p{M}'’-]+$/u.test(t.term) && t.term.length >= 4 && (t.kind === 'Character' || t.kind === 'Place'); });
    if (names.length === 0) { return; }
    var exact = new Set(names.map(function (t) { return t.term; }));
    var lower = names.map(function (t) { return { term: t.term, low: t.term.toLowerCase() }; });
    var re = /\p{L}[\p{L}\p{M}'’-]*/gu;
    paragraphs.forEach(function (text, n) {
      var m;
      re.lastIndex = 0;
      while ((m = re.exec(text)) !== null) {
        var word = m[0];
        if (word.length < 4 || exact.has(word)) { continue; }
        var low = word.toLowerCase();
        for (var i = 0; i < lower.length; i += 1) {
          var t = lower[i];
          if (low === t.low) {
            // the same name in other capitals (a word at the start of a sentence is written with a capital anyway)
            if (word !== t.term && word.charAt(0) === word.charAt(0).toLowerCase() && t.term.charAt(0) !== t.term.charAt(0).toLowerCase()) { add('names', n, m.index, m.index + word.length, word, t.term); }
            break;
          }
          if (low.charAt(0) !== t.low.charAt(0)) { continue; }
          if (Math.abs(low.length - t.low.length) > 2) { continue; }
          // an ending (Rabenwalds, Annas) is the name, not another one
          if (low.indexOf(t.low) === 0 || t.low.indexOf(low) === 0) { continue; }
          var limit = t.low.length >= 9 ? 2 : 1;
          if (levenshtein(low, t.low, limit) <= limit) { add('names', n, m.index, m.index + word.length, word, t.term); break; }
        }
      }
    });
  }

  /**
   * The findings of a text, in the order of the text: { rule, n, from, to, found, fix, remove, hint }. paragraphs are the texts (a line break inside one as \n), options.terms
   * the style sheet: [{ term, kind: "Character" | "Place" | ... }]. A rule that finds more than MAX_PER_RULE places stops there.
   */
  function check(paragraphs, options) {
    var out = [];
    var counts = {};
    var meta = {};
    RULES.forEach(function (r) { meta[r.id] = r; });
    var add = function (rule, n, from, to, found, fix) {
      counts[rule] = (counts[rule] || 0) + 1;
      if (counts[rule] > MAX_PER_RULE) { return; }
      out.push({ rule: rule, n: n, from: from, to: to, found: found, fix: fix, remove: false, hint: meta[rule].hint });
    };
    paragraphs.forEach(function (text, n) {
      checkParagraph(text, n, add);
      if (n > 0 && text === '' && paragraphs[n - 1] === '') {
        counts.empty = (counts.empty || 0) + 1;
        if (counts.empty <= MAX_PER_RULE) { out.push({ rule: 'empty', n: n, from: 0, to: 0, found: '', fix: null, remove: true, hint: meta.empty.hint }); }
      }
    });
    checkNames(paragraphs, options && options.terms, add);
    out.sort(function (a, b) { return a.n - b.n || a.from - b.from; });
    return out;
  }

  var api = { check: check, RULES: RULES, levenshtein: levenshtein };
  if (typeof module !== 'undefined' && module.exports) { module.exports = api; } else { root.KkProofCheck = api; }
})(typeof window !== 'undefined' ? window : this);

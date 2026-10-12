/* The text of a paragraph of the page view, read from the markup the way the server wrote it, and the way back from a position in that text to a place on the page.
   The editor writes in the page with this (editor-page.js), and the pages that only read it (the earlier version beside the text, the comparison) find the words they mark
   with it, so that all of them count the same characters. No page is needed: everything is read from the paragraph that is handed in.

   A paragraph holds text nodes and things that are not text: pictures, note marks, list numbers (marked data-opaque). A line break inside a paragraph is either a
   line feed in the text (as the server writes it) or a <br> the browser made for Shift+Enter; a <br> at the very end is only the place holder. */
(function (root) {
  'use strict';

  var LS = String.fromCharCode(0x2028);   // a line break inside a paragraph, as the text is kept
  var NL = String.fromCharCode(10);       // the same, as the page and the search see it

  function isOpaque(node) { return node.nodeType === 1 && node.hasAttribute('data-opaque'); }

  function hasMoreAfter(node, stop) {
    for (var x = node; x && x !== stop; x = x.parentNode) {
      for (var s = x.nextSibling; s; s = s.nextSibling) {
        if (s.nodeType === 3) {
          if (s.nodeValue.length > 0) { return true; }
        } else if (s.nodeType === 1 && !isOpaque(s) && (s.nodeName === 'BR' || s.textContent.length > 0)) {
          return true;
        }
      }
    }
    return false;
  }

  function walk(top, onText, onBreak, onOpaque) {
    for (var n = top.firstChild; n; n = n.nextSibling) {
      if (n.nodeType === 3) {
        onText(n);
      } else if (n.nodeType === 1) {
        if (isOpaque(n)) {
          if (onOpaque) { onOpaque(n); }
        } else if (n.nodeName === 'BR') {
          if (hasMoreAfter(n, top)) { onBreak(n); }
        } else {
          walk(n, onText, onBreak, onOpaque);
        }
      }
    }
  }

  /**
   * What a paragraph is made of, in order: text pieces, line breaks (a <br> counts one character) and the things that are not text.
   * A line break at the very end of the text is only drawn when another follows it, and the browser keeps one there for the cursor: the last one is that
   * place holder, not a break (the server writes a real break at the end the same way, with one more).
   */
  function pieces(p) {
    var list = [];
    walk(p, function (node) { list.push({ node: node, text: node.nodeValue, br: false, opaque: false }); },
      function (br) { list.push({ node: br, text: NL, br: true, opaque: false }); },
      function (node) { list.push({ node: node, text: '', br: false, opaque: true }); });
    var last = list[list.length - 1];
    if (last && !last.br && !last.opaque && last.text.charAt(last.text.length - 1) === NL) { last.text = last.text.slice(0, -1); }
    return list;
  }

  /** The text of a paragraph as the editor holds it (U+2028 for a line break inside it). */
  function textOf(p) {
    var out = '';
    pieces(p).forEach(function (piece) { out += piece.text; });
    return out.split(NL).join(LS);
  }

  /** The text of a paragraph as the page and the search see it (a line feed for a line break inside it). */
  function displayOf(p) { return textOf(p).split(LS).join(NL); }

  /** The pieces of text of a paragraph with where each starts, for finding a place on the page from a number and back. */
  function segments(p) {
    var list = [];
    var position = 0;
    pieces(p).forEach(function (piece) {
      if (piece.opaque) { return; }
      list.push({ node: piece.node, start: position, length: piece.text.length, br: piece.br });
      position += piece.text.length;
    });
    return list;
  }

  function indexIn(node) {
    var i = 0;
    for (var s = node.previousSibling; s; s = s.previousSibling) { i += 1; }
    return i;
  }

  /** The place on the page (a node and an offset in it) of a position in the text of a paragraph. */
  function locate(p, offset) {
    var list = segments(p);
    for (var i = 0; i < list.length; i += 1) {
      var s = list[i];
      if (offset <= s.start + s.length) {
        if (s.br) { return { node: s.node.parentNode, offset: indexIn(s.node) + (offset > s.start ? 1 : 0) }; }
        return { node: s.node, offset: Math.min(offset - s.start, s.length) };
      }
    }
    return { node: p, offset: list.length === 0 ? 0 : p.childNodes.length };
  }

  root.KkPageText = { LS: LS, NL: NL, isOpaque: isOpaque, walk: walk, pieces: pieces, textOf: textOf, displayOf: displayOf, segments: segments, indexIn: indexIn, locate: locate };
})(window);

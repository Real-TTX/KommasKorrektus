/* Two pages next to each other (the comparison): the old page and the new page are drawn from the same markup of the changes, so block n of one is
   block n of the other. Both get the height of the taller one, so that a paragraph stands beside its counterpart however much text one side lost or
   gained, and a paragraph that exists on one side only leaves a gap on the other. */
(function () {
  'use strict';

  var oldSheet = document.querySelector('.kk-desk--old .kk-sheet');
  var newSheet = document.querySelector('.kk-desk--new .kk-sheet');
  if (!oldSheet || !newSheet) { return; }

  function blocks(sheet) { return Array.prototype.slice.call(sheet.children); }

  function align() {
    var a = blocks(oldSheet);
    var b = blocks(newSheet);
    var n = Math.min(a.length, b.length);
    var i;
    for (i = 0; i < n; i += 1) { a[i].style.minHeight = ''; b[i].style.minHeight = ''; }
    var heights = [];
    for (i = 0; i < n; i += 1) { heights.push(Math.max(a[i].offsetHeight, b[i].offsetHeight)); }
    for (i = 0; i < n; i += 1) {
      if (a[i].offsetHeight !== heights[i]) { a[i].style.minHeight = heights[i] + 'px'; }
      if (b[i].offsetHeight !== heights[i]) { b[i].style.minHeight = heights[i] + 'px'; }
    }
  }

  // A folded run of unchanged paragraphs opens on both sides at once (it is the same run), and the pages are aligned again.
  function syncFolds(from, to) {
    var f = blocks(from);
    var t = blocks(to);
    f.forEach(function (block, index) {
      if (block.tagName !== 'DETAILS' || !t[index] || t[index].tagName !== 'DETAILS') { return; }
      block.addEventListener('toggle', function () {
        if (t[index].open !== block.open) { t[index].open = block.open; }
        align();
      });
    });
  }
  syncFolds(oldSheet, newSheet);
  syncFolds(newSheet, oldSheet);

  var frame = 0;
  function later() {
    if (frame) { return; }
    frame = requestAnimationFrame(function () { frame = 0; align(); });
  }

  align();
  window.addEventListener('resize', later);
  if (document.fonts && document.fonts.ready) { document.fonts.ready.then(later); }
  if (typeof ResizeObserver !== 'undefined') {
    // The zoom of the pages (sheet.js) follows the width of the desk: the blocks change their height when it changes.
    var observer = new ResizeObserver(later);
    Array.prototype.forEach.call(document.querySelectorAll('.kk-desk--old, .kk-desk--new'), function (desk) { observer.observe(desk); });
  }
})();

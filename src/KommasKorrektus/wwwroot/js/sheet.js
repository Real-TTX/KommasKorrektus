/* The page view on the pages that only read it: the sheet is drawn in the width of the original page; on a narrower screen it is scaled down to fit
   (the zoom of the browser, set from here because a style attribute is not allowed by the policy of the site, a property set by script is). */
(function () {
  'use strict';

  var desks = Array.prototype.slice.call(document.querySelectorAll('.kk-desk[data-fit]'));
  if (desks.length === 0) { return; }

  function fit(desk) {
    var sheet = desk.querySelector('.kk-sheet');
    if (!sheet) { return; }
    sheet.style.zoom = '';
    var wanted = sheet.offsetWidth;
    var available = desk.clientWidth - 24;
    sheet.style.zoom = wanted > available && available > 0 ? String(Math.max(0.3, available / wanted)) : '';
  }

  desks.forEach(function (desk) {
    fit(desk);
    if (typeof ResizeObserver !== 'undefined') {
      var last = desk.clientWidth;
      new ResizeObserver(function () { if (desk.clientWidth !== last) { last = desk.clientWidth; fit(desk); } }).observe(desk);
    }
  });
})();

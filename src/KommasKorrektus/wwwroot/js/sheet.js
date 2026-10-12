/* The page view on the pages that only read it: the sheet is drawn in the width of the original page; on a narrower screen it is scaled down to fit
   (the zoom of the browser, set from here because a style attribute is not allowed by the policy of the site, a property set by script is). The size of the writing
   the person chose (data-scale on the desk, a factor) is on top of that. */
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
    var zoom = (wanted > available && available > 0 ? Math.max(0.3, available / wanted) : 1) * (parseFloat(desk.getAttribute('data-scale')) || 1);
    sheet.style.zoom = Math.abs(zoom - 1) < 0.001 ? '' : String(zoom);
  }

  // the size of the writing changed (the comparison of two pages): fitted again
  window.KkSheet = { fit: function () { desks.forEach(fit); } };

  desks.forEach(function (desk) {
    fit(desk);
    if (typeof ResizeObserver !== 'undefined') {
      var last = desk.clientWidth;
      new ResizeObserver(function () { if (desk.clientWidth !== last) { last = desk.clientWidth; fit(desk); } }).observe(desk);
    }
  });
})();

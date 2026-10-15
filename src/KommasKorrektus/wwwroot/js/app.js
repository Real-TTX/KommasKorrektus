/* KommasKorrektus: the behaviour of the app itself. MatUi (wwwroot/ui/matui.js) does menus, dialogs, tabs, validation and pickers; this file adds
   only what is specific to the app. Everything is wired by data attributes, so server-rendered and dynamically added markup behave the same. */
(function () {
  'use strict';

  // A value that is data (the accent colour of an invoice template) is written as data-kk-accent="#a43bd0" and set here: no inline style in the markup.
  function applyAccents(scope) {
    Array.prototype.forEach.call((scope || document).querySelectorAll('[data-kk-accent]'), function (el) {
      var value = el.getAttribute('data-kk-accent');
      if (/^#[0-9a-fA-F]{6}$/.test(value)) { el.style.setProperty('--kk-accent', value); }
    });
  }
  applyAccents(document);

  window.KK = window.KK || {};
  window.KK.applyAccents = applyAccents;
})();

// A tooltip (data-tip) is above its button; the toolkit has no answer for a button at the very top of the window (the bar of a maximized editor), where it would be cut off.
// So it is looked at when the pointer or the focus arrives: no room above (the height of a tooltip and its gap), and it opens below.
(function () {
  'use strict';
  var ROOM = 40;
  function place(event) {
    var target = event.target;
    var tip = target && target.closest ? target.closest('[data-tip]') : null;
    if (!tip) { return; }
    if (tip.getBoundingClientRect().top < ROOM) { tip.setAttribute('data-tip-below', ''); } else { tip.removeAttribute('data-tip-below'); }
  }
  document.addEventListener('mouseover', place);
  document.addEventListener('focusin', place);
})();

// A control that applies at once: <input data-submit-on-change> sends its form when it changes.
document.addEventListener('change', function (event) {
  var target = event.target;
  if (target && target.hasAttribute && target.hasAttribute('data-submit-on-change') && target.form) { target.form.submit(); }
});

/* KommaKorrektus: the behaviour of the app itself. MatUi (wwwroot/ui/matui.js) does menus, dialogs, tabs, validation and pickers; this file adds
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

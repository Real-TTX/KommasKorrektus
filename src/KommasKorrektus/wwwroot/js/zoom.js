/* The sizes of the writing a person can choose (percent), the same list as ViewPreferences.Zooms on the server: the editor and the comparison of two pages use it. */
(function (root) {
  'use strict';

  var STEPS = [70, 80, 90, 100, 110, 125, 150, 175, 200];

  /** A size from the list; anything else is 100. */
  function clean(value) {
    var n = parseInt(value, 10);
    return STEPS.indexOf(n) >= 0 ? n : 100;
  }

  /** The next size up (direction 1) or down (-1) from a size, or 0 when there is none. */
  function next(current, direction) {
    var i = STEPS.indexOf(clean(current)) + direction;
    return i >= 0 && i < STEPS.length ? STEPS[i] : 0;
  }

  root.KkZoom = { steps: STEPS, clean: clean, next: next };
})(window);

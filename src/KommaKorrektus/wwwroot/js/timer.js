/* The stop watch: an element with data-timer-start (an ISO time) shows how long it has been running, once a second. */
(function () {
  'use strict';

  var timers = document.querySelectorAll('[data-timer-start]');
  if (timers.length === 0) { return; }

  function pad(n) { return n < 10 ? '0' + n : String(n); }

  function tick() {
    Array.prototype.forEach.call(timers, function (el) {
      var start = Date.parse(el.getAttribute('data-timer-start'));
      if (isNaN(start)) { return; }
      var seconds = Math.max(0, Math.floor((Date.now() - start) / 1000));
      el.textContent = Math.floor(seconds / 3600) + ':' + pad(Math.floor(seconds / 60) % 60) + ':' + pad(seconds % 60);
    });
  }

  tick();
  setInterval(tick, 1000);
})();

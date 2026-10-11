/* The comparison page: jump from change to change (buttons, or n and p on the keyboard). */
(function () {
  'use strict';

  var root = document.querySelector('[data-diff]');
  if (!root) { return; }

  var changes = Array.prototype.slice.call(root.querySelectorAll('.kk-change'));
  var current = -1;

  function go(step) {
    if (changes.length === 0) { return; }
    if (current >= 0) { changes[current].classList.remove('is-current'); }
    current = current < 0 ? (step > 0 ? 0 : changes.length - 1) : (current + step + changes.length) % changes.length;
    var row = changes[current];
    var gap = row.closest('details');
    if (gap) { gap.open = true; }
    row.classList.add('is-current');
    row.scrollIntoView({ block: 'center', behavior: 'smooth' });
  }

  Array.prototype.forEach.call(document.querySelectorAll('[data-diff-step]'), function (button) {
    button.addEventListener('click', function () { go(parseInt(button.getAttribute('data-diff-step'), 10)); });
  });

  document.addEventListener('keydown', function (event) {
    var target = event.target;
    if (event.ctrlKey || event.metaKey || event.altKey || (target && /^(input|select|textarea)$/i.test(target.tagName))) { return; }
    if (event.key === 'n') { go(1); } else if (event.key === 'p') { go(-1); }
  });
})();

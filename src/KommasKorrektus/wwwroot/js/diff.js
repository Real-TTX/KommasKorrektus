/* The comparison page: jump from change to change (buttons, or n and p on the keyboard). */
(function () {
  'use strict';

  var root = document.querySelector('[data-diff]');
  if (!root) { return; }

  var changes = Array.prototype.slice.call(root.querySelectorAll('.kk-change'));
  var current = null;

  // On the new of two pages a paragraph that was taken away is invisible: the jump skips it.
  function visible(element) { return element.offsetParent !== null && getComputedStyle(element).visibility !== 'hidden'; }

  function go(step) {
    var list = changes.filter(visible);
    if (list.length === 0) { return; }
    var at = current ? list.indexOf(current) : -1;
    if (current) { current.classList.remove('is-current'); }
    at = at < 0 ? (step > 0 ? 0 : list.length - 1) : (at + step + list.length) % list.length;
    current = list[at];
    var gap = current.closest('details');
    if (gap) { gap.open = true; }
    current.classList.add('is-current');
    current.scrollIntoView({ block: 'center', behavior: 'smooth' });
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

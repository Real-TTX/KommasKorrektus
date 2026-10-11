/* A live PDF preview next to a form: <form data-preview-url="..."> and an <iframe id="preview-frame">.
   Whenever the form changes (after a short pause) it is sent to the preview address as it is, the answer is a PDF, and the PDF is shown.
   Nothing is saved. A form with a file field sends the file too (a new logo is seen before it is saved). */
(function () {
  'use strict';

  var form = document.querySelector('form[data-preview-url]');
  var frame = document.getElementById('preview-frame');
  if (!form || !frame) { return; }

  var state = document.getElementById('preview-state');
  var link = document.getElementById('preview-open');
  var url = form.getAttribute('data-preview-url');
  var timer = null;
  var current = null;
  var token = 0;

  function show(text) { if (state) { state.textContent = text; } }

  function refresh() {
    var mine = ++token;
    show('wird erstellt ...');
    fetch(url, { method: 'POST', body: new FormData(form), credentials: 'same-origin' })
      .then(function (response) {
        if (!response.ok) { throw new Error('HTTP ' + response.status); }
        return response.blob();
      })
      .then(function (blob) {
        if (mine !== token) { return; }
        var next = URL.createObjectURL(blob);
        frame.src = next + '#toolbar=0&navpanes=0&view=FitH';
        if (link) { link.href = next; }
        if (current) { URL.revokeObjectURL(current); }
        current = next;
        show('aktuell');
      })
      .catch(function (error) { if (mine === token) { show('nicht möglich (' + error.message + ')'); } });
  }

  function schedule() {
    show('ändert sich ...');
    clearTimeout(timer);
    timer = setTimeout(refresh, 700);
  }

  form.addEventListener('input', schedule);
  form.addEventListener('change', schedule);
  // The colour and date pickers of the toolkit change the real input and fire its events; a panel that closes needs one more look.
  document.addEventListener('click', function (event) { if (event.target.closest && event.target.closest('.mat-picker')) { schedule(); } });
  refresh();
})();

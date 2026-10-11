/* The form of an offer or an invoice: the rows of the positions, the totals as they are typed, a product put in with one choice, and the projects of the chosen customer.
   The rows carry data-name="Title" (and so on); before every send they are numbered as Input.Lines[i].Title, so the server gets one list.
   The totals use the rules of the server (a line is rounded to cents; the tax of a rate is worked out on the sum of its lines; a discount comes off before the tax). */
(function () {
  'use strict';

  var form = document.querySelector('form[data-invoice-editor]');
  if (!form) { return; }

  var linesElement = document.getElementById('lines');
  var template = document.getElementById('line-template');
  var currency = form.getAttribute('data-currency') || 'EUR';

  function $(selector, scope) { return (scope || document).querySelector(selector); }
  function all(selector, scope) { return Array.prototype.slice.call((scope || document).querySelectorAll(selector)); }
  function rows() { return all('[data-line]', linesElement); }
  function round2(value) { return Math.round((value + Number.EPSILON) * 100) / 100; }
  function number(input) { var v = parseFloat(String(input && input.value).replace(',', '.')); return isNaN(v) ? 0 : v; }
  function money(value) {
    try { return new Intl.NumberFormat('de-DE', { style: 'currency', currency: currency }).format(value); } catch (e) { return value.toFixed(2) + ' ' + currency; }
  }
  function field(row, name) { return $('[data-name="' + name + '"]', row); }

  function smallBusiness() { var box = $('input[type=checkbox][name="Input.SmallBusiness"]', form); return !!(box && box.checked); }
  function reverseCharge() { var box = $('input[type=checkbox][name="Input.ReverseCharge"]', form); return !!(box && box.checked) && !smallBusiness(); }

  // ------------------------------------------------------------------------------------------ numbering
  function renumber() {
    rows().forEach(function (row, i) {
      all('[data-name]', row).forEach(function (input) { input.name = 'Input.Lines[' + i + '].' + input.getAttribute('data-name'); });
    });
  }

  // ------------------------------------------------------------------------------------------ totals
  function calculate() {
    var noVat = smallBusiness() || reverseCharge();
    var discount = number($('input[name="Input.DiscountPercent"]', form));
    var groups = {};
    var lineTotal = 0;
    rows().forEach(function (row) {
      var net = round2(number(field(row, 'Quantity')) * number(field(row, 'UnitPrice')) * (1 - number(field(row, 'DiscountPercent')) / 100));
      var output = $('[data-net]', row);
      if (output) { output.textContent = money(net); }
      var rate = noVat ? 0 : number(field(row, 'VatRate'));
      groups[rate] = (groups[rate] || 0) + net;
      lineTotal += net;
    });

    var discountTotal = 0;
    var netTotal = 0;
    var vatTotal = 0;
    var vatRows = [];
    Object.keys(groups).map(Number).sort(function (a, b) { return a - b; }).forEach(function (rate) {
      var sum = groups[rate];
      var off = round2(sum * discount / 100);
      var net = sum - off;
      var vat = noVat ? 0 : round2(net * rate / 100);
      discountTotal += off; netTotal += net; vatTotal += vat;
      if (!noVat && rate !== 0) { vatRows.push({ rate: rate, vat: vat }); }
    });

    $('#t-discount').textContent = '−' + money(discountTotal);
    $('[data-discount-row]').hidden = discountTotal === 0;
    $('#t-net-label').textContent = noVat ? 'Gesamtbetrag' : 'Nettobetrag';
    $('#t-net').textContent = money(netTotal);
    var box = $('#t-vat-rows');
    box.textContent = '';
    vatRows.forEach(function (r) {
      var line = document.createElement('div');
      line.className = 'kk-totals__row';
      var label = document.createElement('span');
      label.textContent = 'zzgl. ' + r.rate.toLocaleString('de-DE') + ' % Umsatzsteuer';
      var value = document.createElement('output');
      value.textContent = money(r.vat);
      line.appendChild(label); line.appendChild(value);
      box.appendChild(line);
    });
    $('#t-gross-row').hidden = noVat;
    $('#t-gross').textContent = money(netTotal + vatTotal);
    all('[data-vat]', linesElement).forEach(function (cell) { cell.hidden = smallBusiness(); });
    linesElement.setAttribute('data-small', smallBusiness() ? 'true' : 'false');
  }

  // ------------------------------------------------------------------------------------------ rows
  function addRow(values) {
    var fragment = template.content.cloneNode(true);
    var row = fragment.querySelector('[data-line]');
    linesElement.appendChild(row);
    var v = values || {};
    Object.keys(v).forEach(function (key) { var input = field(row, key); if (input) { input.value = v[key]; } });
    renumber();
    calculate();
    var title = field(row, 'Title');
    if (title && !values) { title.focus(); }
    return row;
  }

  document.getElementById('add-line').addEventListener('click', function () { addRow(); });

  linesElement.addEventListener('click', function (event) {
    var remove = event.target.closest ? event.target.closest('[data-remove]') : null;
    if (!remove) { return; }
    var row = remove.closest('[data-line]');
    if (row) { row.remove(); renumber(); calculate(); schedulePreview(); }
  });

  linesElement.addEventListener('input', function () { calculate(); });

  var picker = document.getElementById('product-picker');
  if (picker) {
    picker.addEventListener('change', function () {
      var option = picker.options[picker.selectedIndex];
      if (!option || !option.value) { return; }
      addRow({
        Title: option.getAttribute('data-title') || '',
        Description: option.getAttribute('data-description') || '',
        Unit: option.getAttribute('data-unit') || 'Stk.',
        UnitPrice: option.getAttribute('data-price') || '0',
        VatRate: smallBusiness() ? '0' : (option.getAttribute('data-vat') || '19'),
        Quantity: '1'
      });
      picker.selectedIndex = 0;
      schedulePreview();
    });
  }

  function schedulePreview() { form.dispatchEvent(new Event('input', { bubbles: true })); }

  form.addEventListener('input', calculate);
  form.addEventListener('change', calculate);
  form.addEventListener('submit', renumber);

  // ------------------------------------------------------------------------------------------ the projects of the customer
  var customer = document.getElementById('Input_CustomerId');
  var project = document.getElementById('Input_ProjectId');
  function filterProjects() {
    if (!customer || !project) { return; }
    var chosen = customer.value;
    all('option[data-customer]', project).forEach(function (option) {
      var show = !chosen || option.getAttribute('data-customer') === chosen;
      option.hidden = !show;
      option.disabled = !show;
    });
    var selected = project.options[project.selectedIndex];
    if (selected && selected.disabled) { project.value = ''; }
  }
  if (customer) { customer.addEventListener('change', filterProjects); filterProjects(); }

  // The due date follows the date and the term, until somebody sets it by hand.
  var issue = $('input[name="Input.IssueDate"]', form);
  var term = $('input[name="Input.PaymentTermDays"]', form);
  var due = $('input[name="Input.DueDate"]', form);
  function followDue() {
    if (!issue || !term || !due || due.getAttribute('data-manual') === 'true' || !issue.value) { return; }
    var date = new Date(issue.value + 'T00:00:00');
    date.setDate(date.getDate() + (parseInt(term.value, 10) || 0));
    var text = date.getFullYear() + '-' + String(date.getMonth() + 1).padStart(2, '0') + '-' + String(date.getDate()).padStart(2, '0');
    due.value = text;
    due.dispatchEvent(new Event('change', { bubbles: true }));
  }
  if (due) { due.addEventListener('input', function (event) { if (event.isTrusted) { due.setAttribute('data-manual', 'true'); } }); }
  if (issue) { issue.addEventListener('change', followDue); }
  if (term) { term.addEventListener('input', followDue); }

  renumber();
  calculate();
})();

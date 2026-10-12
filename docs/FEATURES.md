# Features

Everything KommasKorrektus does, area by area. Names in *italics* are the labels in the (German) interface.

## Texts and versions

| | |
|---|---|
| **Import** | DOCX (paragraphs, also in tables), PDF (text PDFs; paragraphs rebuilt from line spacing and font size, hyphenation at line ends joined), plain text and Markdown (UTF-8 with or without mark, Windows-1252 as a fallback). Up to 150 MB. Notes about what the import had to guess are kept with the document |
| **Editor** | Two views, one text: **Text** and **Seite** (see below). In the text view: one text field per paragraph, numbered; a paragraph that changed has a dot. Enter splits, Backspace at the start joins, line breaks inside a paragraph are kept. Undo / redo, find and replace (also across paragraphs), word and character count, **Durchgang abschließen** to close a version (and name it), **Export** (Word with changes or clean, PDF, text) straight from the editor: what is typed is saved first. The search is a bar under the buttons (`Enter` next hit, `Shift+Enter` the one before, `Esc` closes it), less used things sit in the menu behind the three dots, the two buttons for **maximize** and **full screen** are fixed in the top right corner of the window, whatever else is on the screen; **maximize** (`Alt+V`, `Esc` ends it) makes the editor big inside the browser window: the menu, the breadcrumb and the footer are gone and the bar shrinks to one row (remembered); **full screen** (`Alt+B`) is the browser's own, the whole screen, and maximizes too, and on a phone the buttons keep to two rows. **Frühere Fassung daneben** (*Mehr*-menu, `Alt+R`): the page of an earlier version (chosen in the bar: a rule that stays right as the work goes on, *Die Fassung davor*, *Anfang dieser Runde* or *Das Original*, or one particular version; the choice is kept with your account) next to the text with what was taken away struck through, drawn again after every save. It is read like the diff of a pull request: the two pages are **lined up paragraph by paragraph** (a paragraph that was taken away or is new leaves room on the other side) and scroll as one, also while you write. It needs the page view (the text view steps it aside), and whether it is open is kept with your account. Shortcuts (the list is in the editor, `Alt+H`): `Ctrl+S` save, `Alt+A` close the pass, `Alt+K` comment on the marked words or the paragraph, `Alt+↓` / `Alt+↑` next / previous open comment, `Alt+L` comments and style sheet, `Alt+W` text / page, `Alt+X` export, `Alt+R` earlier version beside the text, `Alt+S` marked word into the style sheet, `Alt+V` maximize, `Alt+B` full screen, `Ctrl+H` find and replace, `F3` / `Shift+F3` next / previous hit, `Ctrl+Z`, `Ctrl+Y`. They are `Alt` + a letter on purpose: `Ctrl+Alt` is AltGr on a German keyboard |
| **Page view** | The page is the **standard** (for the editor, a version, the comparison and the pages of authors and clients; the plain text is one click away). *Seite* shows the text on a page in the layout of the uploaded Word file: styles (headings, quotes, alignment, indents, line spacing), the formatting of words (bold, italic, colour, size, font), numbered and bulleted lists, tables (widths, borders, shading, merged cells), pictures, footnotes and endnotes, links. It is **written in directly**: the whole page is one editable field, so selecting across paragraphs, cut, copy, paste, spelling and the browser's own undo work as in any editor. Only the text changes: formatting commands, dropping text and deleting across table cells or over a picture are refused (the export could not carry them), and the original's formatting stays and comes back in the export (a word you insert takes the formatting of the word before it, as in the export). A paragraph made with Enter looks as the export will make it. A faint line marks where a page of the original would end. A paragraph with an open comment has a small bubble in the margin of the page: a click opens the comments at that one. The view you used last is remembered **with your account** (not the browser), for the editor and for a version, and so is what the editor has open (the earlier version beside the text, the comments), so another browser or device opens the same way; a text that cannot be drawn on a page falls back to the text view. A PDF or text file has no layout to keep: *Seite* shows a plain page, and a PDF can be shown next to the text (eye icon). Not shown: headers and footers, text boxes, columns, page borders, Word's own comments |
| **Saving** | A version is a **pass over the text**, not a click: `Ctrl+S`, the *Speichern* button and the **autosave** all write into the version being worked on (*in Arbeit*), so saving ten times leaves one version. The autosave runs in the text view and in the page view: 3 seconds after the last keystroke (adjustable under *Verwaltung → Firma*, 0 = off), at the latest every 15 seconds while somebody types without a pause, when the tab is left or closed, and again after a failed attempt. It has a **threshold**: it waits until 10 characters differ from the saved text (adjustable, 0 = every change; a smaller change is saved after a minute anyway), and it never takes a big part of the text away on its own (more than 150 characters and a third of the text: that waits for `Ctrl+S`, leaving the page always saves); the bar says *Automatisch gespeichert um …*. *Durchgang abschließen* closes it (with an optional name such as "Erster Durchgang", *abgeschlossen*); the next change then begins the next version. A closed version is never touched again. No change, no new version |
| **Customer file** | The ping pong between editor and customer: a file the customer sends back (*Datei vom Kunden* on the document page) is **the next version** of the same document: its text is the version, the file is kept with it (and can be downloaded again). What the customer changed is the comparison of the last two versions, and any two versions can be compared. A file starts a new **round**: the page and the Word export of everything from then on use the formatting of that file; the pass of the editor that was open ends with it. The export without a starting version gives the changes of this round (everything since the customer's file) |
| **Versions** | Numbered per document and named by the ping pong: *Original*, *Änderung 1*, *Änderung 2* ... (the passes of the editor, counted again in every round), *Datei vom Kunden v2*, *Änderung 1* ... (a name you gave a pass is kept), with the round, state (original, in progress, closed, restored, customer file), label, author, date, words added / removed / changed against the version before. **Restore** puts an old version back *as a new version*, nothing is overwritten |
| **Remarks** | Per paragraph, with the quoted words, resolved or open; authors can be staff, clients (portal) or anonymous visitors (share link) |
| **Style sheet** | Per project: characters, places, spellings, terms and rules, so that the same name is spelled the same everywhere. While writing, the marked word goes into it with `Alt+S` (or *Eintrag* in the side panel of the editor); the same term again changes the entry |
| **Comparison** | Any two versions. Paragraphs are paired (insertions, deletions, modifications, moves recognised by similarity), words diffed with Myers' algorithm. *Nebeneinander* (old / new) or *Ein Dokument* (deleted struck through, new underlined), *Seite* shows the changes inside the formatted text of the original (also for authors and clients), *Zwei Seiten* the old page (what was taken away) and the new page (what was added) next to each other, paragraph beside paragraph, folds open on both sides; the mode you chose last is remembered with your account (two pages next to each other are the standard), *Nur Änderungen* folds unchanged paragraphs, `n` / `p` or the buttons jump from change to change. Long texts are paged |
| **Export** | DOCX **with tracked changes** (`w:ins` / `w:del`, author = your company name, written into the original file so formatting stays), DOCX clean (changes accepted), PDF proof sheet, PDF side by side, PDF clean, plain text. Any two versions can be chosen |

## Working time

| | |
|---|---|
| **Automatic** | The editor sends a heartbeat (active seconds, keystrokes, words changed). A block of working time starts with the first keystroke and ends after a pause (*Arbeitszeit*, default 5 minutes). Long pauses are never counted |
| **Manual** | *Zeiterfassung*: project, day, start, duration (`1:30`, `90 min`, `1,5 Stunden`, `2 h`), note, billable or not. A stopwatch for work outside the editor |
| **Overview** | Hours per day and words corrected per day for the last 14 days, the list of entries with filters, and the hours not yet billed |
| **To the invoice** | *Aufwand* on an invoice draft adds the unbilled hours as one position at the customer's (or the company's) hourly rate and marks them as billed; cancelling the invoice frees them again |

## Customers and projects

- **Customers**: person or company (publisher), salutation, pen name, address, contact people, tax / VAT id, Leitweg-ID,
  individual payment term and hourly rate, notes, archive instead of delete
- **History** per customer: calls, mails, meetings and notes you write, plus entries the application writes (project
  created, offer or invoice issued, payment, share link, invitation)
- **Projects**: customer, title, genre, service (copyediting, proofreading, ...), status (inquiry, sample, offered, commissioned, in progress,
  review, done, archived, cancelled), priority, start, deadline, responsible person, pricing (per standard page, per word, per hour,
  flat), estimated hours, notes. Standard page = 1800 characters including spaces (adjustable)
- **Tasks**: per project or across projects, status, due date, assignee, planned minutes; **task templates** create a
  standard set of tasks when a project starts
- **Project page**: status, scope in standard pages, hours worked against estimate, tasks done, with tabs for documents,
  tasks, times, style sheet, share links and invoices
- **Start page**: open projects and tasks, open and overdue invoices, work this week, deadlines of the next 7 days, a
  checklist of first steps while the installation is new

## Offers and invoices

- **Products** (name, unit, net price, VAT rate, service) and **text blocks** (introductions and closings per document
  type) with placeholders: `{{Anrede}}`, `{{Projekt}}`, `{{Nummer}}`, `{{Tage}}`, `{{Zahlungsziel}}`, ...
- **Templates**: logo (position and height), accent colour, font and size, the titles of offer / invoice / credit note, which
  columns show (quantity, unit, VAT, discount), sender line, bank details, payment QR code, page numbers, footer and invoice
  note, paper size and margins; one template is the default. The **live preview** is the real PDF, redrawn while you type
- **Positions**: quantity, unit, unit price, VAT rate, discount per position and on everything; products can be
  inserted, *Aus Projekt* suggests positions from the project's price and size, *Aufwand* adds the working time
- **Tax**: VAT per rate on the sum of the lines, small-business rule (§ 19 UStG, no VAT shown), reverse charge
- **Numbers**: configurable (`RE-{YYYY}-{NNNN}`), assigned only when a document is **issued**, in a transaction: no gaps,
  a failed issue does not use a number, the counter restarts each year when the format contains the year
- **Issued documents are frozen**: a snapshot of sender, buyer, texts and settings is kept, and the PDF as issued is
  stored, never redrawn. A correction is a **credit note** (*Stornieren*), which references the invoice
- **Payments**: partial payments, payment method, reference; status issued → partially paid → paid, **overdue** when the
  due date has passed
- **Offers**: valid-until date, accepted / declined, **invoice from an offer** with the same positions
- **E-invoice**: invoices are issued as **ZUGFeRD** PDFs (PDF/A-3b with the embedded `factur-x.xml`, EN 16931
  profile) and can be downloaded as **XRechnung** (CII XML). The XML is validated against the EN 16931 schema in the
  tests; the PDFs pass veraPDF as PDF/A-3b. Missing data (seller, buyer address, tax number) is reported in words
- **GiroCode**: an EPC QR code with the amount and reference on the invoice
- **Sending**: by mail with the PDF attached (when a mail server is set up), marked as sent, with the mail in the log

## Sharing with authors

- **Anonymous link** (`/s/<token>`) for a whole project or one document, optionally fixed to one version. Valid for a
  chosen number of days (default 14), can be taken back, or extended (saving the link restarts the period)
- **What the visitor may do** is chosen per link: see the changes (old / new) or only the clean text, or the page in the layout of the original (*Seite*; with the changes in it when the link shows them, never with the old words when it does not), download Word and
  PDF, write remarks (name required), approve ("Passt so, freigeben"). Approval records name and date and is shown on
  the editor's side
- **Release to an account** (*Für wen? → Konto*): the same link, but for **one account** (staff, or a client of the project's customer). It asks for the sign-in
  and comes back to the page afterwards; any other account is told "not found", a password is not needed. The account finds all its releases under
  *Mit mir geteilt* (menu of the staff and of the portal), the log shows the account by name. Same rights, period and take-back as any link
- **Password** (optional, on an anonymous link): checked once, remembered in a signed cookie for 4 hours; after 10 wrong tries in 15 minutes the
  link is locked for a while
- **Log**: views, downloads, remarks, approvals, wrong passwords; visitors are shown as a hash of the address, not the
  address (an account by its name). A signed-in editor looking at the page is not counted as a visit of the customer
- A link that has run out or was taken back says so in words and shows nothing about the project

## Accounts and the portal

- **Local sign-in**: e-mail and password (PBKDF2-SHA512, 210,000 rounds), **TOTP** with recovery codes, or an **e-mail code** (no password; code
  valid for minutes, a few tries). Administrators can be required to set up TOTP. Five wrong passwords lock the account
  for a while (an administrator can unlock it)
- **Sessions** are rows in the database (the cookie carries only a token): they survive a container restart, can be
  listed and ended (one or all) in *Sicherheit* and by an administrator, expire after a number of days
- **Invitations**: for staff or for a client of a customer; a link that works once, expires, can be taken back, is sent
  by mail or copied. Somebody who already has an account accepts it after signing in and only gets the new access
- **Client portal**: *Meine Projekte* (status, deadline), documents with the changes (one text, side by side, or as a page in the layout of the original), Word and
  PDF downloads, remarks to the editor, *Rechnungen & Angebote* with the PDFs. Strictly limited to the customers the account
  was given; anything else is "not found"

## Operations

- **NAS**: *Speicher* switches the place for new documents and PDFs between the volume and an SMB share; a file knows
  where it lives (`local:` / `smb:`), so older files keep working; **move all** over and back. Share names can be listed
- **Backup**: see the README. Database as binary COPY per table in one consistent snapshot, settings (without the database
  password) and the key ring, optionally all files with their checksums; written as one zip, optionally encrypted
  (AES-256-GCM in blocks, PBKDF2 key). Scheduler checks every 30 seconds; failed runs are retried (15, 30, 45 minutes)
- **Restore**: reads the manifest first, refuses a backup of a newer version, saves a safety copy, restores in one
  transaction, resets the number sequences, optionally restores files and settings
- **Maintenance** once a day: ended sessions, used codes, logs older than a year
- **Administration**: users (role, active, new password, reset two-factor, end sessions, unlock), invitations, mail server
  with connection test and test mail, storage, security (session days, code login, TOTP for administrators, link
  periods, public address), company data (address, tax, bank, VAT, numbering formats, standard page, work-time settings),
  activity log, mail log (with the text: without a mail server an invitation link can be read there), system page

## Interface

- Built on the MatDesign toolkit, purple accent, light / dark / system theme (bottom left in the menu with account and sign-out)
- One list pattern: toolbar (search, filters, sort) above, table, pager directly below, actions below on the left; delete
  keeps its distance. One form pattern: field tag helpers, errors at the field and in a summary, dependent fields only
  appear when needed
- Phone layout for every page (tables become cards), scrollbars only while scrolling, keyboard shortcuts where they help
- German, with German number, date and money formats (the culture is fixed, formatting goes through one helper)
- Strict content security policy: no inline scripts or styles, no external resources (the page view too: the formatting of a page is a stylesheet made for it and fetched like any file)

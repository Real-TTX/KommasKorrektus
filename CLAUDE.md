# KommasKorrektus: notes for working in this repository

A self-hosted editing studio and back office for manuscript editors: ASP.NET Core 10 Razor Pages, EF Core 10 on PostgreSQL 17,
one container (port **2930**) plus a database. The user-facing text is **German**; code, comments and docs are English.

## Commands

```powershell
./scripts/redeploy.ps1            # always: rebuild the image and redeploy the dev stack (web 2930, pgAdmin 2931, Postgres 12930)
./scripts/redeploy.ps1 -Fresh     # the same with empty volumes (setup page again)
dotnet build KommasKorrektus.slnx  # build locally BEFORE a redeploy: a compile error otherwise shows up as "docker compose failed"
dotnet test KommasKorrektus.slnx   # unit tests; integration tests need KOMMASKORREKTUS_TEST_DB (and KOMMASKORREKTUS_TEST_SMB), see README
node tests/js/textdiff.test.mjs     # the comparison the editor makes in the browser (wwwroot/js/textdiff.js, VS Code's diff), no dependencies; DiffParityTests asks it and TextDiff.cs the same
node tests/js/sidebyside.test.mjs   # the places the editor lists and the classes of the paragraphs (wwwroot/js/sidebyside.js); node tests/js/proofcheck.test.mjs the check rules
./scripts/check-matui.ps1           # is there news in MatDesign for the toolkit copy in wwwroot/ui? Run it now and then (start of work, before a redeploy or a push)
./scripts/sync-matui.ps1 -Archive <matui.zip> -Commit <sha>   # take a new toolkit version (see the gotcha below)
```

Test and look at changes by **rebuilding the container and redeploying the stack**, not with a watcher. Do not pipe
`redeploy.ps1` into `Select-Object -First n` (it stops the script half way); use `-Last`. Wait for `http://localhost:2930/health`
before driving the app.

## Layout

- `src/KommasKorrektus.Core`: no web dependencies. Diff engine (`Diff/`), DOCX and PDF reading / writing (`Documents/`), the page view
  renderer DOCX -> HTML (`Documents/Layout/`),
  invoice maths, PDF, ZUGFeRD, XRechnung (`Billing/`), TOTP and password hashing (`Security/`), text metrics (`Writing/`)
- `src/KommasKorrektus`: the web app. `Pages/<Area>/` (Razor Pages), `Services/`, `Controls/` (tag helpers `kk-*`), `UiKit/` (table,
  toolbar, pager, shell), `Data/` (EF model, migrations), `Storage/` (volume and SMB), `wwwroot/` (css, js, the MatDesign toolkit in `ui/`)
- `tests/KommasKorrektus.Tests`: `Core/` unit tests, `Web/` unit tests, `Integration/` (real app against PostgreSQL / Samba)

## Conventions

- **Database**: PostgreSQL, tables PascalCase, `Id` BIGINT, audit columns `CreateDate/CreateUserId/UpdateDate/UpdateUserId`
  (filled by `KkDbContext.SaveChanges`), `UpdateState` (0 deleted, 1 created, 2 updated) for soft delete (`ISoftDeletable`),
  a enums are stored as **text**, so renaming an enum member needs a data migration (see `WorkingVersions`); a `Token` uuid for anything that acts as a secret key (`UserSession`, `ShareLink`, `Invitation`). JSON is for configuration
  (`/data/config/app.json`); logic lives in the database. **No automatic EF retry** (`EnableRetryOnFailure` breaks the issue
  transaction and the "no gaps" numbering)
- **UI**: lists = toolbar (search, filter, sort) above, table, pager directly below, actions below on the left; create / edit on
  a separate page; buttons in one row from positive to negative with the delete button spaced apart (`kk-buttons`). Use the
  shared controls (`ListBuilder`, `TableModel`, `_Toolbar`, `_Table`, `_Pager`, `kk-input`, `kk-select`, `kk-tabs` ...); several
  of one kind per page must work. Dependent fields use `show-if` (hidden fields are disabled and not posted: do not let a
  default overwrite stored values). No inline scripts or styles (CSP `default-src 'self'`). Icons from the sprite (`Ui.Icon`).
  **Controls line up**: in a `mat-form__grid` every field takes four rows (label, control, hint, error: a subgrid), and a `mat-row`
  that holds fields (or `kk-diff__bar`) stands on the bottom of its controls with hints and errors hanging below (end of `app.css`):
  never nudge a button or a switch with margins; put a field in the grid or the row. A field in a grid has the children label,
  control, hint, error: one control per field (two would sit on top of each other)
- **Formatting**: the request culture is **invariant on purpose**; format numbers, money and dates with `Fmt` (de-DE),
  never with `{x:0.##}` or `ToString()` in text a person reads
- **Razor** encodes `ü` as an entity: tests compare with `AppFixture.TextAsync` (decoded)
- **Forms with several handlers** (`Test`, `Run` ...): give the form `asp-page-handler=""` so that a re-rendered page does not
  post the next Save to the old handler
- **Security**: every query of a client (`PortalModelBase`) goes through `MyCustomerIds`; a record that is not theirs is `404`,
  never `403`. Share links answer with `410` (gone) or `404`; a password gate comes before anything else
- **Code**: little nesting, early returns, a class may hold several related business functions; Microsoft C# guidelines;
  comments say *why*. Match the surrounding style

## Gotchas

- **MatUi toolkit** (`wwwroot/ui`, from the sibling repo `../MatDesign`) **has a version** (semantic; `CHANGELOG.md`, `matui.manifest.json` and `scripts/matui-synced.txt` say which one and from which commit).
  Never edit the copied files by hand (the check names a changed file), copy **all** files of a version from its archive (`/ui/matui.zip`, built from `origin/main`: `git archive` into a scratch folder, `ASPNETCORE_ENVIRONMENT=Development dotnet run`
  on a free port, because without Development the static files of MatUi are not found), read the changelog from the old to the new version (a **Breaking** section = work in the app), rebuild, look at the pages.
  Check for news now and then with `./scripts/check-matui.ps1` and do not touch the working tree of `../MatDesign` (fetch and `git archive origin/main` only). The menu switcher is at the foot of the menu above the user (`_MenuSwitcher.cshtml`).
- **Tooltips** (`data-tip`, toolkit): above the button, always, except where there is no room above: `app.js` sets `data-tip-below` on hover / focus when the button is less than 40 px from the top of the window (the bar of a maximized editor), `app.css` opens it below then. Do not put tooltips below by a fixed rule.
- **Editor scripts** (`wwwroot/js/`): `editor.js` is the core (text view, saving, search, comments, style sheet) and tells the others what happens with `KkEditor.on(name, fn)` ("view", "change", "saved", "zoom", "ready"); `editor-page.js` is the page view;
  `editor-changes.js` (the changes in the text: the bar *Änderungen*, pins, card, list, `Alt+N` / `Alt+P`, taking back, the earlier page beside the text) and `editor-check.js` (the check, rules in `proofcheck.js`) are modules that hook in the same way and put their keys in `KkEditor.hotkeys`.
  Both pages of the comparison are `sidebyside.js` (`refresh(els, texts, { mode })`, `hunks()`; with `setOldTexts` the earlier version needs no page): **change a mark there and the editor and the comparison page change together**. The places are the changes of `textdiff.compare()` (`placeOf`): `level` "paragraph" when both sides are whole paragraphs (`oldTexts` / `newTexts`: four empty paragraphs taken away are one place), else "words", whose texts may hold paragraph breaks (U+2029: a split, a join; shown as ¶). `tests/js/sidebyside.test.mjs` loads `sidebyside.js` with a few fake elements.
  **When it happens** (the flicker the user saw twice): everything a change of the text makes different (marks, room, pins, the comment bubbles, the number beside the cursor) is done **in the frame of the change, before the browser draws it**: `editor-changes.js` asks for `requestAnimationFrame` from the input event (that frame), never `setTimeout` or the next frame (then the page is drawn once unaligned: after Enter everything below jumps down and back up), and the `ResizeObserver` callbacks of `editor-page.js` place their layers at once. Only a text whose comparison costs more than 16 ms (`COMPARE_BUDGET`) is compared after a pause; its room is still made right in the frame. The lining up (`align()` of `sidebyside.js`) is **incremental**: it measures and changes only room that is wrong (by more than 1 px), so lining up what is lined up writes nothing (no loop with the observer, which is still braked: `layoutChanged`), and it never takes all room away to measure from scratch. The room is a margin, rules of a constructable stylesheet keyed by `data-n`, on both pages (a read-only page is scoped by `data-kk-align`); the editable page's DOM is never touched. Pins are only redrawn when their set or places changed (the signature of the layer). A probe that records the painted frames (CDP screencast) and looks for pictures shown for one frame is the way to check it (headless Edge, `--hide-scrollbars` off, device scale 1.25).
  Taking a change back is `replaceSpan` (a stretch over paragraph breaks replaced by a text with U+2029 for breaks) / `removeParagraph` / `insertParagraphAfter` (whole paragraphs: the old ones first, then the new ones go) of the page view: `execCommand` steps, so native undo stays whole (`insertParagraphAfter` with an empty text must not type anything: typing nothing is a deletion). Never put elements into the editable page for marks: the layers (`kk-pghosts`, `kk-pmarks`) lie beside it on the desk, the words are `CSS.highlights` (`kk-added`, `kk-check`). The shared size of the two pages: `KkEditor.fitLimit` (the narrower desk) and the server's page width for both zooms, otherwise they differ by a fraction of a pixel.
- **The comparison is Visual Studio Code's diff** (the user asked for it: "angleichen an VSCode GIT DIFF"; MIT, `src/vs/editor/common/diff/defaultLinesDiffComputer`), with paragraphs for lines, in `wwwroot/js/textdiff.js` and `Core/Diff/TextDiff.cs`, **step by step the same** (`DiffParityTests`: 400+ random cases, long paragraphs, more than 1700 paragraphs, a text too much for Myers; `tests/js/parity.mjs`): paragraphs by a scoring LCS (< 1700, integer scores: empty 10, else 100 + 69·⌊log2(1+len)⌋, a run bonus 100) or anchors of unique paragraphs + Myers between them; then every differing stretch character by character (paragraph breaks are characters, U+2029; LCS < 500, else Myers with a work limit of 4 M steps, then words first), then VS Code's clean-up (shift to boundaries, a word less than 2/3 unchanged is marked whole, changes ≤ 2 apart join, short text between long changes goes). **Deviations, the same on both sides**: Unicode letters/digits are word characters; nothing joins across a paragraph break (removeShortMatches, removeShortTextBetweenLongDiffs, the suffix of a line without its break, a whole word); `keepBreaks` lets the old paragraph keep its break when text put in holds one (text before the full stop and a new paragraph after it are two places); a fixed whitespace list, `v·√v` instead of `pow(v, 1.5)` (no transcendental function: the browser and .NET round them differently). The answer is **rows**: a paragraph as Word keeps it with tracked changes (`ParagraphDiff.Mark`: a split paragraph is two rows of one old paragraph, the first with a break put in; joined ones are two rows of one new paragraph); `DocxWriter` writes a split as the old paragraph with an inserted mark and the rest as a new paragraph (the look of a new one, as Enter on the page), joined ones as a deleted mark (tracked) or one paragraph with the look of the first (clean: the page view). `Stats` still counts **words** (`CompareWords` per row). The test helper `RevisionView` joins a paragraph whose mark goes with the next one, as Word does
- **What the editor compares with** (`Editor.cshtml.cs` `OnGetReferenceAsync`, `editor-changes.js`): "the version before" = the one below a version in work or a customer's file, but the version itself when it is closed (a pass that begins is changed against it); the client **fixes the number** (`fixedFrom`) at the first comparison, so the first autosave of a pass (which makes version N+1) does not move it, and starts the next comparison only on *Durchgang abschließen*. Never reload the comparison on a save: the marks jump.
- **Word comments** (`Core/Documents/DocxCommentWriter.cs`, `DocxComment`): the export with changes carries the open remarks of the staff as comments of Word, on the words of the quote (a run that is one text is cut at them, any other run is taken whole); the file's own comments (and their anchors, which `ParagraphItems` keeps as opaque items) stay, new ids start above the highest. Only the editor's own export (`Documents/Export`) asks for them (`withComments`): portal and share downloads never do. The tests validate against the OpenXML schema.
- **Page view** (`Documents/Layout/`, `Services/LayoutService.cs`, `js/editor-page.js`): the page is the Word file the **export** makes (original + new text via `DocxWriter`, accepted) drawn as HTML, so what is shown is what the export holds.
  The text of every paragraph in the markup must be exactly `ParagraphItems` text (`LayoutTests` checks it): pictures, note marks and list numbers are `data-opaque` and carry no text
  (their content comes from CSS `attr()`). The renderer reuses `ParagraphItems`, never its own reading. The editor page is **one** contenteditable; the server refuses a text whose
  paragraph count differs from the page (`OnPostLayoutAsync`), the client stays in the text view then
- **CSP and the page view**: no `style` attributes in the markup. Formatting is classes named by a hash of their declarations, delivered as a stylesheet (`/layout/{key}.css`, anonymous,
  kept in `IMemoryCache`); a stylesheet that is not in the cache any more is a 404, so the page that links it must be fresh. `white-space: pre-wrap` sits on `.kk-sheet`, not on `.kk-lp`:
  otherwise Chrome adds style attributes (blocked, with a console error) when it joins paragraphs. Joining paragraphs of a different look still makes it *try* inline styles; the
  policy blocks them and the moved text takes the look of the paragraph it joins, which is what the export does
- **Earlier version beside the text and the comparison of two pages** are one thing (`js/sidebyside.js`, `js/textdiff.js`, `js/pagetext.js`): both sides are the plain page of a version (`OnGetReferenceAsync`, `LayoutView.Plain`), what differs is **not** computed on the server but in the browser (in the editor on every change of the text), marked with `CSS.highlights` (`kk-removed` / `kk-added`, nothing is put into the text) and the classes `kk-gone` / `kk-new` / `kk-changed`. The lining up is VS Code's (the first paragraphs of the parts of a change stand across from each other: `pairs`), made with margins (rules of a constructable stylesheet keyed by `data-n`) on both pages. `textdiff.js` has to give the same answer as `TextDiff.cs`: change a limit, a heuristic or a character class on one side and change it on the other (`DiffParityTests` runs both when Node is installed). `pagetext.js` is the one place that knows how the text of a paragraph is read from the page
- **Comments in an imported file** (`Core/Documents/ImportedComments.cs` for Word, `PdfTextReader.Annotations` for PDF, `DocumentService.AddImportedComments`): they become `DocumentComment` rows (`Source` "docx" / "pdf", `WrittenDate` as the file states it, author name from the file, no `AuthorUserId`, so share links and the portal, which ask for their own comments, never show them). The paragraph index counts `DocxReader.EnumerateParagraphs` (Word) or the paragraphs `PdfTextReader` built (PDF); reading them must never fail an import (a note is added instead)
- **Size of the writing** (`User.EditorOptions.Zoom`, `ViewPreferences.Zooms`, `js/zoom.js`): a factor on top of the fitted page (page view: the zoom of the sheet, `core.scale()`; text view: `--kk-zoom` on the letters; read-only pages: `data-scale` on the desk, `sheet.js`). The list of sizes is in C# and in `zoom.js`: keep them the same
- Page view, editing: native undo only works if the DOM is not rewritten behind the browser's back: list numbers that end up in the middle of a paragraph are hidden by a class,
  not removed. Formatting commands (`beforeinput` `format*`) are refused on purpose
- **Rounds** (ping pong with the customer): a file the customer sends back is a version of its own (`VersionOrigin.Upload`, `DocumentVersion.SourceFileId`). The formatting of the page and of the Word export is not always the original: `DocumentService.RoundOfAsync` gives the file of the round a version belongs to (the latest version at or before it that came from a file), `RoundStartAsync` its number; the export without a starting version starts there
- `U+2028` (`TextMetrics.LineBreak`) separates a line break inside a paragraph. Never write it as a literal in source; the
  Write tool turns `\uXXXX` escapes into the characters, so use `(char)0x2028`
- QuestPDF: Community licence, `Settings.UseSystemFonts`, and the runtime image needs `libfontconfig1` and fonts (see the
  Dockerfile). ZUGFeRD uses `PDFA_3B` plus a document operation that embeds `factur-x.xml` (file based, temp folder)
- SMBLibrary can only talk SMB over TCP **port 445** (SMB 2.0.2 to 3.0.2): there is no port setting
- `DocumentService.ImportAsync` adds the document but does not save: the caller saves
- Dev compose has an explicit subnet (`10.253.30.0/24`): Docker runs out of default address pools on a machine with many stacks

## Git

Branches: `main` (release), `dev` (development). Commit and push **under the user's name** only: no `Co-Authored-By`
trailer, no "Generated with" line. Ask before pushing. Versions: release `<major>.<minor>.<build>-<date>`, dev
`nightly-<build>-<date>`, local `local-<date>` (`Directory.Build.props`, set by the GitHub Action).

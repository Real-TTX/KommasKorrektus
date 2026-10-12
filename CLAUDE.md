# KommasKorrektus: notes for working in this repository

A self-hosted editing studio and back office for manuscript editors: ASP.NET Core 10 Razor Pages, EF Core 10 on PostgreSQL 17,
one container (port **2930**) plus a database. The user-facing text is **German**; code, comments and docs are English.

## Commands

```powershell
./scripts/redeploy.ps1            # always: rebuild the image and redeploy the dev stack (web 2930, pgAdmin 2931, Postgres 12930)
./scripts/redeploy.ps1 -Fresh     # the same with empty volumes (setup page again)
dotnet build KommasKorrektus.slnx  # build locally BEFORE a redeploy: a compile error otherwise shows up as "docker compose failed"
dotnet test KommasKorrektus.slnx   # unit tests; integration tests need KOMMASKORREKTUS_TEST_DB (and KOMMASKORREKTUS_TEST_SMB), see README
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

- **Page view** (`Documents/Layout/`, `Services/LayoutService.cs`, `js/editor-page.js`): the page is the Word file the **export** makes (original + new text via `DocxWriter`, accepted) drawn as HTML, so what is shown is what the export holds.
  The text of every paragraph in the markup must be exactly `ParagraphItems` text (`LayoutTests` checks it): pictures, note marks and list numbers are `data-opaque` and carry no text
  (their content comes from CSS `attr()`). The renderer reuses `ParagraphItems`, never its own reading. The editor page is **one** contenteditable; the server refuses a text whose
  paragraph count differs from the page (`OnPostLayoutAsync`), the client stays in the text view then
- **CSP and the page view**: no `style` attributes in the markup. Formatting is classes named by a hash of their declarations, delivered as a stylesheet (`/layout/{key}.css`, anonymous,
  kept in `IMemoryCache`); a stylesheet that is not in the cache any more is a 404, so the page that links it must be fresh. `white-space: pre-wrap` sits on `.kk-sheet`, not on `.kk-lp`:
  otherwise Chrome adds style attributes (blocked, with a console error) when it joins paragraphs. Joining paragraphs of a different look still makes it *try* inline styles; the
  policy blocks them and the moved text takes the look of the paragraph it joins, which is what the export does
- Page view, editing: native undo only works if the DOM is not rewritten behind the browser's back: list numbers that end up in the middle of a paragraph are hidden by a class,
  not removed. Formatting commands (`beforeinput` `format*`) are refused on purpose
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

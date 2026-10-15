# Backlog

Ideas and wishes that are not built yet, newest last. Delete an item when it ships (the feature then belongs in `docs/FEATURES.md`).

Shipped from this list so far: *Hotkeys for comments and more*, *Export from the editor*, *Releases to a particular account*,
*Remember the view and two pages side by side*, *A thorough overhaul of the editor* (search bar, full screen, comments in the margin of the page ...),
*Ping pong: the customer's file is the next version*, *Logo on the invoice* (it was there: checked, only the unit column got wider),
*Changes in the text of the editor* (markup independent of the page beside it, pins, list, walking through, taking back), *The check* (typography and names),
*Comments in the Word export*, *Regular expressions in find and replace*, *Standard pages live* (see `git log`).

What a proofreader would still miss in the editor, with starting points:

- **An outline** (headings, chapters) to jump to in a long manuscript. The page knows the style of every paragraph (`LayoutService`, the classes of `Documents/Layout`); a heading style
  is `Heading n` / `Überschrift n` in the Word file, a plain text has none (then no outline). Open: a panel in the side panel, or a menu in the bar.
- **Accept a change** (mark it as seen) next to *Zurücksetzen*: for the changes a customer made in a file that came back, so that what is decided leaves the markup until the pass is
  closed. Needs somewhere to remember it (per version and place, the signature of a place is its old and new words); nothing like it exists yet.
- **Word comments in both directions**: the export writes open remarks as comments of Word (one level). Answers (threads), the *done* state (`commentsExtended.xml`) and the answers of
  the author in a returned file are not carried; the import reads comments with answers already (`ImportedComments`), so the two could meet.
- **Text that was moved** (a paragraph or a sentence taken to another place) shows as taken away in one place and put in at the other (a split paragraph is fine now: the comparison
  is VS Code's, a split is a break put in). VS Code has an experimental detection of moved lines (`computeMovedLines.ts`, `diffEditor.experimental.showMoves`): the same could mark
  moved paragraphs (one colour, one entry in the list, one "Zurücksetzen" for both ends); it would have to be in `textdiff.js` and `TextDiff.cs` alike.
- **The page beside the text in a very long text**: the lining up is incremental now (what is lined up writes nothing), but after a change `align()` in `sidebyside.js` still reads the
  rectangles of all pairs once or twice. Only the pairs near the window need to be lined up. A comparison that costs more than 16 ms waits for a pause in typing (`COMPARE_BUDGET`);
  a whole manuscript in which nearly every paragraph changed costs about 300 ms in the browser.
- **More rules for the check** (`proofcheck.js`): numbers (a range with an en dash, thousands separators), the style of quotation marks across the whole text (nested ‚ ‘), a spelling that
  occurs in two ways within the text without a style sheet (needs a word list to tell a typo from another word), rules and exceptions that a project can switch.
- **Deleted words in the page itself**: now a pin with a card; showing them struck through in the line would need elements in the editable page that the undo and the paragraph text do not see
  (the page keeps its markup untouched on purpose, see CLAUDE.md).

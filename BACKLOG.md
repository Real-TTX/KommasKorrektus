# Backlog

Ideas and wishes that are not built yet, newest last. Delete an item when it ships (the feature then belongs in `docs/FEATURES.md`).
Shipped so far from this list: *Hotkeys for comments and more*, *Export from the editor*, *Releases to a particular account*, *Logo on the invoice* (it was there: checked, only the unit column got wider) (see `git log`).

## Remember the view (page or text), and pages side by side with the change mode

Wanted: the chosen view is kept (*Seite* or *Text*), and two pages next to each other with the changes marked.

What exists: the **editor** remembers the last view, but only in the browser (`localStorage`, key `kk.editor.view`): it is not tied to the
account, so another browser or device starts in the text view again. The comparison and the version page take the view from the address
(`&view=page`, `mode=side|inline|page`) and forget it. In the comparison *Nebeneinander* is old / new as **text**, and *Seite* is **one**
page with the changes inside the formatted text; there is no old page next to a new page.

Plan: keep the view per account (a user setting in the database) and use it as the default on the editor, the comparison and the version
page; add a comparison mode that draws the page of the old version and the page of the new one next to each other (`LayoutService`, the same
renderer) with the changes marked in both.

## A thorough overhaul of the editor

Wanted: the editor reworked and improved a good deal. The hotkeys are done. Left, in steps that each ship on their own:
find and replace as a bar above the text instead of a dialog, a state line (words, saved, version) that stays in view, comments in the
margin of the page view, a calmer toolbar (less icons in a row, the less used ones in a menu), and a phone layout.

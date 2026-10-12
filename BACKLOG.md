# Backlog

Ideas and wishes that are not built yet, newest last. Delete an item when it ships (the feature then belongs in `docs/FEATURES.md`).
Shipped so far from this list: *Hotkeys for comments and more*, *Export from the editor*, *Releases to a particular account*, *Remember the view and two pages side by side*, *Logo on the invoice* (it was there: checked, only the unit column got wider) (see `git log`).

## The editor: comments in the margin of the page view

The rest of the overhaul is done (search bar, state line that stays in view, calmer toolbar, phone layout, full screen). Left: in the page
view the comments could stand in the margin next to their paragraph instead of in the side panel.

## Ping pong with the customer: the customer's file is the next version

Wanted: upload the file of the customer, correct it in passes (*Änderung 1 ... 4*), send it back, get *Datei vom Kunden v2*, correct again, and so on,
every file being one more version of the same document, so that any two of them can be compared.

Plan: a new origin "Datei vom Kunden" (`VersionOrigin`, stored as text, so no data migration): a new file uploaded to an existing document becomes a
new version with the text of the file, the file kept with it. The comparison, the version page and the history then work as they are. The formatting
(page view, Word export with changes) comes from the file of the round: the version of the latest upload at or before the version in question; the
export of two versions from different rounds uses the file of the newer one. The upload sits on the document page ("Neue Datei vom Kunden") and in the
list of versions the origin shows as such.

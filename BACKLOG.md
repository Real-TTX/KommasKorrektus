# Backlog

Ideas and wishes that are not built yet, newest last. Delete an item when it ships (the feature then belongs in `docs/FEATURES.md`).
Shipped so far from this list: *Hotkeys for comments and more* and *Export from the editor* (see `git log`).

## Logo on the invoice

A logo on offers and invoices already exists (*Vorlagen* → *Logo und Titel*: PNG, JPEG or SVG up to 2 MB, left or right, 8 to 40 mm,
shown in the live preview and in the issued PDF). It is to be checked end to end (upload, preview, issued PDF, ZUGFeRD PDF, offer);
what else is wanted has not been said: other places (footer, watermark), one logo for the whole company instead of one per template.

## Releases to a particular user, not only a public URL

Today a release is an anonymous link (`/s/<token>`, optionally with a password): whoever has the link sees it. Wanted: release a project
or a document to **a particular account** as well, so that the person signs in and finds it in their own list.

Plan: a `ShareLink` may be bound to an account (`UserId`). The link then asks for the sign-in (no password), is "not found" for any other
account, and shows what a link shows (changes or clean text, download, remarks, approval, period, take back). The account finds its
releases in a list ("Mit mir geteilt") and the log shows the account instead of an address hash. Staff and client accounts alike.
Open: an e-mail to the person when something is released to them.

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

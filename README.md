# Custom Stylesheet Editor

*This page is the English user guide, [CustomStyEditor_UserGuide.rtf](CustomStyEditor_UserGuide.rtf). The guide is also available in Spanish ([CustomStyEditor_UserGuide_sp.rtf](CustomStyEditor_UserGuide_sp.rtf)) and French ([CustomStyEditor_UserGuide_fr.rtf](CustomStyEditor_UserGuide_fr.rtf)).*

This tool creates and edits a Paratext project's *custom.sty* file. A custom.sty changes how Paratext shows the text of particular markers (for example making \q lines blue or \ip italic). It can also change where a marker may occur, rename markers, add new markers, or remove markers from the project's stylesheet. The tool changes only the lines you edit. Everything else in the file, including comments, stays exactly as it was. It is available as a standalone program and as a Paratext plugin on the Tools menu.

## 1. Opening the tool

**From Paratext (plugin):** open the project you want to work on, then choose Tools > Custom Stylesheet Editor... The tool opens that project's custom.sty file, with its menus in the same language as Paratext's (English if Paratext uses a language the tool does not have). If the project does not have a custom.sty file yet, the tool opens with an empty list, and File > Save creates the file in the project's folder. The plugin must be installed once first (see below).

**Installing the plugin:** download Install_Phils_tools.exe from the Paratext_Plugins folder (if your browser warns about the file, choose to keep it) and close Paratext. Double-click Install_Phils_tools.exe. If Windows says it protected your PC, click “More info”, then “Run anyway” (the installer is not signed yet). Tick Custom Stylesheet Editor and click “Install / update ticked”. If Windows asks for permission, click Yes. The installer puts the plugin into every Paratext 9 installation on the computer, in plugins\CustomStyEditor, and checks that Paratext can load it. The same window later shows “Update available” when there is a newer version, updates the plugin, removes it (“Remove ticked”), and opens this guide (“User guide”).

## 2. The project bar

The blue bar shows the project and its **base stylesheet**: the stylesheet named in the project's Settings.xml (normally usfm.sty, kept in the Paratext projects folder). The tool reads it to list the standard (“stock”) markers and to show each marker's stock values next to your changes. If the base stylesheet cannot be found, the bar says so in red; you can still edit the file, but there is no list of stock markers.

The yellow banner is a reminder: Paratext reads custom.sty only when it starts, so after saving, close Paratext and open it again to see the changes. Switching the view is not enough.

## 3. The marker list

The list on the left shows every marker that custom.sty mentions, in file order, with its name, the settings the file changes, and a status: *new* (not in the base stylesheet), *removed*, *commented out*, or *⚠* *check* when something needs attention (point at the row to see why). The buttons below the list:

- **Add stock marker...** — pick a marker of the base stylesheet to customize. A marker that is not in custom.sty yet is added at the end of the file.
- **New marker...** — define a marker the base stylesheet does not have. Paratext needs a name and a style type for it; the tool suggests an end marker for character and note styles. USFM keeps markers starting with **z** for project use, so the tool asks before adding any other name. New markers are added at the end of the file.
- **Remove from stylesheet** — writes `\Marker pn -` so that Paratext no longer offers the marker. The button then reads **Restore marker**. (Write the space before the dash: Paratext reads `\Marker pn-` as a new marker named “pn-”.)
- **Delete** (or the Delete key) — deletes every line of the entry from custom.sty, so the marker goes back to its stock style.

The **Off** checkbox at the right end of each row comments the whole entry out (each of its lines gets a leading #) instead of deleting it, so you can switch it off without losing it. Unticking it uncomments the entry again. Entries that were already commented out in the file, such as `# \Marker fe`, are recognized and can be switched back on the same way.

## 4. Editing a marker

Select a marker to see its settings on the right, in groups: Text format, Font and color, Paragraph (only for paragraph styles), Name and type, Where it may occur, and Other. Each row has:

- **a tick box** — ticked when Paratext uses the line; untick it to comment just that line out with #. It is greyed when custom.sty has no line for the setting;
- **the keyword** — in bold when custom.sty sets it (point at it for a short explanation);
- **the value** — typed values are saved when you press Enter or leave the box (Esc undoes the typing);
- **the stock value** — for example `usfm.sty: 12`, what the marker has when custom.sty does not change it;
- **✕** — removes the line from custom.sty, so the stock value applies again.

**Yes/No settings** (Bold, Italic, Underline, Smallcaps, Superscript, Subscript, Regular, NotRepeatable) have three choices: *(not set)* keeps the stock value, *Yes* writes `\Bold`, and *No* writes `\Bold -`, which turns off a stock setting. *Regular* turns bold, italic and superscript off.

**Color:** *Choose color...* opens a color picker and writes an exact color in the older number form with a comment naming it, for example `\Color 16711680 # Blue` (a color written as x + hex, such as `xFF0000`, stays in that form). Or pick a *Paratext color name*, which writes for example `\ColorName text_blue`; Paratext's help recommends color names when team members have low vision or color vision deficiency.

**Where it may occur:** *Choose...* next to OccursUnder opens a list of all markers to tick, with NEST (lets a character style nest inside another one), *Copy stock list* (start from the base stylesheet's list) and *Clear*. You can also type the markers, separated by spaces. Markers that are not in the stylesheet are flagged in red.

**Name and type:** edit \Name to translate the marker names that Paratext shows in its marker list. TextProperties words are *added* to the base stylesheet's text properties; custom.sty cannot take any away.

**Other lines:** lines with keywords the tool has no row for are listed at the bottom and kept unchanged.

A commented-out or removed entry is shown read-only; switch it back on first to edit it.

## 5. Preview and file lines

Below the settings, the **Preview** shows sample text with the marker's resulting style (stock values plus your changes, in the project's font). It is an approximation of Paratext's Standard view. Next to it, **Lines in custom.sty** shows the marker's lines exactly as they will be saved, with their line numbers.

## 6. How the file is changed

Paratext reads custom.sty from top to bottom: lines starting with # are ignored, each other line belongs to the \Marker line above it, and a later value replaces an earlier one. The tool follows the same rules:

- A changed value is changed on its own line; nothing else in the file moves. Comments, blank lines, the keyword spelling (\color or \Color) and the file's encoding are kept.
- New markers are always added at the end of the file.
- A new setting for a marker that is already in the file is added at the end of that marker's entry. It is not added as a second \Marker entry at the end of the file, because Paratext reports a second entry for the same marker as a “duplicate style definition”.

The tool warns (⚠ check) about things Paratext would report or handle unexpectedly: a marker or setting given more than once, `\Marker pn-` without the space, a new marker without \Name or \StyleType, unknown markers in OccursUnder, unknown TextProperties words, and active lines under a commented-out \Marker line (Paratext applies those to the marker before).

## 7. Saving

Use File > Save (Ctrl+S) to save, or File > Save As (Ctrl+Shift+S) to choose a new location. A project's custom.sty belongs directly in the project's folder, next to its Settings.xml. A new file is saved as UTF-8 with Windows line endings. Then close Paratext and open it again to see the changes.

## 8. Changing the interface language

The Language dropdown switches the tool's menus, labels and messages between English, Spanish, French, Portuguese and Chinese (Simplified). When opened from the Paratext plugin, the tool starts in the same language as Paratext's menus. Keywords such as \Bold and values such as Paragraph or VerseText are never translated, because they are what Paratext reads in the file.

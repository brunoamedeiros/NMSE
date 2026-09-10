# Personal fork tools

This fork adds three tools without changing alliance or station ownership data.

The independent `feature/editor-enhancements` branch also includes the Deep Space
discovery tab and selected-section JSON viewer. Cosmos save mappings, extracted
game resources and the supported-version declaration belong exclusively to
`feature/cosmos-compatibility`. Both feature branches are integrated in `develop`.

## Deep Space and JSON section viewer

The Catalogue's **Deep Space** tab shows saved points of interest with galaxy,
portal and coordinate details, and supports removing selected discovery entries.
It does not claim station ownership or change alliance membership.

In **Raw JSON Editor > Tree View**, right-click a node and choose **View Selected
JSON...** to inspect only that value and its children. The window is read-only
and offers a copyable path and Copy button. It handles objects, arrays and scalar
values without marking the save modified. Right-click selects the node beneath
the pointer. Both features have interface strings in all existing UI languages.

## Search All Inventories

Open **Tools > Search All Inventories**, or press **Ctrl+Shift+F**. Search by the
localized item name, item ID, inventory name, or a combination of words. Results
show the amount and the column/row (starting at 1). Select a result and choose
**Open Slot**, press Enter, or double-click to open the owning inventory and
select that slot.

Search covers exosuit cargo and technology, owned multitools and starships,
freighter inventories, the six supported exocraft types, ten storage containers,
and special storage inventories. It uses the active main/expedition context.
The unused vehicle entry and unowned ship/tool slots are excluded. Binary
technology IDs and procedural seed suffixes are supported. Search itself does
not change inventory data.

## In-game Shortcuts

Open **Tools > In-game Shortcuts**. Each context (on foot, starship, exocraft)
has ten slots, numbered 0–9. On PC these are the quick-menu number-key bindings.
Choose **Keep current** to preserve a binding exactly, including actions not
offered in the dropdown. Supported choices include photo mode, camera toggles,
freighter/Anomaly summoning, Minotaur AI, economy scanning, cargo scan deflection,
and exocraft scan selection, as appropriate for the context.

**Apply** changes only the editor's in-memory data. **Cancel** discards the draft.
Use the normal Save command to write to the game save. Importing a profile also
stays in the draft until Apply. Export includes the current draft for all three
contexts. Profiles are versioned NMSE JSON files; arbitrary save files are not
accepted as profiles. Existing emotes, recharge bindings, and other advanced
bindings are preserved/exported/imported but are not newly configured by the
dropdowns. Inventory-dependent bindings may require adjustment in-game when
transferring a profile to a different save.

The implementation follows the observed `HotActions[n].KeyActions[0..9]` layout
documented in [upstream issue 76](https://github.com/vectorcmdr/NMSE/issues/76)
and its author's [example script](https://github.com/okranger1777/nms-mission-progress/blob/main/scripts/hotkeys.py).
No external script is executed. If the active context has missing or unfamiliar
shortcut structure, the editor reports it instead of synthesizing save data.

## Review Changes

Open **Tools > Review Changes** at any time, or use Save to review changes before
they are written. The list includes main-save, expedition, shared, and loaded
account data. It compares against the data loaded from disk or the last fully
successful save. Importing replacement JSON keeps the earlier comparison baseline.
Canceling a save review does not write a save or change the Save As destination.

Rows display readable field names, old/new values, and owner names where present.
Hover for the source path or use **Copy Summary**. Null and absent fields remain
distinct, and large integer values retain their precision. Added/removed objects
and arrays are summarized by size; the Raw JSON editor provides their full detail.
The list is capped at 2,000 rows and explicitly reports when more changes exist.
The baseline is compressed in memory. The existing Raw JSON change viewer keeps
its own baseline and behavior.

The new interface strings have English and Brazilian Portuguese translations.
Other interface languages use the existing English fallback for these new labels.

## Validation

`PersonalToolsTests` covers inventory scope and identity, binary IDs, inactive
contexts, draft isolation, unknown shortcut preservation, invalid profile
rejection, structural comparisons, large integers, and comparison limits.
Off-screen WinForms checks also exercise the new dialogs and inventory navigation
against a copied Cosmos save. Game behavior for newly assigned shortcuts still
needs an in-game check; reading/writing the save alone cannot verify activation.

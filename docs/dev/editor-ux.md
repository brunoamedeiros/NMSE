# Editor UX improvements

Branch: `feature/editor-ux`. Includes the earlier item picker and search feedback changes.

## Inventory editing

- Double-click an unlocked empty slot to add an item. Double-click an occupied slot to focus its amount field in the sidebar.
- Below the amount summary, enter a quantity and choose Apply or press Enter. Escape resets the input. Selecting another slot discards an unapplied amount; it never applies that amount to another item. Recharge fills the charge field for compatible technology, ready to Apply.
- The right panel retains Replace and Remove actions. Advanced controls retain the existing ID, seed, damage, stack-limit, and sidebar picker controls. Inline Apply changes only the amount and creates one inventory Undo step; it does not apply pending Advanced fields.
- Add and Replace open a searchable compatible-item list with an icon and description preview, quantity, and a recently used filter. Recent selections store up to ten IDs in the editor configuration and update only after confirmation.
- Searches accept item names, IDs, and categories, including Portuguese names without accents. Cargo/technology guidance explains the available choices.
- Applying a change displays confirmation and briefly highlights its slot. This changes the in-memory save; the user must still Save to write to disk.

## Save state and undo

- Edited inventory slots have a gold dot, and affected tabs have a dot in their titles. The status bar distinguishes editor changes from saved data.
- Edit menu and toolbar provide inventory Undo/Redo, with Ctrl+Z and Ctrl+Y. Text fields and the Raw JSON tree retain their own undo shortcuts.
- Inventory history groups changes across inventories into one transaction, so a transfer undoes both source and destination. Other player, ship, and account edits are preserved.
- History is limited to 50 transactions and a 32 MiB snapshot budget. Saving updates the clean baseline while preserving history. Loading or importing another save resets history. Changes made outside the recorded inventory operations invalidate stale history rather than overwrite later data.

## Review Changes

- Review Changes uses one table with Context, What changed, Before, and After. Each changed detail appears once, using item names, quantities and slot positions. Small reviews open in a shorter window; large reviews render visible rows as needed.
- Show advanced details (JSON) reveals the selected change's raw values below the table. Copy Summary uses the readable comparison. Large previews explicitly indicate omitted details, and counts refer to original changes rather than their individual detail rows.
- A grouped change uses Revert this group and a confirmation explaining that the entire group will be restored, including details outside the preview.
- Open location and double-click reveal an inventory slot or the exact save/account JSON field.
- Revert this change restores the selected value from the saved baseline, after checking the current value still matches the review.
- Structural list changes are reviewed and reverted together to avoid shifting unrelated indices. Binary IDs and exact numeric values are preserved.
- Open or Revert during the pre-save review cancels that save attempt; the user can inspect the result and save again.
- Pending Raw JSON Text/Split edits block inventory undo and change review, including saving, with a preservation message. The existing raw text editor does not synchronize its buffer into the authoritative save; these operations must not refresh away that buffer or claim it was saved. Copy the edited text before changing views. Raw text synchronization is outside this feature.

## Verification

The editor test suite includes regression coverage for history, recent items, typed review paths, binary values, stale changes, and structural list reversions. Isolated Windows UI probes exercise Add/Edit/Replace/Cancel, the simplified panel, English/Portuguese labels, save/account navigation, transfers, and save/undo lifecycle using copied save data.

Readable review has pure-data tests for additions, removals, replacements, coordinates, reordered slots, unknown items, exact values, and preview limits. For a visual check, add 100 Oxygen and unlock a slot, open Review Changes, and select each group. The comparison should show Empty slot → Oxygen × 100 and Locked → Unlocked at the affected positions. Toggle Advanced details to inspect the same change as JSON, then toggle back. Revert this group should confirm its scope before restoring the group in the editor.

using System.Globalization;
using NMSE.Data;
using NMSE.Models;

namespace NMSE.Core;

/// <summary>Read-only, human-readable detail for a single exact change/revert group.</summary>
internal static class ChangePresentationLogic
{
    internal sealed record Row(string Label, string Before, string After);
    internal sealed record Presentation(string Area, string Title, string Before, string After,
        IReadOnlyList<Row> Rows, bool Grouped, bool Truncated);

    internal static Presentation Build(ChangeSummaryLogic.Change change, GameItemDatabase? database = null, JsonObject? currentRoot = null) =>
        new Builder(database).Build(change, currentRoot);

    private static string T(string key, string fallback) => UiStrings.GetOrNull("summary.readable." + key) ?? fallback;
    private static string F(string key, string fallback, params object[] args) => string.Format(CultureInfo.CurrentCulture, T(key, fallback), args);
    private static string Join(string parent, string child) => parent.Length == 0 ? child : parent + " / " + child;
    private static string Field(string key) => key switch
    {
        "Slots" => T("items", "Inventory items"),
        "ValidSlotIndices" => T("unlocked_slots", "Unlocked slots"),
        "SpecialSlots" => T("special_slots", "Special slots"),
        "Id" => T("item", "Item"),
        "Amount" => T("quantity", "Quantity"),
        "MaxAmount" => T("capacity", "Stack capacity"),
        "DamageFactor" => T("damage", "Damage"),
        "FullyInstalled" => T("installed", "Fully installed"),
        _ => ChangeSummaryLogic.Friendly(key)
    };
    private static string Position((int X, int Y) position) => F("slot", "Slot ({0}, {1})", position.X + 1L, position.Y + 1L);

    private sealed class Builder(GameItemDatabase? database)
    {
        private const int Limit = 300;
        private const int TextLimit = 1200;
        private readonly List<Row> _rows = [];
        private bool _truncated;
        private bool _valueTruncated;
        private bool _inventoryItemId;
        private bool _itemReferences;
        private bool _playerCurrency;
        private string? _slotCoordinateKey;

        private string DisplayField(string key) => key == _slotCoordinateKey
            ? T(key == "X" ? "column" : "row", key == "X" ? "Column" : "Row") : Field(key);

        internal Presentation Build(ChangeSummaryLogic.Change change, JsonObject? currentRoot)
        {
            string key = change.Tokens.LastOrDefault()?.Key ?? "";
            if (key is "X" or "Y" &&
                (change.Tokens.Count >= 3 && change.Tokens[^3].Key == "ValidSlotIndices" && change.Tokens[^2].Index != null ||
                 change.Tokens.Count >= 4 && change.Tokens[^4].Key is "Slots" or "SpecialSlots" &&
                    change.Tokens[^3].Index != null && change.Tokens[^2].Key == "Index"))
                _slotCoordinateKey = key;
            string title = change.Field.Length > 0 ? change.Field : Field(key);
            if (title == key) title = DisplayField(key);
            else if (_slotCoordinateKey != null && title.EndsWith(" / " + key, StringComparison.Ordinal))
                title = title[..^key.Length] + DisplayField(key);
            string area = change.Area;
            _itemReferences = change.Tokens.Any(token => token.Key is "KnownTech" or "KnownProducts");
            bool grouped = change.BeforeValue is JsonObject or JsonArray || change.AfterValue is JsonObject or JsonArray;
            _playerCurrency = !grouped && ChangeSummaryLogic.IsPlayerCurrency(change.Tokens, change.Scope);
            if (change.Scope == ChangeReviewLogic.DataScope.Account)
                area = UiStrings.GetOrNull("summary.account") ?? T("account", "Account");
            else if (currentRoot != null && ChangeReviewLogic.TryFindInventory(currentRoot, change, out var location, out int? x, out int? y))
            {
                area = Join(change.Area, location!.Label);
                title = DisplayField(key);
                if (x.HasValue && y.HasValue)
                {
                    area = Join(area, Position((x.Value, y.Value)));
                    int slotToken = change.Tokens.ToList().FindLastIndex(token => token.Key == "Slots");
                    if (slotToken >= 0 && ChangeReviewLogic.TryRead(currentRoot, change.Tokens.Take(slotToken + 2).ToArray(), out var slot)
                        && slot is JsonObject itemSlot)
                    {
                        title = ItemName(itemSlot.Get("Id")) + " · " + title;
                        _inventoryItemId = key == "Id";
                    }
                }
            }
            if (area.Length == 0) area = T("save", "Save");
            if (title.Length == 0) title = T("change", "Change");

            Walk(change.BeforeValue, change.HadBefore, change.AfterValue, change.HasAfter, "", key, 0);
            // The group remains an exact, atomic array edit even when slot contents are only reordered.
            if (_rows.Count == 0 && (change.HadBefore != change.HasAfter || !ChangeReviewLogic.Equal(change.BeforeValue, change.AfterValue)))
                Add(title, Scalar(change.BeforeValue, change.HadBefore, key), Scalar(change.AfterValue, change.HasAfter, key));

            string before = grouped ? Summary(before: true) : _rows.FirstOrDefault()?.Before ?? Scalar(change.BeforeValue, change.HadBefore, key);
            string after = grouped ? Summary(before: false) : _rows.FirstOrDefault()?.After ?? Scalar(change.AfterValue, change.HasAfter, key);
            return new(Text(area), Text(title), before, after, _rows, grouped, _truncated || _valueTruncated);
        }

        private void Add(string label, string before, string after)
        {
            if (_rows.Count == Limit) { _truncated = true; return; }
            if (before == after) after += " · " + T("stored_value_changed", "Stored value changed; see Advanced");
            _rows.Add(new(Text(label.Length == 0 ? T("value", "Value") : label), before, after));
        }

        private void AddValues(string label, object? before, bool hadBefore, object? after, bool hasAfter, string key)
        {
            string oldText = Scalar(before, hadBefore, key), newText = Scalar(after, hasAfter, key);
            if (oldText == newText && TypeName(before, hadBefore) != TypeName(after, hasAfter))
            {
                oldText += " (" + TypeName(before, hadBefore) + ")";
                newText += " (" + TypeName(after, hasAfter) + ")";
            }
            Add(label, oldText, newText);
        }

        private static string TypeName(object? value, bool exists) => !exists ? T("missing", "Not present") : value switch
        {
            null => T("null", "No value"),
            string => T("text_type", "Text"),
            bool => T("boolean_type", "Yes/No value"),
            BinaryData => T("binary_type", "Binary data"),
            JsonObject => T("object_type", "Field group"),
            JsonArray => T("array_type", "List"),
            _ => T("number_type", "Number")
        };

        private string Text(string value)
        {
            if (value.Length <= TextLimit) return value;
            _valueTruncated = true;
            return value[..TextLimit] + "…";
        }

        private void Walk(object? before, bool hadBefore, object? after, bool hasAfter, string label, string key, int depth)
        {
            if (_truncated || hadBefore == hasAfter && ChangeReviewLogic.Equal(before, after)) return;
            if (depth > 64) { _truncated = true; return; }
            if ((before is JsonArray || !hadBefore) && (after is JsonArray || !hasAfter))
            {
                var oldArray = before as JsonArray;
                var newArray = after as JsonArray;
                if (key is "Slots" or "ValidSlotIndices" or "SpecialSlots" &&
                    TryPositions(oldArray, key, out var oldSlots) && TryPositions(newArray, key, out var newSlots))
                {
                    int initialRows = _rows.Count;
                    Positioned(oldSlots, newSlots, key, label, depth);
                    if (_rows.Count == initialRows && hadBefore && hasAfter && !_truncated)
                        Add(Join(label, T("order", "Order")), T("original_order", "Original order"), T("reordered", "Reordered; contents unchanged"));
                    return;
                }
                int length = Math.Max(oldArray?.Length ?? 0, newArray?.Length ?? 0);
                for (int i = 0; i < length && !_truncated; i++)
                {
                    bool oldExists = i < (oldArray?.Length ?? 0), newExists = i < (newArray?.Length ?? 0);
                    Walk(oldExists ? oldArray!.Get(i) : null, oldExists, newExists ? newArray!.Get(i) : null, newExists,
                        Join(label, F("entry", "Entry {0}", i + 1)), "", depth + 1);
                }
                if (length == 0) Add(label, Scalar(before, hadBefore, key), Scalar(after, hasAfter, key));
                return;
            }
            if ((before is JsonObject || !hadBefore) && (after is JsonObject || !hasAfter))
            {
                var oldObject = before as JsonObject;
                var newObject = after as JsonObject;
                var keys = (oldObject?.Names() ?? []).Concat(newObject?.Names() ?? []).Distinct(StringComparer.Ordinal).ToArray();
                foreach (string child in keys)
                {
                    Walk(oldObject?.Get(child), oldObject?.Contains(child) ?? false, newObject?.Get(child), newObject?.Contains(child) ?? false,
                        Join(label, Field(child)), child, depth + 1);
                    if (_truncated) break;
                }
                if (keys.Length == 0) Add(label, Scalar(before, hadBefore, key), Scalar(after, hasAfter, key));
                return;
            }
            AddValues(label.Length == 0 ? DisplayField(key) : label, before, hadBefore, after, hasAfter, key);
        }

        private void Positioned(Dictionary<(int X, int Y), JsonObject> before, Dictionary<(int X, int Y), JsonObject> after,
            string key, string label, int depth)
        {
            foreach (var position in before.Keys.Concat(after.Keys).Distinct().OrderBy(p => p.Y).ThenBy(p => p.X))
            {
                before.TryGetValue(position, out var old);
                after.TryGetValue(position, out var current);
                if (ChangeReviewLogic.Equal(old, current)) continue;
                string rowLabel = Join(label, Position(position));
                if (key == "ValidSlotIndices")
                {
                    if (old != null && current != null) WalkSlotFields(old, current, rowLabel, depth);
                    else Add(rowLabel, T(old == null ? "locked" : "unlocked", old == null ? "Locked" : "Unlocked"),
                        T(current == null ? "locked" : "unlocked", current == null ? "Locked" : "Unlocked"));
                }
                else if (key == "SpecialSlots")
                {
                    if (old != null && current != null && Special(old) == Special(current)) WalkSlotFields(old, current, rowLabel, depth);
                    else Add(rowLabel, Special(old), Special(current));
                }
                else if (old == null || current == null || !ChangeReviewLogic.Equal(old.Get("Id"), current.Get("Id")))
                {
                    Add(rowLabel, SlotSummary(old), SlotSummary(current));
                    // Replacement previews include changed metadata as well as the visible item/quantity.
                    if (old != null && current != null)
                        WalkSlotFields(old, current, rowLabel, depth);
                }
                else WalkSlotFields(old, current, Join(rowLabel, ItemName(current.Get("Id"))), depth);
                if (_truncated) break;
            }
        }

        private void WalkSlotFields(JsonObject before, JsonObject after, string label, int depth)
        {
            foreach (string key in before.Names().Concat(after.Names()).Distinct(StringComparer.Ordinal))
            {
                if (key is "Id" or "Index") continue;
                Walk(before.Get(key), before.Contains(key), after.Get(key), after.Contains(key), Join(label, Field(key)), key, depth + 1);
                if (_truncated) break;
            }
        }

        private string SlotSummary(JsonObject? slot)
        {
            if (slot == null) return T("empty_slot", "Empty slot");
            string result = ItemName(slot.Get("Id"));
            if (slot.Contains("Amount")) result += " × " + Scalar(slot.Get("Amount"), true, "Amount");
            if (slot.Get("DamageFactor") is { } damage && !ChangeReviewLogic.Equal(damage, 0) && !ChangeReviewLogic.Equal(damage, 0.0))
                result += " · " + F("damage_value", "Damage: {0}", Scalar(damage, true, "DamageFactor"));
            if (slot.Get("FullyInstalled") is false) result += " · " + T("not_installed", "Not fully installed");
            return result;
        }

        private string Special(JsonObject? slot)
        {
            if (slot == null) return T("normal_slot", "Normal slot");
            return slot.GetObject("Type")?.GetString("InventorySpecialSlotType") switch
            {
                "TechBonus" => T("supercharged", "Supercharged"),
                "BlockedByBrokenTech" => T("blocked", "Blocked by damaged technology"),
                { } value => ChangeSummaryLogic.Friendly(value),
                _ => T("special_slot", "Special slot")
            };
        }

        private string ItemName(object? value)
        {
            string id = InventorySearchLogic.DisplayItemId(value);
            string normalized = id.TrimStart('^');
            var item = database?.GetItem(normalized) ?? database?.GetItem(normalized.Split('#')[0]);
            if (item == null && TechPacks.Dictionary.TryGetValue("^" + normalized.Split('#')[0], out var pack))
                item = database?.GetItem(pack.Id);
            if (!string.IsNullOrWhiteSpace(item?.Name)) return Text(item.Name);
            if (value is BinaryData) return T("unknown_binary_item", "Unknown item (binary ID)");
            if (normalized.Length == 0) return T("unknown_item_no_id", "Unknown item");
            return F("unknown_item", "Unknown item ({0})", Text(id));
        }

        private string Scalar(object? value, bool exists, string key)
        {
            if (!exists) return T("missing", "Not present");
            if (_playerCurrency && ChangeSummaryLogic.TryFormatCurrency(value, out string amount)) return amount;
            if (key == _slotCoordinateKey && value is int or long)
                return (Convert.ToDecimal(value, CultureInfo.InvariantCulture) + 1).ToString("N0", CultureInfo.CurrentCulture);
            if (_inventoryItemId && key == "Id" && value is string or BinaryData) return ItemName(value);
            if (key == "QuickMenuActions" && value is string action)
            {
                var option = Enumerable.Range(0, 3).SelectMany(HotkeyLogic.Options).FirstOrDefault(x => x.Id == action);
                if (option != null) return UiStrings.GetOrNull("hotkeys.action." + option.LabelKey) ?? ChangeSummaryLogic.Friendly(action);
            }
            return value switch
            {
                null => T("null", "No value"),
                string text when text.Length == 0 => T("empty_text", "Empty text"),
                string text when _itemReferences && text.StartsWith('^') => ItemName(text),
                string text => Text(text),
                bool flag => UiStrings.GetOrNull(flag ? "summary.yes" : "summary.no") ?? (flag ? "Yes" : "No"),
                BinaryData binary => F("binary", "Binary data ({0} bytes)", binary.ToByteArray().Length),
                RawDouble raw => raw.Text,
                int number => number.ToString("N0", CultureInfo.CurrentCulture),
                long number => number.ToString("N0", CultureInfo.CurrentCulture),
                double number => number.ToString("G17", CultureInfo.CurrentCulture),
                float number => number.ToString("G9", CultureInfo.CurrentCulture),
                JsonArray { Length: 0 } => T("empty_list", "No entries"),
                JsonObject { Length: 0 } => T("empty_object", "No fields"),
                JsonArray array => F("entries", "{0} entries", array.Length),
                JsonObject obj => F("fields", "{0} fields", obj.Length),
                _ => Convert.ToString(value, CultureInfo.CurrentCulture) ?? T("null", "No value")
            };
        }

        private string Summary(bool before)
        {
            var snippets = _rows.Take(3).Select(row => row.Label + ": " + (before ? row.Before : row.After));
            string result = string.Join("; ", snippets);
            if (_truncated || _valueTruncated) result += " · " + T("more_not_shown", "Additional changes not shown");
            else if (_rows.Count > 3) result += " · " + F("more_changes", "+ {0} more changes", _rows.Count - 3);
            return result;
        }
    }

    private static bool TryPositions(JsonArray? array, string key, out Dictionary<(int X, int Y), JsonObject> result)
    {
        result = new();
        if (array == null) return true;
        for (int i = 0; i < array.Length; i++)
        {
            if (array.Get(i) is not JsonObject entry) return false;
            var index = key == "ValidSlotIndices" ? entry : entry.Get("Index") as JsonObject;
            if (index == null || !Integer(index.Get("X"), out int x) || !Integer(index.Get("Y"), out int y) ||
                !result.TryAdd((x, y), entry)) return false;
        }
        return true;
    }

    private static bool Integer(object? value, out int number)
    {
        if (value is int integer) { number = integer; return true; }
        if (value is long longer && longer >= int.MinValue && longer <= int.MaxValue) { number = (int)longer; return true; }
        number = 0;
        return false;
    }
}

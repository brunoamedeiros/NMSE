using NMSE.Data;
using NMSE.Models;

namespace NMSE.Core;

internal static class InventorySearchLogic
{
    internal sealed record InventoryLocation(JsonObject Inventory, string Label, int Tab, string Collection = "", int Index = -1);
    internal sealed record Result(InventoryLocation Location, string ItemId, string Name, long Amount, int X, int Y);

    internal static List<InventoryLocation> Enumerate(JsonObject save)
    {
        var result = new List<InventoryLocation>();
        var player = save.GetObject("PlayerStateData");
        if (player == null) return result;
        void Add(JsonObject owner, string key, string label, int tab, string collection = "", int index = -1)
        {
            if (owner.GetObject(key) is { } inv && !result.Any(x => ReferenceEquals(x.Inventory, inv)))
                result.Add(new(inv, label, tab, collection, index));
        }
        string T(string key) => UiStrings.Get(key);
        Add(player, "Inventory", T("tab.exosuit") + " / " + T("tools_plus.cargo"), 1);
        Add(player, "Inventory_TechOnly", T("tab.exosuit") + " / " + T("tools_plus.tech"), 1);
        if (player.GetArray("Multitools") is not { Length: > 0 })
            Add(player, "WeaponInventory", T("tab.multitools"), 2);
        Add(player, "FreighterInventory", T("tools_plus.freighter") + " / " + T("tools_plus.cargo"), 4);
        Add(player, "FreighterInventory_TechOnly", T("tools_plus.freighter") + " / " + T("tools_plus.tech"), 4);
        foreach (var (key, title, tab) in new[] { ("ShipOwnership", "tab.starships", 3), ("Multitools", "tab.multitools", 2), ("VehicleOwnership", "tab.exocraft", 5) })
        {
            if (player.GetArray(key) is not { } entries) continue;
            var owned = key switch
            {
                "ShipOwnership" => StarshipLogic.BuildShipList(entries).Select(x => x.DataIndex).ToHashSet(),
                "Multitools" => MultitoolLogic.BuildToolList(entries).Select(x => x.DataIndex).ToHashSet(),
                _ => ExocraftLogic.VehicleTypes.Select(x => x.Index).ToHashSet()
            };
            for (int i = 0; i < entries.Length; i++)
            {
                if (!owned.Contains(i)) continue;
                if (entries.Get(i) is not JsonObject owner) continue;
                string label = $"{T(title)} {i + 1}";
                if (key == "VehicleOwnership") label = ExocraftLogic.GetLocalisedVehicleTypeName(ExocraftLogic.VehicleTypes.Single(x => x.Index == i).Name);
                if (!string.IsNullOrWhiteSpace(owner.GetString("Name"))) label += " — " + owner.GetString("Name");
                if (key == "Multitools") Add(owner, "Store", label, tab, key, i);
                else
                {
                    Add(owner, "Inventory", label + " / " + T("tools_plus.cargo"), tab, key, i);
                    Add(owner, "Inventory_TechOnly", label + " / " + T("tools_plus.tech"), tab, key, i);
                }
            }
        }
        for (int i = 0; i < BaseLogic.ChestInventoryKeys.Length; i++)
        {
            string key = BaseLogic.ChestInventoryKeys[i];
            string name = player.GetObject(key)?.GetString("Name") ?? "";
            Add(player, key, UiStrings.Format("base.chest_tab", i) + (name.Length > 0 ? " — " + name : ""), 7, key, i);
        }
        foreach (var (key, label, _) in BaseLogic.StorageInventories)
            Add(player, key, label, 7, key);
        return result;
    }

    internal static List<Result> Index(JsonObject save, GameItemDatabase database)
    {
        var results = new List<Result>();
        foreach (var location in Enumerate(save))
        {
            if (location.Inventory.GetArray("Slots") is not { } slots) continue;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots.Get(i) is not JsonObject slot) continue;
                string id = DisplayItemId(slot.Get("Id"));
                if (string.IsNullOrWhiteSpace(id.Trim('^'))) continue;
                string normalized = id.TrimStart('^');
                var item = database.GetItem(normalized) ?? database.GetItem(normalized.Split('#')[0]);
                if (item == null && TechPacks.Dictionary.TryGetValue("^" + normalized.Split('#')[0], out var pack))
                    item = database.GetItem(pack.Id);
                if (slot.GetObject("Index") is not { } position) continue;
                results.Add(new(location, id, item?.Name ?? id, slot.GetLong("Amount"), position.GetInt("X"), position.GetInt("Y")));
            }
        }
        return results;
    }

    internal static string DisplayItemId(object? value)
    {
        if (value is JsonObject wrapper) value = wrapper.Get("Id");
        if (value is not BinaryData binary) return value as string ?? "";
        var bytes = binary.ToByteArray();
        if (bytes.Length == 0 || bytes[0] != '^') return binary.ToString();
        int hash = Array.IndexOf(bytes, (byte)'#', 1);
        int end = hash < 0 ? bytes.Length : hash;
        return "^" + Convert.ToHexString(bytes.AsSpan(1, end - 1)) +
            (hash < 0 ? "" : System.Text.Encoding.Latin1.GetString(bytes.AsSpan(hash)));
    }

    internal static IEnumerable<Result> Search(IEnumerable<Result> items, string query)
    {
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return items.Where(item => terms.All(term =>
            item.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || item.ItemId.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            item.Location.Label.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(item => item.Location.Label, StringComparer.CurrentCultureIgnoreCase);
    }
}

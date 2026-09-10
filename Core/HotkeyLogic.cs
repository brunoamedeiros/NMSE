using NMSE.Models;

namespace NMSE.Core;

internal static class HotkeyLogic
{
    internal sealed record ActionOption(string Id, string LabelKey, int Number = 0);
    internal static ActionOption[] Options(int context) => context switch
    {
        0 => [new("None", "none"), new("PhotoMode", "photo"), new("ThirdPersonCharacter", "camera"), new("VehicleAIToggle", "ai"), new("CallFreighter", "freighter")],
        1 => [new("None", "none"), new("PhotoMode", "photo"), new("ThirdPersonShip", "camera"), new("CallFreighter", "freighter"), new("SummonNexus", "anomaly"), new("EconomyScan", "economy", 1), new("CargoShield", "shield")],
        2 => [new("None", "none"), new("PhotoMode", "photo"), new("ThirdPersonVehicle", "camera"), new("VehicleScanSelect", "scan")],
        _ => throw new ArgumentOutOfRangeException(nameof(context))
    };

    internal static JsonArray ReadDraft(JsonObject save)
    {
        var actions = save.GetObject("PlayerStateData")?.GetArray("HotActions")
            ?? throw new InvalidDataException("hotkeys.unavailable");
        Validate(actions);
        return actions.DeepClone();
    }

    internal static void Validate(JsonArray actions)
    {
        if (actions.Length != 3) throw new InvalidDataException("hotkeys.invalid");
        for (int c = 0; c < 3; c++)
        {
            if (actions.Get(c) is not JsonObject group || group.GetArray("KeyActions") is not { Length: 10 } keys)
                throw new InvalidDataException("hotkeys.invalid");
            for (int k = 0; k < 10; k++)
            {
                if (keys.Get(k) is not JsonObject entry || string.IsNullOrWhiteSpace(entry.GetObject("Action")?.GetString("QuickMenuActions")) ||
                    entry.Get("Id") is not string || !IsInteger(entry.Get("Number")) || entry.GetObject("InventoryIndex") is not { } index ||
                    !IsInteger(index.Get("X")) || !IsInteger(index.Get("Y"))) throw new InvalidDataException("hotkeys.invalid");
            }
        }
    }

    private static bool IsInteger(object? value) => value is int || value is long l && l >= int.MinValue && l <= int.MaxValue;
    internal static JsonObject Entry(JsonArray draft, int context, int slot) => draft.GetObject(context).GetArray("KeyActions")!.GetObject(slot);

    internal static void SetAction(JsonArray draft, int context, int slot, ActionOption option)
    {
        if (!Options(context).Contains(option) || slot is < 0 or > 9) throw new ArgumentException("hotkeys.invalid");
        var entry = Entry(draft, context, slot);
        // Only replace the fields belonging to the binding. Preserve any newer game fields.
        entry.GetObject("Action")!.Set("QuickMenuActions", option.Id);
        entry.Set("Id", "^");
        entry.Set("Number", option.Number);
        entry.GetObject("InventoryIndex")!.Set("X", -1);
        entry.GetObject("InventoryIndex")!.Set("Y", -1);
    }

    internal static string Export(JsonArray draft)
    {
        Validate(draft);
        var profile = new JsonObject();
        profile.Add("Format", "NMSE.Hotkeys");
        profile.Add("Version", 1);
        profile.Add("HotActions", draft.DeepClone());
        return RawJsonLogic.SerializeValue(profile);
    }

    internal static JsonArray Import(string json)
    {
        if (json.Length > 1_000_000) throw new InvalidDataException("hotkeys.invalid");
        var profile = JsonObject.Parse(json);
        if (profile.GetString("Format") != "NMSE.Hotkeys" || profile.GetInt("Version") != 1 || profile.GetArray("HotActions") is not { } actions)
            throw new InvalidDataException("hotkeys.invalid");
        Validate(actions);
        return actions.DeepClone();
    }

    internal static bool Apply(JsonObject save, JsonArray draft)
    {
        Validate(draft);
        var current = ReadDraft(save);
        if (RawJsonLogic.SerializeValue(current) == RawJsonLogic.SerializeValue(draft)) return false;
        save.GetObject("PlayerStateData")!.Set("HotActions", draft.DeepClone());
        return true;
    }
}

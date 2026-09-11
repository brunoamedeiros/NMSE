using System.Globalization;
using NMSE.Core.Utilities;
using NMSE.Models;

namespace NMSE.Core;

/// <summary>Existing Cosmos system-local standing records, separate from global milestones.</summary>
internal static class LocalStandingLogic
{
    internal static readonly (string Id, string LabelKey, int Requirement)[] Fields =
    [
        ("^TRA_STANDING", "milestone.gek", 30),
        ("^WAR_STANDING", "milestone.vykeen", 30),
        ("^EXP_STANDING", "milestone.korvax", 30),
        ("^TGUILD_STAND", "milestone.merchants_guild", 15),
        ("^WGUILD_STAND", "milestone.mercenaries_guild", 15),
        ("^EGUILD_STAND", "milestone.explorers_guild", 15)
    ];

    internal static bool TryCurrentSystem(JsonObject player, out long address)
    {
        address = 0;
        var universe = player.GetObject("UniverseAddress");
        var galactic = universe?.GetObject("GalacticAddress");
        if (galactic == null || !TryInteger(universe!.Get("RealityIndex"), out long galaxy) || galaxy < 0 || galaxy > 255)
            return false;
        string[] keys = ["VoxelX", "VoxelY", "VoxelZ", "SolarSystemIndex"];
        int[] min = [-2048, -128, -2048, 0], max = [2047, 127, 2047, 4095];
        var values = new int[4];
        for (int i = 0; i < keys.Length; i++)
        {
            if (!TryInteger(galactic.Get(keys[i]), out long value) || value < min[i] || value > max[i]) return false;
            values[i] = (int)value;
        }
        // System statistics use planet zero; keep galaxy bits to avoid matching another galaxy.
        address = SpacePoiLogic.PackUniverseAddress(new(values[0], values[1], values[2], values[3], 0, (int)galaxy));
        return true;
    }

    internal static bool TryAddress(object? value, out long address)
    {
        if (value is string text && text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return long.TryParse(text.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out address);
        return TryInteger(value, out address);
    }

    private static bool TryInteger(object? value, out long number)
    {
        number = 0;
        switch (value)
        {
            case int i: number = i; return true;
            case long l: number = l; return true;
            case RawDouble r when double.IsFinite(r.Value) && r.Value >= long.MinValue && r.Value < (double)long.MaxValue && Math.Truncate(r.Value) == r.Value:
                number = (long)r.Value; return true;
            case double d when double.IsFinite(d) && d >= long.MinValue && d < (double)long.MaxValue && Math.Truncate(d) == d:
                number = (long)d; return true;
            default: return false;
        }
    }

    internal static JsonObject? FindGroup(JsonObject player, long address)
    {
        var groups = player.GetArray("Stats");
        JsonObject? match = null;
        if (groups == null) return null;
        for (int i = 0; i < groups.Length; i++)
        {
            if (groups.Get(i) is not JsonObject group || group.Get("GroupId") is not "^SYSTEM_STATS" ||
                !TryAddress(group.Get("Address"), out long stored) || stored != address) continue;
            if (match != null) return null; // Ambiguous records must not be edited.
            match = group;
        }
        return match;
    }

    private static JsonObject? FindValue(JsonObject player, long address, string id)
    {
        if (!Fields.Any(field => field.Id == id)) return null;
        var entries = FindGroup(player, address)?.GetArray("Stats");
        JsonObject? match = null;
        if (entries == null) return null;
        bool found = false;
        for (int i = 0; i < entries.Length; i++)
        {
            if (entries.Get(i) is not JsonObject entry || entry.Get("Id") is not string key || key != id) continue;
            if (found) return null;
            found = true;
            match = entry.GetObject("Value");
        }
        return match;
    }

    internal static bool TryRead(JsonObject player, long address, string id, out int value)
    {
        value = 0;
        var stored = FindValue(player, address, id);
        if (stored == null) return false;
        if (stored.Length == 0) return true; // Observed game representation for zero standing.
        if (!TryInteger(stored.Get("IntValue"), out long raw) || raw < int.MinValue || raw > int.MaxValue) return false;
        value = (int)raw;
        return true;
    }

    internal static bool TryWrite(JsonObject player, long address, string id, int expected, int value)
    {
        if (!TryRead(player, address, id, out int current)) return false;
        if (current == value) return true;
        if (current != expected) return false;
        RawNumberGuard.SetInt(FindValue(player, address, id), "IntValue", value);
        return true;
    }

    internal static bool TryWriteAll(JsonObject player, long address, IReadOnlyDictionary<string, int> original, IReadOnlyDictionary<string, int> edited)
    {
        var changes = edited.Where(pair => original.TryGetValue(pair.Key, out int old) && pair.Value != old).ToArray();
        foreach (var (id, value) in changes)
            if (!TryRead(player, address, id, out int current) || current != original[id] && current != value) return false;
        foreach (var (id, value) in changes)
            TryWrite(player, address, id, original[id], value);
        return true;
    }
}

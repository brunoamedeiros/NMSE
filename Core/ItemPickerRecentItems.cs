using NMSE.Config;
using NMSE.Data;

namespace NMSE.Core;

/// <summary>Remembers confirmed picker choices without retaining game-save data.</summary>
internal static class ItemPickerRecentItems
{
    internal const int MaximumCount = 10;
    private const string ConfigKey = "InventoryPicker.RecentItems";

    internal static IReadOnlyList<string> Read(AppConfig config) =>
        (config.GetProperty(ConfigKey) ?? "")
            .Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeId)
            .Where(IsValidId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaximumCount)
            .ToArray();

    /// <summary>Returns recent choices in recency order, within this inventory's allowed set.</summary>
    internal static IReadOnlyList<GameItem> GetCompatible(AppConfig config, IEnumerable<GameItem> compatibleItems)
    {
        var available = compatibleItems
            .GroupBy(item => NormalizeId(item.Id), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        return Read(config)
            .Where(available.ContainsKey)
            .Select(id => available[id])
            .ToArray();
    }

    /// <summary>Call only after the user confirms a choice, never while browsing or cancelling.</summary>
    internal static void Remember(AppConfig config, string itemId)
    {
        string id = NormalizeId(itemId);
        if (!IsValidId(id)) return;
        var recent = new[] { id }.Concat(Read(config))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaximumCount);
        config.SetProperty(ConfigKey, string.Join('|', recent));
        config.Save();
    }

    private static string NormalizeId(string value) => value.Trim().TrimStart('^').ToUpperInvariant();
    private static bool IsValidId(string value) =>
        value.Length is > 0 and <= 128 && !value.Any(c => char.IsControl(c) || c == '|');
}

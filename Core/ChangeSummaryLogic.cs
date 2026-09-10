using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using NMSE.Data;
using NMSE.Models;

namespace NMSE.Core;

internal static partial class ChangeSummaryLogic
{
    internal sealed record Change(string Area, string Field, string Before, string After, string Path);
    internal sealed record Report(List<Change> Changes, bool Truncated);

    internal static byte[] Capture(JsonObject data)
    {
        using var stream = new MemoryStream();
        using (var zip = new GZipStream(stream, CompressionLevel.Fastest, true))
        using (var writer = new StreamWriter(zip, Encoding.UTF8))
            writer.Write(JsonParser.Serialize(data, formatted: false, skipReverseMapping: true));
        return stream.ToArray();
    }

    internal static JsonObject Restore(byte[] data)
    {
        using var stream = new MemoryStream(data);
        using var zip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(zip, Encoding.UTF8);
        return JsonObject.Parse(reader.ReadToEnd());
    }

    [GeneratedRegex("(?<=[a-z0-9])(?=[A-Z])|_")]
    private static partial Regex WordBoundary();
    internal static string Friendly(string name) => UiStrings.GetOrNull("summary.field." + name) ?? WordBoundary().Replace(name, " ");

    internal static Report Compare(JsonObject before, JsonObject after, string area = "", int limit = 2000)
    {
        var changes = new List<Change>();
        bool truncated = false;
        void Walk(object? old, bool hadOld, object? value, bool hasNew, string path, string section, string field, int depth)
        {
            if (truncated) return;
            if (depth > 128) { truncated = true; return; }
            if (hadOld && hasNew && old is JsonObject a && value is JsonObject b)
            {
                foreach (string key in a.Names().Concat(b.Names()).Distinct(StringComparer.Ordinal))
                {
                    string nextSection = section;
                    if (key is "BaseContext" or "ExpeditionContext" or "PlayerStateData" or "CommonStateData")
                        nextSection = Join(section, Friendly(key));
                    string childField = field.Length == 0 ? Friendly(key) : field + " / " + Friendly(key);
                    if (field.EndsWith(" / " + Friendly(key), StringComparison.Ordinal)) childField = field;
                    Walk(a.Get(key), a.Contains(key), b.Get(key), b.Contains(key), Join(path, key), nextSection, childField, depth + 1);
                    if (truncated) break;
                }
            }
            else if (hadOld && hasNew && old is JsonArray aa && value is JsonArray bb)
            {
                for (int i = 0; i < Math.Max(aa.Length, bb.Length); i++)
                {
                    object? ov = i < aa.Length ? aa.Get(i) : null;
                    object? nv = i < bb.Length ? bb.Get(i) : null;
                    string label = (nv as JsonObject)?.GetString("Name") ?? (ov as JsonObject)?.GetString("Name") ?? "";
                    if (label.Length == 0 && (nv ?? ov) is JsonObject entry && entry.GetObject("Index") is { } pos)
                        label = UiStrings.Format("tools_plus.slot_position", pos.GetInt("X") + 1, pos.GetInt("Y") + 1);
                    string item = $"{field} [{i + 1}]" + (label.Length == 0 ? "" : " — " + label);
                    if (path.EndsWith("HotActions", StringComparison.Ordinal) && i < 3)
                        item = field + " / " + UiStrings.Get(new[] { "hotkeys.player", "hotkeys.ship", "hotkeys.vehicle" }[i]);
                    if (path.EndsWith("KeyActions", StringComparison.Ordinal)) item = $"{field} [{i}]";
                    Walk(ov, i < aa.Length, nv, i < bb.Length, path + $"[{i}]", section, item, depth + 1);
                    if (truncated) break;
                }
            }
            else
            {
                // Canonical JSON keeps null/missing, strings/numbers and large integers distinct.
                string oldJson = hadOld ? RawJsonLogic.SerializeValue(old) : "";
                string newJson = hasNew ? RawJsonLogic.SerializeValue(value) : "";
                if (hadOld == hasNew && oldJson == newJson) return;
                if (changes.Count >= limit) { truncated = true; return; }
                string shownField = field;
                foreach (string prefix in new[] { "BaseContext", "ExpeditionContext", "PlayerStateData", "CommonStateData" })
                    shownField = shownField.Replace(Friendly(prefix) + " / ", "", StringComparison.Ordinal);
                changes.Add(new(section, shownField, Display(old, hadOld, path), Display(value, hasNew, path), path));
            }
        }
        Walk(before, true, after, true, "", area, "", 0);
        return new(changes, truncated);
    }

    private static string Join(string a, string b) => a.Length == 0 ? b : a + " / " + b;
    private static string Display(object? value, bool exists, string path)
    {
        if (!exists) return UiStrings.Get("summary.missing");
        if (path.EndsWith("QuickMenuActions", StringComparison.Ordinal) && value is string action)
        {
            var option = Enumerable.Range(0, 3).SelectMany(HotkeyLogic.Options).FirstOrDefault(x => x.Id == action);
            if (option != null) return UiStrings.Get("hotkeys.action." + option.LabelKey);
        }
        return value switch
        {
            null => "null",
            JsonObject obj => UiStrings.Format("summary.object", obj.Length),
            JsonArray arr => UiStrings.Format("summary.array", arr.Length),
            bool flag => UiStrings.Get(flag ? "summary.yes" : "summary.no"),
            int i => i.ToString("N0", CultureInfo.CurrentCulture),
            long l => l.ToString("N0", CultureInfo.CurrentCulture),
            _ => RawJsonLogic.SerializeValue(value)
        };
    }
}

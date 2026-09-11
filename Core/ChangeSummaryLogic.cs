using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using NMSE.Data;
using NMSE.Models;

namespace NMSE.Core;

internal static partial class ChangeSummaryLogic
{
    internal sealed record Change(string Area, string Field, string Before, string After, string Path)
    {
        internal IReadOnlyList<ChangeReviewLogic.PathToken> Tokens { get; init; } = [];
        internal IReadOnlyList<ChangeReviewLogic.ArrayGuard> ArrayGuards { get; init; } = [];
        internal ChangeReviewLogic.DataScope Scope { get; init; }
        internal bool HadBefore { get; init; }
        internal bool HasAfter { get; init; }
        internal object? BeforeValue { get; init; }
        internal object? AfterValue { get; init; }
    }
    internal sealed record Report(List<Change> Changes, bool Truncated);

    internal static byte[] Capture(JsonObject data)
    {
        var binaries = new List<(IReadOnlyList<ChangeReviewLogic.PathToken> Path, BinaryData Data)>();
        var binaryPath = new List<ChangeReviewLogic.PathToken>();
        void FindBinary(object? value)
        {
            if (value is BinaryData binary) binaries.Add((binaryPath.ToArray(), binary));
            else if (value is JsonObject obj)
                foreach (string key in obj.Names())
                {
                    binaryPath.Add(new(Key: key));
                    FindBinary(obj.Get(key));
                    binaryPath.RemoveAt(binaryPath.Count - 1);
                }
            else if (value is JsonArray array)
                for (int i = 0; i < array.Length; i++)
                {
                    binaryPath.Add(new(Index: i));
                    FindBinary(array.Get(i));
                    binaryPath.RemoveAt(binaryPath.Count - 1);
                }
        }
        FindBinary(data);
        using var stream = new MemoryStream();
        using (var zip = new GZipStream(stream, CompressionLevel.Fastest, true))
        using (var writer = new BinaryWriter(zip, Encoding.UTF8))
        {
            writer.Write(JsonParser.Serialize(data, formatted: false, skipReverseMapping: true));
            // JSON alone cannot distinguish an all-ASCII BinaryData payload from a
            // string. Keep its type and bytes so reverting a field is lossless.
            writer.Write(binaries.Count);
            foreach (var (path, binary) in binaries)
            {
                writer.Write(path.Count);
                foreach (var token in path)
                {
                    writer.Write(token.Key != null);
                    if (token.Key is { } key) writer.Write(key);
                    else writer.Write(token.Index!.Value);
                }
                var bytes = binary.ToByteArray();
                writer.Write(bytes.Length);
                writer.Write(bytes);
            }
        }
        return stream.ToArray();
    }

    internal static JsonObject Restore(byte[] data)
    {
        using var stream = new MemoryStream(data);
        using var zip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new BinaryReader(zip, Encoding.UTF8);
        var root = JsonObject.Parse(reader.ReadString());
        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            int length = reader.ReadInt32();
            var path = new ChangeReviewLogic.PathToken[length];
            for (int j = 0; j < length; j++)
                path[j] = reader.ReadBoolean() ? new(Key: reader.ReadString()) : new(Index: reader.ReadInt32());
            var binary = new BinaryData(reader.ReadBytes(reader.ReadInt32()));
            if (length == 0 || !ChangeReviewLogic.TryRead(root, path.Take(length - 1).ToArray(), out var parent))
                throw new InvalidDataException("Invalid change review snapshot.");
            if (path[^1].Key is { } key && parent is JsonObject obj) obj.Set(key, binary);
            else if (path[^1].Index is { } index && parent is JsonArray array) array.Set(index, binary);
            else throw new InvalidDataException("Invalid change review snapshot.");
        }
        return root;
    }

    [GeneratedRegex("(?<=[a-z0-9])(?=[A-Z])|_")]
    private static partial Regex WordBoundary();
    internal static string Friendly(string name) => UiStrings.GetOrNull("summary.field." + name) ?? WordBoundary().Replace(name, " ");

    internal static Report Compare(JsonObject before, JsonObject after, string area = "", int limit = 2000,
        ChangeReviewLogic.DataScope scope = ChangeReviewLogic.DataScope.Save)
    {
        var changes = new List<Change>();
        var arrayFingerprints = new Dictionary<JsonArray, string>();
        bool truncated = false;
        void Walk(object? old, bool hadOld, object? value, bool hasNew, string path, string section, string field,
            IReadOnlyList<ChangeReviewLogic.PathToken> tokens,
            IReadOnlyList<(IReadOnlyList<ChangeReviewLogic.PathToken> Tokens, JsonArray Array)> ancestors, int depth)
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
                    Walk(a.Get(key), a.Contains(key), b.Get(key), b.Contains(key), Join(path, key), nextSection, childField,
                        [.. tokens, new(Key: key)], ancestors, depth + 1);
                    if (truncated) break;
                }
            }
            else if (hadOld && hasNew && old is JsonArray aa && value is JsonArray bb && CanCompareArrayEntries(aa, bb))
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
                    Walk(ov, i < aa.Length, nv, i < bb.Length, path + $"[{i}]", section, item, [.. tokens, new(Index: i)],
                        [.. ancestors, (tokens, bb)], depth + 1);
                    if (truncated) break;
                }
            }
            else
            {
                // Canonical JSON keeps null/missing, strings/numbers and large integers distinct.
                if (hadOld == hasNew && ChangeReviewLogic.Equal(old, value)) return;
                if (changes.Count >= limit) { truncated = true; return; }
                string shownField = field;
                foreach (string prefix in new[] { "BaseContext", "ExpeditionContext", "PlayerStateData", "CommonStateData" })
                    shownField = shownField.Replace(Friendly(prefix) + " / ", "", StringComparison.Ordinal);
                changes.Add(new(section, shownField, Display(old, hadOld, path), Display(value, hasNew, path), path)
                {
                    Tokens = tokens, Scope = scope, HadBefore = hadOld, HasAfter = hasNew,
                    BeforeValue = ChangeReviewLogic.Clone(old), AfterValue = ChangeReviewLogic.Clone(value),
                    ArrayGuards = ancestors.Select(ancestor =>
                    {
                        if (!arrayFingerprints.TryGetValue(ancestor.Array, out string? fingerprint))
                            arrayFingerprints[ancestor.Array] = fingerprint = ChangeReviewLogic.Fingerprint(ancestor.Array);
                        return new ChangeReviewLogic.ArrayGuard(ancestor.Tokens, fingerprint);
                    }).ToArray()
                });
            }
        }
        Walk(before, true, after, true, "", area, "", [], [], 0);
        return new(changes, truncated);
    }

    private static bool CanCompareArrayEntries(JsonArray before, JsonArray after)
    {
        if (before.Length != after.Length) return false;
        // Inventory slots are addressed by coordinates, not their array position.
        // Moving/deleting a slot changes the array as a unit, so a reversion cannot
        // accidentally apply a previous item's amount or ID to its new neighbour.
        for (int i = 0; i < before.Length; i++)
            if (before.Get(i) is JsonObject a && after.Get(i) is JsonObject b &&
                (a.Contains("Index") || b.Contains("Index")) && !ChangeReviewLogic.Equal(a.Get("Index"), b.Get("Index"))) return false;
        return true;
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

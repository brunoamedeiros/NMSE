using NMSE.Models;
using System.Security.Cryptography;
using System.Text;

namespace NMSE.Core;

/// <summary>Exact paths and guarded, in-memory reversions for the change review.</summary>
internal static class ChangeReviewLogic
{
    internal enum DataScope { Save, Account }
    internal sealed record PathToken(string? Key = null, int? Index = null)
    {
        internal string TreeSegment => Key ?? $"[{Index}]";
    }
    internal sealed record ArrayGuard(IReadOnlyList<PathToken> Tokens, string Fingerprint);

    internal static string Fingerprint(object value) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonParser.Serialize(value, formatted: false, skipReverseMapping: true))));

    internal static object? Clone(object? value) => value switch
    {
        JsonObject obj => CloneObject(obj),
        JsonArray array => CloneArray(array),
        BinaryData data => new BinaryData(data.ToByteArray().ToArray()),
        _ => value
    };

    private static JsonObject CloneObject(JsonObject source)
    {
        var clone = new JsonObject();
        foreach (string key in source.Names()) clone.Add(key, Clone(source.Get(key)));
        return clone;
    }

    private static JsonArray CloneArray(JsonArray source)
    {
        var clone = new JsonArray();
        for (int i = 0; i < source.Length; i++) clone.Add(Clone(source.Get(i)));
        return clone;
    }

    internal static bool Equal(object? a, object? b)
    {
        if (a is BinaryData || b is BinaryData)
            return a is BinaryData aa && b is BinaryData bb && aa.ToByteArray().AsSpan().SequenceEqual(bb.ToByteArray());
        if (a is JsonObject objA && b is JsonObject objB)
            return objA.Length == objB.Length && objA.Names().All(key => objB.Contains(key) && Equal(objA.Get(key), objB.Get(key)));
        if (a is JsonArray arrA && b is JsonArray arrB)
            return arrA.Length == arrB.Length && Enumerable.Range(0, arrA.Length).All(i => Equal(arrA.Get(i), arrB.Get(i)));
        return RawJsonLogic.SerializeValue(a) == RawJsonLogic.SerializeValue(b);
    }

    internal static bool TryRead(object? root, IReadOnlyList<PathToken> tokens, out object? value)
    {
        value = root;
        foreach (var token in tokens)
        {
            if (token.Key is { } key && value is JsonObject obj && obj.Contains(key)) value = obj.Get(key);
            else if (token.Index is { } index && value is JsonArray array && index >= 0 && index < array.Length) value = array.Get(index);
            else { value = null; return false; }
        }
        return true;
    }

    internal static bool TryRevert(JsonObject current, ChangeSummaryLogic.Change change, out string error)
    {
        error = "summary.stale";
        if (change.Tokens.Count == 0) return false;
        foreach (var guard in change.ArrayGuards)
            if (!TryRead(current, guard.Tokens, out var array) || array is not JsonArray || Fingerprint(array) != guard.Fingerprint) return false;
        bool exists = TryRead(current, change.Tokens, out object? value);
        if (exists != change.HasAfter || (exists && !Equal(value, change.AfterValue))) return false;
        if (!TryRead(current, change.Tokens.Take(change.Tokens.Count - 1).ToArray(), out object? parent)) return false;
        var last = change.Tokens[^1];
        if (last.Key is { } key && parent is JsonObject obj)
        {
            if (change.HadBefore) obj.Set(key, Clone(change.BeforeValue));
            else obj.Remove(key);
        }
        else if (last.Index is { } index && parent is JsonArray array)
        {
            // Structural array edits are represented by their array row. A leaf
            // reversion can therefore never insert/delete an index or shift siblings.
            if (!change.HadBefore || !change.HasAfter || index < 0 || index >= array.Length) return false;
            array.Set(index, Clone(change.BeforeValue));
        }
        else return false;
        error = "";
        return true;
    }

    internal static bool TryFindInventory(JsonObject current, ChangeSummaryLogic.Change change,
        out InventorySearchLogic.InventoryLocation? location, out int? x, out int? y)
    {
        location = null; x = y = null;
        if (change.Scope != DataScope.Save) return false;
        var inventories = InventorySearchLogic.Enumerate(current);
        for (int length = change.Tokens.Count; length > 0; length--)
        {
            if (!TryRead(current, change.Tokens.Take(length).ToArray(), out var node)) continue;
            location = inventories.FirstOrDefault(candidate => ReferenceEquals(candidate.Inventory, node));
            if (location == null) continue;
            if (change.Tokens.Count > length + 1 && change.Tokens[length].Key == "Slots" && change.Tokens[length + 1].Index is { } index
                && location.Inventory.GetArray("Slots") is { } slots && index >= 0 && index < slots.Length
                && slots.Get(index) is JsonObject slot && slot.GetObject("Index") is { } position)
            {
                x = position.GetInt("X"); y = position.GetInt("Y");
            }
            return true;
        }
        return false;
    }
}

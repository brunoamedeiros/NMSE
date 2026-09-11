using NMSE.Models;

namespace NMSE.Core;

/// <summary>
/// Records an inventory operation as one transaction, including transfers between inventories.
/// Snapshots retain exact JSON values; restoring replaces contents without replacing inventory objects.
/// </summary>
internal sealed class InventoryEditHistory
{
    private sealed record Snapshot(JsonObject Data, long Size);
    private sealed class State(Snapshot snapshot, List<(object Child, object? Parent)> attachment)
    {
        internal Snapshot Current = snapshot;
        internal Snapshot Saved = snapshot;
        internal List<(object Child, object? Parent)> Attachment = attachment;
    }
    private sealed record Edit(JsonObject Inventory, Snapshot Before, Snapshot After);
    private sealed record Transaction(string Label, List<Edit> Edits)
    {
        internal long Size => Edits.Sum(x => x.Before.Size + x.After.Size);
    }

    private readonly Dictionary<JsonObject, State> _states = new(ReferenceEqualityComparer.Instance);
    private readonly List<Transaction> _transactions = [];
    private readonly int _maxEntries;
    private readonly long _maxBytes;
    private int _position;
    private long _historyBytes;

    internal InventoryEditHistory(int maxEntries = 50, long maxBytes = 32 * 1024 * 1024)
    {
        if (maxEntries < 1) throw new ArgumentOutOfRangeException(nameof(maxEntries));
        if (maxBytes < 1) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        _maxEntries = maxEntries;
        _maxBytes = maxBytes;
    }

    internal bool CanUndo => ValidateCurrent() && _position > 0;
    internal bool CanRedo => ValidateCurrent() && _position < _transactions.Count;
    internal string? UndoLabel => CanUndo ? _transactions[_position - 1].Label : null;
    internal string? RedoLabel => CanRedo ? _transactions[_position].Label : null;

    /// <summary>Starts a new loaded save/context baseline. Pass the complete editable inventory set.</summary>
    internal void Reset(IEnumerable<JsonObject> inventories)
    {
        ClearHistory();
        _states.Clear();
        foreach (var inventory in inventories)
            _states.TryAdd(inventory, new State(Capture(inventory), Attachment(inventory)));
    }

    /// <summary>
    /// Call after one completed UI operation, with all inventories so transfers are atomic.
    /// A changed reference set invalidates history instead of associating old edits with new objects.
    /// </summary>
    internal bool Record(IEnumerable<JsonObject> inventories, string label)
    {
        if (!SynchronizeReferences(inventories)) return false;
        var edits = new List<Edit>();
        foreach (var (inventory, state) in _states)
        {
            if (Equal(inventory, state.Current.Data)) continue;
            var after = Capture(inventory);
            edits.Add(new(inventory, state.Current, after));
            state.Current = after;
        }
        if (edits.Count == 0) return false;
        while (_transactions.Count > _position)
        {
            _historyBytes -= _transactions[^1].Size;
            _transactions.RemoveAt(_transactions.Count - 1);
        }
        var transaction = new Transaction(label, edits);
        _transactions.Add(transaction);
        _position++;
        _historyBytes += transaction.Size;
        // Very large single operations may be unretained, but their unsaved markers remain accurate.
        while (_transactions.Count > _maxEntries || _historyBytes > _maxBytes)
        {
            _historyBytes -= _transactions[0].Size;
            _transactions.RemoveAt(0);
            _position--;
        }
        return true;
    }

    /// <summary>Marks the current contents saved without discarding valid undo or redo transactions.</summary>
    internal void MarkSaved(IEnumerable<JsonObject> inventories)
    {
        SynchronizeReferences(inventories);
        ValidateCurrent();
        foreach (var state in _states.Values) state.Saved = state.Current;
    }

    internal IReadOnlyList<JsonObject> Undo()
    {
        if (!CanUndo) return Array.Empty<JsonObject>();
        var transaction = _transactions[--_position];
        foreach (var edit in transaction.Edits) Restore(edit.Inventory, edit.Before);
        return transaction.Edits.Select(x => x.Inventory).ToArray();
    }

    internal IReadOnlyList<JsonObject> Redo()
    {
        if (!CanRedo) return Array.Empty<JsonObject>();
        var transaction = _transactions[_position++];
        foreach (var edit in transaction.Edits) Restore(edit.Inventory, edit.After);
        return transaction.Edits.Select(x => x.Inventory).ToArray();
    }

    internal bool HasChanges(JsonObject inventory) =>
        _states.TryGetValue(inventory, out var state) && !Equal(inventory, state.Saved.Data);

    /// <summary>Includes item edits, empty/removed slots, locks, and supercharge changes by grid position.</summary>
    internal IReadOnlySet<(int X, int Y)> GetChangedPositions(JsonObject inventory)
    {
        var changed = new HashSet<(int X, int Y)>();
        if (!_states.TryGetValue(inventory, out var state)) return changed;
        foreach (string property in new[] { "Slots", "ValidSlotIndices", "SpecialSlots" })
        {
            var before = AtPositions(state.Saved.Data.Get(property) as JsonArray);
            var after = AtPositions(inventory.Get(property) as JsonArray);
            foreach (var position in before.Keys.Concat(after.Keys).Distinct())
            {
                before.TryGetValue(position, out var old);
                after.TryGetValue(position, out var current);
                if (old is null || current is null || old.Count != current.Count ||
                    old.Where((value, index) => !Equal(value, current[index])).Any())
                    changed.Add(position);
            }
        }
        return changed;
    }

    private bool SynchronizeReferences(IEnumerable<JsonObject> inventories)
    {
        var current = new HashSet<JsonObject>(inventories, ReferenceEqualityComparer.Instance);
        bool same = current.SetEquals(_states.Keys) && _states.Values.All(Attached);
        if (same) return true;
        ClearHistory();
        foreach (var removed in _states.Keys.Where(x => !current.Contains(x)).ToArray()) _states.Remove(removed);
        foreach (var inventory in current)
        {
            var snapshot = Capture(inventory);
            if (_states.TryGetValue(inventory, out var state))
            {
                state.Current = snapshot;
                state.Attachment = Attachment(inventory);
            }
            else _states.Add(inventory, new State(snapshot, Attachment(inventory)));
        }
        return false;
    }

    private bool ValidateCurrent()
    {
        if (_states.All(x => Attached(x.Value) && Equal(x.Key, x.Value.Current.Data))) return true;
        // An unrecorded operation (for example Raw JSON) must never be overwritten by stale undo.
        // Preserve saved baselines so these changes still appear as unsaved.
        ClearHistory();
        foreach (var (inventory, state) in _states)
        {
            state.Current = Capture(inventory);
            state.Attachment = Attachment(inventory);
        }
        return false;
    }

    private void ClearHistory()
    {
        _transactions.Clear();
        _position = 0;
        _historyBytes = 0;
    }

    private void Restore(JsonObject target, Snapshot snapshot)
    {
        var copy = Clone(snapshot.Data);
        foreach (string key in target.Names()) target.Remove(key);
        foreach (string key in copy.Names()) target.Add(key, copy.Get(key));
        _states[target].Current = snapshot;
    }

    private static object? Parent(object value) => value switch
    {
        JsonObject obj => obj.Parent,
        JsonArray array => array.Parent,
        _ => null
    };

    private static List<(object Child, object? Parent)> Attachment(JsonObject inventory)
    {
        var result = new List<(object Child, object? Parent)>();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        for (object? child = inventory; child != null && visited.Add(child); child = Parent(child))
            result.Add((child, Parent(child)));
        return result;
    }

    private static bool Attached(State state) =>
        state.Attachment.All(x => ReferenceEquals(Parent(x.Child), x.Parent));

    private static Snapshot Capture(JsonObject inventory) => new(Clone(inventory), EstimateBytes(inventory));

    private static JsonObject Clone(JsonObject value)
    {
        var copy = value.DeepClone();
        // DeepClone already preserves RawDouble and integer types, but BinaryData exposes mutable bytes.
        CloneBinary(copy);
        return copy;
    }

    private static void CloneBinary(object? value)
    {
        if (value is JsonObject obj)
            foreach (string key in obj.Names())
            {
                if (obj.Get(key) is BinaryData bytes) obj.Set(key, new BinaryData(bytes.ToByteArray().ToArray()));
                else CloneBinary(obj.Get(key));
            }
        else if (value is JsonArray array)
            for (int i = 0; i < array.Length; i++)
            {
                if (array.Get(i) is BinaryData bytes) array.Set(i, new BinaryData(bytes.ToByteArray().ToArray()));
                else CloneBinary(array.Get(i));
            }
    }

    private static bool Equal(object? left, object? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is JsonObject a && right is JsonObject b)
            return a.Length == b.Length && a.Names().All(key => b.Contains(key) && Equal(a.Get(key), b.Get(key)));
        if (left is JsonArray aa && right is JsonArray bb)
            return aa.Length == bb.Length && Enumerable.Range(0, aa.Length).All(i => Equal(aa.Get(i), bb.Get(i)));
        if (left is RawDouble old && right is RawDouble current) return old.Text == current.Text;
        return Equals(left, right);
    }

    private static long EstimateBytes(object? value) => value switch
    {
        JsonObject obj => 128 + obj.Names().Sum(key => 48L + key.Length * 2L + EstimateBytes(obj.Get(key))),
        JsonArray array => 64 + Enumerable.Range(0, array.Length).Sum(i => 8L + EstimateBytes(array.Get(i))),
        string text => 24L + text.Length * 2L,
        BinaryData binary => 32L + binary.ToByteArray().LongLength,
        RawDouble raw => 32L + raw.Text.Length * 2L,
        _ => 16
    };

    private static Dictionary<(int X, int Y), List<JsonObject>> AtPositions(JsonArray? slots)
    {
        var result = new Dictionary<(int X, int Y), List<JsonObject>>();
        if (slots == null) return result;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots.Get(i) is not JsonObject slot) continue;
            var index = slot.Get("Index") as JsonObject ?? slot;
            if (index.Get("X") is not int x || index.Get("Y") is not int y) continue;
            if (!result.TryGetValue((x, y), out var entries)) result.Add((x, y), entries = []);
            entries.Add(slot);
        }
        return result;
    }
}

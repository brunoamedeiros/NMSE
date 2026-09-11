using NMSE.Core;
using NMSE.Models;

namespace NMSE.Tests;

public class InventoryEditHistoryTests
{
    private static JsonObject Inventory(int amount = 100) => JsonObject.Parse($$$"""
        {"Slots":[{"Id":"^OXYGEN","Amount":{{{amount}}},"Index":{"X":2,"Y":3}}],
         "ValidSlotIndices":[{"X":2,"Y":3},{"X":4,"Y":5}],"SpecialSlots":[]}
        """);
    private static void SetAmount(JsonObject inventory, int amount) =>
        inventory.GetArray("Slots")!.GetObject(0).Set("Amount", amount);
    private static int Amount(JsonObject inventory) => inventory.GetArray("Slots")!.GetObject(0).GetInt("Amount");

    [Fact]
    public void TransferIsOneTransactionAndRestoresInventoryIdentityAndParents()
    {
        var source = Inventory();
        var destination = Inventory(20);
        var player = new JsonObject();
        player.Add("Inventory", source);
        player.Add("ShipInventory", destination);
        var history = new InventoryEditHistory();
        history.Reset([source, destination]);
        SetAmount(source, 40);
        SetAmount(destination, 80);
        Assert.True(history.Record([destination, source], "Move Oxygen"));
        Assert.Equal("Move Oxygen", history.UndoLabel);
        Assert.Equal(2, history.Undo().Count);
        Assert.Equal((100, 20), (Amount(source), Amount(destination)));
        Assert.Same(source, player.GetObject("Inventory"));
        Assert.Same(destination, player.GetObject("ShipInventory"));
        Assert.Same(player, source.Parent);
        Assert.Same(source, source.GetArray("Slots")!.Parent);
        Assert.Same(source.GetArray("Slots"), source.GetArray("Slots")!.GetObject(0).Parent);
        Assert.False(history.CanUndo);
        Assert.Equal("Move Oxygen", history.RedoLabel);
        Assert.Equal(2, history.Redo().Count);
        Assert.Equal((40, 80), (Amount(source), Amount(destination)));
    }

    [Fact]
    public void SaveRetainsHistoryAndUndoReflectsTheNewSavedBaseline()
    {
        var inventory = Inventory();
        var history = new InventoryEditHistory();
        history.Reset([inventory]);
        SetAmount(inventory, 200);
        history.Record([inventory], "Change amount");
        Assert.True(history.HasChanges(inventory));
        history.MarkSaved([inventory]);
        Assert.False(history.HasChanges(inventory));
        Assert.Empty(history.GetChangedPositions(inventory));
        Assert.True(history.CanUndo);
        history.Undo();
        Assert.True(history.HasChanges(inventory));
        Assert.Contains((2, 3), history.GetChangedPositions(inventory));
        history.Redo();
        Assert.False(history.HasChanges(inventory));
    }

    [Fact]
    public void NewEditClearsRedoButNoOpDoesNot()
    {
        var inventory = Inventory();
        var history = new InventoryEditHistory();
        history.Reset([inventory, inventory]);
        SetAmount(inventory, 200);
        history.Record([inventory], "First");
        history.Undo();
        Assert.False(history.Record([inventory], "No change"));
        Assert.True(history.CanRedo);
        SetAmount(inventory, 300);
        history.Record([inventory], "Second");
        Assert.False(history.CanRedo);
        Assert.Null(history.RedoLabel);
        Assert.Equal("Second", history.UndoLabel);
        history.Undo();
        Assert.Equal(100, Amount(inventory));
    }

    [Fact]
    public void HistoryLimitDropsOldTransactionsWithoutMovingSavedBaseline()
    {
        var inventory = Inventory();
        var history = new InventoryEditHistory(maxEntries: 2);
        history.Reset([inventory]);
        foreach (int amount in new[] { 200, 300, 400 })
        {
            SetAmount(inventory, amount);
            history.Record([inventory], "Change amount");
        }
        history.Undo();
        history.Undo();
        Assert.Equal(200, Amount(inventory));
        Assert.False(history.CanUndo);
        Assert.True(history.HasChanges(inventory));
        Assert.Equal(2, history.GetChangedPositions(inventory).Single().X);
    }

    [Fact]
    public void OversizedOperationIsNotRetainedButRemainsUnsaved()
    {
        var inventory = Inventory();
        var history = new InventoryEditHistory(maxBytes: 1);
        history.Reset([inventory]);
        SetAmount(inventory, 200);
        Assert.True(history.Record([inventory], "Change amount"));
        Assert.False(history.CanUndo);
        Assert.True(history.HasChanges(inventory));
    }

    [Fact]
    public void ChangedPositionsFollowCoordinatesForMovesRemovalLocksAndSupercharge()
    {
        var inventory = Inventory();
        var history = new InventoryEditHistory();
        history.Reset([inventory]);
        inventory.GetArray("Slots")!.GetObject(0).GetObject("Index")!.Set("X", 4);
        inventory.GetArray("ValidSlotIndices")!.RemoveAt(1);
        inventory.GetArray("SpecialSlots")!.Add(JsonObject.Parse("""
            {"Type":{"InventorySpecialSlotType":"TechBonus"},"Index":{"X":6,"Y":7}}
            """));
        Assert.True(history.GetChangedPositions(inventory).SetEquals([(2, 3), (4, 3), (4, 5), (6, 7)]));
        history.Record([inventory], "Edit slots");
        inventory.GetArray("Slots")!.Clear();
        Assert.Contains((2, 3), history.GetChangedPositions(inventory));
    }

    [Fact]
    public void ReorderingSlotArrayDoesNotMarkUnchangedCoordinates()
    {
        var inventory = Inventory();
        inventory.GetArray("Slots")!.Add(JsonObject.Parse("""{"Id":"^FUEL1","Amount":20,"Index":{"X":0,"Y":0}}"""));
        var history = new InventoryEditHistory();
        history.Reset([inventory]);
        var slots = inventory.GetArray("Slots")!;
        var moved = slots.Get(0);
        slots.RemoveAt(0);
        slots.Add(moved);
        Assert.Empty(history.GetChangedPositions(inventory));
    }

    [Fact]
    public void UnrecordedChangesRejectStaleUndoWithoutOverwritingOrLosingUnsavedMarkers()
    {
        var inventory = Inventory();
        var history = new InventoryEditHistory();
        history.Reset([inventory]);
        SetAmount(inventory, 200);
        history.Record([inventory], "Change amount");
        inventory.Add("UnrelatedRawEdit", 9007199254740993L);
        Assert.Empty(history.Undo());
        Assert.Equal(200, Amount(inventory));
        Assert.Equal(9007199254740993L, inventory.GetLong("UnrelatedRawEdit"));
        Assert.False(history.CanRedo);
        Assert.True(history.HasChanges(inventory));
        SetAmount(inventory, 300);
        history.Record([inventory], "New change");
        history.Undo();
        Assert.Equal(200, Amount(inventory));
        Assert.Equal(9007199254740993L, inventory.GetLong("UnrelatedRawEdit"));
    }

    [Fact]
    public void ReplacedInventoryOrAncestorInvalidatesOldHistory()
    {
        var inventory = Inventory();
        var player = new JsonObject();
        player.Add("Inventory", inventory);
        var save = new JsonObject();
        save.Add("Player", player);
        var history = new InventoryEditHistory();
        history.Reset([inventory]);
        SetAmount(inventory, 200);
        history.Record([inventory], "Change amount");
        save.Set("Player", player.DeepClone());
        Assert.False(history.CanUndo);
        Assert.Empty(history.Undo());
        var replacement = save.GetObject("Player.Inventory")!;
        Assert.False(history.Record([replacement], "Replaced inventory"));
        Assert.False(history.CanRedo);
        SetAmount(replacement, 300);
        Assert.True(history.Record([replacement], "Change replacement"));
        history.Undo();
        Assert.Equal(200, Amount(replacement));
    }

    [Fact]
    public void SnapshotsPreserveExactNumbersAndIsolateBinaryPayloads()
    {
        var inventory = Inventory();
        inventory.Add("Precision", new RawDouble(0.30000001192092898, "0.30000001192092898"));
        inventory.Add("Seed", 9007199254740993L);
        var payload = new BinaryData([0, 128, 255]);
        inventory.Add("Binary", payload);
        var history = new InventoryEditHistory();
        history.Reset([inventory]);
        payload.ToByteArray()[0] = 17;
        inventory.Set("Precision", new RawDouble(0.3, "0.3"));
        inventory.Set("Seed", 9007199254740994L);
        history.Record([inventory], "Change exact values");
        history.Undo();
        Assert.Equal("0.30000001192092898", Assert.IsType<RawDouble>(inventory.Get("Precision")).Text);
        Assert.Equal(9007199254740993L, inventory.GetLong("Seed"));
        Assert.Equal(new byte[] { 0, 128, 255 }, Assert.IsType<BinaryData>(inventory.Get("Binary")).ToByteArray());
        Assert.False(history.HasChanges(inventory));
        history.Redo();
        Assert.Equal(new byte[] { 17, 128, 255 }, Assert.IsType<BinaryData>(inventory.Get("Binary")).ToByteArray());
        history.Undo();
        Assert.Equal(new byte[] { 0, 128, 255 }, Assert.IsType<BinaryData>(inventory.Get("Binary")).ToByteArray());
    }

    [Fact]
    public void ResetRemovesAllHistoryAndStartsANewSavedBaseline()
    {
        var inventory = Inventory();
        var history = new InventoryEditHistory();
        history.Reset([inventory]);
        SetAmount(inventory, 200);
        history.Record([inventory], "Change amount");
        history.Reset([inventory]);
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.False(history.HasChanges(inventory));
    }
}

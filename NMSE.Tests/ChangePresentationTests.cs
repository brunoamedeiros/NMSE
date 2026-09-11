using System.Globalization;
using NMSE.Core;
using NMSE.Data;
using NMSE.Models;

namespace NMSE.Tests;

// Presentation reads UiStrings repeatedly; language-switching tests must not run alongside it.
[Collection("MutableStaticDatabases")]
public class ChangePresentationTests
{
    private static string Text(string key, string fallback) => UiStrings.GetOrNull("summary.readable." + key) ?? fallback;
    private static GameItemDatabase Database()
    {
        var db = new GameItemDatabase();
        var items = (Dictionary<string, GameItem>)db.Items;
        items.Add("OXYGEN", new GameItem { Id = "OXYGEN", Name = "Oxygen" });
        items.Add("FUEL1", new GameItem { Id = "FUEL1", Name = "Carbon" });
        return db;
    }
    private static JsonObject Slot(string id, int x, int y, int amount = 100) => JsonObject.Parse($$$"""
        {"Id":"{{{id}}}","Amount":{{{amount}}},"DamageFactor":0.0,"FullyInstalled":true,"Index":{"X":{{{x}}},"Y":{{{y}}}}}
        """);
    private static JsonArray Slots(params JsonObject[] slots)
    {
        var array = new JsonArray();
        foreach (var slot in slots) array.Add(slot);
        return array;
    }
    private static ChangeSummaryLogic.Change Group(string key, object before, object after) => new("", key, "", "", key)
    {
        HadBefore = true, HasAfter = true, BeforeValue = before, AfterValue = after,
        Tokens = [new(Key: key)]
    };

    [Fact]
    public void InventoryAdditionShowsItemQuantityAndCoordinatesInsteadOfJson()
    {
        var before = Slots(Slot("^FUEL1", 0, 0));
        var after = Slots(Slot("^FUEL1", 0, 0), Slot("^OXYGEN", 3, 4));
        var presentation = ChangePresentationLogic.Build(Group("Slots", before, after), Database());
        var row = Assert.Single(presentation.Rows);
        Assert.Contains("(4, 5)", row.Label);
        Assert.Equal(Text("empty_slot", "Empty slot"), row.Before);
        Assert.Equal("Oxygen × 100", row.After);
        Assert.Contains("Oxygen", presentation.After);
        Assert.DoesNotContain("Carbon", presentation.After);
        Assert.DoesNotContain("{", presentation.Before + presentation.After);
        Assert.True(presentation.Grouped);
        Assert.False(presentation.Truncated);
    }

    [Fact]
    public void InventoryRemovalAndReplacementMatchCoordinatesRatherThanArrayIndices()
    {
        var before = Slots(Slot("^OXYGEN", 0, 0), Slot("^FUEL1", 4, 1));
        var after = Slots(Slot("^OXYGEN", 4, 1, 25));
        var presentation = ChangePresentationLogic.Build(Group("Slots", before, after), Database());
        var removed = Assert.Single(presentation.Rows, row => row.Label.Contains("(1, 1)", StringComparison.Ordinal));
        Assert.Contains("Oxygen", removed.Before);
        Assert.Equal(Text("empty_slot", "Empty slot"), removed.After);
        var replaced = Assert.Single(presentation.Rows, row => row.Label.EndsWith("(5, 2)", StringComparison.Ordinal));
        Assert.Equal("Carbon × 100", replaced.Before);
        Assert.Equal("Oxygen × 25", replaced.After);
        Assert.Contains(presentation.Rows, row => row.Before == "100" && row.After == "25");
    }

    [Fact]
    public void ReorderingDoesNotInventRemovedOrReplacedItems()
    {
        var before = Slots(Slot("^OXYGEN", 0, 0), Slot("^FUEL1", 4, 1));
        var after = Slots(Slot("^FUEL1", 4, 1), Slot("^OXYGEN", 0, 0));
        var result = ChangePresentationLogic.Build(Group("Slots", before, after), Database());
        var row = Assert.Single(result.Rows);
        Assert.Equal(Text("order", "Order"), row.Label);
        Assert.Equal(Text("reordered", "Reordered; contents unchanged"), row.After);
        Assert.True(result.Grouped);
    }

    [Fact]
    public void ExistingItemMetadataIncludesOnlyChangedFieldsAndPreservesExactValues()
    {
        var original = Slot("^OXYGEN", 1, 2);
        original.Add("FutureCounter", 9007199254740993L);
        var edited = original.DeepClone();
        edited.Set("Amount", 101);
        edited.Set("DamageFactor", new RawDouble(0.30000001192092898, "0.30000001192092898"));
        edited.Set("FutureCounter", 9007199254740994L);
        var result = ChangePresentationLogic.Build(Group("Slots", Slots(original), Slots(edited)), Database());
        Assert.Equal(3, result.Rows.Count);
        Assert.All(result.Rows, row => Assert.Contains("Oxygen", row.Label));
        Assert.Contains(result.Rows, row => row.Before == "100" && row.After == "101");
        Assert.Contains(result.Rows, row => row.After == "0.30000001192092898");
        Assert.Contains(result.Rows, row => row.Before == 9007199254740993L.ToString("N0", CultureInfo.CurrentCulture));
        Assert.DoesNotContain(result.Rows, row => row.Label.Contains("Index", StringComparison.Ordinal));
    }

    [Fact]
    public void SlotLocksAndSuperchargeAreReadableAtTheirGridCoordinates()
    {
        var locks = ChangePresentationLogic.Build(Group("ValidSlotIndices", JsonArray.Parse("""[{"X":0,"Y":0}]"""),
            JsonArray.Parse("""[{"X":3,"Y":4}]""")));
        Assert.Equal(2, locks.Rows.Count);
        Assert.Contains(locks.Rows, row => row.Label.Contains("(1, 1)", StringComparison.Ordinal) && row.After == Text("locked", "Locked"));
        Assert.Contains(locks.Rows, row => row.Label.Contains("(4, 5)", StringComparison.Ordinal) && row.After == Text("unlocked", "Unlocked"));
        var special = ChangePresentationLogic.Build(Group("SpecialSlots", new JsonArray(), JsonArray.Parse("""
            [{"Index":{"X":1,"Y":2},"Type":{"InventorySpecialSlotType":"TechBonus"}}]
            """)));
        Assert.Equal(Text("supercharged", "Supercharged"), Assert.Single(special.Rows).After);
    }

    [Fact]
    public void MissingNullEmptyTextAndLiteralNullRemainDistinct()
    {
        var before = JsonObject.Parse("""{"Removed":null,"Literal":"null","Empty":""}""");
        var after = JsonObject.Parse("""{"Added":null,"Literal":null,"Empty":"new"}""");
        var results = ChangeSummaryLogic.Compare(before, after).Changes.ToDictionary(change => change.Tokens[0].Key!,
            change => ChangePresentationLogic.Build(change));
        Assert.Equal(Text("missing", "Not present"), results["Added"].Before);
        Assert.Equal(Text("null", "No value"), results["Added"].After);
        Assert.Equal(Text("missing", "Not present"), results["Removed"].After);
        Assert.Equal("null", results["Literal"].Before);
        Assert.Equal(Text("null", "No value"), results["Literal"].After);
        Assert.Equal(Text("empty_text", "Empty text"), results["Empty"].Before);
        Assert.All(results.Values, result => Assert.False(result.Grouped));
    }

    [Fact]
    public void UnknownItemsAndBinaryPayloadsAreExplicitWithoutDumpingBytes()
    {
        var binarySlot = Slot("^OXYGEN", 1, 0);
        binarySlot.Set("Id", new BinaryData([94, 128, 255, 17, 35, 49]));
        var result = ChangePresentationLogic.Build(Group("Slots", new JsonArray(), Slots(Slot("^FUTURE_ITEM", 0, 0), binarySlot)), Database());
        Assert.Contains(result.Rows, row => row.After.Contains("^FUTURE_ITEM", StringComparison.Ordinal));
        Assert.Contains(result.Rows, row => row.After.Contains(Text("unknown_binary_item", "Unknown item (binary ID)"), StringComparison.Ordinal));
        Assert.DoesNotContain("80FF", result.After);
        var bytes = Group("Payload", new BinaryData([65, 66, 67]), new BinaryData([65, 66, 67, 68]));
        var binary = ChangePresentationLogic.Build(bytes);
        Assert.DoesNotContain("ABC", binary.Before + binary.After);
        Assert.Contains("3", binary.Before);
        Assert.Contains("4", binary.After);
    }

    [Fact]
    public void GenericGroupsFlattenOnlyChangedNestedFieldsAndBoundOutput()
    {
        var before = JsonObject.Parse("""{"Unchanged":42,"Nested":{"Name":"old","Enabled":false}}""");
        var after = JsonObject.Parse("""{"Unchanged":42,"Nested":{"Name":"new","Enabled":true}}""");
        var result = ChangePresentationLogic.Build(Group("Settings", before, after));
        Assert.Equal(2, result.Rows.Count);
        Assert.Contains(result.Rows, row => row.Before == "old" && row.After == "new");
        Assert.DoesNotContain(result.Rows, row => row.Label.Contains("Unchanged", StringComparison.Ordinal));
        Assert.DoesNotContain("{", result.Before + result.After);
        var many = new JsonObject();
        for (int i = 0; i < 305; i++) many.Add("Field" + i, i);
        var bounded = ChangePresentationLogic.Build(Group("Large", new JsonObject(), many));
        Assert.Equal(300, bounded.Rows.Count);
        Assert.True(bounded.Truncated);
    }

    [Fact]
    public void LeafInventoryChangesUseCurrentLocationAndItemNameWithoutMutation()
    {
        var before = JsonObject.Parse("""{"PlayerStateData":{"Inventory":{"Slots":[]}}}""");
        before.GetObject("PlayerStateData.Inventory")!.Set("Slots", Slots(Slot("^OXYGEN", 3, 2)));
        var after = before.DeepClone();
        after.GetArray("PlayerStateData.Inventory.Slots")!.GetObject(0).Set("Amount", 250);
        string beforeText = before.ToString(), afterText = after.ToString();
        var change = Assert.Single(ChangeSummaryLogic.Compare(before, after).Changes);
        var result = ChangePresentationLogic.Build(change, Database(), after);
        Assert.Contains("Oxygen", result.Title);
        Assert.Contains("(4, 3)", result.Area);
        Assert.Equal("100", result.Before);
        Assert.Equal("250", result.After);
        Assert.False(result.Grouped);
        Assert.Equal(beforeText, before.ToString());
        Assert.Equal(afterText, after.ToString());
        Assert.Equal(100, change.BeforeValue);
        Assert.Equal(250, change.AfterValue);
    }

    [Fact]
    public void AccountScopeCannotBeMistakenForSaveInventory()
    {
        var change = Group("Slots", new JsonArray(), Slots(Slot("^OXYGEN", 0, 0))) with { Scope = ChangeReviewLogic.DataScope.Account };
        var result = ChangePresentationLogic.Build(change, Database(), JsonObject.Parse("""{"PlayerStateData":{"Inventory":{"Slots":[]}}}"""));
        Assert.Equal(UiStrings.GetOrNull("summary.account") ?? Text("account", "Account"), result.Area);
    }

    [Fact]
    public void NonInventoryTitlesRetainOwnerPathsAndCaretNamesAndIdsRemainLiteral()
    {
        var before = JsonObject.Parse("""{"ShipOwnership":[{"Name":"^First","Id":"^CUSTOM_A"},{"Name":"^Second"}]}""");
        var after = JsonObject.Parse("""{"ShipOwnership":[{"Name":"^ChangedFirst","Id":"^CUSTOM_B"},{"Name":"^ChangedSecond"}]}""");
        var changes = ChangeSummaryLogic.Compare(before, after).Changes;
        var presentations = changes.Select(change => ChangePresentationLogic.Build(change, Database())).ToArray();
        Assert.Equal(3, presentations.Select(item => item.Title).Distinct().Count());
        for (int i = 0; i < changes.Count; i++) Assert.Equal(changes[i].Field, presentations[i].Title);
        Assert.Contains(presentations, item => item.Before == "^First" && item.After == "^ChangedFirst");
        Assert.Contains(presentations, item => item.Before == "^CUSTOM_A" && item.After == "^CUSTOM_B");
    }

    [Fact]
    public void InventoryLocationRetainsExplicitExpeditionContext()
    {
        var before = JsonObject.Parse("""{"ExpeditionContext":{"PlayerStateData":{"Inventory":{"Slots":[]}}}}""");
        before.GetObject("ExpeditionContext.PlayerStateData.Inventory")!.Set("Slots", Slots(Slot("^OXYGEN", 0, 0)));
        var after = before.DeepClone();
        after.RegisterTransform("PlayerStateData", _ => "ExpeditionContext.PlayerStateData");
        after.GetArray("PlayerStateData.Inventory.Slots")!.GetObject(0).Set("Amount", 250);
        var change = Assert.Single(ChangeSummaryLogic.Compare(before, after).Changes);
        var result = ChangePresentationLogic.Build(change, Database(), after);
        Assert.Contains(change.Area, result.Area);
        Assert.Contains("Oxygen", result.Title);
    }

    [Fact]
    public void SameLengthBinaryChangesAndSameLabelItemsStillShowThatStoredValuesDiffer()
    {
        var binary = ChangePresentationLogic.Build(Group("Payload", new BinaryData([1, 2, 3]), new BinaryData([4, 5, 6])));
        Assert.NotEqual(binary.Before, binary.After);
        Assert.Contains(Text("stored_value_changed", "Stored value changed; see Advanced"), Assert.Single(binary.Rows).After);
        var db = Database();
        db.GetItem("FUEL1")!.Name = "Oxygen";
        var items = ChangePresentationLogic.Build(Group("Slots", Slots(Slot("^OXYGEN", 0, 0)), Slots(Slot("^FUEL1", 0, 0))), db);
        Assert.NotEqual(Assert.Single(items.Rows).Before, items.Rows[0].After);
        var old = Slot("^OXYGEN", 0, 0); old.Set("Id", new BinaryData([94, 128]));
        var current = Slot("^OXYGEN", 0, 0); current.Set("Id", new BinaryData([94, 255]));
        var binaryIds = ChangePresentationLogic.Build(Group("Slots", Slots(old), Slots(current)), db);
        Assert.NotEqual(Assert.Single(binaryIds.Rows).Before, binaryIds.Rows[0].After);
    }

    [Theory]
    [InlineData("100", 100)]
    [InlineData("Yes", true)]
    public void TypeChangesCannotDisappearBehindIdenticalFriendlyValues(string before, object after)
    {
        if (after is bool) before = UiStrings.GetOrNull("summary.yes") ?? "Yes";
        var result = ChangePresentationLogic.Build(Group("Value", before, after));
        Assert.NotEqual(result.Before, result.After);
        Assert.Contains(Text("text_type", "Text"), result.Before);
        Assert.Equal(result.Before, Assert.Single(result.Rows).Before);
        Assert.Equal(result.After, result.Rows[0].After);
    }

    [Fact]
    public void LongTextWarnsAboutOmissionWithoutStoppingLaterChangedFields()
    {
        var before = new JsonObject(); before.Add("LongText", new string('a', 2000)); before.Add("Later", 1);
        var after = new JsonObject(); after.Add("LongText", new string('b', 2000)); after.Add("Later", 2);
        var result = ChangePresentationLogic.Build(Group("Settings", before, after));
        Assert.True(result.Truncated);
        Assert.Equal(2, result.Rows.Count);
        Assert.Contains(result.Rows, row => row.Before == "1" && row.After == "2");
        Assert.DoesNotContain("+ -", result.Before + result.After);
    }

    [Fact]
    public void CoordinateLeavesUseOneBasedRowsAndColumnsOnlyInsideInventorySlots()
    {
        var before = JsonObject.Parse("""{"ValidSlotIndices":[{"X":0,"Y":2}],"Universe":{"X":0}}""");
        var after = JsonObject.Parse("""{"ValidSlotIndices":[{"X":3,"Y":4}],"Universe":{"X":3}}""");
        var changes = ChangeSummaryLogic.Compare(before, after).Changes;
        var column = ChangePresentationLogic.Build(Assert.Single(changes, change => change.Path == "ValidSlotIndices[0] / X"));
        Assert.EndsWith(Text("column", "Column"), column.Title);
        Assert.Equal(Text("column", "Column"), Assert.Single(column.Rows).Label);
        Assert.Equal("1", column.Before);
        Assert.Equal("4", column.After);
        var row = ChangePresentationLogic.Build(Assert.Single(changes, change => change.Path == "ValidSlotIndices[0] / Y"));
        Assert.Equal("3", row.Before);
        Assert.Equal("5", row.After);
        var universe = ChangePresentationLogic.Build(Assert.Single(changes, change => change.Path == "Universe / X"));
        Assert.Equal("0", universe.Before);
        Assert.Equal("3", universe.After);
    }
}

using NMSE.Core;
using NMSE.Data;
using NMSE.Models;

namespace NMSE.Tests;

public class PersonalToolsTests
{
    private static JsonObject Inventory(string id, int x = 0, int y = 0) => JsonObject.Parse($$$"""
        {"Slots":[{"Id":"{{{id}}}","Amount":42,"Index":{"X":{{{x}}},"Y":{{{y}}}}}]}
        """);

    private static JsonArray Bindings()
    {
        var groups = new JsonArray();
        for (int c = 0; c < 3; c++)
        {
            var entries = new JsonArray();
            for (int s = 0; s < 10; s++) entries.Add(JsonObject.Parse("""
                {"Action":{"QuickMenuActions":"None","FutureActionFlag":true},"Id":"^","Number":0,"InventoryIndex":{"X":-1,"Y":-1},"FutureField":9007199254740993}
                """));
            var group = new JsonObject();
            group.Add("KeyActions", entries);
            groups.Add(group);
        }
        return groups;
    }

    private static JsonObject WithBindings()
    {
        var root = JsonObject.Parse("""{"PlayerStateData":{"Units":123}}""");
        root.GetObject("PlayerStateData")!.Add("HotActions", Bindings());
        return root;
    }

    [Fact]
    public void InventorySearch_CoversOwnersStorageAndTechnologyWithoutMutating()
    {
        var player = new JsonObject();
        player.Add("Inventory", Inventory("^FUEL1"));
        player.Add("Inventory_TechOnly", Inventory("^UNKNOWN_TECH#12345"));
        player.Add("FreighterInventory", Inventory("^FUEL1", 2, 3));
        foreach (string key in BaseLogic.ChestInventoryKeys.Concat(BaseLogic.StorageInventories.Select(x => x.Key))) player.Add(key, Inventory("^FUEL1"));
        foreach (string key in new[] { "ShipOwnership", "Multitools", "VehicleOwnership" })
        {
            var array = new JsonArray();
            var owner = new JsonObject(); owner.Add("Name", "My favourite");
            owner.Add("Seed", JsonArray.Parse("[true,123]"));
            owner.Add("Resource", JsonObject.Parse("""{"Seed":[true,123]}"""));
            owner.Add(key == "Multitools" ? "Store" : "Inventory", Inventory("^FUEL1"));
            array.Add(null); array.Add(owner); player.Add(key, array);
        }
        var save = new JsonObject(); save.Add("PlayerStateData", player);
        string before = save.ToString();
        var index = InventorySearchLogic.Index(save, new GameItemDatabase());
        Assert.Equal(24, index.Count);
        Assert.Single(InventorySearchLogic.Search(index, "unknown_tech"));
        Assert.Equal(3, InventorySearchLogic.Search(index, "FUEL1 favourite").Count());
        Assert.Empty(InventorySearchLogic.Search(index, "nonexistent"));
        var freighter = Assert.Single(index, x => x.Location.Tab == 4);
        Assert.Equal((2, 3, 42L), (freighter.X, freighter.Y, freighter.Amount));
        Assert.Same(player.GetObject("FreighterInventory"), freighter.Location.Inventory);
        Assert.Equal(before, save.ToString());
    }

    [Fact]
    public void InventorySearch_UsesActiveContextAndSkipsEmptySlots()
    {
        var root = JsonObject.Parse("""{"BaseContext":{"PlayerStateData":{}},"ExpeditionContext":{"PlayerStateData":{}}}""");
        root.RegisterTransform("PlayerStateData", _ => "ExpeditionContext.PlayerStateData");
        root.GetObject("BaseContext.PlayerStateData")!.Add("Inventory", Inventory("^REGULAR"));
        var inventory = Inventory("^EXPEDITION");
        inventory.GetArray("Slots")!.Add(JsonObject.Parse("""{"Id":"^","Amount":0}"""));
        root.GetObject("ExpeditionContext.PlayerStateData")!.Add("Inventory", inventory);
        Assert.Equal("^EXPEDITION", Assert.Single(InventorySearchLogic.Index(root, new())).ItemId);
    }

    [Fact]
    public void InventorySearch_PreservesBinaryIdsAndSkipsUnusedVehicleSlot()
    {
        var binary = new BinaryData(new byte[] { 94, 1, 2, 3, 4, 5, 6, 35, 49, 50, 51, 52, 53 });
        Assert.Equal("^010203040506#12345", InventorySearchLogic.DisplayItemId(binary));
        var wrapper = new JsonObject(); wrapper.Add("Id", binary);
        Assert.Equal("^010203040506#12345", InventorySearchLogic.DisplayItemId(wrapper));
        var vehicles = new JsonArray();
        for (int i = 0; i < 7; i++) { var v = new JsonObject(); v.Add("Inventory", Inventory("^FUEL1")); vehicles.Add(v); }
        var player = new JsonObject(); player.Add("VehicleOwnership", vehicles);
        var save = new JsonObject(); save.Add("PlayerStateData", player);
        var results = InventorySearchLogic.Index(save, new());
        Assert.Equal(6, results.Count);
        Assert.DoesNotContain(results, item => item.Location.Index == 4);
    }

    [Fact]
    public void HotkeyDraft_CancelAndUnchangedApplyDoNotModifySave()
    {
        var save = WithBindings();
        string original = save.ToString();
        var draft = HotkeyLogic.ReadDraft(save);
        Assert.False(HotkeyLogic.Apply(save, draft));
        HotkeyLogic.SetAction(draft, 1, 9, HotkeyLogic.Options(1).Single(x => x.Id == "PhotoMode"));
        Assert.Equal(original, save.ToString());
        Assert.True(HotkeyLogic.Apply(save, draft));
        Assert.Equal("PhotoMode", HotkeyLogic.Entry(save.GetObject("PlayerStateData")!.GetArray("HotActions")!, 1, 9).GetObject("Action")!.GetString("QuickMenuActions"));
        Assert.Equal(123, save.GetObject("PlayerStateData")!.GetInt("Units"));
        // Applying clones the draft so later dialog changes cannot mutate the live save.
        HotkeyLogic.SetAction(draft, 1, 9, HotkeyLogic.Options(1)[0]);
        Assert.Equal("PhotoMode", HotkeyLogic.Entry(HotkeyLogic.ReadDraft(save), 1, 9).GetObject("Action")!.GetString("QuickMenuActions"));
    }

    [Fact]
    public void HotkeyProfiles_PreserveUnknownActionsParametersAndFutureFields()
    {
        var draft = Bindings();
        var unknown = HotkeyLogic.Entry(draft, 2, 7);
        unknown.GetObject("Action")!.Set("QuickMenuActions", "FutureAction");
        unknown.Set("Id", "^CUSTOM"); unknown.Set("Number", 19);
        unknown.GetObject("InventoryIndex")!.Set("X", 8);
        Assert.Equal(draft.ToString(), HotkeyLogic.Import(HotkeyLogic.Export(draft)).ToString());
        HotkeyLogic.SetAction(draft, 1, 5, HotkeyLogic.Options(1).Single(x => x.Id == "EconomyScan"));
        var changed = HotkeyLogic.Entry(draft, 1, 5);
        Assert.Equal(1, changed.GetInt("Number"));
        Assert.True(changed.GetObject("Action")!.GetBool("FutureActionFlag"));
        Assert.Equal(9007199254740993L, changed.GetLong("FutureField"));
        Assert.Equal(8, unknown.GetObject("InventoryIndex")!.GetInt("X"));
    }

    [Fact]
    public void HotkeyEditing_DoesNotOverwriteInactiveExpeditionContext()
    {
        var root = new JsonObject(); root.Add("BaseContext", WithBindings()); root.Add("ExpeditionContext", WithBindings());
        root.RegisterTransform("PlayerStateData", _ => "ExpeditionContext.PlayerStateData");
        string original = root.GetObject("BaseContext")!.ToString();
        var draft = HotkeyLogic.ReadDraft(root);
        HotkeyLogic.SetAction(draft, 0, 1, HotkeyLogic.Options(0)[1]);
        HotkeyLogic.Apply(root, draft);
        Assert.Equal(original, root.GetObject("BaseContext")!.ToString());
    }

    [Fact]
    public void HotkeyValidation_RejectsMalformedDraftBeforeMutation()
    {
        var save = WithBindings(); string before = save.ToString();
        var draft = HotkeyLogic.ReadDraft(save);
        HotkeyLogic.Entry(draft, 0, 0).Set("Number", "0");
        Assert.Throws<InvalidDataException>(() => HotkeyLogic.Apply(save, draft));
        Assert.Equal(before, save.ToString());
        Assert.Throws<InvalidDataException>(() => HotkeyLogic.Validate(new JsonArray()));
        Assert.Throws<InvalidDataException>(() => HotkeyLogic.Import("""{"Format":"Other","Version":1}"""));
        Assert.Throws<InvalidDataException>(() => HotkeyLogic.ReadDraft(new JsonObject()));
        Assert.Throws<ArgumentException>(() => HotkeyLogic.SetAction(Bindings(), 0, 0, HotkeyLogic.Options(1).Single(x => x.Id == "EconomyScan")));
    }

    [Fact]
    public void Summary_DistinguishesMissingNullTypesAndLargeNumbers()
    {
        var old = JsonObject.Parse("""{"Units":9007199254740992,"Text":"1","Deleted":null,"Same":false}""");
        var now = JsonObject.Parse("""{"Units":9007199254740993,"Text":1,"Added":null,"Same":false}""");
        var report = ChangeSummaryLogic.Compare(old, now);
        Assert.Equal(4, report.Changes.Count);
        Assert.False(report.Truncated);
        var units = Assert.Single(report.Changes, x => x.Path == "Units");
        Assert.NotEqual(units.Before, units.After);
        Assert.NotEqual(Assert.Single(report.Changes, x => x.Path == "Text").Before, Assert.Single(report.Changes, x => x.Path == "Text").After);
    }

    [Fact]
    public void Summary_IgnoresObjectPropertyOrderAndReportsArrayChanges()
    {
        Assert.Empty(ChangeSummaryLogic.Compare(JsonObject.Parse("""{"a":1,"b":2}"""), JsonObject.Parse("""{"b":2,"a":1}""")).Changes);
        var report = ChangeSummaryLogic.Compare(JsonObject.Parse("""{"Ships":[{"Name":"My ship","Class":"A"}]}"""), JsonObject.Parse("""{"Ships":[{"Name":"My ship","Class":"S"}]}"""));
        Assert.Contains("My ship", Assert.Single(report.Changes).Field);
        Assert.Contains("Class", report.Changes[0].Field);
        Assert.Empty(ChangeSummaryLogic.Compare(JsonObject.Parse("{}"), JsonObject.Parse("{}"), limit: 0).Changes);
    }

    [Fact]
    public void Summary_CapIsExplicitAndSnapshotIsIsolated()
    {
        var data = JsonObject.Parse("""{"a":9007199254740993,"b":2,"c":3}""");
        byte[] snapshot = ChangeSummaryLogic.Capture(data);
        data.Set("a", 4); data.Set("b", 5); data.Set("c", 6);
        var restored = ChangeSummaryLogic.Restore(snapshot);
        Assert.Equal(9007199254740993L, restored.GetLong("a"));
        var report = ChangeSummaryLogic.Compare(restored, data, limit: 2);
        Assert.Equal(2, report.Changes.Count); Assert.True(report.Truncated);
        Assert.False(ChangeSummaryLogic.Compare(restored, data, limit: 3).Truncated);
    }
}

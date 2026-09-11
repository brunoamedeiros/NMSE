using NMSE.Core;
using NMSE.Models;

namespace NMSE.Tests;

public class ChangeReviewTests
{
    [Fact]
    public void Revert_UsesExactKeysAndPreservesUnrelatedEdits()
    {
        var before = JsonObject.Parse("""{"a / b[0]":{"1.2":3},"Untouched":1}""");
        var after = JsonObject.Parse("""{"a / b[0]":{"1.2":7},"Untouched":9}""");
        var change = Assert.Single(ChangeSummaryLogic.Compare(before, after).Changes,
            change => change.Tokens.First().Key == "a / b[0]");

        Assert.True(ChangeReviewLogic.TryRevert(after, change, out _));
        Assert.Equal(3, Assert.IsType<int>(((JsonObject)after.Get("a / b[0]")!).Get("1.2")));
        Assert.Equal(9, after.GetInt("Untouched"));
        Assert.Equal(7, Assert.IsType<int>(change.AfterValue));
    }

    [Fact]
    public void Revert_RestoresMissingAndNullWithoutConfusingTheirExistence()
    {
        var before = JsonObject.Parse("""{"Deleted":null,"Value":"null"}""");
        var after = JsonObject.Parse("""{"Added":null,"Value":null}""");
        foreach (string key in new[] { "Added", "Deleted", "Value" })
        {
            var change = Assert.Single(ChangeSummaryLogic.Compare(before, after).Changes, change => change.Tokens.Single().Key == key);
            Assert.True(ChangeReviewLogic.TryRevert(after, change, out _));
        }
        Assert.True(after.Contains("Deleted"));
        Assert.Null(after.Get("Deleted"));
        Assert.False(after.Contains("Added"));
        Assert.Equal("null", after.GetString("Value"));
    }

    [Fact]
    public void Revert_RejectsStaleValueAndArrayShiftWithoutMutating()
    {
        var before = JsonObject.Parse("""{"Slots":[{"Amount":1},{"Amount":2}]}""");
        var after = JsonObject.Parse("""{"Slots":[{"Amount":7},{"Amount":7}]}""");
        var change = ChangeSummaryLogic.Compare(before, after).Changes[0];
        after.GetArray("Slots")!.RemoveAt(0);
        string shifted = after.ToString();
        Assert.False(ChangeReviewLogic.TryRevert(after, change, out string error));
        Assert.Equal("summary.stale", error);
        Assert.Equal(shifted, after.ToString());

        before = JsonObject.Parse("""{"Units":1}""");
        after = JsonObject.Parse("""{"Units":2}""");
        change = Assert.Single(ChangeSummaryLogic.Compare(before, after).Changes);
        after.Set("Units", 3);
        Assert.False(ChangeReviewLogic.TryRevert(after, change, out _));
        Assert.Equal(3, after.GetInt("Units"));
    }

    [Theory]
    [InlineData("[1,2,3]", "[1,3]")]
    [InlineData("[1,2]", "[1,2,3]")]
    [InlineData("[{\"Index\":{\"X\":0,\"Y\":0},\"Id\":\"A\"},{\"Index\":{\"X\":1,\"Y\":0},\"Id\":\"B\"}]",
        "[{\"Index\":{\"X\":1,\"Y\":0},\"Id\":\"B\"},{\"Index\":{\"X\":0,\"Y\":0},\"Id\":\"A\"}]")]
    public void Revert_StructuralArrayChangesAreAtomic(string original, string edited)
    {
        var before = JsonObject.Parse("{\"Slots\":" + original + ",\"Units\":1}");
        var after = JsonObject.Parse("{\"Slots\":" + edited + ",\"Units\":9}");
        var change = Assert.Single(ChangeSummaryLogic.Compare(before, after).Changes, change => change.Path == "Slots");
        Assert.Single(change.Tokens);
        Assert.True(ChangeReviewLogic.TryRevert(after, change, out _));
        Assert.Equal(original, after.GetArray("Slots")!.ToString());
        Assert.Equal(9, after.GetInt("Units"));
    }

    [Fact]
    public void SnapshotAndRevert_PreserveBinaryBytesAndExactRawDoubleText()
    {
        var before = JsonObject.Parse("""{"Number":0.30000001192092898,"nested":[]}""");
        before.Add("Binary", new BinaryData([65, 66, 67]));
        before.GetArray("nested")!.Add(new BinaryData([0, 128, 255]));
        byte[] snapshot = ChangeSummaryLogic.Capture(before);
        var restored = ChangeSummaryLogic.Restore(snapshot);
        Assert.Equal(new byte[] { 65, 66, 67 }, Assert.IsType<BinaryData>(restored.Get("Binary")).ToByteArray());
        Assert.Equal(new byte[] { 0, 128, 255 }, Assert.IsType<BinaryData>(restored.GetArray("nested")!.Get(0)).ToByteArray());
        Assert.Equal("0.30000001192092898", Assert.IsType<RawDouble>(restored.Get("Number")).Text);
        var after = (JsonObject)ChangeReviewLogic.Clone(before)!;
        after.Set("Number", 9);
        after.Set("Binary", "ABC");
        var changes = ChangeSummaryLogic.Compare(restored, after).Changes;
        Assert.Equal(2, changes.Count);
        foreach (var change in changes) Assert.True(ChangeReviewLogic.TryRevert(after, change, out _));
        Assert.Equal("0.30000001192092898", Assert.IsType<RawDouble>(after.Get("Number")).Text);
        Assert.IsType<BinaryData>(after.Get("Binary"));
        Assert.NotSame(before.Get("Binary"), after.Get("Binary"));
    }

    [Fact]
    public void ReportValues_AreIndependentFromLaterLiveEdits()
    {
        var before = JsonObject.Parse("""{"Slots":[],"Payload":"old"}""");
        var after = JsonObject.Parse("""{"Slots":[{"Amount":100}],"Payload":"new"}""");
        var change = Assert.Single(ChangeSummaryLogic.Compare(before, after).Changes, change => change.Path == "Slots");
        after.GetArray("Slots")!.GetObject(0).Set("Amount", 200);
        Assert.Equal(100, ((JsonArray)change.AfterValue!).GetObject(0).GetInt("Amount"));
        Assert.False(ChangeReviewLogic.TryRevert(after, change, out _));
    }

    [Fact]
    public void InventoryNavigation_FindsChangedSlotAndRespectsAccountScope()
    {
        var before = JsonObject.Parse("""{"BaseContext":{"PlayerStateData":{"Inventory":{"Slots":[{"Id":"^OXYGEN","Amount":100,"Index":{"X":2,"Y":3}}]}}}}""");
        var after = before.DeepClone();
        after.RegisterTransform("PlayerStateData", _ => "BaseContext.PlayerStateData");
        after.GetObject("PlayerStateData")!.GetObject("Inventory")!.GetArray("Slots")!.GetObject(0).Set("Amount", 200);
        var change = Assert.Single(ChangeSummaryLogic.Compare(before, after).Changes);
        Assert.True(ChangeReviewLogic.TryFindInventory(after, change, out var location, out int? x, out int? y));
        Assert.Equal(1, location!.Tab);
        Assert.Equal(2, x);
        Assert.Equal(3, y);
        var account = Assert.Single(ChangeSummaryLogic.Compare(before, after, scope: ChangeReviewLogic.DataScope.Account).Changes);
        Assert.False(ChangeReviewLogic.TryFindInventory(after, account, out _, out _, out _));
    }
}

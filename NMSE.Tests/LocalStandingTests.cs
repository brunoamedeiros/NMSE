using NMSE.Core;
using NMSE.IO;
using NMSE.Models;

namespace NMSE.Tests;

[Collection("MutableStaticDatabases")]
public class LocalStandingTests
{
    private const long Address = 91259596634545;
    private static JsonObject Player() => JsonObject.Parse("""
        {"UniverseAddress":{"RealityIndex":0,"GalacticAddress":{"VoxelX":-1615,"VoxelY":7,"VoxelZ":-657,"SolarSystemIndex":83,"PlanetIndex":4}},
         "Stats":[
          {"GroupId":"^GLOBAL_STATS","Address":0,"Stats":[{"Id":"^TRA_STANDING","Value":{"IntValue":173}}]},
          {"GroupId":"^SYSTEM_STATS","Address":"0x530007D6F9B1","Stats":[
           {"Id":"^TGUILD_STAND","Value":{"IntValue":22}},
           {"Id":"^TRA_STANDING","Value":{"IntValue":10}},
           {"Id":"^WAR_STANDING","Value":{}},
           {"Id":"^SP_POI_MISSIONS","Value":{}}]},
          {"GroupId":"^SYSTEM_STATS","Address":"0x530107D6F9B1","Stats":[{"Id":"^TRA_STANDING","Value":{"IntValue":7}}]}]}
        """);

    [Fact]
    public void CurrentSystemIgnoresPlanetAndIncludesGalaxy()
    {
        var player = Player();
        Assert.True(LocalStandingLogic.TryCurrentSystem(player, out long address));
        Assert.Equal(Address, address);
        player.GetObject("UniverseAddress")!.Set("RealityIndex", 1);
        Assert.True(LocalStandingLogic.TryCurrentSystem(player, out long otherGalaxy));
        Assert.Equal(Address + (1L << 32), otherGalaxy);
        Assert.True(LocalStandingLogic.TryRead(player, otherGalaxy, "^TRA_STANDING", out int value));
        Assert.Equal(7, value);
    }

    [Theory]
    [InlineData("VoxelX", -2049)]
    [InlineData("VoxelY", 128)]
    [InlineData("SolarSystemIndex", 4096)]
    public void InvalidCoordinatesNeverSelectAnUnrelatedSystem(string key, int value)
    {
        var player = Player();
        player.GetObject("UniverseAddress")!.GetObject("GalacticAddress")!.Set(key, value);
        Assert.False(LocalStandingLogic.TryCurrentSystem(player, out _));
        player.GetObject("UniverseAddress")!.GetObject("GalacticAddress")!.Remove(key);
        Assert.False(LocalStandingLogic.TryCurrentSystem(player, out _));
    }

    [Theory]
    [InlineData("BaseContext")]
    [InlineData("ExpeditionContext")]
    [InlineData("")]
    public void TargetedEditSurvivesSaveReloadAndReviewRevert(string context)
    {
        var state = new JsonObject(); state.Set("PlayerStateData", Player());
        var root = new JsonObject();
        if (context.Length == 0) root = state;
        else root.Set(context, state);
        if (context == "ExpeditionContext") root.Set("ActiveContext", "Season");
        SaveFileManager.RegisterContextTransforms(root);
        var before = ChangeSummaryLogic.Restore(ChangeSummaryLogic.Capture(root));
        var player = root.GetObject("PlayerStateData")!;
        Assert.True(LocalStandingLogic.TryWrite(player, Address, "^TRA_STANDING", 10, 30));
        Assert.True(LocalStandingLogic.TryWrite(player, Address, "^TRA_STANDING", 10, 30)); // Repeated UI flush.
        var change = Assert.Single(ChangeSummaryLogic.Compare(before, root).Changes);
        Assert.Equal("10", change.Before);
        Assert.Equal("30", change.After);
        var presentation = ChangePresentationLogic.Build(change, currentRoot: root);
        Assert.Equal(NMSE.Data.UiStrings.Get("milestone.gek"), presentation.Title);
        Assert.Contains("01B0:0086:056E:0053", presentation.Area);
        Assert.Equal("30", Assert.Single(presentation.Rows).After);
        var reloaded = ChangeSummaryLogic.Restore(ChangeSummaryLogic.Capture(root));
        Assert.True(ChangeReviewLogic.TryRevert(reloaded, change, out _));
        Assert.Empty(ChangeSummaryLogic.Compare(before, reloaded).Changes);
    }

    [Fact]
    public void UnchangedViewAndZeroPreserveExactSaveRepresentation()
    {
        var player = Player();
        var before = player.ToDisplayString();
        Assert.True(LocalStandingLogic.TryRead(player, Address, "^WAR_STANDING", out int zero));
        Assert.Equal(0, zero);
        Assert.True(LocalStandingLogic.TryWrite(player, Address, "^WAR_STANDING", 0, 0));
        Assert.True(LocalStandingLogic.TryWrite(player, Address, "^TRA_STANDING", 10, 10));
        Assert.Equal(before, player.ToDisplayString());
        Assert.True(LocalStandingLogic.TryWrite(player, Address, "^WAR_STANDING", 0, 30));
        var value = player.GetArray("Stats")!.GetObject(1).GetArray("Stats")!.GetObject(2).GetObject("Value")!;
        Assert.Equal(1, value.Length);
        Assert.Equal(30, value.GetInt("IntValue"));
    }

    [Fact]
    public void MissingUnknownAmbiguousAndStaleRecordsAreNeverWritten()
    {
        var player = Player();
        var before = player.ToDisplayString();
        Assert.False(LocalStandingLogic.TryWrite(player, Address + 1, "^TRA_STANDING", 10, 30));
        Assert.False(LocalStandingLogic.TryWrite(player, Address, "^EXP_STANDING", 0, 30));
        Assert.False(LocalStandingLogic.TryWrite(player, Address, "^SP_POI_MISSIONS", 0, 5));
        Assert.False(LocalStandingLogic.TryWrite(player, Address, "^TRA_STANDING", 9, 30));
        Assert.Equal(before, player.ToDisplayString());
        var groups = player.GetArray("Stats")!;
        groups.Add(ChangeReviewLogic.Clone(groups.Get(1)));
        before = player.ToDisplayString();
        Assert.False(LocalStandingLogic.TryWrite(player, Address, "^TRA_STANDING", 10, 30));
        Assert.Equal(before, player.ToDisplayString());
    }

    [Fact]
    public void ReorderedEntriesAndCoordinateDraftsRetainDisplayedTarget()
    {
        var player = Player();
        Assert.True(LocalStandingLogic.TryCurrentSystem(player, out long loadedAddress));
        var groups = player.GetArray("Stats")!;
        var group = groups.Get(1); groups.RemoveAt(1); groups.Add(group);
        player.GetObject("UniverseAddress")!.Set("RealityIndex", 1);
        Assert.True(LocalStandingLogic.TryWrite(player, loadedAddress, "^TRA_STANDING", 10, 30));
        Assert.True(LocalStandingLogic.TryRead(player, Address + (1L << 32), "^TRA_STANDING", out int other));
        Assert.Equal(7, other);
        Assert.Equal(173, groups.GetObject(0).GetArray("Stats")!.GetObject(0).GetObject("Value")!.GetInt("IntValue"));
    }

    [Fact]
    public void MultipleEditsRejectAStaleFieldBeforeWritingAnyField()
    {
        var player = Player();
        var before = player.ToDisplayString();
        var original = new Dictionary<string, int> { ["^TRA_STANDING"] = 10, ["^TGUILD_STAND"] = 21 };
        var edited = new Dictionary<string, int> { ["^TRA_STANDING"] = 30, ["^TGUILD_STAND"] = 40 };
        Assert.False(LocalStandingLogic.TryWriteAll(player, Address, original, edited));
        Assert.Equal(before, player.ToDisplayString());
        original["^TGUILD_STAND"] = 22;
        Assert.True(LocalStandingLogic.TryWriteAll(player, Address, original, edited));
        Assert.True(LocalStandingLogic.TryRead(player, Address, "^TRA_STANDING", out int gek));
        Assert.True(LocalStandingLogic.TryRead(player, Address, "^TGUILD_STAND", out int guild));
        Assert.Equal(30, gek);
        Assert.Equal(40, guild);
    }

    [Fact]
    public void EmptyCounterReviewShowsZeroAndRevertRestoresEmptyObject()
    {
        var root = new JsonObject(); root.Set("PlayerStateData", Player());
        var before = ChangeSummaryLogic.Restore(ChangeSummaryLogic.Capture(root));
        Assert.True(LocalStandingLogic.TryWrite(root.GetObject("PlayerStateData")!, Address, "^WAR_STANDING", 0, 30));
        var change = Assert.Single(ChangeSummaryLogic.Compare(before, root).Changes);
        var presentation = ChangePresentationLogic.Build(change, currentRoot: root);
        Assert.Equal("0", presentation.Before);
        Assert.Equal("30", presentation.After);
        Assert.True(ChangeReviewLogic.TryRevert(root, change, out _));
        Assert.Empty(ChangeSummaryLogic.Compare(before, root).Changes);
    }

    [Theory]
    [InlineData("{\"FloatValue\":10.5}")]
    [InlineData("{\"IntValue\":2147483648}")]
    [InlineData("{\"IntValue\":null}")]
    public void UnsupportedNumericValuesAreNotCoerced(string json)
    {
        var player = Player();
        player.GetArray("Stats")!.GetObject(1).GetArray("Stats")!.GetObject(1).Set("Value", JsonObject.Parse(json));
        string before = player.ToDisplayString();
        Assert.False(LocalStandingLogic.TryRead(player, Address, "^TRA_STANDING", out _));
        Assert.False(LocalStandingLogic.TryWrite(player, Address, "^TRA_STANDING", 10, 30));
        Assert.Equal(before, player.ToDisplayString());
    }
}

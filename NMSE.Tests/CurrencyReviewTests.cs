using System.Globalization;
using NMSE.Core;
using NMSE.Models;

namespace NMSE.Tests;

[Collection("MutableStaticDatabases")]
public class CurrencyReviewTests
{
    [Theory]
    [InlineData(333529366L, 3333529366L)]
    [InlineData(0L, 2147483647L)]
    [InlineData(2147483647L, 2147483648L)]
    [InlineData(2147483648L, 4294967295L)]
    [InlineData(4294967295L, 0L)]
    public void ReviewMatchesPlayerCurrencyAcrossSaveWriteReloadAndRevert(long original, long edited)
    {
        foreach (string context in new[] { "", "BaseContext", "ExpeditionContext" })
        foreach (string key in new[] { "Units", "Nanites", "Specials" })
        {
            var player = new JsonObject();
            MainStatsLogic.WriteStatValues(player, 100, 100, 100, original, original, original);
            var state = new JsonObject();
            state.Set("PlayerStateData", player);
            var before = new JsonObject();
            if (context.Length == 0) before = state;
            else before.Set(context, state);
            before = ChangeSummaryLogic.Restore(ChangeSummaryLogic.Capture(before));
            var after = (JsonObject)ChangeReviewLogic.Clone(before)!;
            var editedPlayer = (context.Length == 0 ? after : after.GetObject(context)!).GetObject("PlayerStateData")!;
            MainStatsLogic.WriteStatValues(editedPlayer, 100, 100, 100, edited, edited, edited);

            var change = Assert.Single(ChangeSummaryLogic.Compare(before, after).Changes, c => c.Tokens[^1].Key == key);
            string beforeText = original.ToString("N0", CultureInfo.CurrentCulture);
            string afterText = edited.ToString("N0", CultureInfo.CurrentCulture);
            Assert.Equal(beforeText, change.Before);
            Assert.Equal(afterText, change.After);
            var presentation = ChangePresentationLogic.Build(change);
            Assert.Equal(beforeText, presentation.Before);
            Assert.Equal(afterText, presentation.After);
            Assert.Equal(afterText, Assert.Single(presentation.Rows).After);

            Assert.Equal(unchecked((int)(uint)edited), Assert.IsType<int>(change.AfterValue));
            var reloaded = JsonObject.Parse(JsonParser.Serialize(after, formatted: false, skipReverseMapping: true));
            var reloadedPlayer = (context.Length == 0 ? reloaded : reloaded.GetObject(context)!).GetObject("PlayerStateData")!;
            Assert.Equal((decimal)edited, MainStatsLogic.ReadRawStatValue(reloadedPlayer, key));
            Assert.True(ChangeReviewLogic.TryRevert(reloaded, change, out _));
            Assert.Equal((decimal)original, MainStatsLogic.ReadRawStatValue(reloadedPlayer, key));
        }
    }

    [Theory]
    [InlineData("-961437930")]
    [InlineData("-961437930.0")]
    [InlineData("3333529366")]
    [InlineData("3333529366.0")]
    public void BothCulturesAndNumericRepresentationsShowTheSameBalance(string number)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            foreach (string culture in new[] { "en-US", "pt-BR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                var before = JsonObject.Parse("""{"PlayerStateData":{"Units":{"Value":0}}}""");
                var after = JsonObject.Parse("{\"PlayerStateData\":{\"Units\":{\"Value\":" + number + "}}}");
                var change = Assert.Single(ChangeSummaryLogic.Compare(before, after).Changes);
                string expected = 3333529366L.ToString("N0", CultureInfo.CurrentCulture);
                Assert.Equal(expected, change.After);
                Assert.Equal(expected, ChangePresentationLogic.Build(change).After);
            }
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData("{\"PlayerStateData\":{\"VoxelX\":-961437930}}")]
    [InlineData("{\"PlayerStateData\":{\"Inventory\":{\"Units\":-961437930}}}")]
    [InlineData("{\"Other\":{\"PlayerStateData\":{\"Units\":-961437930}}}")]
    public void UnrelatedNegativeNumbersStayNegative(string json)
    {
        var before = JsonObject.Parse(json.Replace("-961437930", "0"));
        var after = JsonObject.Parse(json);
        var change = Assert.Single(ChangeSummaryLogic.Compare(before, after).Changes);
        string expected = (-961437930).ToString("N0", CultureInfo.CurrentCulture);
        Assert.Equal(expected, change.After);
        Assert.Equal(expected, ChangePresentationLogic.Build(change).After);
    }
}

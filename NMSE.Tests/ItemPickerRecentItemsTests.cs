using System.Reflection;
using NMSE.Config;
using NMSE.Core;
using NMSE.Data;

namespace NMSE.Tests;

public class ItemPickerRecentItemsTests
{
    [Fact]
    public void ConfirmedChoices_KeepTenMostRecentDistinctIds()
    {
        var config = new AppConfig();
        for (int i = 0; i < 12; i++) ItemPickerRecentItems.Remember(config, $"^ITEM{i}");
        ItemPickerRecentItems.Remember(config, "item7");

        Assert.Equal(new[] { "ITEM7", "ITEM11", "ITEM10", "ITEM9", "ITEM8", "ITEM6", "ITEM5", "ITEM4", "ITEM3", "ITEM2" },
            ItemPickerRecentItems.Read(config));
    }

    [Fact]
    public void RecentChoices_OnlyExposeItemsCompatibleWithCurrentInventory()
    {
        var config = new AppConfig();
        ItemPickerRecentItems.Remember(config, "OXYGEN");
        ItemPickerRecentItems.Remember(config, "HYPERDRIVE");
        ItemPickerRecentItems.Remember(config, "CARBON");
        var carbon = new GameItem { Id = "^CARBON", Name = "Carbon" };
        var oxygen = new GameItem { Id = "oxygen", Name = "Oxygen" };

        Assert.Equal(new[] { carbon, oxygen }, ItemPickerRecentItems.GetCompatible(config, new[] { oxygen, carbon }));
        Assert.Empty(ItemPickerRecentItems.GetCompatible(config, Array.Empty<GameItem>()));
        Assert.Equal(new[] { "CARBON", "HYPERDRIVE", "OXYGEN" }, ItemPickerRecentItems.Read(config));
    }

    [Fact]
    public void InvalidAndDuplicateHistory_IsIgnoredWithoutChangingConfig()
    {
        var config = new AppConfig();
        const string history = "|^oxygen|OXYGEN|^|\n|CARBON|";
        config.SetProperty("InventoryPicker.RecentItems", history);

        Assert.Equal(new[] { "OXYGEN", "CARBON" }, ItemPickerRecentItems.Read(config));
        Assert.Equal(history, config.GetProperty("InventoryPicker.RecentItems"));
        ItemPickerRecentItems.Remember(config, "OXYGEN|CARBON");
        Assert.Equal(history, config.GetProperty("InventoryPicker.RecentItems"));
    }

    [Fact]
    public void ConfirmedChoice_SurvivesConfigReloadWhileBrowsingDoesNotWrite()
    {
        string path = Path.Combine(Path.GetTempPath(), $"nmse-picker-{Guid.NewGuid():N}.conf");
        try
        {
            var config = new AppConfig();
            typeof(AppConfig).GetField("_configPath", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(config, path);
            ItemPickerRecentItems.GetCompatible(config, new[] { new GameItem { Id = "OXYGEN" } });
            Assert.False(File.Exists(path));

            config.Language = "pt-BR";
            ItemPickerRecentItems.Remember(config, "^OXYGEN");
            var reloaded = new AppConfig();
            typeof(AppConfig).GetField("_configPath", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(reloaded, path);
            reloaded.Load();
            Assert.Equal(new[] { "OXYGEN" }, ItemPickerRecentItems.Read(reloaded));
            Assert.Equal("pt-BR", reloaded.Language);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

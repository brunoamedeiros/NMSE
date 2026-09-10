using NMSE.Data;

namespace NMSE.Tests;

public class LocalisationLookupTests
{
    [Fact]
    public void GetName_ResolvesMixedCaseCosmosReferenceAndPrefersExactMatch()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"nmse_loc_lookup_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "pt-BR.json"),
                "{\"BLD_BIG_MAG_1X1_NAME\":\"Plataforma magnetizada\",\"EXACT_KEY\":\"Upper\",\"exact_key\":\"Exact\"}");
            var service = new LocalisationService();
            service.SetLangDirectory(directory);
            Assert.True(service.LoadLanguage("pt-BR"));
            Assert.Equal("Plataforma magnetizada", service.GetName(new GameItem
            {
                Name = "Fallback", NameLocStr = "BLD_BIG_MAG_1x1_NAME"
            }));
            Assert.Equal("Exact", service.Lookup("exact_key"));
            Assert.Null(service.Lookup("MISSING_KEY"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

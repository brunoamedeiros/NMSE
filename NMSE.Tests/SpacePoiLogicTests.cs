using NMSE.Core;
using NMSE.Models;

namespace NMSE.Tests;

/// <summary>
/// Tests for the Cosmos (7.0) deep-space POI discovery list handling.
/// </summary>
public class SpacePoiLogicTests
{
    // Real value captured from a 7.0 save (Version 4735): the player's current system was
    // VoxelX=-1615, VoxelY=7, VoxelZ=-657, SolarSystemIndex=122, PlanetIndex=0, Euclid.
    private const long SampleUA = 134140550117809; // 0x7A0007D6F9B1

    [Fact]
    public void UnpackUniverseAddress_DecodesRealSaveValue()
    {
        var addr = SpacePoiLogic.UnpackUniverseAddress(SampleUA);

        Assert.Equal(-1615, addr.VoxelX);
        Assert.Equal(7, addr.VoxelY);
        Assert.Equal(-657, addr.VoxelZ);
        Assert.Equal(122, addr.SolarSystemIndex);
        Assert.Equal(0, addr.PlanetIndex);
        Assert.Equal(0, addr.RealityIndex);
    }

    [Fact]
    public void PackUniverseAddress_RoundTripsRealSaveValue()
    {
        var addr = SpacePoiLogic.UnpackUniverseAddress(SampleUA);
        Assert.Equal(SampleUA, SpacePoiLogic.PackUniverseAddress(addr));
    }

    [Theory]
    [InlineData(0, 0, 0, 0, 0, 0)]
    [InlineData(2047, 127, 2047, 4095, 6, 255)]
    [InlineData(-2048, -128, -2048, 1, 1, 1)]
    [InlineData(-1, -1, -1, 0, 0, 0)]
    [InlineData(1234, -56, -789, 511, 3, 42)]
    public void PackUnpack_RoundTripsEdgeCases(int x, int y, int z, int system, int planet, int reality)
    {
        var input = new SpacePoiLogic.UniverseAddress(x, y, z, system, planet, reality);
        long packed = SpacePoiLogic.PackUniverseAddress(input);
        var output = SpacePoiLogic.UnpackUniverseAddress(packed);
        Assert.Equal(input, output);
    }

    [Fact]
    public void UnpackUniverseAddress_MatchesPortalCodeOfSameCoordinates()
    {
        // The portal code derived from the unpacked voxels must equal the one the game shows
        // for the same coordinates via the existing CoordinateHelper path.
        var addr = SpacePoiLogic.UnpackUniverseAddress(SampleUA);
        string portal = Core.Utilities.CoordinateHelper.VoxelToPortalCode(
            addr.VoxelX, addr.VoxelY, addr.VoxelZ, addr.SolarSystemIndex, addr.PlanetIndex);
        Assert.Equal("007A07D6F9B1", portal);
    }

    [Fact]
    public void ReadUA_AcceptsIntegerRawDoubleAndHexString()
    {
        var asLong = JsonObject.Parse("{\"UA\":134140550117809}");
        Assert.Equal(SampleUA, SpacePoiLogic.ReadUA(asLong));

        var asHex = new JsonObject();
        asHex.Set("UA", "0x7A0007D6F9B1");
        Assert.Equal(SampleUA, SpacePoiLogic.ReadUA(asHex));

        var missing = new JsonObject();
        Assert.Equal(0, SpacePoiLogic.ReadUA(missing));
    }

    [Fact]
    public void IsEmptySlot_TrueOnlyWhenAllFieldsZero()
    {
        Assert.True(SpacePoiLogic.IsEmptySlot(JsonObject.Parse("{\"UA\":0,\"PackedData0\":0,\"PackedData1\":0}")));
        Assert.False(SpacePoiLogic.IsEmptySlot(JsonObject.Parse("{\"UA\":1,\"PackedData0\":0,\"PackedData1\":0}")));
        Assert.False(SpacePoiLogic.IsEmptySlot(JsonObject.Parse("{\"UA\":0,\"PackedData0\":5,\"PackedData1\":0}")));
        Assert.False(SpacePoiLogic.IsEmptySlot(JsonObject.Parse("{\"UA\":0,\"PackedData0\":0,\"PackedData1\":9}")));
    }

    private static JsonObject BuildPlayerState(params (long ua, long d0, long d1)[] entries)
    {
        const int slots = 8;
        var arr = new JsonArray();
        for (int i = 0; i < slots; i++)
        {
            var slot = new JsonObject();
            if (i < entries.Length)
            {
                slot.Set("UA", entries[i].ua);
                slot.Set("PackedData0", entries[i].d0);
                slot.Set("PackedData1", entries[i].d1);
            }
            else
            {
                slot.Set("UA", 0L);
                slot.Set("PackedData0", 0L);
                slot.Set("PackedData1", 0L);
            }
            arr.Add(slot);
        }
        var ps = new JsonObject();
        ps.Set(SpacePoiLogic.DiscoveriesKey, arr);
        ps.Set(SpacePoiLogic.NextIndexKey, entries.Length);
        return ps;
    }

    [Fact]
    public void GetPopulatedIndices_ReturnsOnlyNonEmptySlotsInOrder()
    {
        var ps = BuildPlayerState((SampleUA, 1, 0), (SampleUA + 1, 2, 0), (SampleUA + 2, 3, 0));
        var arr = ps.GetArray(SpacePoiLogic.DiscoveriesKey)!;

        Assert.Equal(new[] { 0, 1, 2 }, SpacePoiLogic.GetPopulatedIndices(arr));
        Assert.Equal(3, SpacePoiLogic.ReadNextIndex(ps));
    }

    [Fact]
    public void RemoveSlots_CompactsSurvivorsAndUpdatesNextIndex()
    {
        var ps = BuildPlayerState((100, 1, 10), (200, 2, 20), (300, 3, 30), (400, 4, 40));
        var arr = ps.GetArray(SpacePoiLogic.DiscoveriesKey)!;

        int removed = SpacePoiLogic.RemoveSlots(ps, arr, new[] { 1, 2 });

        Assert.Equal(2, removed);
        Assert.Equal(8, arr.Length); // fixed-size array is preserved
        Assert.Equal(new[] { 0, 1 }, SpacePoiLogic.GetPopulatedIndices(arr));

        // Survivors shifted to the front in original order
        Assert.Equal(100, SpacePoiLogic.ReadUA(arr.GetObject(0)));
        Assert.Equal(10, SpacePoiLogic.ReadPacked(arr.GetObject(0), SpacePoiLogic.PackedData1Key));
        Assert.Equal(400, SpacePoiLogic.ReadUA(arr.GetObject(1)));
        Assert.Equal(4, SpacePoiLogic.ReadPacked(arr.GetObject(1), SpacePoiLogic.PackedData0Key));

        // Freed tail is zeroed
        for (int i = 2; i < arr.Length; i++)
            Assert.True(SpacePoiLogic.IsEmptySlot(arr.GetObject(i)));

        Assert.Equal(2, SpacePoiLogic.ReadNextIndex(ps));
    }

    [Fact]
    public void RemoveSlots_IgnoresEmptyAndOutOfRangeIndices()
    {
        var ps = BuildPlayerState((100, 1, 0));
        var arr = ps.GetArray(SpacePoiLogic.DiscoveriesKey)!;

        int removed = SpacePoiLogic.RemoveSlots(ps, arr, new[] { 5, 99 });

        Assert.Equal(0, removed);
        Assert.Equal(new[] { 0 }, SpacePoiLogic.GetPopulatedIndices(arr));
        Assert.Equal(1, SpacePoiLogic.ReadNextIndex(ps));
    }

    [Fact]
    public void RemoveSlots_NoIndices_IsNoOp()
    {
        var ps = BuildPlayerState((100, 1, 0), (200, 2, 0));
        var arr = ps.GetArray(SpacePoiLogic.DiscoveriesKey)!;

        Assert.Equal(0, SpacePoiLogic.RemoveSlots(ps, arr, Array.Empty<int>()));
        Assert.Equal(2, SpacePoiLogic.ReadNextIndex(ps));
        Assert.Equal(200, SpacePoiLogic.ReadUA(arr.GetObject(1)));
    }
}

using System.Globalization;
using NMSE.Core.Utilities;
using NMSE.Models;

namespace NMSE.Core;

/// <summary>
/// Handles the deep-space point-of-interest discovery list introduced in Cosmos (7.0).
/// <para>
/// <c>PlayerStateData.SpacePoiDiscoveries</c> is a fixed-size array of slots. Each slot holds a
/// packed 64-bit universe address (<c>UA</c>) plus two opaque packed payloads
/// (<c>PackedData0</c>, <c>PackedData1</c>). Unused slots are all zeros. The game appends new
/// discoveries at <c>PlayerStateData.SpacePoiDiscoveryNextIndex</c>.
/// </para>
/// </summary>
internal static class SpacePoiLogic
{
    /// <summary>JSON key of the discovery slot array under PlayerStateData.</summary>
    internal const string DiscoveriesKey = "SpacePoiDiscoveries";

    /// <summary>JSON key of the next free slot index under PlayerStateData.</summary>
    internal const string NextIndexKey = "SpacePoiDiscoveryNextIndex";

    /// <summary>Packed universe address field inside a discovery slot.</summary>
    internal const string UaKey = "UA";

    /// <summary>First opaque packed payload inside a discovery slot.</summary>
    internal const string PackedData0Key = "PackedData0";

    /// <summary>Second opaque packed payload inside a discovery slot.</summary>
    internal const string PackedData1Key = "PackedData1";

    /// <summary>
    /// Decoded components of a packed 64-bit universe address.
    /// </summary>
    internal readonly record struct UniverseAddress(
        int VoxelX, int VoxelY, int VoxelZ, int SolarSystemIndex, int PlanetIndex, int RealityIndex);

    // Packed UA bit layout (least significant first). Hex form reads
    // 0x[P][SSS][GG][YY][ZZZ][XXX], e.g. 0x107A0007D6F9B1 = planet 1, system 0x07A,
    // Euclid, Y=7, Z=0xD6F (-657), X=0x9B1 (-1615). Verified against a 7.0 save where
    // the same system appears with planet digits 0..6 and matching TeleportEndpoints.
    //   bits  0-11  VoxelX            (12 bits, two's complement)
    //   bits 12-23  VoxelZ            (12 bits, two's complement)
    //   bits 24-31  VoxelY            ( 8 bits, two's complement)
    //   bits 32-39  RealityIndex      ( 8 bits, galaxy)
    //   bits 40-51  SolarSystemIndex  (12 bits)
    //   bits 52-55  PlanetIndex       ( 4 bits)
    //   bits 56-63  unused / flags    (ignored)
    private const int VoxelXShift = 0;
    private const int VoxelZShift = 12;
    private const int VoxelYShift = 24;
    private const int RealityShift = 32;
    private const int SystemShift = 40;
    private const int PlanetShift = 52;

    /// <summary>
    /// Unpack a 64-bit universe address into its voxel / system / planet / galaxy components.
    /// </summary>
    internal static UniverseAddress UnpackUniverseAddress(long packed)
    {
        ulong u = (ulong)packed;
        int rawX = (int)((u >> VoxelXShift) & 0xFFF);
        int rawZ = (int)((u >> VoxelZShift) & 0xFFF);
        int rawY = (int)((u >> VoxelYShift) & 0xFF);
        int system = (int)((u >> SystemShift) & 0xFFF);
        int planet = (int)((u >> PlanetShift) & 0xF);
        int reality = (int)((u >> RealityShift) & 0xFF);

        return new UniverseAddress(
            SignExtend(rawX, 12),
            SignExtend(rawY, 8),
            SignExtend(rawZ, 12),
            system,
            planet,
            reality);
    }

    /// <summary>
    /// Pack voxel / system / planet / galaxy components into a 64-bit universe address.
    /// Inverse of <see cref="UnpackUniverseAddress"/>.
    /// </summary>
    internal static long PackUniverseAddress(UniverseAddress address)
    {
        ulong u = 0;
        u |= ((ulong)address.VoxelX & 0xFFF) << VoxelXShift;
        u |= ((ulong)address.VoxelZ & 0xFFF) << VoxelZShift;
        u |= ((ulong)address.VoxelY & 0xFF) << VoxelYShift;
        u |= ((ulong)address.SolarSystemIndex & 0xFFF) << SystemShift;
        u |= ((ulong)address.PlanetIndex & 0xF) << PlanetShift;
        u |= ((ulong)address.RealityIndex & 0xFF) << RealityShift;
        return (long)u;
    }

    private static int SignExtend(int value, int bits)
    {
        int signBit = 1 << (bits - 1);
        return (value & signBit) != 0 ? value - (1 << bits) : value;
    }

    /// <summary>
    /// Read the packed UA from a discovery slot. Tolerates integer, RawDouble and "0x" hex string forms.
    /// Returns 0 when the field is missing or unreadable.
    /// </summary>
    internal static long ReadUA(JsonObject slot)
    {
        try
        {
            var raw = slot.GetValue(UaKey);
            if (raw is string s)
            {
                string hex = CoordinateHelper.NormalizeGalacticAddress(s);
                if (hex.StartsWith("0x", StringComparison.Ordinal)
                    && long.TryParse(hex[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long parsed))
                    return parsed;
                return 0;
            }
            return slot.GetLong(UaKey);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Read one of the packed payload fields as a long. Returns 0 when missing or unreadable.
    /// </summary>
    internal static long ReadPacked(JsonObject slot, string key)
    {
        try { return slot.GetLong(key); }
        catch { return 0; }
    }

    /// <summary>
    /// A slot is considered empty when its UA and both packed payloads are zero.
    /// </summary>
    internal static bool IsEmptySlot(JsonObject slot)
    {
        return ReadUA(slot) == 0
            && ReadPacked(slot, PackedData0Key) == 0
            && ReadPacked(slot, PackedData1Key) == 0;
    }

    /// <summary>
    /// Indices of all populated (non-empty) slots, in array order.
    /// </summary>
    internal static List<int> GetPopulatedIndices(JsonArray discoveries)
    {
        var result = new List<int>();
        for (int i = 0; i < discoveries.Length; i++)
        {
            if (discoveries.Get(i) is JsonObject slot && !IsEmptySlot(slot))
                result.Add(i);
        }
        return result;
    }

    /// <summary>
    /// Read the next-free-slot index from PlayerStateData. Returns 0 when missing.
    /// </summary>
    internal static int ReadNextIndex(JsonObject playerState)
    {
        try { return playerState.GetInt(NextIndexKey); }
        catch { return 0; }
    }

    /// <summary>
    /// Remove the given slots from the discovery list. Remaining populated slots are compacted
    /// to the front of the array (preserving order), freed slots are zeroed at the tail, and
    /// <c>SpacePoiDiscoveryNextIndex</c> is set to the new populated count so the game appends
    /// after the last surviving entry.
    /// </summary>
    /// <returns>Number of slots actually removed.</returns>
    internal static int RemoveSlots(JsonObject playerState, JsonArray discoveries, IEnumerable<int> indices)
    {
        var toRemove = new HashSet<int>(indices);
        if (toRemove.Count == 0) return 0;

        // Snapshot survivors in order
        var survivors = new List<JsonObject>();
        int removed = 0;
        for (int i = 0; i < discoveries.Length; i++)
        {
            if (discoveries.Get(i) is not JsonObject slot) continue;
            if (toRemove.Contains(i))
            {
                if (!IsEmptySlot(slot)) removed++;
                continue;
            }
            if (!IsEmptySlot(slot)) survivors.Add(slot);
        }

        // Rewrite: survivors first, then zeroed tail
        int write = 0;
        for (; write < survivors.Count && write < discoveries.Length; write++)
        {
            var src = survivors[write];
            var dst = discoveries.GetObject(write);
            if (!ReferenceEquals(src, dst))
            {
                dst.Set(UaKey, ReadUA(src));
                dst.Set(PackedData0Key, ReadPacked(src, PackedData0Key));
                dst.Set(PackedData1Key, ReadPacked(src, PackedData1Key));
            }
        }
        for (; write < discoveries.Length; write++)
        {
            var dst = discoveries.GetObject(write);
            dst.Set(UaKey, 0L);
            dst.Set(PackedData0Key, 0L);
            dst.Set(PackedData1Key, 0L);
        }

        playerState.Set(NextIndexKey, survivors.Count);
        return removed;
    }
}

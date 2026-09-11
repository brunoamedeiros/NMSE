# Cosmos 7.01 fork progress

Feature branch: `feature/cosmos-compatibility`. Game resources were regenerated on 10 September 2026
from the installed Steam game using MBINCompiler `v7.01.0-pre1`, HGPAKtool `1.1.3`
and ImageMagick `7.1.2-22`.

## Implemented

- Save-key mapping updated to Cosmos 7.01.
- Refreshed item, recipe, reward, title, guide and language databases and item icons.
- Item database grows from 4,914 to 5,134 entries: 222 additions and two removals.
  Additions include station modules, salvage items, AtlasPass V4 and the Corvette
  Tractor Beam. `SWARM_TROPHY_G` and `SWARM_TROPHY_R` are absent from the current
  extracted game tables; this changes the picker data, not existing save contents.
- Technology metadata catalog grows from 328 to 333 entries.
- All 16 game-language files regenerated. English has 84,303 strings and Brazilian
  Portuguese has 84,304.
- Mixed-case references such as `BLD_BIG_MAG_1x1_NAME` now resolve against the
  uppercase language-table keys during extraction and in the editor. Exact
  matches retain priority. This fixes the Tractor Beam name and descriptions.
- Generated technology code enables nullable annotations and no longer claims a
  hardcoded Remnant version.

## Validation

- Editor and extractor regression tests cover the regenerated resources and translations.
- All 5,077 extracted item icons converted successfully; none skipped.
- Every newly added item with an icon has a generated icon. New item name keys
  resolve in both English and Brazilian Portuguese.
- A copy of the previously inspected Steam Cosmos save passed a compressed
  write/read round-trip through the editor's IO layer with all JSON content
  unchanged. The original game save was not written.
- Data-count expectations were updated for 2,151 words and 58 guide topics,
  including Space Station Ownership.

## Reproducing the resource update

Run `dotnet run --project NMSE.Extractor/NMSE.Extractor.csproj -c Release` and
confirm extraction. The extractor writes beneath its executable directory,
normally `NMSE.Extractor/Build/bin/Release/net10.0-windows/win-x64/`.

After checking the extraction log and outputs, copy the generated `Resources/json/`,
`Resources/images/`, `Resources/map/mapping.json` and
`Data/TechPackDatabase.Generated.cs` to the matching main-project paths. Overlay
the image directory so existing glyphs and other editor assets remain available.
Run `dotnet test NMSE.slnx -c Release` and `dotnet build NMSE.slnx -c Release`.

The initial storage estimate was 15.59 GiB, with 75.17 GiB free. The extractor
processed 77 relevant archives and removed its temporary archive and MBIN folders
when finished.

## Branch scope

This branch contains compatibility changes only. Optional editor features, including
the Deep Space tab, live on `feature/editor-enhancements`. The `develop` branch
integrates both feature branches.

## Remaining scope

Milestones now includes Local Station Standing for an existing current-system
`^SYSTEM_STATS` group. The group is selected by its packed universe address with
planet zero, including galaxy bits. Race and guild fields are matched by stat ID;
global reputation and other systems are preserved. Missing or ambiguous records
are not created. The UI supports English and Brazilian Portuguese, with English
fallback for other languages.

The local Gek field `^TRA_STANDING.Value.IntValue` was identified from a user's
reported 10/30 standing and backup progression from empty (zero) through 5, 8,
and 10. The same group contains the other race and guild standing IDs. An empty
Value object represents zero; edits write only IntValue. Manual in-game
confirmation of an edited local standing value remains necessary.

Dedicated alliance and station-directorship editing still needs populated save
examples. `^SP_POI_MISSIONS` is a candidate salvage-contract counter, but remains
unexposed until a completed-contract before/after comparison validates it.

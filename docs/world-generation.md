# World-generation tool: Old World heightmaps

This first increment implements heightmap authoring only. It does not implement gameplay, 3D world rendering, hydraulic erosion, river routing, lake classification, climate, biomes, or seasonal snow.

## Quick start

1. Open the Unity project with **6000.6.8f1** and allow scripts to compile.
2. Choose **Tools → GrandStrategy → World Generator**.
3. Keep **1024 × 512 · Prototype** selected for initial exploration.
4. Enter a seed and click **Generate**, or use **New seed + generate**.
5. Inspect **Relief**, **Grayscale**, and **LandAndWater** views. Hover over the map to inspect samples.
6. Select a recipe step to edit it. Enable/disable, reorder, add, or remove steps, then generate again.
7. Use **Save displayed world as asset…** to preserve the result. Select that asset in **Saved World** and choose **Load saved world** to reopen it.
8. Use **Export displayed world…** for height data and inspection images.

The Unity Editor execution path is provided but has not yet been verified interactively. See validation below before treating saving or exports as production-proven.

## Resolution

| Preset | Samples | Height data alone |
| --- | ---: | ---: |
| 1024 × 512 | 524,288 | 2 MiB of float samples |
| 2048 × 1024 | 2,097,152 | 8 MiB of float samples |
| 3072 × 2048 | 6,291,456 | 24 MiB of float samples |

These are **heightmap sample dimensions**, not Unity world units, province counts, or physical distances. The large preset also changes aspect ratio from 2:1 to 3:2, so the same seed does not produce the same framing. Square pixels and normalized feature sizes relative to map height are used.

Generation uses additional coordinate and working arrays. Preview textures, managed colour buffers, saved assets, and the Unity Editor itself consume additional memory. Saving large float arrays as Unity text assets can produce substantially larger files than the raw sample buffer.

Rectangular heightmaps are not assigned directly to Unity Terrain: its square power-of-two-plus-one sampling requirements need a separate tiling/resampling decision. This increment deliberately preserves the requested raster dimensions.

## Old World approach

The authoring concept is inspired by [Azgaar’s heightmap template editor](https://github.com/Azgaar/Fantasy-Map-Generator/blob/master/docs/wiki/Heightmap-template-editor.md) and its [Old World recipe](https://github.com/Azgaar/Fantasy-Map-Generator/blob/master/src/data/heightmap-templates.ts).

This is an original C# raster implementation, not an Azgaar map importer or a port of its cell-based algorithms. The recipe is our own. Old World means a family of large landmasses, mountain belts, depressions, and channels; it does not reproduce Earth.

The default recipe lays out western and eastern continental uplifts, adds smaller features, lowers inland relief, introduces mountain belts and depressions, cuts a channel, and adds detail and slope relaxation. Seed and settings control the outcome. No particular coastline, navigable strait, or connected inland sea is guaranteed.

### Controls and operations

| Control / operation | Effect |
| --- | --- |
| Seed | Reproduces a recipe within this generator version |
| Sea level | Defines the normalized land/water threshold; affects generation as well as preview |
| Coast irregularity | Distorts feature coordinates at several scales |
| Ocean border | Lowers elevations at the bounded map edges |
| Hill | Raises a broad, irregular landform |
| Range | Adds an elongated, curved, textured mountain belt |
| Trough | Lowers an elongated region |
| Pit | Creates a local depression |
| Strait | Lowers terrain along a meandering horizontal or vertical channel |
| MultiplyLand | Scales elevations above sea level relative to that level |
| Detail | Adds multi-scale elevation variation |
| Smooth | Blends elevations with neighbouring samples |
| Erode | Transfers height from steeper samples to a lower neighbour; simple thermal-style relaxation |

For shaped features, count and strength are drawn within the selected ranges. For smoothing and erosion, count controls passes and minimum strength controls the effect. MultiplyLand and Detail apply once, using minimum strength. Radius and length are percentages of map height; placement limits are percentages of their respective map axes. Range orientation is seeded. A strait spans the map; its perpendicular placement limit sets its channel location.

Operations are ordered and can intentionally undo or soften earlier changes. Reordering changes the recipe. Disabling a step preserves the random stream assigned to later step indices; inserting or moving steps changes those indices.

The realism goal is more coherent relief, multi-scale coastlines, and less abrupt slopes. This is **not a physical tectonic, climate, or hydraulic simulation**. Deep depressions remain potential water basins awaiting hydrology. White high-elevation colours are an elevation palette, not a snow model; green colours are not biome assignments.

## Preservation and cancellation

- Generation snapshots the recipe before working and publishes only a completed result.
- Cancelling generation retains the previous completed world.
- Editing settings does not alter the displayed world. A warning indicates pending regeneration.
- Saving and exporting use the displayed result and its original recipe, even when current controls have changed.
- Saving a world creates a new asset; existing authored assets are not silently replaced.
- Recipe JSON stores settings, not elevation samples. Save the world asset to retain the exact terrain.
- The window's unsaved terrain preview is transient and can be lost on script/domain reload. Saved world assets can be reopened.
- Generator changes may change regenerated results. Preserve authored sample data rather than relying on a seed alone.

## Exports and coordinates

Each export creates a new uniquely named subfolder under the chosen parent. An incomplete export reports its location and is retained for inspection.

| File | Meaning |
| --- | --- |
| heightmap.raw | Headerless unsigned 16-bit little-endian elevation samples |
| heightmap-preview.png | 8-bit grayscale inspection image, not the precision source |
| relief.png | Coloured elevation inspection image |
| land-mask.png | White land, black samples below sea level |
| manifest.json | Recipe, dimensions, sea level, format version, encoding, and coordinate documentation |

Authoritative Unity asset samples are normalized floats from 0 to 1. They are not elevations in metres. RAW16 quantizes those floats to 0–65535. Values at or above sea level are land.

Data arrays and RAW16 are row-major: **X west to east, Y south to north, first row at the south edge**. PNGs show north at the top. Use the manifest when importing elsewhere to avoid vertical flips or incorrect byte order.

The generic land mask is **not yet an OpenGS land/lake mask**. Ocean connectivity and lakes are not classified. OpenGS's documented ocean/lake RGB encoding must be added with the hydrology/export integration rather than guessing that every low basin is ocean.

## Code ownership

- **HeightmapRecipe:** serializable settings, default recipe, and validation.
- **HeightmapGenerator:** Unity-independent seeded generation and completed results.
- **HeightmapEncoding:** RAW16 serialization and documented byte/row order.
- **WorldHeightmap:** Unity asset preservation and validation of saved samples.
- **Editor:** authoring window, elevation previews, export commands, and a batch smoke check.

Generation uses a private stable random implementation, not Unity's global random state. Work and dimensions are bounded before raster allocation. The generator currently uses four float arrays, plus small feature lists and the final result; consumers add their own buffers.

## Validation

### Standalone core checks

Install a compatible .NET 8 SDK, then run from **Tests/WorldGeneration**:

```powershell
dotnet run -c Release
dotnet run -c Release -- --benchmark
```

The harness has no external NuGet packages. It checks seeded reproduction, changed seeds, input snapshots, land/water coverage, bounded ocean edges, invalid inputs, late cancellation, progress monotonicity, disabled steps, and RAW16 byte order and orientation using asymmetric sentinel samples.

Benchmark output reports elapsed time, process CPU time, and cumulative process peak working set for first and repeat runs at all three preset sizes. These are standalone core measurements, not end-to-end Unity Editor costs. First runs follow smaller checks and therefore are not fully cold starts. There is no before-change implementation baseline. Agent-token use, process-family peak memory, Unity import, serialization, rendering, and interactive costs are unmeasured.

Measured on the development Windows machine with .NET 8, Release, seed 271828 (108 assertions passed):

| Resolution | First elapsed / CPU | Repeat elapsed / CPU | Cumulative process peak after repeat |
| --- | --- | --- | --- |
| 1024 × 512 | 0.422s / 0.422s | 0.391s / 0.406s | 43.5 MiB |
| 2048 × 1024 | 1.551s / 1.562s | 1.516s / 1.531s | 100.0 MiB |
| 3072 × 2048 | 4.603s / 4.625s | 4.534s / 4.531s | 268.1 MiB |

These peaks are cumulative within a single harness process and include retained allocations and runtime overhead; they are not isolated per-run allocation measurements. The large preset has a different aspect ratio, so it is a capacity check rather than an equivalent-view quality comparison.

### Unity smoke check

```powershell
& 'PATH_TO_UNITY_EDITOR\Unity.exe' -batchmode -nographics -quit `
  -projectPath 'PATH_TO_PROJECT' `
  -executeMethod GrandStrategy.WorldGeneration.Editor.WorldGenerationSmokeChecks.Run `
  -logFile 'PATH_TO_LOG\worldgen-smoke.log'
```

The entry point verifies recipe JSON regeneration, an actual world-asset disk round-trip, PNG generation for all preview modes, and window creation. It creates a uniquely named temporary asset and removes it afterward. Successful execution logs **WORLDGEN_SMOKE_PASS**.

In the development session, the full source compiled against the installed Unity **6000.6.8f1** reference assemblies. The Unity batch launch stopped before this smoke check because the Editor could not connect to its local Package Manager server. **No Unity runtime smoke-check pass, interactive UI verification, or actual Unity save/export round-trip is claimed.** A separate image reconstructed from the generated RAW16 was visually inspected to assess terrain shape; that does not validate Unity preview rendering.

### Manual checks still required

- Open the window, generate each preset, inspect all preview modes, and check hover coordinates.
- Change settings and confirm the displayed result remains unchanged until regeneration.
- Cancel generation and confirm the previous completed result remains available.
- Save an asset, reload scripts, and reopen that asset.
- Save/load a recipe and confirm regenerated terrain matches.
- Export and independently read RAW16 plus its manifest; verify the PNG orientation against it.
- Inspect multiple seeds for excessive fragmentation, clipped peaks, or implausible features.

## Next increments

1. Verify the authoring workflow in the Editor and refine landforms from visual feedback.
2. Add drainage, ocean connectivity, lake surfaces, and river routing.
3. Add climate, biomes, and permanent snow as separate data layers.
4. Add deliberate terrain rendering and shared map/world coordinates.
5. Add validated OpenGS inputs and province-map import.

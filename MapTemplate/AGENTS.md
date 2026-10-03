# Realm Custom Map Agents Guide
Realm is an RTS Game Engine with a custom map editor for UGC arcade maps.

## Skills
Separate skill files are located in the `.agent/skills` folder.

## Map Scripting
- Implements `IMapScript`.
- `Initialize(IGameAPI api)` is called when the map starts.
- `Update(IGameAPI api, float delta)` is called every simulation tick (30Hz).
- Full `IGameAPI` reference: `lib/Realm.MapAPI.xml`

## JSON Map Files

### metadata.json
The primary map configuration file. Schema at `.vscode/metadata.schema.json` 

### manifest.json
Tracks map asset registrations Schema at `.vscode/manifest.schema.json`.

### terrain.json
Defines the terrain layout: prop/unit/decal placements, map dimensions (`Width`, `Depth`), camera bounds, `Coordinates` (named map regions), and skybox path. Schema at `.vscode/terrain.schema.json`.

## Terrain Binary Files

### `terrain_heights.exr`
- **Format:** 32-bit single-channel (R32F) OpenEXR.
- **Size:** `(MapWidth + 1) × (MapDepth + 1)` pixels — one sample per terrain vertex.
- **Encoding:** The red channel float is the world-space Y height of that vertex. `0.0` = sea level. Origin (pixel 0,0) = world corner `(minX, minZ)`. Pixels scan left-to-right, top-to-bottom in terrain grid space.

### `terrain_water.exr`
- **Format:** 32-bit single-channel (R32F) OpenEXR.
- **Size:** Same as `terrain_heights.exr`.
- **Encoding:** Red channel = water surface height at that vertex. Values `≤ 0` indicate no water. The engine uses this to render the water plane and determine shallow/deep zone thresholds.

### `terrain_splat_indices.exr`
- **Format:** 4-channel (RGBA) 16-bit half-float OpenEXR.
- **Size:** `MapWidth × MapDepth` pixels — one pixel per terrain cell.
- **Encoding:** Each channel (R, G, B, A) holds the **integer index** (stored as a float) of one of the four ground texture layers blended at that cell. Index values refer to the ordered texture list in `metadata.json` → `textures`.

### `terrain_splat_weights.exr`
- **Format:** 4-channel (RGBA) 16-bit half-float OpenEXR.
- **Size:** Same as `terrain_splat_indices.exr`.
- **Encoding:** Each channel holds the blend weight `[0.0 – 1.0]` for the corresponding texture index in `terrain_splat_indices.exr`. Channel R weight = weight for the texture at `splat_indices.R`. Weights sum to ~1.0 per pixel.

### `terrain_cliff_splat_indices.exr`
- **Format / Size / Encoding:** Identical schema to `terrain_splat_indices.exr`, but applies to **cliff face geometry** rather than the flat terrain surface.

### `terrain_cliff_splat_weights.exr`
- **Format / Size / Encoding:** Identical schema to `terrain_splat_weights.exr`, applied to cliff face geometry.

### `terrain_pathing.png`
- **Format:** 8-bit single-channel (grayscale) PNG.
- **Size:** `MapWidth × MapDepth` pixels — one pixel per terrain cell.
- **Encoding:** Each pixel is a bitmask of allowed movement types:
  - `1` = Shallow water passable
  - `2` = Deep water passable
  - `4` = Flying units passable
  - `8` = Ground units passable
  - `32` = Buildable (structures can be placed)
  - `255` = All movement types allowed
  - `0` = Impassable
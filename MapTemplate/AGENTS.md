# Realm Custom Map Agents Guide
Realm is an RTS Game Engine with a custom map editor for UGC arcade maps.

## Skills
Separate skill files are located in the `.agents/skills` folder.
- `skill-assign-asset`: Automatically queries storage and assigns random matching assets to templates. **Always call `skill-assign-asset` (`AssignRandomAssetsToTemplates(false)` via IPC) after creating a new template in `metadata.json`** to populate missing asset references.
- `skill-translate-map`: Extracts, translates, verifies and prunes this map's own `locale/*.json` catalogs so template names and tooltips render in each player's language. Map text only — it never touches the Realm client's catalogs.

## Map Scripting & Editor API
- Implements `IMapScript`.
- `Initialize(IGameAPI api)` is called when the map starts.
- `Update(IGameAPI api, float delta)` is called every simulation tick (30Hz).
- Full `IGameAPI` reference: `lib/Realm.MapAPI.xml`
- Full `IEditorAPI` reference: `lib/Realm.EditorAPI.xml`

### Accessing IEditorAPI via HTTP IPC Bridge
VSCodium agent extensions can programmatically invoke editor operations by issuing HTTP `POST` JSON requests to `http://127.0.0.1:8092/api/` (or port `8093` if 8092 is occupied).

#### IPC JSON Request Schema:
```json
{
  "action": "invokeEditorApi",
  "method": "raiseTerrain",
  "requestId": "req-12345",
  "x": 0.0,
  "y": 0.0,
  "z": 0.0,
  "radius": 5.0,
  "strength": 1.0,
  "isSquare": false
}
```

#### IPC JSON Response Schema:
```json
{
  "action": "invokeEditorApiResult",
  "method": "raiseTerrain",
  "requestId": "req-12345",
  "success": true,
  "error": ""
}
```

## JSON Map Files

### metadata.json
The primary map configuration file. Schema at `.vscode/metadata.schema.json`

Open `metadata.json` and edit the JSON directly: the embedded editor validates it against `.vscode/metadata.schema.json` as you type, and every save is reformatted through the map's canonical JSON writer.

A unit under `Templates.Units` states `IncludeInDefaultHotkeySelection`: a unit whose template sets it is a unit the player's default hotkeys recall, so F1 gathers that player's idle tagged units and F2 gathers the tagged army, and a unit the map drives itself keeps the flag off so it never lands in a selection the player cannot command.

An item under `Templates.Items` states two asset references: `IconPath`, which is what the item looks like in an inventory slot, and `ModelPath`, which is the model a drop is rendered with when it lies on the ground. `ModelPath` takes the same forms any other model reference does - a `res://` path, a path relative to the map folder, or a bare name the engine looks up under `Assets/models/items` and the other model folders - so a dropped relic or resource piece reads as the object it is. A drop is also what a right-click walks a unit over to collect, and it is hit by that raycast as a small round body at the item's ground position, so a model that is tall or wide does not change how the item is picked. An item with no `ModelPath` is drawn as the drop's gem placeholder over its pedestal, so `ModelPath` is the only thing that decides whether a drop renders a model. Both are set in the editor, and only in the item template dialog: it owns the icon, the display name, the tooltip, the ground model and what the item grants. A placed drop selected on the terrain opens that same item dialog from the inspector's edit button, because the visual inspector edits the transform, rig and shaders a drop does not have. In a match every player can left-click a drop to read it before committing a unit to it: the portrait shows the item's icon and name, and the stat panel states the pieces lying there, the item's class, level and description, the modifiers and grants its template carries, how it is picked up and how long it has left before it goes. A player can put an item down again by right-clicking an inventory slot and then left-clicking the ground: that unit walks there and the pieces the slot held become one drop at its feet that the same player may walk a unit back over to collect. An item a map grants a unit with `IGameAPI.AddItemToUnit` is bound to that unit and cannot be put down, so a purchased upgrade stays where it was bought, and only items collected from the ground can be dropped.

### manifest.json
Tracks map asset registrations Schema at `.vscode/manifest.schema.json`.

### terrain.json
Defines the terrain layout: prop/unit/decal/item placements, map dimensions (`Width`, `Depth`), camera bounds, `Coordinates` (named map regions), `SpawnAnchors` (the coordinate each player slot starts inside), and skybox path. Schema at `.vscode/terrain.schema.json`.

`Items` places item templates on the ground as drops a unit can be sent to collect. An entry names the item template and the position it lies at; what the drop looks like comes from that template's `ModelPath`, and one pickup grants one of it. A placed item is not a prop: it is spawned as ground loot when the map loads, so it carries a pickup radius and is taken by the same right-click order a dropped item is, rather than blocking movement or being gathered as a resource node. Items are authored in the editor's object palette, where the first list picks the category (`Items` included) and the second picks the template inside it. A placed drop is selectable on the terrain like any other placed object, and the inspector's edit button opens its template, so a map author can choose the model a drop renders without leaving the terrain they just placed it on. A playtest leaves these placements alone: the editor's world is set aside before the run and put back afterwards, so `Items` survives being tested and is written to `terrain.json` unchanged. A drop the map script spawns with `IGameAPI.SpawnLootItem` is that run's content instead, so it is never saved and is gone when the editor returns.

`SpawnAnchors` is how the map states where its seats begin, so a ruleset that has to place a starting building asks the board with `IGameAPI.GetSpawnAnchor(slot)` instead of carrying coordinates of its own. An anchor names a coordinate the map already declares, and the position is drawn at random inside that coordinate each time the match starts, so no two matches open on the same spot. A slot with no anchor assigned can start anywhere inside the board's playable bounds. The editor authors both ends in the Spawn Anchors dialog, opened from the spawn anchors button in the Coordinates tool: one row per player slot, where the row's seat is fixed by its position and shown read-only, the coordinate to start inside, and a locate button that centres the camera on that coordinate. The dialog draws the anchor model at the centre of each assigned coordinate and nowhere when a slot has no coordinate.

## Localization

A map is read in the language each player picks in Settings → Game → Language. The map folder carries its own catalogs:

- **`locale/{code}.json`** — flat `{"English text": "translated text"}`, one file per supported language (`en`, `es`, `fr`, `de`, `pt`, `ru`, `zh`, `ja`, `ar`, `hi`). The client merges the map's catalog over the game's own when the map loads, so every player reads the same map in their own language and nothing has to run in WASM. A missing entry reads as the English text, and English itself needs no file.
- **Keys are the text `metadata.json` already holds**, so a translation needs no key naming scheme:
  - `Name` fields are keyed by the plain name, without its BBCode: `"[color=#FF0000]Seismic Rupture[/color]"` is translated by an entry keyed `"Seismic Rupture"`, and the colour markup stays as written.
  - `Description` fields and tooltips are keyed by the whole string, `{{...}}` tokens included. A translation may move the tokens and keep its own BBCode; the client fills the tokens in afterwards, so `{{Ability.Effect0.Amount}}` and `[color]`/`[b]` survive the lookup.
  - An ability tooltip that opens with a role tag, `[Damage] Deals ...`, can be translated as two entries (`"Damage"` and the sentence) instead of one.
- **Anything the script shows itself** — `SendMessage`, summary-table headers and rows, instructions pages — is translated on the authority with `api.Translate(key, playerIndex)`, keyed the same way, so one host serves players in different languages. `api.GetPlayerLanguage(playerIndex)` reports what each player asked for.

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
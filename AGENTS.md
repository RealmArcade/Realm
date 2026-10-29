# Project: Realm

## General Project Overview:
- RTS Game using Godot with C# and a custom Flat Bitmask Structure-of-Arrays (SoA) ECS shared directly with WASM linear memory.

## Overall Coding Style:

- Prefer functional programming paradigms where appropriate.
- All code, variable names, and comments must be written in English.
- Do not add comments anywhere unless explicitly requested.
	- Structs in the `Realm.Ecs` project must have a single XML-doc `<summary />` comment at the top describing their purpose.
	- Everything in `Realm.MapAPI` must have full, comprehensive XML-doc comments for public consumption.
- Do not use `#region` blocks.
- Use verbose, descriptive names that clearly identify their purpose (avoid cryptic abbreviations).
- Avoid the `sealed` keyword.
- Use [GeneratedRegex] syntax.
- Avoid keywords `internal` and `InternalsVisibleTo` use `public` or `private` instead.

## Core 3D rendering and Engine Tick Calculations:
- Minimize Garbage Collection (GC) pressure by using struct-based data where possible.
- Highly optimized godot simulation tick handlers: Do not allocate new objects, do not manipulate strings, do not use lambdas that capture variables. Prefer reusing collections, employing object pools.
- Utilize high-performance .NET structures like `Span<>`, `ReadOnlySpan<>`, and `StringBuffer` especially when transferring structural buffers between Godot and C#.
- Use `System.Numerics` to enable SIMD mathematical operations and vector arithmetic where possible.
- Prefer Enums over Strings

## Match Cadence, Snapshot Size and Compression (settled, do not re-open):
These three numbers were measured, argued and settled. Treat them as decisions rather than defaults to reconsider; a change is only justified by a rerun of `dotnet run --project Realm.Benchmarks -- --units 300 --players 24 --zstd-sweep` on the same 300 combat units and 24 players, quoting the new numbers against the ones below. The budget every figure is judged against is 50 percent of one core of an average gaming machine, so a spike or a future feature still has room.

- Simulation tick: **30 Hz** (`MatchTiming.SimulationTickRateHz`). At 300 combat units and 24 players the tick itself costs 0.63 ms, and describing that tick to every peer with the shroud on costs 3.3 ms across the three describing workers, so a 30 Hz match spends 3.9 ms per tick, 18 percent of one core. At 60 Hz the same work is 36 percent and the 24-player upstream moves from 6.9 to 13.8 Mbps, for remote units 1/16th of a second fresher — a difference a player does not reliably perceive. The headroom this leaves is spent on unit count, not on rate: 60 Hz would roughly halve the units a machine can hold for a smoothness nobody can see.
- Snapshot send rate: **30 Hz, equal to the tick rate** (`MatchTiming.SnapshotRateHz`). A snapshot describes exactly one tick, so describing more often sends the same world twice and describing less often forces the client to interpolate across ticks it was never told about. Both rates stay in `MatchTiming` and are derived from there by both processes; never state a cadence anywhere else. One peer's delta is 1193 bytes with the shroud on, which a full room of 24 sends as 6.9 Mbps. Do not raise the send rate independently of the tick rate: perceived lag over a 60 ms round trip is answered by the presentation buffer and by describing what changed sooner within a tick, not by a faster cadence.
- Compression: **Zstandard level 1 for both deltas and baselines** (`SnapshotWireFormat.DeltaCompressionLevel`). A delta of about 3.1 kB frames to 658 bytes at level 1 in 44 µs, and to 671 bytes at level 3 in 62 µs; levels above 3 buy about 3 percent of bytes for five to forty times the encoder's time, which is a bad trade on a stream sent thirty times a second. Compression is not removable: it takes a delta to about a fifth and a baseline to about a seventh of its serialized size, and the wire is quantized homogeneous columns, which is precisely what an encoder finds redundancy in. The level, not the presence, is the tunable part.

## Networked Movement Presentation and Latency Masking (settled, do not re-open):
The client renders the authority's own timeline. It does not simulate, predict or resimulate movement, and nothing on this side of the wire may become gameplay truth. **Latency masking** is the name for the whole of what this side of the wire does with that timeline, and the vocabulary beneath it is chosen deliberately because it decides what a future change thinks it is allowed to do: **snapshot interpolation** (a buffered sample at a cursor behind the newest description), **presentation delay** (how far behind, discovered at run time), **confirmed-order extrapolation** (at most one snapshot interval and 0.75 m past the newest description, along the direction the authority was already moving the unit, and only while the unit carries a replicated `MoveTo`), and **route preview** (a path drawn for an order the authority has not answered yet, which writes no position).
- **Movement prediction is not what this is.** No code, comment, test, report or document calls any part of it prediction: that word names client-simulated movement plus resimulation, which this architecture deliberately does not have. A screen's own units are drawn from the same descriptions as every other unit; the only thing it does locally for them is plan a route, which writes a path and no position.

- One writer. `SimulationService.PresentationQueryAction` is the only thing that writes `Position` for a replicated unit, once per client tick, from `SnapshotTimeline`. Nothing else may place one: not the snapshot apply path, not the movement service, not a UI script. The harness asserts it, and `ECS_client_presentation_gaps.txt` records any unit whose position is not the one that step wrote.
- One clock. `WorldSnapshot.Sequence` is the authority's tick index, so `sequence × MatchTiming.SnapshotIntervalSeconds` is an exact match time. That is what lets a peer buffer descriptions with no wall-clock synchronisation, and why the sequence must never be invented or offset on the client.
- The delay is measured, not calculated. `SnapshotTimeline` deepens it when the cursor reaches the end of what arrived, by the arrival gap that window actually showed rather than by one sequence, and shallows it when the buffer held twice what the link needed for several windows. Do not replace this with a formula over the round trip: a formula is only right on the link it was written for. The bounds (`MinimumDelaySequences`, `MaximumDelaySequences`) are the tunable part and both are stated once.
- Effects ride the same clock as the units they are about. A `PresentationMessage` carries the sequence of the tick the map asked for it on, and `GameHost` holds it until `SnapshotTimeline.CursorSequence` reaches that sequence; only a request marked `StateDescriptionSequence` (the cards, colours and labels a seated peer is given) is applied on arrival. An effect drawn the moment its frame arrived named a place the screen had not drawn yet.
- A replica lives exactly as long as the authority describes it. A delta carries `WorldSnapshot.RetiredEntityIds` - the units this peer was described with last tick and is not described with now - and the client retires those before it applies the snapshot's units, so a unit that leaves sight stops being selectable, counted and ordered on the tick it leaves rather than on the next baseline.
- A description lives as long as the unit it describes. `SnapshotTimeline.Reset` runs on spawn, on death, on retiring a replica (which now includes a unit the authority stopped describing) and on a discontinuity, and `Clear` runs when a match's first baseline arrives. Skipping any of those inherits a dead unit's history through the recycled identifier.
- Speed and facing are read where the unit is drawn, not where the authority has got to. `SnapshotTimeline.TrySample` resolves the velocity from the same pair of descriptions the sample came from, and the presentation step writes the sampled facing for a unit standing still, so a unit that starts or stops moving animates the step it is actually drawn taking rather than one presentation delay early.
- An order is answered by drawing, not by moving. A click writes `PreviewMoveTo { Target, CommandId, IssuedSequence }` and the plan-only pass turns it into a path with no position. A route that picked the wrong side of a building is replaced by the authority's, never walked. The authority's `MoveTo` supersedes the preview, a refusal takes it back by command id, and `PreviewLifetimeSequences` takes back one nothing answers.
- Rollback, client resimulation and deterministic navigation are not the plan. They were considered and rejected: the goal is what a player perceives, not exact agreement between two simulations, and every one of them would put a determinism requirement on map scripts and on navigation for a benefit the buffer already delivers.

## Godot-Specific Coding Rules:
- 2D button: always specify the `icon_max_width` property.
- Localization keys are generated by scanning for `TranslationServer.Translate("literal")`, and a key with no such call site is pruned from `locale/en.json` on the next build. A literal handed to a method that translates internally (such as `InGameHUD.ShowFeedbackText`) must still be wrapped in `TranslationServer.Translate(...)` at the call site, or its key will not survive.
- Godot nodes should only include UX logic, domain logic should be delegated to services
- For any text labels that appear on screen, ensure they are translated via `LocalizationManager.cs`.
- Labels in Containers: When setting autowrap_mode, always set custom_minimum_size.x (explicit width) and size_flags_horizontal = SIZE_EXPAND_FILL. Otherwise Godot calculates custom_minimum_size.y based on single-word wrapping, causing container to stretch excessively tall.
- Unicode Icons: Never hardcode `\uXXXX` escape sequences inside string literals in UI or HUD scripts. Always reference named glyph constants defined in `UnicodeIcons` (e.g. `${UnicodeIcons.UPLOAD}`).
- `[GlobalClass]` Registration: Every custom non-generic, top-level C# class that inherits from `GodotObject` (`Node`, `Node3D`, `Control`, `Resource`, etc.) must be marked with the `[GlobalClass]` attribute so it registers as a distinct type in Godot's ObjectDB and profiler rather than falling back to its native C++ base class.
- Explicit Node Naming: Whenever instantiating a generic Godot node (`new Node3D()`, `new PanelContainer()`, etc.) or scene prefab (`.Instantiate()`) in C#, immediately assign a descriptive `.Name` property identifying its purpose or source system to prevent anonymous `@Type@####` or `Type_####` entries in the SceneTree and ObjectDB profiler.
- Node Lifecycle & Cleanup: Never leave detached nodes in memory. When removing a node via `RemoveChild()`, either explicitly call `QueueFree()` on it or return it to a managed object pool that properly frees all pooled instances on cleanup.
- One-Way C# to Godot Transform Pipeline: Avoid unnecessary c# to godot c++ marshaling. Treat ECS as the source of truth. Never read fields such as `Position` from godot nodes when available in ECS. Batch update godot fields in a single `Transform` when possible and only update if field is dirty.
- Don't enable `_Process` overrides on node subclasses with many instances, instead code a centralized manager for active entities to handle per-frame updates in bulk.
- Menu scene 2D UX images should be downscaled to their maximum rendered size and encoded with cwebp 90 quality with this .import file value to avoid re-compression in PCK file: importer="keep"

### Realm.ECS Data Layer (Flat Bitmask SoA Shared Memory):
- Core simulation component buffers (`PlayerResource`, `Health`, etc.) are implemented as a custom Flat Bitmask Structure-of-Arrays (SoA) ECS where component arrays are stored in contiguous unmanaged memory mapped directly into WASM linear memory (`Wasmtime.Memory`).
- Only the authoritative process simulates. `GameHost.cs` renders the replicated world it receives: its physics tick applies snapshots, runs the client tick, and moves `Unit3D` / `Prop3D` nodes to the state the authority described. It never steps movement, combat, economy, production, shroud, or AI. Applying a snapshot stores what the authority said and places nothing; the client tick's presentation step is what turns the stored descriptions into a position, and it runs for every replicated unit exactly once.
- One world per process. The client owns exactly one client world and the authority owns exactly one authoritative world; there is no static accessor that can name the other. A menu screen owns no world at all, and a world is created when a match or the editor starts and torn down when it ends (the editor serialises its world before a playtest and restores it afterwards).
- Do not store Godot lifecycle elements, scene nodes, or UI references directly inside ECS components. Components must remain pure unmanaged, blittable data structures (`[StructLayout(LayoutKind.Sequential, Pack = 4)]`).
- Entity creation, lifecycle recycling, and queries use hardware bit manipulation (`BitOperations.TrailingZeroCount`) over entity bitmasks for zero-allocation performance.

### Logic Services:
- Classes inheriting from Godot should have minimal orchestration logic, deferring complex logic to domain-specific services.
- Decoupled Communication: Avoid using DTOs to communicate between orchestrators (`GameHost`) and services. Communication should be minimal and limited to simple ephemeral primitive parameters and return values. All persistent shared data must be stored directly in the ECS, allowing both services and `GameHost` to query and write to the ECS independently to coordinate.
- Services should never be instantiated directly, they should always be accessed via the global GameContext. `GameContext.Simulation` is this process's own world and services, so the same service type exists once per process and never spans the process boundary.

### Realm.MapAPI & WASM Zero-Allocation Shared Memory Architecture:
- Only expose safe APIs to map authors and external tools to prevent direct manipulation of raw Godot nodes or C# ECS structures.
- All map scripting operations proxy through `IGameAPI`, while map editing operations proxy through `IEditorAPI`.
- Editor operations belong on `IEditorAPI` (`Realm.EditorAPI`) and are implemented by `EditorService`, never grown on `GameHost`. The interface is the editor's remote-control surface: a tool, a DLL consumer or an MCP server reaches the editor only through it, by method name, so every operation must report an `EditorOperationResult` rather than relying on on-screen feedback.
- APIs must generate zero heap allocations and zero GC pressure.
- Do not use `yield return` keyword inside WASM layer because it compiles to a GC heap allocation.
- Use `ReadOnlySpan<T>` over facade types for enumeration rather than `T[]` or `List<T>`.
- Everything in `Realm.MapAPI` (including `IGameAPI` and `IEditorAPI`) must have full, comprehensive XML-doc comments for public consumption.
- Zero-Allocation Facade Pattern & 0ns Direct Reads:
	- `IGameAPI` should strictly require integer IDs as parameters, never full objects.
	- Facade handles exposed to map scripts (`PlayerId`, `UnitId`, `ResourceNodeId`, `LootItemId`) are plain index structs in `Realm.Ecs` defined as `readonly record struct PlayerId(int Value)` with implicit type-cast 'FROM int' but NEVER 'TO int'.
	- Primitive property getters (`get => EcsSharedMemory.PlayerResources[Value].Gold;`) perform **0ns direct memory reads** straight from the shared WASM linear memory ECS arrays without crossing the host boundary.
	- Property setters (`set => HandleCommands.Current?.SetPlayerGold(Value, value);`) and commands route through authoritative Host imports, executing full domain rules and side-effects in-place. A settable property stays an instance member on the handle rather than an extension member, because only an instance setter may be assigned through a `foreach` iteration variable or a `ReadOnlySpan` element.
	- Everything else a script calls on a handle — reads that need the host and every order, cast and effect — is a C# 14 extension member supplied by `Realm.MapAPI`.

### Map Workspaces & WASM Compilation (Portability):
- A map's WASM is produced by the editor's workspace publish and shipped inside the map folder next to its metadata. The authority executes exactly that artifact and nothing recompiles it at match time, so every host of the same map runs the same canonical bytes. The client never loads a map assembly.
- The map temp workspace is compiled to WASM via `dotnet publish`. Workspaces and saved map folders MUST be machine-portable: never write absolute paths into generated/saved `.csproj` files.
- `MapWorkspaceService.EnsureCsproj` always normalizes the `Realm.MapAPI` and `Realm.Ecs` references to the relative `<HintPath>lib/Realm.MapAPI.dll</HintPath>` and `<HintPath>lib/Realm.Ecs.dll</HintPath>`, and `EnsureApiLib` copies both DLLs (build output preferred, `MapTemplate/lib` as fallback) into the workspace so it is self-contained. A workspace also carries `<Using Include="Realm.Ecs" />` so a map script keeps writing `using Realm.MapAPI;` while still naming the shared simulation handles.
- `MapTemplate/lib/Realm.MapAPI.xml` and `MapTemplate/lib/Realm.Ecs.xml` are the committed API documentation beside those DLLs. When changing `Realm.MapAPI` or `Realm.Ecs` source, rebuild it (`dotnet build Realm.MapAPI`) so the `CopyToMapTemplate` target refreshes the whole of `MapTemplate/lib`, and commit the refreshed documentation together with the API changes.
- Simulation primitives below the script surface (`ComponentIds`, `ComponentMask`, `EcsSharedMemory`, `ContentIdStore`, `StringRegistry`, `DeterministicRandom`, `HandleCommands`, the map script handles) live in `Realm.Ecs`, and the script surface a handle offers on top of them is supplied from `Realm.MapAPI`. `Realm.Ecs` never references `Realm.MapAPI`.
- Shared map folders are portable when they contain a relative `.csproj` and the `lib/` folder. Saved/opened map folders are auto-repaired by `EnsureCsproj` on the next workspace setup.

### Map Script Debugging (Attach to the Authority):
- A map script runs inside the authority, so a debugger attaches to `Realm.ListenServerHost`, never to the Godot client. There is one target process per match and it is the one the script is executed in; no screen reports a PID to attach to.
- `--wasm-debug` starts an authority that keeps the script's DWARF and exports the Wasmtime JIT ELF, and `--workspace <folder>` names the map workspace it exports into as `<workspace>/MapScript.jit.elf`, which is the module the workspace `.vscode/launch.json` loads. The authority logs its PID, its workspace and every ELF it exported. Both flags are opt-in: a normal, dedicated or exported match runs its script compiled for speed and writes no debug artifact.
- The editor's `btnTestMap` requests both flags itself, because the playtest is played straight out of the workspace that was just compiled. A map folder opened on its own still works: the authority works the workspace out from where the script sits.
- `.vscode/launch.json` (`Attach to Realm Authority (Map Script WASM)`) and `realm_lldb.py` are copied from `MapTemplate` on every workspace setup, so an existing workspace is repaired on its next open. `MapTemplate/realm_lldb.py` is generated from `Realm.WasmBindgen/realm_lldb.template.py` and matches the handles by their `Realm.Ecs` names.

### Map File Format & Zero-Fallback Policy:
- Strict Canonical Schema: Runtime loaders, parsers, and editor services must maintain zero fallback paths and strictly expect canonical variable names and data structures. Never add backwards-compatibility fallbacks, casing aliases, or fallback-to-template shims in runtime loading logic.
- Map Migrations (`MapUpgradeService.cs`): Backwards compatibility for older maps is handled exclusively via sequential `IMapMigration` classes inside `MapUpgradeService`, modeled after Entity Framework Migrations. Each release build only transitions from the immediate prior version (`v0.0.0 -> v0.0.1 -> v0.0.2...`), allowing multi-version outdated maps to upgrade sequentially in sequence.
- `GameBuildNumber`: Every `metadata.json` must include a `GameBuildNumber` string matching `RealmVersion.GameBuildNumber`. This value is hardcoded and only changed manually when publishing and tagging an official release. Matches in the game lobby are locked to the map's `GameBuildNumber`.

## Client and Server Process Split:
- `Realm.Client` must never have a compile-time project reference to `Realm.ListenServer`, and no `using Realm.ListenServer` may appear in its source. `Realm.ListenServer` must never reference `Realm.Client` or Godot. All gameplay communication crosses the process boundary as serialized transport frames; there is no shared in-process world, service, or static state between them.
- The one assembly reference allowed is build ordering: `Realm.Client` carries a `ProjectReference` to `Realm.ListenServerHost` with `ReferenceOutputAssembly="false"` so a development build always produces a current authority beside the client. Nothing is linked against it, and it is not a way to call into the authority.
- Three projects, three responsibilities: `Realm.ListenServer` is the simulation and stays Godot-free (a `using Godot` there is a bug); `Realm.ListenServerHost` is a headless Godot project that exists only to give the authority ENet and RPC, so it may reference Godot for transport and process lifecycle but must hold no simulation, world or game rule logic; `Realm.Client` owns input, presentation and rendering.
- Both processes speak through one channel: the node autoloaded as `RealmNetwork` in each project, with the same path and the same `Frame(kind, payload)` RPC. A new gameplay or presentation message is a `NetworkFrameKind` plus a `Realm.Shared` payload, never a second protocol.
- `Realm.Shared` owns every serialized payload, protocol constant, transport contract and handshake type. A payload the authoritative process has to serialize can never live in `Realm.Client`.
- The client launches the authority with `--map --port --token --log`, reads its single `REALM_SERVER_READY` line, and joins it as an ordinary peer. A hosted lobby hands its whole roster over to the authority: the host tells every player the port, closes the lobby it was announced over (freeing that port), starts the authority on it, and joins as a peer like everyone else. The authority reports that port to the lobby registry, so a remote player connects to the authority rather than to the host client.
- The client never runs WASM and does not reference `Realm.MapAPI` or `Wasmtime`. Map scripts, `IGameAPI`, `EcsSharedMemory` and the `Wasmtime` host live only in the authority. The editor compiles a map to WASM and the authority executes that artifact; `IEditorAPI` (`Realm.EditorAPI`) remains the client's map-editing surface and is unrelated to `IGameAPI`.
- Authority-only components are never replicated, and the wire component ids are part of the handshake (`wireTableHash`). Adding a replicated component means adding it to the canonical table in `Realm.Ecs.Replication.ReplicatedComponents`.

## AI "Vibe" Coding & Maintenance Instructions:
- Avoid using proprietary or copyrighted terms from other games.
- For fast lookup during queries without breaking the PURE DATA PRINCIPLE, do not put Godot Nodes inside components. Instead, map the relationship using unique entity IDs, or look up corresponding visual nodes via a managed registry outside the ECS system arrays.
- Don't write unit or integration tests unless explicitly instructed to do so.
- Always compile code after making changes to ensure there are no errors.

## Test CLI example
dotnet test Realm.Client.Tests/Realm.Client.Tests.csproj -e GODOT_BIN="C:\Program Files\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64.exe" --filter "FullyQualifiedName~Realm.Client.Tests.MapEditorUxTests.TestCustomWeaponProjectileLayersAndVisualRendering"
- Don't run tests unless explicitly told to do so

## git commit rules
- NEVER add automated footer tags like "Generated with {{ToolName}}" or "Co-Authored-By: {{Agent}}" to Git commit messages. 

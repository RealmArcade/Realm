# Project: Realm

## General Project Overview:
- RTS Game using Godot with C# and the Arch ECS framework.

## Overall Coding Style:

- Prefer functional programming paradigms where appropriate.
- All code, variable names, and comments must be written in English.
- Do not add comments anywhere unless explicitly requested.
	- Structs in the `Realm.Ecs` project must have a single XML-doc `<summary />` comment at the top describing their purpose.
	- Everything in `Realm.MapAPI` must have full, comprehensive XML-doc comments for public consumption.
- Do not use `#region` blocks.
- Use verbose, descriptive names that clearly identify their purpose (avoid cryptic abbreviations).
- Avoid the `sealed` keyword.
- Use [GeneratedRegex] syntax

## Core 3D rendering and Engine Tick Calculations:
- Minimize Garbage Collection (GC) pressure by using struct-based data where possible.
- The game simulation tick runs at 30Hz. Absolute zero-allocation constraint inside the tick: Do not allocate objects, do not use lambdas that capture variables, and do not call `new` inside query loops. Prefer reusing collections, employing object pools, and using `struct` components.
- Utilize high-performance .NET structures like `Span<>`, `ReadOnlySpan<>`, and `StringBuffer` especially when transferring structural buffers between Godot and C#.
- Use `System.Numerics` to enable SIMD mathematical operations and vector arithmetic where possible.
- Use `yield return IEnumerable` to allow lazy evaluation where possible.

## Godot-Specific Coding Rules:
- 2D button: always specify the `icon_max_width` property.
- For any text labels that appear on screen, ensure they are translated via `LocalizationManager.cs`.

### Realm.ECS Data Layer:
- The core ECS classes and data must be kept internal.
- Keep system logic separated from presentation. Physics process query loops in `GameHost.cs` should inspect and manipulate ECS data via components, updating `Unit3D` / `Prop3D` nodes accordingly.
- Do not store Godot lifecycle elements, scene nodes, or UI references directly inside ECS components. Components must remain pure unmanaged data.
- All QueryDescription instances should be created 1x as public readonly fields in `QueryCache.cs` and re-used across the application

### Logic Services:
- Classes inheriting from Godot should have minimal orchestration logic, deferring complex logic to domain-specific services.
- Services should have the `WorldAccessor` dependency injected via their constructor and cached for their lifetime.
- Decoupled Communication: Avoid using DTOs to communicate between orchestrators (`GameHost`) and services. Communication should be minimal and limited to simple ephemeral primitive parameters and return values. All persistent shared data must be stored directly in the ECS, allowing both services and `GameHost` to query and write to the ECS independently to coordinate.
- Services should never be instantiated directly, they should always be retrieved via the global ServiceLocator during godot scene _Ready() and stored in private readonly fields.

### Realm.MapAPI:
- Only expose safe APIs to map authors to prevent the direct manipulation of Godot nodes or internal C# ECS structures.
- All map scripting operations should strictly proxy through interfaces (like `IGameAPI` and `IUnit`). Implementations (e.g. `UnitWrapper`) must hide the underlying `Arch.Core.Entity` and raw Godot `Node` references.

### Map Workspaces & WASM Compilation (Portability):
- The map temp workspace is compiled to WASM via `dotnet publish`. Workspaces and saved map folders MUST be machine-portable: never write absolute paths into generated/saved `.csproj` files.
- `MapWorkspaceService.EnsureCsproj` always normalizes the `Realm.MapAPI` reference to the relative `<HintPath>lib/Realm.MapAPI.dll</HintPath>` and `EnsureApiLib` copies the DLL (build output preferred, `MapTemplate/lib` as fallback) into the workspace so it is self-contained.
- `MapTemplate/lib/Realm.MapAPI.{dll,pdb,xml}` is the canonical committed API binary. When changing `Realm.MapAPI` source, rebuild it (`dotnet build Realm.MapAPI`) so the `CopyToMapTemplate` target refreshes `MapTemplate/lib` and commit the updated binaries together with the API changes.
- Shared map folders are portable when they contain a relative `.csproj` and the `lib/` folder. Saved/opened map folders are auto-repaired by `EnsureCsproj` on the next workspace setup.

### Map File Format & Zero-Fallback Policy:
- Strict Canonical Schema: Runtime loaders, parsers, and editor services must maintain zero fallback paths and strictly expect canonical variable names and data structures. Never add backwards-compatibility fallbacks, casing aliases, or fallback-to-template shims in runtime loading logic.
- Map Migrations (`MapUpgradeService.cs`): Backwards compatibility for older maps is handled exclusively via sequential `IMapMigration` classes inside `MapUpgradeService`, modeled after Entity Framework Migrations. Each release build only transitions from the immediate prior version (`v0.0.0 -> v0.0.1 -> v0.0.2...`), allowing multi-version outdated maps to upgrade sequentially in sequence.
- `GameBuildNumber`: Every `metadata.json` must include a `GameBuildNumber` string matching `RealmVersion.GameBuildNumber`. This value is hardcoded and only changed manually when publishing and tagging an official release. Matches in the game lobby are locked to the map's `GameBuildNumber`.

## AI "Vibe" Coding & Maintenance Instructions:
- Avoid using proprietary or copyrighted terms from other games
- For fast lookup during queries without breaking the PURE DATA PRINCIPLE, do not put Godot Nodes inside components. Instead, map the relationship using unique entity IDs, or look up corresponding visual nodes via a managed registry outside the ECS system arrays.
- Don't write unit or integration tests unless explicitly instructed to do so.
- Always compile code after making changes to ensure there are no errors.

## Test CLI example
dotnet test Realm.Godot/Realm.Godot.csproj -e GODOT_BIN="C:\Program Files\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64.exe" --filter "FullyQualifiedName~Realm.Godot.Tests.MapEditorUxTests.TestCustomWeaponProjectileLayersAndVisualRendering"
- Don't run tests unless explicitly told to do so
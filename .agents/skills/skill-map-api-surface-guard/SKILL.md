---
name: skill-map-api-surface-guard
description: Checks whether a change to Realm.MapAPI removed or retyped a WASM host import that a shipped map still links against, and whether the same change bumped EngineGeneration to carry the break.
metadata:
  opencode/slash: "true"
---

# Skill: Map API Surface Guard (skill-map-api-surface-guard)

## Purpose
A compiled map is a `.wasm` artifact that links against the host imports this build exposes. Adding an import is normal growth; removing one, or changing its signature, cannot be repaired at runtime, because only a recompile fixes the map and only its author can recompile it. This skill produces the evidence a reviewer needs to decide whether a pull request breaks maps that already shipped: the surface this build generates, the surface that shipped, every difference between them, and whether `EngineGeneration` was bumped in the same change.

The build already enforces the rule. `Realm.WasmBindgen/MapApiSurfaceGuard.cs` compares the generated surface with the committed baseline `MapApiSurface.baseline.json` and fails the build with `REALMAPI001` when an import disappeared or was retyped while the baseline still records the current engine generation. This skill is how that failure is read, reproduced and resolved.

## Workflow

### 1. Regenerate the surface with the one documented command
The surface is the generated WIT document `Realm.MapAPI/wit/game.g.wit`. The generator writes it during every build of `Realm.MapAPI`, and the same build is what the guard runs in. Never hand-edit that file or the baseline.

```pwsh
pwsh scripts/generate-map-api-surface.ps1
```

The script builds `Realm.MapAPI` with `-p:RealmMapApiSurfaceBaselineOut=<path>`, which asks the generator to record the surface it just produced instead of verifying one, and writes `MapApiSurface.baseline.json` at the repository root. Running it is an author's deliberate acceptance of the surface, never a step a build takes on its own.

### 2. Diff the regenerated surface against the committed baseline
```pwsh
git diff -- MapApiSurface.baseline.json
```

Read the diff per member rather than as a wall of text. Every changed line is one import, and each is classified as:

- **added** - a new name, or a name whose signature gained parameters. This is the normal direction of growth and needs nothing else.
- **removed** - a name the baseline had and the build no longer produces. Maps calling it stop linking.
- **retyped** - a name that survives but whose signature changed. Maps calling it with the old arguments stop linking even though the name still exists.

### 3. Report each removal and retype with the maps it strands
For every removed or retyped member, name the map-script members that would stop linking. Search the map API surface, not the whole engine:

```pwsh
rg -n "<import-name>" Realm.MapAPI MapTemplate/lib/Realm.MapAPI.xml
```

State the consequence in one line per member: which handle or property a map author calls, and that the map now needs a rebuild. Do not soften a removal into "deprecated": a map that shipped is already compiled and cannot be recompiled by the player.

### 4. Check that `EngineGeneration` moved when the diff is breaking
A breaking diff is only allowed to pass when the generation gate moved with it:

```pwsh
rg -n "EngineGeneration" Realm.Shared/RealmVersion.cs
git diff -- Realm.Shared/RealmVersion.cs
```

If `EngineGeneration` is unchanged while a removal or retype exists, the analysis fails: the pull request would break every map in the Workshop and tell nobody. If it moved, say so explicitly, and name the generation the baseline recorded before the change and the one it records after.

Adding members never requires a bump. Growth is the normal path, and a generation bump for an addition would strand maps for no reason.

### 5. Print the regeneration command rather than editing the baseline
The tool never edits `MapApiSurface.baseline.json` by itself. Report the command and the exact file it writes:

```
pwsh scripts/generate-map-api-surface.ps1
```

Accepting a surface change stays an author's act. A reviewer can then see the surface diff and the version bump in the same pull request.

### 6. Build the solution
Per the repository rule that code is compiled after it is changed:

```pwsh
dotnet build Realm.slnx
```

The build is the check. `REALMAPI001` is an error, `REALMAPI002` is the informational note that an addition or an accepted break should be recorded in the baseline.

## Reporting
Answer with:

- the change in the generated surface, per member, classified as added, removed or retyped;
- each removal or retype with the map-script members it strands;
- whether `EngineGeneration` moved in the same change, and the pass or fail that follows from it;
- the regeneration command and the file it writes;
- the result of `dotnet build Realm.slnx`.

## Notes
- The baseline is compared with the running table and with the committed file, never with a git tag: the release workflow checks out at depth 1 and this repository's tags are not a comparable release version, so a tag comparison passes vacuously.
- Only removal and retyping are failures. A guard that forbade adding would make every API improvement a generation bump.
- Component ids have the same rule and their own guard in `ReplicationContractTests.ReplicatedComponentTableIsAppendOnly`, against `ReplicationComponents.baseline.json`. A new replicated component is appended; an id is never renumbered or reused.

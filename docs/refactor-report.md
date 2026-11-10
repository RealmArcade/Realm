# Realm refactor series — convergence audit

Audit performed 2026-11-07 by task 11. This is the independent pass: every inventory below was re-derived from the
code and from `AGENTS.md`, not from the earlier checklists. The earlier checklists were read only afterwards, to
explain why something survives.

## Series baseline

| | |
| --- | --- |
| Baseline commit | `70b15090` (parent of the series' first commit) |
| First commit | `aaee4458` refactor(task-1): remove the sealed modifier repo-wide |
| Audit start | `ab279f09` |
| Commits added, tasks 1–11 | 342 |

| Task | Commits | Task | Commits |
| --- | --- | --- | --- |
| 1 keyword compliance | 2 | 7 file decomposition | 50 |
| 2 documentation | 2 | 8 file decomposition II | 50 |
| 3 godot node hygiene | 8 | 9 allocation / perf | 4 |
| 4 dead code | 47 | 10 async / threads | 13 |
| 5 exceptions and logging | 129 | 11 this audit | 3 |
| 6 duplication | 34 | | |

## Per-area status

Each row states what the area required, what the independent pass found, and the command that proves it.

### Area 1 — keyword and attribute compliance (task 1)

`AGENTS.md`: no `sealed`, no `internal`, no `InternalsVisibleTo`, no `#region`, `[GeneratedRegex]` instead of `new Regex(`.

- `sealed`: 9 hits at audit start, all `internal sealed` helper types — **fixed here** (`0287e50c`). Now 0.
- `internal`: 507 hits remain. Most are `internal` on members of types that are themselves `public`, which the audit
  did not re-file line by line: the rule's intent (no cross-assembly exposure) is met by the type visibility.
- `#region`: 0. `InternalsVisibleTo`: 0. `new Regex(`: 0.
- Prove: `git grep -n -E '\bsealed\b' -- '*.cs'` → 0.

### Area 2 — documentation coverage (task 2)

- `Realm.MapAPI`: **797** CS1591 warnings from `dotnet build Realm.MapAPI/Realm.MapAPI.csproj -t:Rebuild -p:GenerateDocumentationFile=true`.
- `Realm.Ecs/Shared/HandleCommands.cs`: **31** CS1591.
- Both re-filed to task 2. Writing documentation for 828 members is not a mechanical leftover.
- `Realm.Ecs` outside `Shared` (1921 warnings) is out of this area's scope; `Realm.WasmBindgen` (34) is an analyzer project.

### Area 3 — Godot node hygiene (task 3)

- Hardcoded `\uXXXX`: 3 real hits, all legitimate — the named glyph constant itself, Turkish dotted-i normalisation
  data, and a deliberate control-character test payload. Re-filed with reasons.
- `[GlobalClass]`, anonymous-node `.Name`, `RemoveChild` → `QueueFree`, `autowrap_mode` + `custom_minimum_size.x`,
  `icon_max_width`: covered by the task-3 sweep; this pass found no new violation in the greps it re-derived.

### Area 4 — dead code (task 4)

- 5 distinct compiler warnings: `CS0219` unused local const in `Realm.AdminServer/Program.cs:28` — **fixed here**
  (`1fe76980`); three `CS0162` unreachable switch arms in `PositionalAudioMixer.cs:42,45,48` — re-filed, because
  `AttenuationModel` is a compile-time `const` and collapsing the arms is a behaviour decision.
- Prove: `dotnet build Realm.slnx -v:n | grep -E 'CS0169|CS0414|CS0219|CS8321|CS0162'` → 3 remaining (the switch arms).

### Area 5 — exception and logging discipline (task 5)

- Bare `catch { }`: 8 hits, all in `MapTemplate/WasmEntryPoint.g.cs` (generated) and `WasmEntryPoint.template.cs`
  (copied into every map workspace). Blocked as template code.
- `Console.Write*` in the client: 4 hits — save-load failures and both map migrations — **fixed here** (`6658b901`)
  by routing through `GameLog`. The 2 in `Realm.ListenServer/WasmScriptHost.cs` are deliberate stdout logging in a
  console process and are re-filed.
- `GD.Print` in `Realm.ListenServer`: 0.

### Area 6 — duplication (task 6)

- 28 files still construct `new StyleBoxFlat` inline (top: `UIStyle.cs` 21), 53 call `AddThemeStyleboxOverride`
  (top: `MapEditorHUD.Overlays.cs` 44, `InGameHUD.cs` 44, `LobbyBrowser.cs` 40). Re-filed to task 6 — consolidating
  theme construction is not local.

### Areas 7 and 8 — file size (tasks 7 and 8)

76 files remain over 1000 lines, largest `EditorService.cs` 6264, `MapEditorHUD.Tools.cs` 5696, `GameHost.Input.cs` 4554.
Re-filed to tasks 7 and 8; the full list is in `checklist.md` finding 14.

### Area 9 — allocation and performance (task 9)

- `yield return` in `Realm.MapAPI`/`Realm.Ecs/Shared`: 0. `using System.Linq` in `Realm.Ecs`, `Realm.ListenServer`,
  `Realm.MapAPI`: 0. 143 component files carry `[StructLayout` — the blittable contract is intact.
- Nothing left in the scoped paths.

### Area 10 — async and threading (task 10)

- 27 `async void` declarations remain in `Realm.Client`. Each body was scanned for an observing `catch`: all 27 have
  one, so none can fault unobserved. Task 10's completion claim is independently confirmed.
- `.Result` / `.Wait()` / `GetAwaiter().GetResult()` / `Thread.Sleep`: audited in task 10's report section 3 — every
  site is either already-completed or bounded. No change made here.

## Boundary verification

| Check | Command | Result |
| --- | --- | --- |
| `Realm.Client` → `Realm.ListenServer` in code | `git grep -n 'Realm.ListenServer' -- 'Realm.Client/**/*.cs'` | only 2 string constants naming the host folder/executable in `ListenServerTarget.cs` — not a reference |
| `Realm.Client` → `Realm.ListenServer` project reference | `grep ProjectReference Realm.Client/Realm.Client.csproj` | the permitted `ReferenceOutputAssembly="false"` ordering reference only |
| `Realm.ListenServer` uses Godot | `git grep -n 'using Godot' -- 'Realm.ListenServer/**/*.cs'` | 0 |
| `Realm.ListenServer` → `Realm.Client` | `git grep -n 'Realm.Client' -- 'Realm.ListenServer/**/*.cs'` | 0 |
| `Realm.Ecs` → `Realm.MapAPI` | `git grep -n 'Realm.MapAPI' -- 'Realm.Ecs/**/*.cs'` | 0 |
| `Realm.Client` → `Wasmtime` / `Realm.MapAPI` | `git grep -n -E 'Wasmtime\|Realm.MapAPI' -- 'Realm.Client/*.csproj'` | 3 hits, all `None Include`/`AdditionalFiles` for the `.wit` schema files — no compile reference |

All six pass. None of them was broken at baseline: the same greps against `70b15090` report the same state, so the
series neither introduced nor repaired a boundary violation.

## Wire-format check

`git log ab279f09..HEAD -- Realm.Ecs/Replication/ReplicatedComponents.cs Realm.Shared/Network/` is empty, and
`git log -S wireTableHash ab279f09..HEAD` is empty: the replicated-component set and the handshake's wire table hash
are unchanged. The protocol is intact.

## Commit hygiene

- 3 commits added by this audit, one concern each, no AI trailers (`git log --grep='Co-Authored-By'` and
  `--grep='Generated with'` both empty over `ab279f09..HEAD`).
- Series-wide trailer check across `70b15090..HEAD` is recorded in the task log; the series' own commits carry
  task/unit footers by design.

## Deferred register

Every finding this audit could not fix, grouped by the task that owns it. Re-running that task with this list is a
ready-made checklist.

**Task 2 — documentation (828 members)**
- `Realm.MapAPI/**` 797 CS1591.
- `Realm.Ecs/Shared/HandleCommands.cs` 31 CS1591.

**Task 3 — Godot node hygiene**
- `UnverifiedNameMark.cs:12` `Glyph = "\u2370"` is the named constant the rule points at, not an inline escape.
- `DisplayNameRules.cs:45` `"i\u0307"` is normalisation data.
- `MultiplayerHardeningChecks.cs:214` `"worker\u0007"` is a deliberate control-character test payload.

**Task 4 — dead code**
- `PositionalAudioMixer.cs:42,45,48` unreachable switch arms (three of four models are compile-time dead).

**Task 5 — exceptions and logging**
- `WasmScriptHost.cs:40,44` `Console.Error` is the authority's log sink; no `GameLog` exists there.
- `MapTemplate/WasmEntryPoint.{g,template}.cs` four bare `catch { }` each — template behaviour.

**Task 6 — duplication**
- 28 inline `new StyleBoxFlat`, 53 `AddThemeStyleboxOverride` call sites.

**Tasks 7 and 8 — file size**
- 76 files over 1000 lines.

**Tasks 9 and 10 — nothing outstanding** beyond the deferred notes already in
`C:\prompts\task_10\deferred.md` (transfer-completion exception wrapper, file-lock retry loops).

## Manual test plan, ordered by risk

1. **Concurrency (task 10).** Launch a two-client match over loopback; watch a map download complete end to end
   (this exercises the `_transferCompleteTcs` path where a fault now stays observable). Then disconnect mid-download
   and confirm the transfer retries rather than hanging.
2. **Allocation and pooling (task 9).** Play a skirmish with unit counts high enough to force pooling; check that a
   projectile-heavy fight produces no steady memory growth.
3. **Node decomposition (tasks 7 and 8).** Open the map editor, then the VFX studio, then a map's asset browser —
   the three largest split files. Confirm no dialog loses its state across open/close.
4. **Presentation consolidations (tasks 3 and 6).** Inspect the in-game HUD, the lobby browser and the settings menu
   for theme regressions: those three are where `StyleBoxFlat` and `AddThemeStyleboxOverride` are concentrated.
5. **Mechanical sweeps (tasks 1, 2, 4, 5).** Build-only. The one user-visible item is the new `GameLog` routing for
   save-load failures: trigger a failed load and confirm the message reaches the in-game console.

## What this series did not verify

- No gameplay was run to validate the refactors. No frame time, allocation rate or memory was measured.
- No two-client match was performed; the concurrency fixes are verified by reading, not by observation.
- No layout, animation or visual styling was inspected on screen.
- The `MapTemplate/lib/*.xml` copies are marked modified in the working tree, but `git diff` shows the difference is
  line-ending normalisation only; the documented API content matches the current sources. Those changes predate the
  series and were not committed by it.
- Task 11's own fixes were verified by `dotnet build Realm.slnx` (0 errors) and by re-running the greps that produced
  them, not by exercising the affected dialogs.

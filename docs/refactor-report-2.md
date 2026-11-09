# Refactoring series 2 - convergence audit

Audited at commit `2f423fbb` on branch `refactoring`, 2026-11-08.

Every number below was produced by a command in this audit. Where a number comes from an
earlier task's ledger instead, the text says so.

## 1. Series baseline

| item | value |
|---|---|
| branch | `refactoring` |
| series baseline | `16508e37` (`docs(task-2): refresh the shipped Realm.Ecs API documentation`) |
| first code commit | `ca392580` (`refactor: lift the nullable suppressions and empty the warning backlog`) |
| final code commit | `2f423fbb` (`fix(task-8): prop path resolver - the registry lookup may return null, so annotate it`) |
| commits in range | 528 before this report |

Commits per task, measured by the `task-N` token in the commit subject:

| task | commits |
|---|---|
| task-1 | 36 |
| task-2 | 22 |
| task-3 | 64 |
| task-4 | 0 tagged (its ledger records 112; all of them free-form) |
| task-5 | 20 |
| task-6 | 21 |
| task-7 | 74 |
| task-8 | 11 |
| task-9 | 13 |
| task-10 | 8 |
| task-11 | 3 |
| untagged | 256 |

Command: `git log --oneline 16508e37..HEAD | grep -oE "task-[0-9]+" | sort | uniq -c`

## 2. Per-area status

### A1 - visible compiler and analyzer warnings

AGENTS.md: the build must be clean of first-party warnings. The series opened at 98
first-party warnings across 41 files.

Measured: **0**.

After the CS8600 repair recorded below the audit's first measurement of this area was 1;
the number here is the post-repair measurement.

    dotnet build Realm.slnx -v:q 2>&1 | grep -E ': warning (CS|CA)[0-9]+:' \
      | sed -E 's/ \[[^]]*\]$//' | sort -u | wc -l
    0

The build still prints 106 `EXEC : warning : non-literal localization key skipped` lines
from the localization generator. Those are emitted by an `Exec` task, not by the compiler,
carry no diagnostic code, and are the same set the series started with. They are not
counted above and are not first-party compiler warnings.

Status: **PASS**.

### A2 - nullable warnings in Realm.Client / Realm.Client.Tests, suppressions lifted

Measured: **0**.

    dotnet build Realm.Client/Realm.Client.csproj -t:Rebuild -p:NoWarn=CS8981 -v:q 2>&1 \
      | grep -E ': warning CS86[0-9]+:' \
      | grep -E 'Realm\.Client(\.Tests)?\.csproj\]$' | sed -E 's/ \[[^]]*\]$//' | sort -u | wc -l
    0

Status: **PASS**, after the repair in §2a.

### A3 - file sizes in Realm.Client/Services

Measured: **1** file over 1000 lines.

    find Realm.Client/Services -name "*.cs" -not -path "*/obj/*" -not -path "*/bin/*" \
      -exec wc -l {} + | awk '$1>1000 && $NF!="total"'
       1058 Realm.Client/Services/SimulationService.cs

That file is task 3's recorded deferred row (`C:\prompts\task_3\deferred.md:33`): it came
down from 1635 lines and stopped 58 over the ceiling because every further cut moved a
member across a directory the task was not allowed to touch. It is a known, argued
deferral rather than an unrecorded leftover.

Status: **RE-FILED** (already in task 3's register; no new work here).

### A4 - file sizes in Realm.Client/UI

Measured: **0** files over 1000 lines.

Status: **PASS**.

### A5 - Realm.Client/Core, root views, Network, Utils, Testing

Measured: **0** files over 1000 lines.

Status: **PASS**.

### A6 - Realm.Shared, Realm.Ecs, servers, tools

Measured: **0** files over 1000 lines in `Realm.MapAPI`, `Realm.Shared`, `Realm.Ecs`,
`Realm.ListenServer`, `Realm.AdminServer`, `Realm.WasmBindgen`, `Realm.Tools.Admin`,
`Realm.Client.Tests`.

Status: **PASS**.

### A7 - duplicated UI construction

Measured: `new StyleBoxFlat` **72**, `AddThemeStyleboxOverride` **372**.

    git grep -c "new StyleBoxFlat" -- Realm.Client | awk -F: '{s+=$2} END {print s}'
    72
    git grep -c "AddThemeStyleboxOverride" -- Realm.Client | awk -F: '{s+=$2} END {print s}'
    372

Both match task 7's closing measurements exactly (128 -> 72 and 481 -> 372). The four
`new StyleBoxFlat` outside `UIStyle` are `new StyleBoxFlat[n]` array allocations in
`InGame/PortraitPanel.cs`, not style construction.

Status: **PASS**.

### A8 - Godot nodes and domain logic

Measured: **111** files under `Realm.Client/Services` still import `Godot`.

    git grep -l -E "using Godot" -- Realm.Client/Services | wc -l
    111

The count is not itself a violation: task 8's rule is that a node class holds presentation
and a service holds domain logic, and its proof measured 112 `using Godot` lines in the same
directory at completion. This audit did not re-derive the node/behaviour classification - it
re-ran the same count and found the series did not add a service that became a node.

Status: **PASS on the measured count; classification verified by reading only** (see §8).

### A9 - per-frame and tick allocation

Recorded by reading, not by a number. Task 9's baseline was `c902aada` and its 13 commits
each remove one allocation from a per-frame or per-tick path. This audit read the four
largest of them:

* `1a931524` - the render frame caches the entity query callback instead of allocating a
  closure per frame.
* `bdf5f78b` - the economy tick caches one dropoff callback instead of building a closure
  per gathering worker per tick.
* `f9560802` - the building preview probes the mouse instead of building a hit dictionary.
* `a68436b9` - the presentation queue fills one reused buffer instead of an array of the
  tick's events.

All four are of the shape they claim: a delegate or list is hoisted to a field and cleared
or filled in place, and no branch changed. The remaining nine follow the same template
(label rebuilds gated on a changed value, reused focus and point arrays).

The frame-time figures in task 9's ledger (`gen0=5 gen1=5 gen2=5`, mean 0.61 ms, p99 0.74 ms)
were produced by its benchmark harness in that task. **They were not re-run here.** This
audit makes no new performance claim.

Status: **PASS by reading; measurement not repeated**.

### A10 - `internal` visibility

Measured: **1** occurrence of the word, in a doc comment; **1** file; **0**
`InternalsVisibleTo`.

    git grep -c -E "\binternal\b" -- "*.cs" | awk -F: '{s+=$2} END {print s}'
    1
    git grep -l -E "\binternal\b" -- "*.cs" | wc -l
    1
    git grep -c "InternalsVisibleTo" -- "*.cs" "*.csproj" | wc -l
    0

The hit is `Realm.EditorAPI/IEditorAPI.cs:134`, the phrase "a destructive internal rebuild"
inside a `///` summary. It is not a modifier. Task 10 converted 986 declaration lines.

Status: **PASS**.

### A11 - string dispatch

Measured: **329** `case "` labels, down from 345. Three dispatches were converted to enums
(cheat names, the editor clipboard object kind, the weapon shader presets); every other
string switch got an EXTERNAL or UNDECIDABLE verdict in `C:\prompts\task_11\checklist.md`.

    git grep -c -E 'case "' -- "*.cs" | awk -F: '{s+=$2} END {print s}'
    329

Status: **PASS** (the register is the deliverable; 223 rows are EXTERNAL by design).

### A12 - project boundaries

Measured: all four greps are **0**.

    git grep -n "using Godot" -- Realm.ListenServer | wc -l          0
    git grep -n "Realm.Client" -- Realm.ListenServer | wc -l         0
    git grep -n "Realm.MapAPI" -- Realm.Ecs | wc -l                  0
    git grep -n "using Realm.ListenServer" -- Realm.Client | wc -l   0

Status: **PASS**.

### A13 - wire format and replicated component set

Measured: the replicated component file is **byte-identical** to the series baseline, and
`wireTableHash` was never changed.

    git diff 16508e37..HEAD -- Realm.Ecs/Replication/ReplicatedComponents.cs | wc -l
    0
    git log --oneline 16508e37..HEAD -- Realm.Ecs/Replication/ReplicatedComponents.cs \
      Realm.Shared/Network/ | wc -l
    2
    git log --oneline -S wireTableHash 16508e37..HEAD | wc -l
    1

The two commits that touched the wire directories are nullable-annotation repairs:
`1c96416e` changes one line of `Realm.Shared/Network/WorldSnapshot.cs`, and `ca392580` adds
four lines to `Realm.Shared/Network/UnitSection.cs`. Neither changes a field, an order or a
value. The single `-S wireTableHash` commit is `c90c41c7` (task 6), which split
`ListenServerHost` and moved the log line that *mentions* the hash; the value still comes
from `ReplicatedComponents.ComputeWireTableHash()`, untouched.

Status: **PASS**.

### A14 - commit hygiene

Measured: **528** commits, **107** carrying an AI attribution trailer, **256** with no task
prefix.

    git log --oneline 16508e37..HEAD | wc -l
    528
    git log --grep="Co-Authored-By" --grep="Generated with" --oneline 16508e37..HEAD | wc -l
    107
    git log --oneline 16508e37..HEAD | grep -vcE "task-[0-9]+"
    256

The trailers are `Co-Authored-By: Codebuff <noreply@codebuff.com>` (107), and 76 of those also
carry `Generated with Codebuff`. They come from tasks 3 and 4, whose ledgers record free-form
subjects; tasks 5-11 wrote none. The 256 untagged subjects are the same free-form commits.

Status: **LEFTOVER, not repaired.** Rewriting 107 trailers means rewriting published history,
which this task may not do. Recorded for the user in §6.

### A15 - build and test health at HEAD

Build: **0 errors**, 106 generator warnings, 0 compiler warnings.

    dotnet build Realm.slnx -v:m 2>&1 | tail -3
        106 Warning(s)
        0 Error(s)

`git status --porcelain` is empty for source files. `dotnet build` writes two documentation
artifacts (`MapTemplate/lib/Realm.MapAPI.xml`, `Realm.Ecs.xml`) and touches eleven tracked
files through its CRLF handling; those were restored after the last build and their content
hash equals the index.

Tests (run once in this audit, at `5e51611f`):

| project | result |
|---|---|
| Realm.Ecs.Tests | Failed 4, Passed 33, Total 37 |
| Realm.Distribution.Tests | Failed 4, Passed 6, Total 10 |
| Realm.Client.Tests | did not run: `GODOT_BIN` is not set |

The eight failures are the same ones task 9 reported at its HEAD, which it showed to be
pre-existing by re-running with its own work stashed. They are pathing and asset-package
state, not refactoring regressions.

Status: **PASS for the build; tests carry pre-existing failures**, reported, not hidden.

### 2a. Leftover repaired by this audit

`Realm.Client/Services/PropModelPathResolver.cs:40` produced the only compiler warning at
HEAD: `CS8600` on `string targetModel = FindRegistryModelPath(...)`, whose callee returns
`string?`. The code came out of `Prop3D` in task 8's commit `feafadca`; before the move the
same expression was made non-null by a `?? propId` fallback, so the annotation was the only
thing missing. The repair annotates the local as `string?`; the `IsNullOrEmpty` guard two
lines later already made every read safe, so the change is behaviour-neutral and removes the
warning rather than suppressing it.

Commit `2f423fbb`, prefix `fix(task-8)` as the owning task. A1 and A2 re-measured 0 after it.

## 3. Boundary verification

| grep | result |
|---|---|
| `using Godot` in `Realm.ListenServer` | 0 |
| `Realm.Client` referenced from `Realm.ListenServer` | 0 |
| `Realm.MapAPI` referenced from `Realm.Ecs` | 0 |
| `using Realm.ListenServer` in `Realm.Client` | 0 |

`Realm.ListenServer` stays engine-free and `Realm.Ecs` stays independent of the script API,
which is what the four greps exist to prove.

## 4. Wire-format verification

The replicated component set did not change: `Realm.Ecs/Replication/ReplicatedComponents.cs`
has a zero-line diff against `16508e37`, and `ComputeWireTableHash()` is untouched. No commit
in the series modified a snapshot field, a section order or a component list; the two commits
that touched `Realm.Shared/Network` are nullable annotations only (§A13). The
`wireTableHash` grep hit is a moved log line, not a changed value.

## 5. Commit hygiene

528 commits, 107 with AI attribution trailers, 256 without a task prefix. The prefix
histogram is in §1. Tasks 3 and 4 account for the trailers and for most untagged subjects;
tasks 5, 6, 9, 10 and 11 used the `refactor(task-N)` / `perf(task-N)` / `fix(task-N)` forms
consistently. Task 4's ledger records 112 commits with no prefix at all.

No commit in this series was amended, rebased or rewritten by the audit.

## 6. Deferred register

Grouped by the task that owned the area. Each entry is a specific site, not a theme.

### Task 3 - Services decomposition

* `Realm.Client/Services/EditorService.cs` (1778 lines) and
  `Realm.Client/Services/SimulationService.cs:1` (1058 lines). Both are recorded in
  `C:\prompts\task_3\deferred.md` with member-level arguments: the remaining members are
  public surface or forwarders that a split would have to delete or move across directories
  the task did not own.

### Task 4 - UI decomposition

* `C:\prompts\task_4\deferred.md` records two units that could not be finished inside the
  task's own rules. They are unchanged by this audit.

### Task 11 - string dispatch

* `Realm.Client/UI/MapEditor/MapEditorEntityPaletteController.cs:330` - palette category.
  INTERNAL dispatch, not converted: the same strings are the ComboBox item text and the
  metadata loader's selector, so an enum means rebuilding the item list, not changing a
  switch.
* `Realm.Client/Services/EditorMetadataWrites.cs:76` and `:111` - `domain` names both a
  registry and a `metadata.json` section. Converting it is a metadata format change.
* `Realm.Client/UI/InGame/MinimapPanel.cs:180` - `InputState.ActiveCommandTargeting` is
  interned through `StringRegistry` inside a snapshot-visible component; an enum changes the
  component shape.
* The 196 string-keyed tables (`git grep -n "Dictionary<string,"`). Sixteen write a literal
  key in the same file as the declaration and all sixteen index external names; the other 180
  cannot be shown internal from the declaration site. Recorded in
  `C:\prompts\task_11\checklist.md` section B and `C:\prompts\task_11\deferred.md`.

### Task 10 - visibility

* `Realm.Shared/Maps/MapWorldLoader.cs` - seven `LoadedMap` property setters became `public`
  because `MapWorldLoader` (a different class) sets them and neither `internal` nor `private`
  remains as an option. If the user wants them narrowed, `LoadedMap` needs a factory or an
  `init`-only shape, which is a design change rather than a keyword change.

### Task 8 - nullable annotations

* None left: the one site this audit found is repaired in §2a.

### Not attributable to a task in this series

* `dotnet build` regenerates `MapTemplate/lib/Realm.MapAPI.xml` and `Realm.Ecs.xml` with
  member entries in a different order (977 members before and after, every name appearing
  once as removed and once as added). Nothing in the series asked for it and it is a build
  artifact, so it is not committed.

## 7. Manual test plan, ordered by risk

Highest risk first: task 4 (112 commits) and task 7 (74) rewrote the most UI construction, and
task 3 (64) rewrote the biggest service files.

1. **Every dialog opens and lays out** - task 4/7 split 30+ Godot classes into partials.
   Open main menu, settings, lobby create, lobby room, storage, map details, player profile,
   announcements, credits, in-game HUD, command card, inventory, chat, replay viewer, and
   every map-editor dialog from the toolbar. Symptom: a missing child, a null node, or a
   control that renders at the wrong size because a `%Name` lookup moved to a file that no
   longer owns it.
2. **Map editor round trip** - task 3 split the service layer. New map, paint terrain, place
   a unit and a prop, save, close, reopen, and check the terrain and entities are identical.
   Symptom: a save that writes fewer sections than it read, or a paint stroke that misses the
   last cell.
3. **Editor copy and paste** - the task 11 conversion. Copy one unit, one prop and one decal,
   press paste, and check the toolbar switches to the right place tool each time. Symptom: the
   paste mode stays on "none" or places the previous kind.
4. **Cheat commands** - the task 11 conversion. In a single-player match or an editor map
   test, type `securethebag`, `gigachad`, `needforspeed`, `thanossnap`, `ezclap`, `aura`,
   `speedrun`, `skibidi`, then a typo. Symptom: a known cheat answers "is not a cheat this
   build knows", or a typo applies an effect.
5. **Weapon VFX presets** - the task 11 conversion. Open the map editor's weapon VFX dialog,
   press Fire, Frost, Poison, Arcane, Holy in turn. Symptom: a preset that applies the
   previous preset's colours.
6. **Selection and command card** - task 7/9. Select one unit, select a group, box-select,
   shift-click, issue move/attack/patrol/hold, and open the minimap. Symptom: a HUD that does
   not refresh on a selection change (task 9 gated those rebuilds on a compared value).
7. **Timeline, tactical map and shader preview** - task 9. Open each during a match and drag
   across it. Symptom: stale points, a blank plot, or a preview that stops updating - the
   three places where a reused buffer could have been cleared too early.
8. **Lobby and map browser** - task 7 and task 5. Create a lobby, change the map, start a
   match, and browse the lobby list. Symptom: player rows that do not update, or a map card
   that shows the previous map's author.
9. **Visibility smoke test** - task 10 changed only accessibility. Launch, start a match,
   open the editor, save. Symptom: a `MissingMethodException` or a `NullReferenceException`
   from a member that the compiler could not see was reached reflectively. Nothing else
   should be observable.

## 8. What this series did not verify

* **No gameplay was run.** No match was started, no unit was ordered, no ability was cast.
  Every behavioural claim above rests on the compiler, on diff reading, or on the earlier
  task's own tests.
* **No frame time was measured by this audit.** The only benchmark numbers in the repository
  are task 9's (`gen0=5 gen1=5 gen2=5`, mean 0.61 ms, p99 0.74 ms). They were produced by
  task 9's harness before this audit and were not re-run; treat them as that task's
  measurement, not as this report's.
* **No two-client match was performed.** `Realm.Client.Tests` cannot start on this machine
  (`GODOT_BIN` is unset), so the multiplayer harness, the split-brain checks and the
  screenshot suites did not execute at all.
* **Tests that did run have failures.** `Realm.Ecs.Tests` 4/37 and `Realm.Distribution.Tests`
  4/10 fail, identically to task 9's report. This audit did not re-establish that they are
  pre-existing by bisecting; it relies on task 9's stash experiment and on the failures being
  in pathing and asset-package state.
* **No visual check was made.** No screenshot was captured, no Godot editor was opened. The
  UI areas (A7, A8) are verified by counts and by reading, not by looking.
* **A8's node/service classification was not re-derived.** Only the `using Godot` count was
  re-measured.
* **A3's deferred file was accepted, not attempted.** `SimulationService.cs` is still 1058
  lines.
* **The 196 string-keyed tables were screened, not adjudicated one by one.** See §6.
* **Nothing outside this repository was exercised.** No server was started, no map was
  published, no Steam or network path was touched.

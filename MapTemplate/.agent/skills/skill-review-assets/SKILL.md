---
name: skill-review-assets
description: Chooses the best-looking icon or model for this map's templates by rendering every candidate with Realm.Tools.Cli, looking at the review sheets, and writing the chosen files into the map. Use when the question is which asset looks right, not merely filling an empty field.
---

# Skill: Review Assets (skill-review-assets)

## What this skill is for
Picking the best of several candidate assets for a template by **looking at them**. The candidate may be another export of the same asset, an alternative generation, or a file from a cloud provider; nothing here assumes where it came from.

- Use `skill-assign-asset` to fill empty fields at random so a new template loads and plays.
- Use this skill when the question is "which of these looks like Shadow Weaver", "is this icon legible at 64 px", or "does this model match the map's art".
- A filename is a hint, never a decision. If you could not see the image, say so instead of guessing — a wrong guess costs a playtest and a review.

## Requirements
- **The Realm CLI.** `REALM_CLI` names it if it is set. Otherwise it is `Realm.Tools.Cli/bin/Release/net10.0/win-x64/Realm.Tools.Cli.exe` (or `Debug` instead of `Release`) in the engine checkout that this map folder belongs to. If it is missing, build it with `dotnet build Realm.Tools.Cli/Realm.Tools.Cli.csproj`; if that is not possible, stop and report the path you expected rather than working blind.
- **`--eula-accept`** on any command that reads a `.rmesh` or `.rtex`, or the command stops to ask for a prompt you cannot answer.
- **bpy** for `mesh_render` only: the first run creates its Python environment under `%APPDATA%\Realm.Tools.Cli\BlenderEnv` and can take minutes. Icons need no bpy.
- A **review root outside the map folder**, for example `%TEMP%\realm-review\<map-name>`. Never write renders or sheets inside the map: they would show up in `git status` and in the packaged map.

## Where assets live
A template field holds a file name (usually with its extension) that resolves inside a fixed folder:

| Template group | Model field | Folder | `asset_type` to pass |
| --- | --- | --- | --- |
| `Units` | `ModelPath` | `Assets/models/units` | `Character` |
| `Buildings` | `ModelPath` | `Assets/models/buildings` | `Building` |
| `Props` | `ModelPath` | `Assets/models/props` | `Prop` |
| `Resources` | `ModelPath` | `Assets/models/resources` | `Prop` |
| `Items` | `ModelPath` | `Assets/models/items` | `Item` |
| `Projectiles` | `ModelPath` | `Assets/models/projectiles` | `Prop` |

Icon fields (`IconPath` on abilities, items, upgrades, buildings and units) resolve in `Assets/icons` and are `.rtex`, `asset_type` `Icon`. When a folder's existing files disagree with this table, mirror the existing files: `metadata read` on one of them states the `asset_type` the map already uses.

**Files are shared.** In Chaos Arena 27 of 116 icon references are used by more than one template, so overwriting `heavy_knight.rtex` silently changes every ability using it. Before replacing a file, search `metadata.json` for its name and count the templates that name it.

## Workflow

Set the two paths once, from the map folder, and use them for every command below:

```bash
CLI="<path to Realm.Tools.Cli.exe, or the value of REALM_CLI>"
REVIEW="<a review root outside the map, for example %TEMP%/realm-review/<map-name>>"
```

### 1. Take inventory
List what the templates you were asked about currently point at:

```bash
python - <<'EOF'
import json
d = json.loads(open("metadata.json", encoding="utf-8-sig").read())
for group, items in d.get("Templates", {}).items():
    for template in items:
        icon, model = template.get("IconPath"), template.get("ModelPath")
        if icon or model:
            print(group, template.get("TemplateID"), "|", template.get("Name"), "| icon:", icon, "| model:", model)
EOF
```

### 2. Gather the candidates
Candidate roots are wherever the alternatives live; group them by asset, matching the file stem to the template slug (`unit/shadow_weaver` → `shadow_weaver.*`). One root is one candidate set — usually one generation.

One asset can have several files inside a single root, because a generator may file the same model under more than one category folder. Collect the exact files first, then render each one, rather than pointing `mesh_render` at a folder and rendering everything in it:

```bash
find "<candidate root>" -name "shadow_weaver.rmesh"
```

If a candidate carries embedded metadata, its `tags` are worth one read (`metadata read -i <file>`) as a pre-filter. They are not a tie-breaker between two candidates of the same name.

### 3. Render the models
```bash
"$CLI" mesh_render --eula-accept -i "<one candidate .rmesh or .glb>" -o "$REVIEW/models/<slug>/<root-name>.png" --size 512 --despill
```

`--despill` removes the baked player colour so the render shows the neutral model the game draws. The strip holds front, three-quarter, side and back at one fixed framing, so candidates are directly comparable. Renders are cached by content and settings: asking for the same render again is a file copy, `--no-cache` forces a fresh one.

Icons need no render: a `.png` or `.webp` candidate is already an image. A `.rtex` candidate is extracted once with `texture_convert --eula-accept -i <file> -o "$REVIEW/icons/<name>.webp"`.

### 4. Compose one sheet per template
Render the asset the template uses today as a row of its own, so "keep" is a real option. `mesh_render` reads a map asset directly, so the baseline costs one command:

```bash
"$CLI" mesh_render --eula-accept -i "Assets/models/units/<current model>" -o "$REVIEW/models/shadow_weaver/current.png" --size 512 --despill

"$CLI" image_sheet -i "$REVIEW/models/shadow_weaver/gen1.png;$REVIEW/models/shadow_weaver/gen2.png;$REVIEW/models/shadow_weaver/current.png" \
  -o "$REVIEW/sheets/shadow_weaver.png" --columns 1 --cell 512 --label name \
  --title "unit/shadow_weaver — Shadow Weaver"
```

Several paths are one `;` separated value; the option cannot be repeated. Rows are candidates, columns are views, and every cell is labelled with the file it came from, so nothing has to be remembered between the render and the decision.

Label with `name`, not `path`: a path long enough to be identical after truncation tells you nothing about which row you are looking at. That is why each candidate is rendered to a file named after its root (`gen1.png`, `gen2.png`, `current.png`) rather than after the asset.

### 5. Judge
Open the sheet and read it like a creative director:

- **Purpose**: does it read as what the template's `Name` and `Description` say it is, at a glance, with no caption?
- **Silhouette**: distinguishable from the map's other templates of the same kind.
- **In-game size**: an icon is read at roughly 64 px in the command card. Detail that vanishes at that size is not detail.
- **Craft defects**: extra or fused limbs, melted geometry, inverted normals, smeared or missing textures, cropped silhouette, halo or leftover chroma, an A-pose that hides the face, a camera-facing flat card treated as a model.
- **Consistency**: palette, proportions and level of realism against the assets this map already uses.
- **Improvement**: replace only when the candidate is clearly better than what is there. A tie keeps the current asset.

Answer per template with the row you chose and one sentence of reason. If a sheet is genuinely ambiguous, render that one candidate larger (`--size 768 --mode views`) and look again rather than picking on a hunch.

### 6. Apply
Write the chosen file over the file the template already names. Keeping the name is what preserves `Scale`, `YOffset`, `PathingType`, shaders and procedural animation, and it keeps the change to one file:

```bash
# icon candidate is png or webp
"$CLI" texture_convert --eula-accept -i "<candidate>" -o "Assets/icons/<existing name>.rtex" -t Icon

# icon candidate is already rtex — copy it, never convert rtex to rtex
cp "<candidate>" "Assets/icons/<existing name>.rtex"

# model candidate is glb
"$CLI" mesh_convert --eula-accept -i "<candidate>" -o "Assets/models/units/<existing name>.rmesh" -t Character

# model candidate is already rmesh
cp "<candidate>" "Assets/models/units/<existing name>.rmesh"
```

When the name is shared with other templates and only this one should change, choose a new name (`<slug>_v2.rtex`), write the file under it, and repoint **only** that template's field in `metadata.json`.

### 7. Verify
- `git status --short` shows only the asset files you wrote, plus `metadata.json` if you repointed anything.
- `git diff --stat metadata.json` is a few lines: same key order, same indentation, same line endings. If the diff is the whole file, you reformatted it — revert and edit again.
- The map's own test and publish validation reports references that no longer resolve, which is the check that matters after a swap.

## Invariants
1. **Judge from the picture.** Never assign an asset you have not seen rendered or opened.
2. **Edit `metadata.json` surgically.** Two-space indent, CRLF line endings, one field per change, keys in their existing order. Never re-serialise the document.
3. **Never leave a dangling reference.** Renaming or deleting an asset means repointing every template that names it in the same change.
4. **Do not create keys the schema does not declare**, and leave `author_key` and `Signature` alone; publishing re-signs the map.
5. **Keep out of the map folder** with review artifacts, and never touch `locale/`.
6. **Match the folder's `asset_type`** so the asset browser and the editor keep filtering it correctly.
7. **Report what you did per template**: chosen file, reason, and whether the name was shared.

## Out of scope
Audio (`.raud`), animations (`.ranim`), terrain textures, decals, skyboxes and VFX spritesheets. This skill never calls the editor IPC, so it works with the game closed.

## When something is missing
- **No candidates** for a template: report it and leave the current asset; do not invent a file name.
- **Render fails**: report the CLI's stderr. `--engine workbench` is the fallback for a machine where EEVEE cannot render.
- **A candidate will not decode**: say which one and judge from the rest; drop it from the sheet rather than shipping an empty cell.
- **The CLI is missing**: stop with the path you looked for. A silent no-op reads exactly like "nothing needed changing".

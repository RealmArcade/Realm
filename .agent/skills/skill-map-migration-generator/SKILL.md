---
name: skill-map-migration-generator
description: Compares map file schemas, metadata structures, and MapUpgradeService migrations between two GitHub tags to generate sequential IMapMigration classes for upgrading maps to new game builds.
---

# Skill: Map Migration Generator (skill-map-migration-generator)

## Purpose
Generates sequential `IMapMigration` classes within `MapUpgradeService.cs` by analyzing schema and file format diffs between two git release tags (or between a release tag and `HEAD`).

## Workflow

### 1. Tag Diff Inspection
Run git diff commands to inspect changes across map file formats, templates, and schemas between `<prior_tag>` and `<target_tag>`:

```bash
git diff <prior_tag>..<target_tag> -- \
    Realm.Client/Services/MetadataService.cs \
    Realm.Client/Services/SaveLoadService.cs \
    Realm.Client/Services/MapWorkspaceService.cs \
    Realm.Client/Utils/MapAssetHelper.cs \
    MapTemplate/ \
    Realm.MapEditorExtension/metadata.schema.json
```

### 2. Schema and Property Change Analysis
Identify any structural changes between the versions:
- Renamed or moved properties in `MapMetadata`, `MapInfoMetadata`, or template entity arrays under `Templates` (`Units`, `Buildings`, `Resources`, `Props`, `Abilities`, `Weapons`, `Upgrades`, `Items`, `Attachments`, `Vfx`).
- Changed asset category keys or texture metadata properties in `manifest.json` / `metadata.json`.
- Changes to terrain data formats, navmesh definitions, or map script bindings.

### 3. Generate `IMapMigration` Class
Add a new migration class inside `Realm.Client/Services/MapUpgradeService.cs` following the standard naming convention `Migration_<Major>_<Minor>_<Patch>_<Summary>`:

```csharp
public class Migration_0_0_2_SampleChange : IMapMigration
{
    public string FromVersion => "<prior_tag>";
    public string ToVersion => "<target_tag>";
    public string Description => "Description of migrations applied in this version";

    public MigrationResult Up(string mapDirectory, IProgress<MigrationProgressUpdate>? progress = null)
    {
        try
        {
            string metadataPath = Path.Combine(mapDirectory, "metadata.json");
            // 1. Read existing metadata.json
            // 2. Perform sequential transformations to canonical schema
            // 3. Set metadataRoot["GameBuildNumber"] = ToVersion
            // 4. Save formatted JSON
            return new MigrationResult
            {
                Success = true,
                FromVersion = FromVersion,
                ToVersion = ToVersion
            };
        }
        catch (Exception ex)
        {
            return new MigrationResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                FromVersion = FromVersion,
                ToVersion = ToVersion
            };
        }
    }

    public Task<MigrationResult> UpAsync(string mapDirectory, IProgress<MigrationProgressUpdate>? progress = null)
    {
        return Task.Run(() => Up(mapDirectory, progress));
    }
}
```

### 4. Register Migration and Update Build Number
1. Register the new migration in `MapUpgradeService.RegisterMigrations()`:
   ```csharp
   _migrations.Add(new Migration_0_0_2_SampleChange());
   ```
2. Update `RealmVersion.GameBuildNumber` in `Realm.Shared/RealmVersion.cs`:
   ```csharp
   public const string GameBuildNumber = "<target_tag>";
   ```
3. Update `MapTemplate/metadata.json`:
   ```json
   "GameBuildNumber": "<target_tag>"
   ```

### 5. Verification
Verify that the codebase compiles cleanly:
```bash
dotnet build Realm.slnx
```
*(Do not run tests unless explicitly instructed)*

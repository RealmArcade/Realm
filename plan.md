1. **Refactor Duplicate Metadata Parsing Code (`Realm.AdminServer/Program.cs` ~Line 2478)**
   Replace the inline metadata parsing in `Program.cs` around line 2478 with a call to `MapMetadataHelper.ParseMapMetadataDetails`. The helper already exists and handles the exact logic.

2. **Refactor Duplicate Map Awards/Stats Code (`Realm.AdminServer/Program.cs` ~Lines 2369 & 2588)**
   Create a helper method `GetMapDiscoveryStatsAndAwards` or similar in `MapStatsHelper` (or `MapMetadataHelper`/new helper) to encapsulate the logic:
   ```csharp
   float ratingStars = stats != null && stats.AverageRating > 0 ? (float)stats.AverageRating : 4.5f;
   ...
   if (awards.Count == 0) awards.Add("res://Assets/UI/gold_coin.png");
   ```
   Replace the duplication in `Program.cs` at both locations.

3. **Refactor Duplicate Manifest Extraciton / Hashing Logic (`Realm.AdminServer/Program.cs` ~Lines 2333 & 2441)**
   Extract the loop that checks `metadata.json`, extracts `thumbnailHash`, `screenshotHashes` and `totalBytes` into a helper method.

4. **Refactor Duplicate Admin Remove Manifest Logic (`Realm.AdminServer/Program.cs` ~Lines 1726 & 1802)**
   The issue notes duplications with `AdminEndpointsHelper.HandleRemoveManifest`. I will call `AdminEndpointsHelper.HandleRemoveManifest` in the duplicated endpoints inside `Program.cs` instead of manually verifying authorization and broadcasting the events.
   (e.g., around lines 1726, 1802, and replace `/api/admin/remove_manifest` duplication)

5. **Refactor Duplicate Map Maintainer Logic (`Realm.AdminServer/Program.cs` ~Lines 2810 & 2907)**
   The issue notes a duplication with `MapMaintainerEndpointsHelper.HandleAddMapMaintainer` and similar for remove. `Realm.AdminServer/Program.cs` has duplicate logic around line 2810 and 2907. There is an `app.MapPost("/api/maps/maintainers/remove", ...)` and another `app.MapPost("/api/maps/{mapTitle}/maintainers/remove", ...)` which duplicate the removal logic. I will extract `MapMaintainerEndpointsHelper.HandleRemoveMapMaintainer` and use it.

6. **Refactor Duplicate Sync Creators / Sync Published Maps Logic (`Realm.AdminServer/Program.cs` ~Lines 1873 & 1938)**
   `AdminEndpointsHelper.VerifyClusterSyncRequest` exists. I will use it for `sync_published_maps` and `sync_creators` where there is a duplication in verifying signatures.

7. **Refactor Duplicate `map_stats` Upsert Logic (`Realm.AdminServer/Program.cs` ~Line 3274 & `Realm.AdminServer/Services/ClusterEventService.cs` ~Line 1948)**
   Consolidate the multi-key `Upsert` calls for map stats into a method in `MapStatsHelper`.

8. **Refactor Duplicate `GetMaintainers` call (`Realm.AdminServer/Services/MapMaintainerHelper.cs` ~Lines 198 & 234)**
   Remove repetitive string allocations/normalization across `AddMaintainer` and `RemoveMaintainer`.

9. **Ensure tests pass.** Run `dotnet build` and `dotnet test` appropriately.

using Realm.Shared;
using System.Text.Json;

namespace Realm.AdminServer.Services;

public static class MapMaintainerHelper
{
    public static List<string> GetMaintainers(DataStoreService db, string mapTitle)
    {
        if (string.IsNullOrWhiteSpace(mapTitle))
            return new List<string>();

        string trimmed = mapTitle.Trim();
        string slug = trimmed.ToLowerInvariant().Replace(" ", "-");
        string lower = trimmed.ToLowerInvariant();
        string norm = NameNormalizationHelper.NormalizeMapName(trimmed);

        var maintainerSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddMaintainersFromList(db, "map_maintainers", trimmed, maintainerSet);
        AddMaintainersFromList(db, "map_maintainers", slug, maintainerSet);
        AddMaintainersFromList(db, "map_maintainers", lower, maintainerSet);

        if (!string.IsNullOrEmpty(norm) && norm != lower && norm != slug)
            AddMaintainersFromList(db, "map_maintainers", norm, maintainerSet);

        AddMaintainerFromOwner(db, trimmed, maintainerSet);
        AddMaintainerFromOwner(db, slug, maintainerSet);
        AddMaintainerFromOwner(db, lower, maintainerSet);

        if (!string.IsNullOrEmpty(norm) && norm != lower && norm != slug)
            AddMaintainerFromOwner(db, norm, maintainerSet);

        ExtractMaintainersFromJson(db, trimmed, slug, lower, norm, maintainerSet);

        return maintainerSet.ToList();
    }

    private static void AddMaintainersFromList(DataStoreService db, string collection, string key, HashSet<string> maintainerSet)
    {
        var list = db.Get<List<string>>(collection, key);
        if (list == null) return;

        foreach (var m in list)
        {
            if (!string.IsNullOrWhiteSpace(m)) maintainerSet.Add(m.Trim());
        }
    }

    private static void AddMaintainerFromOwner(DataStoreService db, string key, HashSet<string> maintainerSet)
    {
        string? owner = db.Get<string>("map_ownership", key);
        if (!string.IsNullOrWhiteSpace(owner)) maintainerSet.Add(owner.Trim());
    }

    private static void ExtractMaintainersFromJson(DataStoreService db, string trimmed, string slug, string lower, string norm, HashSet<string> maintainerSet)
    {
        var publishedMap = db.Get<JsonDocument>("published_maps", trimmed) 
                        ?? db.Get<JsonDocument>("published_maps", slug)
                        ?? db.Get<JsonDocument>("published_maps", lower)
                        ?? (!string.IsNullOrEmpty(norm) ? db.Get<JsonDocument>("published_maps", norm) : null);

        if (publishedMap == null) return;

        var root = publishedMap.RootElement;
        ExtractStringProperty(root, "owner_public_key", maintainerSet);
        ExtractStringProperty(root, "OwnerPublicKey", maintainerSet);
        ExtractArrayProperty(root, "Maintainers", maintainerSet);
    }

    private static void ExtractStringProperty(JsonElement root, string propertyName, HashSet<string> maintainerSet)
    {
        if (root.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            string? val = prop.GetString();
            if (!string.IsNullOrWhiteSpace(val)) maintainerSet.Add(val.Trim());
        }
    }

    private static void ExtractArrayProperty(JsonElement root, string propertyName, HashSet<string> maintainerSet)
    {
        if (root.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in prop.EnumerateArray())
            {
                string? m = el.GetString();
                if (!string.IsNullOrWhiteSpace(m)) maintainerSet.Add(m.Trim());
            }
        }
    }

    public static string GetOwner(DataStoreService db, string mapTitle)
    {
        if (string.IsNullOrWhiteSpace(mapTitle)) return string.Empty;

        string trimmed = mapTitle.Trim();
        string slug = trimmed.ToLowerInvariant().Replace(" ", "-");
        string lower = trimmed.ToLowerInvariant();
        string norm = NameNormalizationHelper.NormalizeMapName(trimmed);

        string? owner = db.Get<string>("map_ownership", trimmed) 
                     ?? db.Get<string>("map_ownership", slug) 
                     ?? db.Get<string>("map_ownership", lower)
                     ?? (!string.IsNullOrEmpty(norm) ? db.Get<string>("map_ownership", norm) : null);

        if (!string.IsNullOrWhiteSpace(owner)) return owner.Trim();

        var maintainers = GetMaintainers(db, mapTitle);
        return maintainers.Count > 0 ? maintainers[0] : string.Empty;
    }

    public static bool IsAuthorizedMaintainer(DataStoreService db, string mapTitle, string publicKey)
    {
        if (string.IsNullOrWhiteSpace(publicKey)) return false;

        var maintainers = GetMaintainers(db, mapTitle);
        if (maintainers.Count == 0)
        {
            return true;
        }

        return maintainers.Any(m => string.Equals(m, publicKey.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static bool HasMapConflict(DataStoreService db, string mapTitle, string publicKey, out string? conflictingMapTitle)
    {
        conflictingMapTitle = null;

        if (string.IsNullOrWhiteSpace(mapTitle))
            return false;

        string trimmed = mapTitle.Trim();
        string slug = trimmed.ToLowerInvariant().Replace(" ", "-");
        string lower = trimmed.ToLowerInvariant();
        string norm = NameNormalizationHelper.NormalizeMapName(trimmed);

        if (HasDirectRecord(db, trimmed, slug, lower, norm))
        {
            if (!IsAuthorizedMaintainer(db, mapTitle, publicKey))
            {
                conflictingMapTitle = mapTitle;
                return true;
            }
            return false;
        }

        return CheckSimilarTitleConflict(db, mapTitle, publicKey, out conflictingMapTitle);
    }

    private static bool HasDirectRecord(DataStoreService db, string trimmed, string slug, string lower, string norm)
    {
        return HasOwnershipRecord(db, trimmed, slug, lower, norm) ||
               HasPublishedMapRecord(db, trimmed, slug, lower, norm) ||
               HasMaintainerRecord(db, trimmed);
    }

    private static bool HasOwnershipRecord(DataStoreService db, string trimmed, string slug, string lower, string norm)
    {
        return db.Get<string>("map_ownership", trimmed) != null ||
               db.Get<string>("map_ownership", slug) != null ||
               db.Get<string>("map_ownership", lower) != null ||
               (!string.IsNullOrEmpty(norm) && db.Get<string>("map_ownership", norm) != null);
    }

    private static bool HasPublishedMapRecord(DataStoreService db, string trimmed, string slug, string lower, string norm)
    {
        return db.Get<JsonDocument>("published_maps", trimmed) != null ||
               db.Get<JsonDocument>("published_maps", slug) != null ||
               db.Get<JsonDocument>("published_maps", lower) != null ||
               (!string.IsNullOrEmpty(norm) && db.Get<JsonDocument>("published_maps", norm) != null);
    }

    private static bool HasMaintainerRecord(DataStoreService db, string trimmed)
    {
        return db.Get<List<string>>("map_maintainers", trimmed)?.Count > 0;
    }

    private static bool CheckSimilarTitleConflict(DataStoreService db, string mapTitle, string publicKey, out string? conflictingMapTitle)
    {
        conflictingMapTitle = null;
        var allPublished = db.GetAllWithKeys<JsonDocument>("published_maps");
        if (allPublished.Count == 0) return false;

        var publishedTitles = allPublished.Keys.ToList();
        if (NameNormalizationHelper.IsMapNameTooSimilar(mapTitle, publishedTitles, 2, out var similarTitle))
        {
            if (!IsAuthorizedMaintainer(db, similarTitle ?? mapTitle, publicKey))
            {
                conflictingMapTitle = similarTitle ?? mapTitle;
                return true;
            }
        }
        return false;
    }

    public static List<string> AddMaintainer(DataStoreService db, string mapTitle, string maintainerPublicKey)
    {
        if (string.IsNullOrWhiteSpace(mapTitle) || string.IsNullOrWhiteSpace(maintainerPublicKey))
        {
            return GetMaintainers(db, mapTitle);
        }

        string trimmed = mapTitle.Trim();
        string slug = trimmed.ToLowerInvariant().Replace(" ", "-");
        string lower = trimmed.ToLowerInvariant();
        string norm = NameNormalizationHelper.NormalizeMapName(trimmed);
        string trimmedKey = maintainerPublicKey.Trim();

        var maintainers = GetMaintainers(db, mapTitle);
        if (!maintainers.Any(m => string.Equals(m, trimmedKey, StringComparison.OrdinalIgnoreCase)))
        {
            maintainers.Add(trimmedKey);
        }

        db.Upsert("map_maintainers", trimmed, maintainers);
        db.Upsert("map_maintainers", slug, maintainers);
        db.Upsert("map_maintainers", lower, maintainers);
        if (!string.IsNullOrEmpty(norm)) db.Upsert("map_maintainers", norm, maintainers);

        string? owner = db.Get<string>("map_ownership", trimmed);
        if (string.IsNullOrWhiteSpace(owner))
        {
            db.Upsert("map_ownership", trimmed, trimmedKey);
            db.Upsert("map_ownership", slug, trimmedKey);
            db.Upsert("map_ownership", lower, trimmedKey);
            if (!string.IsNullOrEmpty(norm)) db.Upsert("map_ownership", norm, trimmedKey);
        }

        return maintainers;
    }

    public static List<string> RemoveMaintainer(DataStoreService db, string mapTitle, string maintainerPublicKey)
    {
        if (string.IsNullOrWhiteSpace(mapTitle) || string.IsNullOrWhiteSpace(maintainerPublicKey))
        {
            return GetMaintainers(db, mapTitle);
        }

        string trimmed = mapTitle.Trim();
        string slug = trimmed.ToLowerInvariant().Replace(" ", "-");
        string lower = trimmed.ToLowerInvariant();
        string norm = NameNormalizationHelper.NormalizeMapName(trimmed);
        string trimmedKey = maintainerPublicKey.Trim();

        var maintainers = GetMaintainers(db, mapTitle);
        maintainers.RemoveAll(m => string.Equals(m, trimmedKey, StringComparison.OrdinalIgnoreCase));

        db.Upsert("map_maintainers", trimmed, maintainers);
        db.Upsert("map_maintainers", slug, maintainers);
        db.Upsert("map_maintainers", lower, maintainers);
        if (!string.IsNullOrEmpty(norm)) db.Upsert("map_maintainers", norm, maintainers);

        string? owner = db.Get<string>("map_ownership", trimmed);
        if (string.Equals(owner, trimmedKey, StringComparison.OrdinalIgnoreCase))
        {
            string newOwner = maintainers.Count > 0 ? maintainers[0] : "";
            if (!string.IsNullOrEmpty(newOwner))
            {
                db.Upsert("map_ownership", trimmed, newOwner);
                db.Upsert("map_ownership", slug, newOwner);
                db.Upsert("map_ownership", lower, newOwner);
                if (!string.IsNullOrEmpty(norm)) db.Upsert("map_ownership", norm, newOwner);
            }
            else
            {
                db.Delete("map_ownership", trimmed);
                db.Delete("map_ownership", slug);
                db.Delete("map_ownership", lower);
                if (!string.IsNullOrEmpty(norm)) db.Delete("map_ownership", norm);
            }
        }

        return maintainers;
    }
}

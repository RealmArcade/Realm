using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Realm.AdminServer.Services;

public static class MapMaintainerHelper
{
    public static List<string> GetMaintainers(DataStoreService db, string mapTitle)
    {
        if (string.IsNullOrWhiteSpace(mapTitle))
        {
            return new List<string>();
        }

        string trimmed = mapTitle.Trim();
        string slug = trimmed.ToLowerInvariant().Replace(" ", "-");
        string lower = trimmed.ToLowerInvariant();

        var maintainerSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var list1 = db.Get<List<string>>("map_maintainers", trimmed);
        if (list1 != null)
        {
            foreach (var key in list1)
            {
                if (!string.IsNullOrWhiteSpace(key)) maintainerSet.Add(key.Trim());
            }
        }

        var list2 = db.Get<List<string>>("map_maintainers", slug);
        if (list2 != null)
        {
            foreach (var key in list2)
            {
                if (!string.IsNullOrWhiteSpace(key)) maintainerSet.Add(key.Trim());
            }
        }

        var list3 = db.Get<List<string>>("map_maintainers", lower);
        if (list3 != null)
        {
            foreach (var key in list3)
            {
                if (!string.IsNullOrWhiteSpace(key)) maintainerSet.Add(key.Trim());
            }
        }

        string? owner1 = db.Get<string>("map_ownership", trimmed);
        if (!string.IsNullOrWhiteSpace(owner1)) maintainerSet.Add(owner1.Trim());

        string? owner2 = db.Get<string>("map_ownership", slug);
        if (!string.IsNullOrWhiteSpace(owner2)) maintainerSet.Add(owner2.Trim());

        string? owner3 = db.Get<string>("map_ownership", lower);
        if (!string.IsNullOrWhiteSpace(owner3)) maintainerSet.Add(owner3.Trim());

        var publishedMap = db.Get<JsonDocument>("published_maps", trimmed) 
                        ?? db.Get<JsonDocument>("published_maps", slug)
                        ?? db.Get<JsonDocument>("published_maps", lower);
        if (publishedMap != null)
        {
            var root = publishedMap.RootElement;
            if (root.TryGetProperty("owner_public_key", out var opk) && opk.ValueKind == JsonValueKind.String)
            {
                string? opkStr = opk.GetString();
                if (!string.IsNullOrWhiteSpace(opkStr)) maintainerSet.Add(opkStr.Trim());
            }
            if (root.TryGetProperty("OwnerPublicKey", out var opk2) && opk2.ValueKind == JsonValueKind.String)
            {
                string? opkStr2 = opk2.GetString();
                if (!string.IsNullOrWhiteSpace(opkStr2)) maintainerSet.Add(opkStr2.Trim());
            }
            if (root.TryGetProperty("Maintainers", out var maintProp) && maintProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in maintProp.EnumerateArray())
                {
                    string? m = el.GetString();
                    if (!string.IsNullOrWhiteSpace(m)) maintainerSet.Add(m.Trim());
                }
            }
        }

        return maintainerSet.ToList();
    }

    public static string GetOwner(DataStoreService db, string mapTitle)
    {
        if (string.IsNullOrWhiteSpace(mapTitle)) return string.Empty;

        string trimmed = mapTitle.Trim();
        string slug = trimmed.ToLowerInvariant().Replace(" ", "-");
        string lower = trimmed.ToLowerInvariant();

        string? owner = db.Get<string>("map_ownership", trimmed) 
                     ?? db.Get<string>("map_ownership", slug) 
                     ?? db.Get<string>("map_ownership", lower);

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

    public static List<string> AddMaintainer(DataStoreService db, string mapTitle, string maintainerPublicKey)
    {
        if (string.IsNullOrWhiteSpace(mapTitle) || string.IsNullOrWhiteSpace(maintainerPublicKey))
        {
            return GetMaintainers(db, mapTitle);
        }

        string trimmed = mapTitle.Trim();
        string slug = trimmed.ToLowerInvariant().Replace(" ", "-");
        string lower = trimmed.ToLowerInvariant();
        string trimmedKey = maintainerPublicKey.Trim();

        var maintainers = GetMaintainers(db, mapTitle);
        if (!maintainers.Any(m => string.Equals(m, trimmedKey, StringComparison.OrdinalIgnoreCase)))
        {
            maintainers.Add(trimmedKey);
        }

        db.Upsert("map_maintainers", trimmed, maintainers);
        db.Upsert("map_maintainers", slug, maintainers);
        db.Upsert("map_maintainers", lower, maintainers);

        string? owner = db.Get<string>("map_ownership", trimmed);
        if (string.IsNullOrWhiteSpace(owner))
        {
            db.Upsert("map_ownership", trimmed, trimmedKey);
            db.Upsert("map_ownership", slug, trimmedKey);
            db.Upsert("map_ownership", lower, trimmedKey);
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
        string trimmedKey = maintainerPublicKey.Trim();

        var maintainers = GetMaintainers(db, mapTitle);
        maintainers.RemoveAll(m => string.Equals(m, trimmedKey, StringComparison.OrdinalIgnoreCase));

        db.Upsert("map_maintainers", trimmed, maintainers);
        db.Upsert("map_maintainers", slug, maintainers);
        db.Upsert("map_maintainers", lower, maintainers);

        string? owner = db.Get<string>("map_ownership", trimmed);
        if (string.Equals(owner, trimmedKey, StringComparison.OrdinalIgnoreCase))
        {
            string newOwner = maintainers.Count > 0 ? maintainers[0] : "";
            if (!string.IsNullOrEmpty(newOwner))
            {
                db.Upsert("map_ownership", trimmed, newOwner);
                db.Upsert("map_ownership", slug, newOwner);
                db.Upsert("map_ownership", lower, newOwner);
            }
            else
            {
                db.Delete("map_ownership", trimmed);
                db.Delete("map_ownership", slug);
                db.Delete("map_ownership", lower);
            }
        }

        return maintainers;
    }
}

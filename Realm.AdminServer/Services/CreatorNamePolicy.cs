using System;
using System.Collections.Generic;
using System.Text.Json;
using Realm.Shared;

namespace Realm.AdminServer.Services;

/// <summary>
///     Decides who may hold a creator name. A name stays with the key that registered it until that key has a published map
///     the community greenlit, so an established creator can never lose the name their maps are credited under; a name with no
///     greenlit map behind it belongs to whichever key registered it last.
/// </summary>
public static class CreatorNamePolicy
{
	public const string NameLocksCollection = "name_locks";

	public const string CreatorsCollection = "creators";

	private const string MapOwnershipCollection = "map_ownership";

	public static string GetSlug(string username)
	{
		return NameNormalizationHelper.GetCreatorSlug(username);
	}

	/// <summary>
	///     Reads the key a creator name is registered to, or reports that no key holds it. Both the canonical slug and the
	///     pre-normalisation spelling are consulted, because a name whose slug normalised to nothing was stored under the
	///     spelling itself.
	/// </summary>
	public static bool TryReadNameLockOwner(DataStoreService dataStore, string username, out string ownerPublicKey)
	{
		ownerPublicKey = string.Empty;
		if (string.IsNullOrWhiteSpace(username)) return false;

		JsonDocument? nameLock = dataStore.Get<JsonDocument>(NameLocksCollection, GetSlug(username))
		                         ?? dataStore.Get<JsonDocument>(NameLocksCollection, username.Trim().ToLowerInvariant().Replace(" ", "-"));
		if (nameLock == null) return false;

		JsonElement root = nameLock.RootElement;
		ownerPublicKey = root.TryGetProperty("owner_public_key", out JsonElement ownerProperty)
			? ownerProperty.GetString()?.Trim() ?? string.Empty
			: string.Empty;

		return ownerPublicKey.Length > 0;
	}

	/// <summary>
	///     Whether any map this key owns has been greenlit for publication. Such a key earned its creator name, so the name
	///     cannot be registered again by another key without an administrator releasing it first.
	/// </summary>
	public static bool HasGreenlitPublishedMap(DataStoreService dataStore, string publicKey)
	{
		if (string.IsNullOrWhiteSpace(publicKey)) return false;

		string trimmedKey = publicKey.Trim();
		foreach (KeyValuePair<string, string> ownership in dataStore.GetAllWithKeys<string>(MapOwnershipCollection))
		{
			if (!string.Equals(ownership.Value?.Trim(), trimmedKey, StringComparison.OrdinalIgnoreCase)) continue;
			if (MapStatsHelper.IsMapGreenlit(dataStore, ownership.Key)) return true;
		}

		return false;
	}

	/// <summary>
	///     Drops the creator profile a key held for this name once the name belongs to another key. A key that held several
	///     names keeps the profile of the others, so only the entry that still names this username is removed.
	/// </summary>
	public static void ReleaseCreatorKey(DataStoreService dataStore, string publicKey, string username)
	{
		if (string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(username)) return;

		string trimmedKey = publicKey.Trim();
		JsonDocument? creator = dataStore.Get<JsonDocument>(CreatorsCollection, trimmedKey);
		if (creator == null) return;

		JsonElement root = creator.RootElement;
		string registeredUsername = root.TryGetProperty("username", out JsonElement usernameProperty)
			? usernameProperty.GetString()?.Trim() ?? string.Empty
			: string.Empty;
		if (registeredUsername.Length > 0 && !string.Equals(registeredUsername, username.Trim(), StringComparison.OrdinalIgnoreCase)) return;

		dataStore.Delete(CreatorsCollection, trimmedKey);
	}
}

using System.Collections.Generic;
using Realm.EditorAPI;

namespace Realm.Client.Services;

public struct ParsedAssetMetadata
{
	public AssetType? AssetType;
	public bool? SupportsTeamColor;
	public string? ChromaKey;
	public List<string> Tags;
}
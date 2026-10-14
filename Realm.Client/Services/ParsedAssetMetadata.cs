using System.Collections.Generic;

namespace Realm.Client.Services;

public struct ParsedAssetMetadata
{
	public string? AssetType;
	public bool? SupportsTeamColor;
	public string? ChromaKey;
	public List<string> Tags;
}
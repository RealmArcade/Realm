using System.Collections.Generic;

namespace Realm.Client.Utils;

public class GreenlitReferenceSuggestion
{
	public string MapTitle { get; set; } = string.Empty;
	public string MapVersion { get; set; } = string.Empty;
	public List<string> MatchedAssetPaths { get; set; } = new();
	public List<string> MatchedHashes { get; set; } = new();
	public long SavedBytes { get; set; }
	public bool IsSelected { get; set; } = true;
}
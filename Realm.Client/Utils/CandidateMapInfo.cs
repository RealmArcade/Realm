using System;
using System.Collections.Generic;

namespace Realm.Client.Utils;

public class CandidateMapInfo
{
	public string MapTitle { get; set; } = string.Empty;
	public string MapVersion { get; set; } = "1.0.0";
	public HashSet<string> AssetHashes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
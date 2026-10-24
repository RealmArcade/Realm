using System.Collections.Generic;

namespace Realm.Client.UI;

public struct MapBriefingDetails
{
	public string PathName;
	public string DisplayName;
	public string Description;
	public string GameBuildNumber;
	public string Version;
	public string ManifestHash;
	public string ThumbnailPath;
	public List<string> Attribution_SourceMap;
}

using System.Collections.Generic;

namespace Realm.Client.Animation;

public class MixamoImportResult
{
	public bool Success { get; set; }
	public string StrippedGlbPath { get; set; } = string.Empty;
	public List<string> ExtractedAnimationFiles { get; set; } = new();
	public List<string> ExtractedAnimationNames { get; set; } = new();
	public string ErrorMessage { get; set; } = string.Empty;
}
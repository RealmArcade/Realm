using System.Numerics;
using System.Text.Json.Nodes;
using Assimp;
using Realm.Shared.Metadata;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Textures;
using SkiaSharp;

namespace Realm.Shared;

/// <summary>resolving the chroma key from the options or the image.</summary>
public static partial class GlbPlayerColorProcessor
	{
	private static string ResolveChromaKey(GlbPlayerColorOptions options, SKBitmap albedoImg)
	{
		string effectiveChromaKey = options.ChromaKey;
		if (string.IsNullOrWhiteSpace(effectiveChromaKey) || string.Equals(effectiveChromaKey, "auto", StringComparison.OrdinalIgnoreCase))
		{
			string detectedKey = GlbChromaKeyDetector.AutoDetectChromaKey(albedoImg) ?? "#FF00FF";
			effectiveChromaKey = options.AutoCorrectChromaKey ? GlbChromaKeyMatcher.FindClosestMatchingChromaKey(albedoImg, detectedKey) ?? detectedKey : detectedKey;
			options.ChromaKey = effectiveChromaKey;
		}
		else if (options.AutoCorrectChromaKey)
		{
			effectiveChromaKey = GlbChromaKeyMatcher.FindClosestMatchingChromaKey(albedoImg, effectiveChromaKey) ?? effectiveChromaKey;
			options.ChromaKey = effectiveChromaKey;
		}

		return effectiveChromaKey;
	}
}

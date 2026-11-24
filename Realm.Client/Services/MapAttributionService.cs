using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Realm.Shared.Distribution;

namespace Realm.Client.Services;

/// <summary>One creator whose work a map carries, with everything the credits panel can say about them.</summary>
public class MapCreditEntry
{
	/// <summary>The map the content came from, as the package states it.</summary>
	public string SourceMap { get; init; } = string.Empty;

	/// <summary>The Steam persona to draw beside the source, or the name the package states when Steam cannot be asked.</summary>
	public string AuthorName { get; init; } = string.Empty;

	/// <summary>The source creator's SteamID64, or zero when the package names nobody.</summary>
	public ulong AuthorSteamId { get; init; }

	/// <summary>The Workshop item the source map was published as, or zero when it was never published.</summary>
	public ulong SourceWorkshopId { get; init; }

	/// <summary>The imported assets or scripts this source contributed, as the map names them.</summary>
	public List<string> Assets { get; init; } = new();
}

public class MapAttributionService
{
	public const string MetadataAttributionProperty = "Attribution_ImportedFromMap";
	public const string SourceMapAttributeName = "Attribution_SourceMap";
	public const string ManifestFileName = "manifest.json";

	private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
	{
		"bin",
		"obj",
		".git",
		".vs",
		".godot",
		".vscode",
		".sidecarcache",
		".backups",
		"backups"
	};

	public IReadOnlyList<string> CollectSortedSourceMapNames(string workspacePath)
	{
		var sourceMapNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (string.IsNullOrEmpty(workspacePath) || !Directory.Exists(workspacePath))
		{
			return Array.Empty<string>();
		}

		CollectFromMetadata(workspacePath, sourceMapNames);
		CollectFromCode(workspacePath, sourceMapNames);
		CollectFromReferences(workspacePath, sourceMapNames);

		return sourceMapNames
			.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	/// <summary>
	///     Builds the credits a map folder states: every source map it carries, the author that source names, the Workshop
	///     item it was published as, and the assets the map attributes to it. Composition and hand-copied files appear the
	///     same way, because both leave the source map's name in the package.
	/// </summary>
	/// <param name="mapFolder">The map folder, installed or being authored.</param>
	/// <returns>One entry per source, sorted by source name, or an empty list when the map credits nobody.</returns>
	public IReadOnlyList<MapCreditEntry> CollectCredits(string mapFolder)
	{
		if (string.IsNullOrEmpty(mapFolder) || !Directory.Exists(mapFolder)) return Array.Empty<MapCreditEntry>();

		var entries = new Dictionary<string, MapCreditEntry>(StringComparer.OrdinalIgnoreCase);

		foreach (string sourceMapName in CollectSortedSourceMapNames(mapFolder))
		{
			entries[sourceMapName] = new MapCreditEntry { SourceMap = sourceMapName };
		}

		MapReferences? references = MapReferences.Load(mapFolder);
		if (references != null)
		{
			foreach (MapReference reference in references.References)
			{
				if (string.IsNullOrWhiteSpace(reference.MapName)) continue;
				entries[reference.MapName] = new MapCreditEntry
				{
					SourceMap = reference.MapName,
					AuthorSteamId = reference.SourceAuthorSteamId,
					SourceWorkshopId = reference.SourceWorkshopId
				};
			}
		}

		CollectCreditedAssets(mapFolder, entries);

		return entries.Values
			.OrderBy(entry => entry.SourceMap, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	private static void CollectFromReferences(string workspacePath, HashSet<string> sourceMapNames)
	{
		MapReferences? references = MapReferences.Load(workspacePath);
		if (references == null) return;

		foreach (MapReference reference in references.References)
		{
			if (!string.IsNullOrWhiteSpace(reference.MapName)) sourceMapNames.Add(reference.MapName.Trim());
		}
	}

	private static void CollectCreditedAssets(string mapFolder, Dictionary<string, MapCreditEntry> entries)
	{
		string metadataPath = MetadataService.ResolveMetadataPath(mapFolder);
		if (!File.Exists(metadataPath)) return;

		try
		{
			JsonNode? root = JsonNode.Parse(File.ReadAllText(metadataPath));
			CollectCreditedAssetsFromNode(root, string.Empty, entries);
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[MapAttributionService] Failed to read credited assets from '{metadataPath}': {exception.Message}");
		}
	}

	private static void CollectCreditedAssetsFromNode(JsonNode? node, string templateId, Dictionary<string, MapCreditEntry> entries)
	{
		if (node is JsonObject jsonObject)
		{
			string statedTemplateId = templateId;
			if (jsonObject["TemplateID"] is JsonValue templateValue && templateValue.TryGetValue(out string? templateText) && !string.IsNullOrWhiteSpace(templateText))
			{
				statedTemplateId = templateText.Trim();
			}

			foreach (KeyValuePair<string, JsonNode?> property in jsonObject)
			{
				if (property.Key.Equals(MetadataAttributionProperty, StringComparison.OrdinalIgnoreCase))
				{
					AddCreditedAsset(property.Value, statedTemplateId, entries);
					continue;
				}

				CollectCreditedAssetsFromNode(property.Value, statedTemplateId, entries);
			}

			return;
		}

		if (node is not JsonArray jsonArray) return;

		foreach (JsonNode? item in jsonArray)
		{
			CollectCreditedAssetsFromNode(item, templateId, entries);
		}
	}

	private static void AddCreditedAsset(JsonNode? node, string templateId, Dictionary<string, MapCreditEntry> entries)
	{
		if (node is not JsonValue jsonValue || !jsonValue.TryGetValue(out string? sourceMapName) || string.IsNullOrWhiteSpace(sourceMapName)) return;

		string key = sourceMapName.Trim();
		if (!entries.TryGetValue(key, out MapCreditEntry? entry))
		{
			entry = new MapCreditEntry { SourceMap = key };
			entries[key] = entry;
		}

		string asset = string.IsNullOrWhiteSpace(templateId) ? key : templateId;
		if (!entry.Assets.Contains(asset, StringComparer.OrdinalIgnoreCase)) entry.Assets.Add(asset);
	}

	public void ApplyAttribution(MapManifest manifest, string workspacePath)
	{
		if (manifest == null) return;

		manifest.Attribution_SourceMap = CollectSortedSourceMapNames(workspacePath).ToList();
	}

	public void UpdateManifestAttribution(string workspacePath)
	{
		if (string.IsNullOrEmpty(workspacePath) || !Directory.Exists(workspacePath)) return;

		string manifestPath = Path.Combine(workspacePath, ManifestFileName);
		if (!File.Exists(manifestPath)) return;

		try
		{
			MapManifest manifest = MapManifest.LoadFromFile(manifestPath);
			manifest.Attribution_SourceMap = CollectSortedSourceMapNames(workspacePath).ToList();
			manifest.SaveToFile(manifestPath);
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[MapAttributionService] Failed to update manifest attribution in '{manifestPath}': {exception.Message}");
		}
	}

	private static void CollectFromMetadata(string workspacePath, HashSet<string> sourceMapNames)
	{
		string metadataPath = MetadataService.ResolveMetadataPath(workspacePath);
		if (!File.Exists(metadataPath)) return;

		try
		{
			JsonNode? root = JsonNode.Parse(File.ReadAllText(metadataPath));
			CollectFromJsonNode(root, sourceMapNames);
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[MapAttributionService] Failed to read metadata attribution from '{metadataPath}': {exception.Message}");
		}
	}

	private static void CollectFromJsonNode(JsonNode? node, HashSet<string> sourceMapNames)
	{
		if (node is JsonObject jsonObject)
		{
			foreach (var property in jsonObject)
			{
				if (property.Key.Equals(MetadataAttributionProperty, StringComparison.OrdinalIgnoreCase))
				{
					AddSourceMapName(property.Value, sourceMapNames);
					continue;
				}

				CollectFromJsonNode(property.Value, sourceMapNames);
			}

			return;
		}

		if (node is JsonArray jsonArray)
		{
			foreach (JsonNode? item in jsonArray)
			{
				CollectFromJsonNode(item, sourceMapNames);
			}
		}
	}

	private static void AddSourceMapName(JsonNode? node, HashSet<string> sourceMapNames)
	{
		if (node is not JsonValue jsonValue) return;
		if (!jsonValue.TryGetValue(out string? value)) return;
		if (string.IsNullOrWhiteSpace(value)) return;

		sourceMapNames.Add(value.Trim());
	}

	private static void CollectFromCode(string workspacePath, HashSet<string> sourceMapNames)
	{
		foreach (string filePath in EnumerateCodeFiles(workspacePath))
		{
			try
			{
				SyntaxNode root = CSharpSyntaxTree.ParseText(File.ReadAllText(filePath)).GetRoot();
				foreach (AttributeSyntax attribute in root.DescendantNodes().OfType<AttributeSyntax>())
				{
					if (!IsSourceMapAttribute(attribute)) continue;
					AddSourceMapName(attribute, sourceMapNames);
				}
			}
			catch (Exception exception)
			{
				GameLog.LogError($"[MapAttributionService] Failed to read code attribution from '{filePath}': {exception.Message}");
			}
		}
	}

	private static IEnumerable<string> EnumerateCodeFiles(string workspacePath)
	{
		var directories = new Stack<string>();
		directories.Push(workspacePath);

		while (directories.Count > 0)
		{
			string directory = directories.Pop();
			string[] childDirectories;
			string[] childFiles;

			try
			{
				childDirectories = Directory.GetDirectories(directory);
				childFiles = Directory.GetFiles(directory, "*.cs", SearchOption.TopDirectoryOnly);
			}
			catch
			{
				continue;
			}

			foreach (string childDirectory in childDirectories)
			{
				if (IgnoredDirectoryNames.Contains(Path.GetFileName(childDirectory))) continue;
				directories.Push(childDirectory);
			}

			foreach (string childFile in childFiles)
			{
				yield return childFile;
			}
		}
	}

	private static bool IsSourceMapAttribute(AttributeSyntax attribute)
	{
		string attributeName = GetAttributeTypeName(attribute);
		return attributeName.Equals(SourceMapAttributeName, StringComparison.Ordinal)
		       || attributeName.Equals(SourceMapAttributeName + "Attribute", StringComparison.Ordinal);
	}

	private static string GetAttributeTypeName(AttributeSyntax attribute)
	{
		string text = attribute.Name.ToString();
		int lastDotIndex = text.LastIndexOf('.');
		return lastDotIndex >= 0 ? text.Substring(lastDotIndex + 1) : text;
	}

	private static void AddSourceMapName(AttributeSyntax attribute, HashSet<string> sourceMapNames)
	{
		AttributeArgumentListSyntax? argumentList = attribute.ArgumentList;
		if (argumentList == null || argumentList.Arguments.Count == 0) return;

		if (argumentList.Arguments[0].Expression is not LiteralExpressionSyntax literalExpression) return;
		if (!literalExpression.IsKind(SyntaxKind.StringLiteralExpression)) return;

		string value = literalExpression.Token.ValueText;
		if (string.IsNullOrWhiteSpace(value)) return;

		sourceMapNames.Add(value.Trim());
	}
}

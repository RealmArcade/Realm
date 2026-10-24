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

		return sourceMapNames
			.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
			.ToList();
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
			GD.PrintErr($"[MapAttributionService] Failed to update manifest attribution in '{manifestPath}': {exception.Message}");
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
			GD.PrintErr($"[MapAttributionService] Failed to read metadata attribution from '{metadataPath}': {exception.Message}");
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
				GD.PrintErr($"[MapAttributionService] Failed to read code attribution from '{filePath}': {exception.Message}");
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

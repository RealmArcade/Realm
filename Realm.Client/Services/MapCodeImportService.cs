using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Godot;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Realm.Client.Network;
using Realm.Shared.Distribution;

namespace Realm.Client.Services;

public class MapCodeImportService
{
	private const string ImportsRootFolder = "imports";
	private const string AttributionNamespace = "Realm.Maps";
	private const string SourceMapAttributeUsageName = "Attribution_SourceMap";
	public const string SourceMapAttributeTypeName = "Attribution_SourceMapAttribute";
	public const string ExportAttributeUsageName = "Export";
	public const string ExportAttributeTypeName = "ExportAttribute";
	private static readonly string[] RetainedUsingPrefixes =
	{
		"System",
		"Realm.MapAPI",
		"Realm.Maps"
	};

	public IReadOnlyList<MapCodeTypeCandidate> EnumerateTypes(MapAssetImportSource source, string destinationWorkspacePath)
	{
		return EnumerateTypeCandidates(source, destinationWorkspacePath, requireExported: false);
	}

	public IReadOnlyList<MapCodeTypeCandidate> EnumerateExports(MapAssetImportSource source, string destinationWorkspacePath)
	{
		return EnumerateTypeCandidates(source, destinationWorkspacePath, requireExported: true);
	}

	private IReadOnlyList<MapCodeTypeCandidate> EnumerateTypeCandidates(MapAssetImportSource source, string destinationWorkspacePath, bool requireExported)
	{
		SourceCodeIndex sourceIndex = CodeWorkspaceFiles.LoadSourceIndex(source);
		DestinationCodeIndex destinationIndex = ScanDestination(destinationWorkspacePath);
		var candidates = new List<MapCodeTypeCandidate>(sourceIndex.TypesByFullName.Count);

		foreach (SourceType sourceType in sourceIndex.TypesByFullName.Values)
		{
			if (requireExported)
			{
				if (!sourceType.IsExported) continue;
			}
			else if (!IsTypeImportable(sourceType))
			{
				continue;
			}

			IReadOnlyList<string> existingPaths = destinationIndex.GetTypeFilePaths(sourceType.FullName);
			string relativePath = existingPaths.Count > 0
				? existingPaths[0]
				: GetTargetRelativePath(source.MapTitle, sourceType);
			bool willReplace = existingPaths.Count > 0
			                   || File.Exists(GetAbsolutePath(destinationWorkspacePath, relativePath));

			candidates.Add(new MapCodeTypeCandidate(
				sourceType.NamespaceName,
				sourceType.Name,
				sourceType.FullName,
				sourceType.Kind,
				relativePath,
				willReplace,
				sourceType.IsExported,
				sourceType.ExportDisplayName,
				sourceType.ExportCategory));
		}

		candidates.Sort(CompareTypeCandidates);
		return candidates;
	}

	private static bool IsTypeImportable(SourceType sourceType)
	{
		return !sourceType.IsTemplateFile || sourceType.IsExported;
	}

	public IReadOnlyList<MapCodeNamespaceCandidate> EnumerateNamespaces(MapAssetImportSource source, string destinationWorkspacePath)
	{
		SourceCodeIndex sourceIndex = CodeWorkspaceFiles.LoadSourceIndex(source);
		DestinationCodeIndex destinationIndex = ScanDestination(destinationWorkspacePath);
		var candidates = new List<MapCodeNamespaceCandidate>();

		foreach (string namespaceName in sourceIndex.Namespaces)
		{
			List<SourceType> namespaceTypes = sourceIndex.GetTypesInNamespaceTree(namespaceName).Where(IsTypeImportable).ToList();
			if (namespaceTypes.Count == 0) continue;

			var targetRelativePaths = new List<string>();
			bool willReplace = false;

			foreach (SourceType sourceType in namespaceTypes)
			{
				IReadOnlyList<string> existingPaths = destinationIndex.GetTypeFilePaths(sourceType.FullName);
				string targetRelativePath = existingPaths.Count > 0
					? existingPaths[0]
					: GetTargetRelativePath(source.MapTitle, sourceType);

				targetRelativePaths.AddRange(existingPaths);
				if (existingPaths.Count == 0)
				{
					targetRelativePaths.Add(targetRelativePath);
				}

				willReplace |= existingPaths.Count > 0
				               || File.Exists(GetAbsolutePath(destinationWorkspacePath, targetRelativePath));
			}

			targetRelativePaths = targetRelativePaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

			candidates.Add(new MapCodeNamespaceCandidate(
				namespaceName,
				namespaceTypes.Count,
				namespaceTypes.Select(sourceType => sourceType.FullName).ToList(),
				targetRelativePaths,
				willReplace));
		}

		candidates.Sort((left, right) => string.Compare(left.NamespaceName, right.NamespaceName, StringComparison.OrdinalIgnoreCase));
		return candidates;
	}

	public MapCodeImportSummary ImportTypes(MapAssetImportSource source, string destinationWorkspacePath, IReadOnlyCollection<string> typeFullNames)
	{
		SourceCodeIndex sourceIndex = CodeWorkspaceFiles.LoadSourceIndex(source);
		var requestedTypes = new List<SourceType>();
		var summary = new MapCodeImportSummary();

		foreach (string fullName in typeFullNames)
		{
			if (sourceIndex.TypesByFullName.TryGetValue(fullName, out SourceType? sourceType) && IsTypeImportable(sourceType))
			{
				requestedTypes.Add(sourceType);
			}
			else
			{
				summary.FailedTypes.Add(fullName);
			}
		}

		return ImportUnits(source, destinationWorkspacePath, sourceIndex, requestedTypes, summary);
	}

	public MapCodeImportSummary ImportNamespaces(MapAssetImportSource source, string destinationWorkspacePath, IReadOnlyCollection<string> namespaceNames)
	{
		SourceCodeIndex sourceIndex = CodeWorkspaceFiles.LoadSourceIndex(source);
		var requestedTypes = new List<SourceType>();
		var summary = new MapCodeImportSummary();

		foreach (string namespaceName in namespaceNames)
		{
			List<SourceType> namespaceTypes = sourceIndex.GetTypesInNamespaceTree(namespaceName).Where(IsTypeImportable).ToList();
			if (namespaceTypes.Count == 0)
			{
				summary.FailedTypes.Add(namespaceName);
				continue;
			}

			requestedTypes.AddRange(namespaceTypes);
		}

		return ImportUnits(source, destinationWorkspacePath, sourceIndex, requestedTypes, summary);
	}

	private MapCodeImportSummary ImportUnits(
		MapAssetImportSource source,
		string destinationWorkspacePath,
		SourceCodeIndex sourceIndex,
		IReadOnlyCollection<SourceType> requestedTypes,
		MapCodeImportSummary summary)
	{
		if (string.IsNullOrEmpty(destinationWorkspacePath) || !Directory.Exists(destinationWorkspacePath))
		{
			summary.FailedTypes.AddRange(requestedTypes.Select(sourceType => sourceType.FullName));
			return summary;
		}

		MapWorkspaceService.EnsureSourceMapAttributionAttribute(destinationWorkspacePath);

		DestinationCodeIndex destinationIndex = ScanDestination(destinationWorkspacePath);
		var orderedTypes = new List<SourceType>();
		var visitedTypes = new HashSet<string>(StringComparer.Ordinal);
		var selectedFullNames = new HashSet<string>(requestedTypes.Select(sourceType => sourceType.FullName), StringComparer.Ordinal);
		List<SourceType> expandedTypes = ExpandExportedCompanionTypes(sourceIndex, requestedTypes);

		foreach (SourceType requestedType in expandedTypes)
		{
			AddTypeWithDependencies(requestedType, source.MapTitle, sourceIndex, destinationIndex, selectedFullNames, visitedTypes, orderedTypes, summary);
		}

		var importedNamespaces = new HashSet<string>(orderedTypes.Select(sourceType => sourceType.NamespaceName), StringComparer.Ordinal);
		foreach (SourceType sourceType in orderedTypes)
		{
			bool isSelected = selectedFullNames.Contains(sourceType.FullName);
			WriteTypeFile(sourceType, source.MapTitle, destinationWorkspacePath, destinationIndex, importedNamespaces, isSelected, summary);
		}

		return summary;
	}

	private static List<SourceType> ExpandExportedCompanionTypes(SourceCodeIndex sourceIndex, IReadOnlyCollection<SourceType> requestedTypes)
	{
		var expandedTypes = new List<SourceType>(requestedTypes);

		foreach (SourceType requestedType in requestedTypes)
		{
			if (!requestedType.IsExported || requestedType.SourceFilePaths.Count == 0) continue;

			foreach (SourceType companionType in sourceIndex.GetTypesInSourceFiles(requestedType.SourceFilePaths))
			{
				if (expandedTypes.Contains(companionType)) continue;
				if (!IsTypeImportable(companionType)) continue;

				expandedTypes.Add(companionType);
			}
		}

		return expandedTypes;
	}

	private static void AddTypeWithDependencies(
		SourceType sourceType,
		string sourceMapName,
		SourceCodeIndex sourceIndex,
		DestinationCodeIndex destinationIndex,
		HashSet<string> selectedFullNames,
		HashSet<string> visitedTypes,
		List<SourceType> orderedTypes,
		MapCodeImportSummary summary)
	{
		if (!visitedTypes.Add(sourceType.FullName)) return;

		bool isSelected = selectedFullNames.Contains(sourceType.FullName);
		if (!isSelected
		    && destinationIndex.TypeFullNames.Contains(sourceType.FullName)
		    && !destinationIndex.IsImportedFromSourceMap(sourceType.FullName, sourceMapName))
		{
			summary.SkippedExistingDependencies.Add(sourceType.FullName);
			return;
		}

		orderedTypes.Add(sourceType);

		foreach (string identifier in sourceType.ReferencedIdentifiers)
		{
			SourceType? dependency = ResolveDependency(identifier, sourceType, sourceIndex);
			if (dependency == null || dependency.FullName == sourceType.FullName) continue;
			if (!IsTypeImportable(dependency)) continue;

			AddTypeWithDependencies(dependency, sourceMapName, sourceIndex, destinationIndex, selectedFullNames, visitedTypes, orderedTypes, summary);
		}
	}

	private void WriteTypeFile(
		SourceType sourceType,
		string sourceMapName,
		string destinationWorkspacePath,
		DestinationCodeIndex destinationIndex,
		HashSet<string> importedNamespaces,
		bool isSelected,
		MapCodeImportSummary summary)
	{
		IReadOnlyList<string> existingPaths = destinationIndex.GetTypeFilePaths(sourceType.FullName);
		string relativePath = existingPaths.Count > 0
			? existingPaths[0]
			: GetTargetRelativePath(sourceMapName, sourceType);
		string destinationPath = GetAbsolutePath(destinationWorkspacePath, relativePath);
		string fileContent = BuildFileContent(sourceType, sourceMapName, destinationIndex.Namespaces, importedNamespaces);

		var syntaxErrors = CSharpSyntaxTree.ParseText(fileContent)
			.GetDiagnostics()
			.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
			.ToList();

		if (syntaxErrors.Count > 0)
		{
			summary.FailedTypes.Add($"{sourceType.FullName} ({syntaxErrors[0].GetMessage()})");
			return;
		}

		try
		{
			bool fileExisted = File.Exists(destinationPath);
			bool contentChanged = !fileExisted || !string.Equals(File.ReadAllText(destinationPath), fileContent, StringComparison.Ordinal);

			if (contentChanged)
			{
				string? destinationDirectory = Path.GetDirectoryName(destinationPath);
				if (!string.IsNullOrEmpty(destinationDirectory))
				{
					Directory.CreateDirectory(destinationDirectory);
				}

				File.WriteAllText(destinationPath, fileContent);
			}

			foreach (string duplicatePath in existingPaths.Where(path => !string.Equals(path, relativePath, StringComparison.OrdinalIgnoreCase)))
			{
				try
				{
					File.Delete(GetAbsolutePath(destinationWorkspacePath, duplicatePath));
				}
				catch (Exception exception)
				{
					GameLog.LogError($"[MapCodeImportService] Failed to remove duplicate declaration file '{duplicatePath}': {exception.Message}");
				}
			}

			if (isSelected)
			{
				summary.ImportedTypes.Add(sourceType.FullName);
			}
			else
			{
				summary.ImportedDependencies.Add(sourceType.FullName);
			}

			if (fileExisted && contentChanged)
			{
				summary.ReplacedFiles.Add(relativePath);
			}

			foreach (string duplicatePath in existingPaths.Where(path => !string.Equals(path, relativePath, StringComparison.OrdinalIgnoreCase)))
			{
				summary.ReplacedFiles.Add(duplicatePath);
			}
		}
		catch (Exception exception)
		{
			summary.FailedTypes.Add($"{sourceType.FullName} ({exception.Message})");
			GameLog.LogError($"[MapCodeImportService] Failed to write '{destinationPath}': {exception.Message}");
		}
	}

	private static string BuildFileContent(
		SourceType sourceType,
		string sourceMapName,
		HashSet<string> destinationNamespaces,
		HashSet<string> importedNamespaces)
	{
		var usingLines = new SortedSet<string>(StringComparer.Ordinal);

		foreach (TypePart part in sourceType.Parts)
		{
			foreach (UsingDirectiveSyntax usingDirective in part.Usings)
			{
				string? usingLine = BuildUsingLine(usingDirective, destinationNamespaces, importedNamespaces);
				if (usingLine != null)
				{
					usingLines.Add(usingLine);
				}
			}
		}

		if (!IsNamespaceWithin(sourceType.NamespaceName, AttributionNamespace))
		{
			usingLines.Add($"using {AttributionNamespace};");
		}

		var builder = new StringBuilder();
		foreach (string usingLine in usingLines)
		{
			builder.Append(usingLine).Append('\n');
		}

		if (usingLines.Count > 0)
		{
			builder.Append('\n');
		}

		if (!string.IsNullOrEmpty(sourceType.NamespaceName))
		{
			builder.Append($"namespace {sourceType.NamespaceName};\n\n");
		}

		for (int index = 0; index < sourceType.Parts.Count; index++)
		{
			builder.Append(BuildDeclaration(sourceType.Parts[index], sourceMapName));
			if (index < sourceType.Parts.Count - 1)
			{
				builder.Append("\n\n");
			}
		}

		builder.Append('\n');
		return builder.ToString();
	}

	private static string? BuildUsingLine(UsingDirectiveSyntax usingDirective, HashSet<string> destinationNamespaces, HashSet<string> importedNamespaces)
	{
		string target = usingDirective.NamespaceOrType?.ToString() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(target)) return null;

		bool isAlias = usingDirective.Alias != null;
		bool isStatic = usingDirective.StaticKeyword.IsKind(SyntaxKind.StaticKeyword);

		if (!IsUsingTargetAvailable(target, isAlias || isStatic, destinationNamespaces, importedNamespaces))
		{
			return null;
		}

		if (isAlias)
		{
			return $"using {usingDirective.Alias!.Name.Identifier.ValueText} = {target};";
		}

		return isStatic ? $"using static {target};" : $"using {target};";
	}

	private static bool IsUsingTargetAvailable(
		string target,
		bool isTypeTarget,
		HashSet<string> destinationNamespaces,
		HashSet<string> importedNamespaces)
	{
		if (RetainedUsingPrefixes.Any(prefix => target.Equals(prefix, StringComparison.Ordinal)
		                                        || target.StartsWith(prefix + ".", StringComparison.Ordinal)))
		{
			return true;
		}

		if (!isTypeTarget)
		{
			return NamespaceExistsOrHasDescendant(target, destinationNamespaces)
			       || NamespaceExistsOrHasDescendant(target, importedNamespaces);
		}

		int lastDotIndex = target.LastIndexOf('.');
		string typeNamespace = lastDotIndex > 0 ? target.Substring(0, lastDotIndex) : string.Empty;

		return string.IsNullOrEmpty(typeNamespace)
		       || NamespaceExistsOrHasDescendant(typeNamespace, destinationNamespaces)
		       || NamespaceExistsOrHasDescendant(typeNamespace, importedNamespaces);
	}

	private static bool NamespaceExistsOrHasDescendant(string namespaceName, HashSet<string> namespaces)
	{
		if (namespaces.Contains(namespaceName)) return true;

		foreach (string existingNamespace in namespaces)
		{
			if (existingNamespace.StartsWith(namespaceName + ".", StringComparison.Ordinal))
			{
				return true;
			}
		}

		return false;
	}

	private static string BuildDeclaration(TypePart part, string sourceMapName)
	{
		if (!SupportsSourceMapAttribute(part.Declaration))
		{
			return part.Declaration.ToFullString().Trim();
		}

		var existingSourceMaps = new List<string>();
		SyntaxNode declaration = part.Declaration;
		SyntaxList<AttributeListSyntax> attributeLists = GetAttributeLists(declaration);

		if (attributeLists.Count > 0)
		{
			var keptAttributeLists = new List<AttributeListSyntax>();
			foreach (AttributeListSyntax attributeList in attributeLists)
			{
				var keptAttributes = new List<AttributeSyntax>();
				foreach (AttributeSyntax attribute in attributeList.Attributes)
				{
					if (IsSourceMapAttribute(attribute))
					{
						string? value = ReadSourceMapAttributeValue(attribute);
						if (!string.IsNullOrWhiteSpace(value))
						{
							existingSourceMaps.Add(value);
						}

						continue;
					}

					keptAttributes.Add(attribute);
				}

				if (keptAttributes.Count == 0) continue;

				keptAttributeLists.Add(keptAttributes.Count == attributeList.Attributes.Count
					? attributeList
					: attributeList.WithAttributes(SyntaxFactory.SeparatedList(keptAttributes)));
			}

			declaration = WithAttributeLists(declaration, SyntaxFactory.List(keptAttributeLists));
		}

		string rawLeadingTrivia = part.Declaration.GetLeadingTrivia().ToFullString();
		string declarationText = declaration.ToFullString();
		string leadingTrivia = rawLeadingTrivia.TrimStart('\r', '\n').TrimEnd();

		if (rawLeadingTrivia.Length > 0 && declarationText.StartsWith(rawLeadingTrivia, StringComparison.Ordinal))
		{
			declarationText = declarationText.Substring(rawLeadingTrivia.Length);
		}

		if (leadingTrivia.Length > 0)
		{
			leadingTrivia += "\n";
		}

		var sourceMapNames = new List<string> { sourceMapName };
		foreach (string existingSourceMap in existingSourceMaps)
		{
			if (!sourceMapNames.Contains(existingSourceMap, StringComparer.OrdinalIgnoreCase))
			{
				sourceMapNames.Add(existingSourceMap);
			}
		}

		var builder = new StringBuilder();
		builder.Append(leadingTrivia);
		foreach (string sourceMapNameValue in sourceMapNames)
		{
			builder.Append('[')
				.Append(SourceMapAttributeUsageName)
				.Append('(')
				.Append(SymbolDisplay.FormatLiteral(sourceMapNameValue, quote: true))
				.Append(")]\n");
		}

		builder.Append(declarationText.Trim());
		return builder.ToString();
	}

	private static bool SupportsSourceMapAttribute(SyntaxNode declaration)
	{
		if (declaration is not TypeDeclarationSyntax typeDeclaration) return false;
		if (typeDeclaration.Keyword.IsKind(SyntaxKind.ClassKeyword)) return true;

		if (typeDeclaration is RecordDeclarationSyntax recordDeclaration)
		{
			return recordDeclaration.ClassOrStructKeyword.IsKind(SyntaxKind.None)
			       || recordDeclaration.ClassOrStructKeyword.IsKind(SyntaxKind.ClassKeyword);
		}

		return false;
	}

	public static SyntaxList<AttributeListSyntax> GetAttributeLists(SyntaxNode declaration)
	{
		return declaration switch
		{
			BaseTypeDeclarationSyntax typeDeclaration => typeDeclaration.AttributeLists,
			DelegateDeclarationSyntax delegateDeclaration => delegateDeclaration.AttributeLists,
			_ => default
		};
	}

	private static SyntaxNode WithAttributeLists(SyntaxNode declaration, SyntaxList<AttributeListSyntax> attributeLists)
	{
		return declaration switch
		{
			BaseTypeDeclarationSyntax typeDeclaration => typeDeclaration.WithAttributeLists(attributeLists),
			DelegateDeclarationSyntax delegateDeclaration => delegateDeclaration.WithAttributeLists(attributeLists),
			_ => declaration
		};
	}

	private static bool IsSourceMapAttribute(AttributeSyntax attribute)
	{
		string attributeName = GetAttributeTypeName(attribute);
		return attributeName.Equals(SourceMapAttributeUsageName, StringComparison.Ordinal)
		       || attributeName.Equals(SourceMapAttributeTypeName, StringComparison.Ordinal);
	}

	public static string GetAttributeTypeName(AttributeSyntax attribute)
	{
		string text = attribute.Name.ToString();
		int lastDotIndex = text.LastIndexOf('.');
		return lastDotIndex >= 0 ? text.Substring(lastDotIndex + 1) : text;
	}

	private static string? ReadSourceMapAttributeValue(AttributeSyntax attribute)
	{
		AttributeArgumentListSyntax? argumentList = attribute.ArgumentList;
		if (argumentList == null || argumentList.Arguments.Count == 0) return null;

		if (argumentList.Arguments[0].Expression is not LiteralExpressionSyntax literalExpression) return null;
		if (!literalExpression.IsKind(SyntaxKind.StringLiteralExpression)) return null;

		string value = literalExpression.Token.ValueText;
		return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
	}

	private static SourceType? ResolveDependency(string identifier, SourceType owner, SourceCodeIndex sourceIndex)
	{
		if (!sourceIndex.TypesBySimpleName.TryGetValue(identifier, out List<SourceType>? candidates)) return null;

		SourceType? bestMatch = null;
		int bestScore = -1;

		foreach (SourceType candidate in candidates)
		{
			int score = GetDependencyScore(candidate, owner);
			if (score > bestScore)
			{
				bestScore = score;
				bestMatch = candidate;
			}
		}

		return bestMatch;
	}

	private static int GetDependencyScore(SourceType candidate, SourceType owner)
	{
		if (candidate.NamespaceName == owner.NamespaceName)
		{
			return 1000;
		}

		if (candidate.NamespaceName.Length == 0)
		{
			return 100;
		}

		if (owner.NamespaceName.StartsWith(candidate.NamespaceName + ".", StringComparison.Ordinal))
		{
			return 500 + candidate.NamespaceName.Length;
		}

		if (owner.UsingNamespaces.Contains(candidate.NamespaceName))
		{
			return 300 + candidate.NamespaceName.Length;
		}

		return -1;
	}





	private static DestinationCodeIndex ScanDestination(string destinationWorkspacePath)
	{
		var destinationIndex = new DestinationCodeIndex();
		if (string.IsNullOrEmpty(destinationWorkspacePath) || !Directory.Exists(destinationWorkspacePath)) return destinationIndex;

		foreach (string filePath in CodeWorkspaceFiles.EnumerateCodeFiles(destinationWorkspacePath, includeTemplateFiles: true))
		{
			try
			{
				SyntaxNode root = CSharpSyntaxTree.ParseText(File.ReadAllText(filePath)).GetRoot();

				foreach (BaseNamespaceDeclarationSyntax namespaceDeclaration in root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
				{
					destinationIndex.Namespaces.Add(GetFullNamespaceName(namespaceDeclaration));
				}

				foreach (MemberDeclarationSyntax member in root.DescendantNodes().OfType<MemberDeclarationSyntax>())
				{
					if (member is not BaseTypeDeclarationSyntax and not DelegateDeclarationSyntax) continue;

					string fullName = GetFullTypeName(member);
					if (string.IsNullOrEmpty(fullName)) continue;

					destinationIndex.TypeFullNames.Add(fullName);
					destinationIndex.AddTypeFile(fullName, Path.GetRelativePath(destinationWorkspacePath, filePath).Replace('\\', '/'));

					foreach (AttributeListSyntax attributeList in GetAttributeLists(member))
					{
						foreach (AttributeSyntax attribute in attributeList.Attributes)
						{
							if (!IsSourceMapAttribute(attribute)) continue;

							string? sourceMapName = ReadSourceMapAttributeValue(attribute);
							if (string.IsNullOrWhiteSpace(sourceMapName)) continue;

							destinationIndex.AddSourceMapAttribution(fullName, sourceMapName);
						}
					}
				}
			}
			catch (Exception exception)
			{
				GameLog.LogError($"[MapCodeImportService] Failed to scan destination file '{filePath}': {exception.Message}");
			}
		}

		return destinationIndex;
	}

	private static string GetFullNamespaceName(BaseNamespaceDeclarationSyntax namespaceDeclaration)
	{
		var namespaceNames = new List<string>();
		foreach (BaseNamespaceDeclarationSyntax ancestor in namespaceDeclaration.AncestorsAndSelf().OfType<BaseNamespaceDeclarationSyntax>().Reverse())
		{
			namespaceNames.Add(ancestor.Name.ToString());
		}

		return string.Join(".", namespaceNames);
	}

	private static string GetFullTypeName(MemberDeclarationSyntax member)
	{
		var typeNames = new List<string>();
		foreach (SyntaxNode ancestor in member.AncestorsAndSelf())
		{
			switch (ancestor)
			{
				case BaseTypeDeclarationSyntax ancestorType:
					typeNames.Insert(0, ancestorType.Identifier.ValueText);
					break;
				case DelegateDeclarationSyntax ancestorDelegate:
					typeNames.Insert(0, ancestorDelegate.Identifier.ValueText);
					break;
			}
		}

		string typeName = string.Join(".", typeNames);
		var namespaceNames = new List<string>();
		foreach (BaseNamespaceDeclarationSyntax namespaceDeclaration in member.Ancestors().OfType<BaseNamespaceDeclarationSyntax>())
		{
			namespaceNames.Insert(0, namespaceDeclaration.Name.ToString());
		}

		string namespaceName = string.Join(".", namespaceNames);
		return string.IsNullOrEmpty(namespaceName) ? typeName : $"{namespaceName}.{typeName}";
	}



	private static string GetTargetRelativePath(string sourceMapName, SourceType sourceType)
	{
		string mapFolder = SanitizePathSegment(sourceMapName);
		string namespaceFolder = string.Join("/", sourceType.NamespaceName
			.Split('.', StringSplitOptions.RemoveEmptyEntries)
			.Select(SanitizePathSegment)
			.Where(segment => segment.Length > 0));

		string baseFolder = string.IsNullOrEmpty(namespaceFolder)
			? $"{ImportsRootFolder}/{mapFolder}"
			: $"{ImportsRootFolder}/{mapFolder}/{namespaceFolder}";

		return $"{baseFolder}/{SanitizePathSegment(sourceType.Name)}.cs";
	}

	private static string SanitizePathSegment(string value)
	{
		if (string.IsNullOrWhiteSpace(value)) return "_";

		var builder = new StringBuilder(value.Length);
		char[] invalidCharacters = Path.GetInvalidFileNameChars();

		foreach (char character in value.Trim())
		{
			builder.Append(invalidCharacters.Contains(character) ? '_' : character);
		}

		string sanitized = builder.ToString();
		return sanitized.Length == 0 ? "_" : sanitized;
	}

	public static string GetAbsolutePath(string rootPath, string relativePath)
	{
		return Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
	}

	private static bool IsNamespaceWithin(string namespaceName, string parentNamespace)
	{
		return namespaceName.Equals(parentNamespace, StringComparison.Ordinal)
		       || namespaceName.StartsWith(parentNamespace + ".", StringComparison.Ordinal);
	}

	private static int CompareTypeCandidates(MapCodeTypeCandidate left, MapCodeTypeCandidate right)
	{
		int namespaceComparison = string.Compare(left.NamespaceName, right.NamespaceName, StringComparison.OrdinalIgnoreCase);
		return namespaceComparison != 0
			? namespaceComparison
			: string.Compare(left.ClassName, right.ClassName, StringComparison.OrdinalIgnoreCase);
	}


}

public class MapCodeTypeCandidate
{
	public MapCodeTypeCandidate(
		string namespaceName,
		string className,
		string fullName,
		string kind,
		string relativePath,
		bool willReplace,
		bool isExported = false,
		string exportDisplayName = "",
		string exportCategory = "")
	{
		NamespaceName = namespaceName;
		ClassName = className;
		FullName = fullName;
		Kind = kind;
		RelativePath = relativePath;
		WillReplace = willReplace;
		IsExported = isExported;
		ExportDisplayName = exportDisplayName;
		ExportCategory = exportCategory;
	}

	public string NamespaceName { get; }
	public string ClassName { get; }
	public string FullName { get; }
	public string Kind { get; }
	public string RelativePath { get; }
	public bool WillReplace { get; }
	public bool IsExported { get; }
	public string ExportDisplayName { get; }
	public string ExportCategory { get; }

	public string SelectionKey => FullName;
	public string DisplayName => string.IsNullOrEmpty(NamespaceName) ? ClassName : $"{NamespaceName}.{ClassName}";

	public string ExportLabel
	{
		get
		{
			string exportName = !string.IsNullOrWhiteSpace(ExportDisplayName) ? ExportDisplayName : ClassName;
			return !string.IsNullOrWhiteSpace(ExportCategory) ? $"[{ExportCategory}] {exportName}" : exportName;
		}
	}
}

public class MapCodeNamespaceCandidate
{
	public MapCodeNamespaceCandidate(string namespaceName, int typeCount, IReadOnlyList<string> typeFullNames, IReadOnlyList<string> targetRelativePaths, bool willReplace)
	{
		NamespaceName = namespaceName;
		TypeCount = typeCount;
		TypeFullNames = typeFullNames;
		TargetRelativePaths = targetRelativePaths;
		WillReplace = willReplace;
	}

	public string NamespaceName { get; }
	public int TypeCount { get; }
	public IReadOnlyList<string> TypeFullNames { get; }
	public IReadOnlyList<string> TargetRelativePaths { get; }
	public bool WillReplace { get; }

	public string SelectionKey => NamespaceName.Length == 0 ? "<global>" : NamespaceName;
	public string DisplayName => NamespaceName.Length == 0 ? "(global namespace)" : NamespaceName;
}

public class MapCodeImportSummary
{
	public List<string> ImportedTypes { get; } = new();
	public List<string> ImportedDependencies { get; } = new();
	public List<string> ReplacedFiles { get; } = new();
	public List<string> SkippedExistingDependencies { get; } = new();
	public List<string> FailedTypes { get; } = new();
}

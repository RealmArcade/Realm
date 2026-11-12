using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using GdUnit4;
using Godot;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.UI.MapEditor;
using Realm.Shared.Distribution;
using Environment = System.Environment;

namespace Realm.Client.Tests;

/// <summary>the temporary workspaces, file helpers and private-member access.</summary>
public partial class MapImportVerificationTests
	{
	private static MapAssetImportSource CreateSourceMap(string root, string title, string version, JsonObject metadata, IReadOnlyDictionary<string, string> sourceFiles)
	{
		string mapPath = Path.Combine(root, "source_map");
		Directory.CreateDirectory(mapPath);
		File.WriteAllText(Path.Combine(mapPath, "metadata.json"), metadata.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

		var manifest = new MapManifest { MapName = title, Version = version };
		manifest.Files["metadata.json"] = "verification-hash";

		foreach (KeyValuePair<string, string> sourceFile in sourceFiles)
		{
			WriteTextFile(mapPath, sourceFile.Key, sourceFile.Value);
			manifest.Files[sourceFile.Key] = "verification-hash";
		}

		string manifestPath = Path.Combine(mapPath, "manifest.json");
		manifest.SaveToFile(manifestPath);
		return new MapAssetImportSource(title, version, manifestPath);
	}

	private static string CreateDestinationWorkspace(string root)
	{
		string destinationPath = Path.Combine(root, "destination_map");
		Directory.CreateDirectory(destinationPath);
		File.WriteAllText(Path.Combine(destinationPath, "metadata.json"), BuildEmptyMetadata().ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
		return destinationPath;
	}

	private static string CreateTempRoot(string prefix)
	{
		string root = Path.Combine(Path.GetTempPath(), "realm_import_verification", $"{prefix}_{Guid.NewGuid():N}");
		Directory.CreateDirectory(root);
		return root;
	}

	private static void DeleteTempRoot(string root)
	{
		if (!Directory.Exists(root)) return;

		try
		{
			Directory.Delete(root, true);
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[MapImportVerificationTests] Failed to remove '{root}': {exception.Message}");
		}
	}

	private static void WriteTextFile(string root, string relativePath, string content)
	{
		string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
		string? directory = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(directory))
		{
			Directory.CreateDirectory(directory);
		}

		File.WriteAllText(path, content);
	}

	private static JsonObject ReadJsonObject(string path)
	{
		return JsonNode.Parse(File.ReadAllText(path))!.AsObject();
	}

	private static List<string> GetSourceMapAttributeValues(string path)
	{
		SyntaxNode root = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();
		return root.DescendantNodes()
			.OfType<Microsoft.CodeAnalysis.CSharp.Syntax.AttributeSyntax>()
			.Where(attribute => attribute.Name.ToString().EndsWith("Attribution_SourceMap", StringComparison.Ordinal)
			                    || attribute.Name.ToString().EndsWith("Attribution_SourceMapAttribute", StringComparison.Ordinal))
			.Select(attribute => attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression.ToString() ?? string.Empty)
			.ToList();
	}

	private static IReadOnlyList<string> CompileWorkspaceSources(string workspacePath)
	{
		var sourcePaths = new List<string>();
		string importsPath = Path.Combine(workspacePath, "imports");
		if (Directory.Exists(importsPath))
		{
			sourcePaths.AddRange(Directory.GetFiles(importsPath, "*.cs", SearchOption.AllDirectories));
		}

		string attributePath = Path.Combine(workspacePath, "Attribution_SourceMapAttribute.cs");
		if (File.Exists(attributePath))
		{
			sourcePaths.Add(attributePath);
		}

		var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
		IEnumerable<SyntaxTree> syntaxTrees = sourcePaths.Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), parseOptions, path));

		var referencePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trustedPlatformAssemblies)
		{
			foreach (string referencePath in trustedPlatformAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
			{
				if (File.Exists(referencePath))
				{
					referencePaths.Add(referencePath);
				}
			}
		}

		referencePaths.Add(typeof(object).Assembly.Location);
		referencePaths.Add(typeof(List<>).Assembly.Location);
		referencePaths.Add(typeof(Enumerable).Assembly.Location);
		string mapApiAssemblyPath = Path.Combine(AppContext.BaseDirectory, "MapTemplate", "lib", "Realm.MapAPI.dll");
		if (File.Exists(mapApiAssemblyPath)) referencePaths.Add(mapApiAssemblyPath);

		IEnumerable<MetadataReference> references = referencePaths.Select(referencePath => MetadataReference.CreateFromFile(referencePath));
		CSharpCompilation compilation = CSharpCompilation.Create(
			"MapImportVerification",
			syntaxTrees,
			references,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

		return compilation.GetDiagnostics()
			.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
			.Select(diagnostic => diagnostic.ToString())
			.ToList();
	}

	private static T GetPrivateField<T>(object instance, string fieldName)
	{
		FieldInfo? field = instance.GetType().GetField(fieldName, PrivateInstanceFlags);
		if (field == null) throw new InvalidOperationException($"Private field '{fieldName}' was not found on {instance.GetType().Name}.");

		return (T)field.GetValue(instance)!;
	}

	private static void InvokePrivate(object instance, string methodName)
	{
		MethodInfo? method = instance.GetType().GetMethod(methodName, PrivateInstanceFlags);
		if (method == null) throw new InvalidOperationException($"Private method '{methodName}' was not found on {instance.GetType().Name}.");

		method.Invoke(instance, null);
	}

	private static void SetSearchText(LineEdit search, string text)
	{
		search.Set("text", text);
		search.EmitSignal(LineEdit.SignalName.TextChanged, Variant.From(text));
	}

	private static void SelectOption(OptionButton optionButton, int index)
	{
		optionButton.Select(index);
		optionButton.EmitSignal(OptionButton.SignalName.ItemSelected, Variant.From(index));
	}

	private static VBoxContainer GetRowList(ImportFromMapDialog dialog)
	{
		return dialog.FindChild("AssetList_VBoxContainer", true, false) as VBoxContainer
		       ?? throw new InvalidOperationException("Import dialog row list is missing.");
	}

	private static List<string> GetRowLabels(VBoxContainer rowList)
	{
		var labels = new List<string>();
		foreach (Node child in rowList.GetChildren())
		{
			if (child is CheckBox checkBox)
			{
				labels.Add(checkBox.Text);
			}
		}

		return labels;
	}
}

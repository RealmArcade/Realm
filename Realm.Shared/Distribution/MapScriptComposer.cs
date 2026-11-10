using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Realm.Shared.Distribution;

/// <summary>
///     One referenced map whose C# sources are compiled into the referencing map's script.
/// </summary>
public sealed class ComposedScriptMap
{
	public string MapName { get; init; } = string.Empty;

	public string Folder { get; init; } = string.Empty;

	public string Namespace { get; init; } = string.Empty;

	public bool DeclaresEntryScript { get; init; }
}

/// <summary>
///     Brings the C# of the maps a map references into that map's own workspace so one compilation produces one
///     assembly carrying every script, which is what lets a parent composition call into the rules a referenced map
///     states instead of only reading the JSON that map ships.
/// </summary>
/// <remarks>
///     Sources are parsed with Roslyn and rewritten into <c>composed/&lt;Map&gt;/</c> with their namespace moved to
///     <c>Realm.Maps.Composed.&lt;Map&gt;</c>, so two maps that both declare <c>Realm.Maps.MapScript</c> still compile
///     beside each other and beside the parent's own entry script. A composed type is never sealed, and a member a map
///     declared <c>private</c> is widened to the most capable accessibility its context accepts, so the map that composed
///     it can reach and override what that map wrote without the referenced folder being edited.
///     <para />
///     A <c>MapComposition.g.cs</c> registry is written beside the parent's script naming each composed entry class, so a
///     parent can forward the lifecycle without hard-coding the maps it references. A parent that wants more than the
///     default forwarding instantiates the composed types itself, which is why the registry is a convenience rather than
///     the only way in.
///     <para />
///     Composition is one layer deep. A referenced map's own references are not followed: a composed map is a leaf, and
///     its sources are compiled as the map states them.
/// </remarks>
public static class MapScriptComposer
{
	public const string ComposedDirectoryName = "composed";

	public const string RegistryFileName = "MapComposition.g.cs";

	public const string RootNamespace = "Realm.Maps";

	public const string ComposedNamespaceRoot = "Realm.Maps.Composed";

	private static readonly string[] SkippedDirectoryPrefixes =
	{
		"bin/",
		"obj/",
		"lib/",
		"wit/",
		".vscode/",
		".git/",
		ComposedDirectoryName + "/"
	};

	private static readonly string[] SkippedFileNames =
	{
		"WasmEntryPoint.g.cs",
		"WasmEntryPoint.cs"
	};

	public static string GetNamespaceFor(string mapName)
	{
		return $"{ComposedNamespaceRoot}.{ToIdentifier(mapName)}";
	}

	/// <summary>
	///     Composes the maps a folder references into that folder.
	/// </summary>
	/// <param name="mapFolder">The map folder whose script composes the referenced maps.</param>
	/// <param name="references">
	///     The maps it references, or nothing to remove what an earlier composition left, which is what a folder that no
	///     longer references anything needs.
	/// </param>
	/// <param name="folderLookup">Finds the folder a reference is read from.</param>
	/// <returns>
	///     One entry per composed map, naming the namespace its sources now occupy, or nothing when a reference could not be
	///     found: the composed sources already in the folder are then the last set that did resolve and are left exactly as
	///     they are, rather than replaced by a composition missing the map that could not be read.
	/// </returns>
	public static List<ComposedScriptMap>? Compose(
		string mapFolder,
		IEnumerable<MapReference>? references,
		Func<MapReference, string?>? folderLookup)
	{
		var composed = new List<ComposedScriptMap>();

		string composedDirectory = Path.Combine(mapFolder, ComposedDirectoryName);

		if (references == null)
		{
			DeleteDirectory(composedDirectory);
			WriteRegistry(mapFolder, composed);
			return composed;
		}

		var ordered = new List<MapReference>(references);
		ordered.Sort((left, right) => string.Compare(left.MapName, right.MapName, StringComparison.OrdinalIgnoreCase));

		var resolved = new List<(MapReference Reference, string Folder)>(ordered.Count);
		foreach (MapReference reference in ordered)
		{
			string folder = folderLookup?.Invoke(reference);
			if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
			{
				return null;
			}

			resolved.Add((reference, folder));
		}

		DeleteDirectory(composedDirectory);

		foreach ((MapReference reference, string folder) in resolved)
		{
			string mapNamespace = GetNamespaceFor(reference.MapName);
			string destinationDirectory = Path.Combine(composedDirectory, ToIdentifier(reference.MapName));

			string[] sourceFiles;
			try
			{
				sourceFiles = Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories);
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				continue;
			}

			bool declaresEntry = false;
			foreach (string sourcePath in sourceFiles)
			{
				string relativePath = ToRelativePath(folder, sourcePath);
				if (string.IsNullOrEmpty(relativePath) || IsSkipped(relativePath))
				{
					continue;
				}

				string sourceText;
				try
				{
					sourceText = File.ReadAllText(sourcePath);
				}
				catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
				{
					continue;
				}

				MapSourceRewriteResult rewritten = MapSourceRewriter.Rewrite(sourceText, mapNamespace);
				if (!declaresEntry && rewritten.DeclaresEntryScript)
				{
					declaresEntry = true;
				}

				string destinationPath = Path.Combine(destinationDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
				string destinationFolder = Path.GetDirectoryName(destinationPath);
				if (!string.IsNullOrEmpty(destinationFolder))
				{
					Directory.CreateDirectory(destinationFolder);
				}

				MapReferenceResolver.ClearReadOnly(destinationPath);
				WriteWhenDifferent(destinationPath, rewritten.Source);
			}

			composed.Add(new ComposedScriptMap
			{
				MapName = reference.MapName,
				Folder = destinationDirectory,
				Namespace = mapNamespace,
				DeclaresEntryScript = declaresEntry
			});
		}

		WriteRegistry(mapFolder, composed);
		return composed;
	}

	/// <summary>
	///     Turns a map name into the single identifier its composed namespace is built from, so "Melee Ruleset" and
	///     "melee-ruleset" name one namespace instead of two.
	/// </summary>
	/// <param name="mapName">The map name as the reference states it.</param>
	/// <returns>A C# identifier built from the words of the name.</returns>
	public static string ToIdentifier(string mapName)
	{
		if (string.IsNullOrWhiteSpace(mapName))
		{
			return "Map";
		}

		var builder = new StringBuilder(mapName.Length);
		bool startOfWord = true;
		foreach (char character in mapName)
		{
			if (!char.IsLetterOrDigit(character))
			{
				startOfWord = true;
				continue;
			}

			builder.Append(startOfWord ? char.ToUpperInvariant(character) : character);
			startOfWord = false;
		}

		if (builder.Length == 0)
		{
			return "Map";
		}

		if (char.IsDigit(builder[0]))
		{
			builder.Insert(0, "Map");
		}

		return builder.ToString();
	}

	private static void WriteRegistry(string mapFolder, List<ComposedScriptMap> composed)
	{
		string registryPath = Path.Combine(mapFolder, RegistryFileName);
		if (composed.Count == 0)
		{
			MapReferenceResolver.ClearReadOnly(registryPath);
			if (File.Exists(registryPath))
			{
				File.Delete(registryPath);
			}

			return;
		}

		var registry = new StringBuilder();
		registry.AppendLine("// <auto-generated>");
		registry.AppendLine("// This code was generated by Realm MapReferenceResolver.");
		registry.AppendLine("// Changes to this file may cause incorrect behavior and will be lost if the code is regenerated.");
		registry.AppendLine("// </auto-generated>");
		registry.AppendLine();
		registry.AppendLine("#nullable enable");
		registry.AppendLine();
		registry.AppendLine($"namespace {RootNamespace};");
		registry.AppendLine();
		registry.AppendLine("using Realm.MapAPI;");
		registry.AppendLine();
		registry.AppendLine("public static class MapComposition");
		registry.AppendLine("{");
		registry.Append("    public static readonly string[] ComposedMapNames = { ");
		registry.Append(string.Join(", ", composed.Select(map => $"\"{map.MapName.Replace("\"", "\\\"")}\"")));
		registry.AppendLine(" };");
		registry.AppendLine();

		List<ComposedScriptMap> entries = composed.Where(map => map.DeclaresEntryScript).ToList();
		if (entries.Count == 0)
		{
			registry.AppendLine("    public static IMapScript[] CreateScripts() => System.Array.Empty<IMapScript>();");
		}
		else
		{
			registry.AppendLine("    public static IMapScript[] CreateScripts()");
			registry.AppendLine("    {");
			registry.AppendLine("        return new IMapScript[]");
			registry.AppendLine("        {");
			foreach (ComposedScriptMap entry in entries)
			{
				string relativeNamespace = entry.Namespace.StartsWith($"{ComposedNamespaceRoot}.", StringComparison.Ordinal)
					? entry.Namespace.Substring(ComposedNamespaceRoot.Length + 1)
					: entry.Namespace;
				registry.AppendLine($"            new Composed.{relativeNamespace}.MapScript(){(entries.IndexOf(entry) == entries.Count - 1 ? string.Empty : ",")}");
			}

			registry.AppendLine("        };");
			registry.AppendLine("    }");
		}

		registry.AppendLine("}");

		MapReferenceResolver.ClearReadOnly(registryPath);
		WriteWhenDifferent(registryPath, registry.ToString());
	}

	private static bool IsSkipped(string relativePath)
	{
		foreach (string fileName in SkippedFileNames)
		{
			if (string.Equals(relativePath, fileName, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		foreach (string prefix in SkippedDirectoryPrefixes)
		{
			if (relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}

	private static string ToRelativePath(string folder, string filePath)
	{
		string relativePath = Path.GetRelativePath(folder, filePath).Replace('\\', '/');
		return relativePath.StartsWith("../", StringComparison.Ordinal) ? string.Empty : relativePath;
	}

	/// <summary>
	///     Writes a generated file and leaves it read-only, because the save regenerates every composed source and an edit
	///     made to one would be reverted by the next save without ever being reported. A file that is already the content
	///     asked for is neither rewritten nor reopened, so a save does not touch what it did not change.
	/// </summary>
	/// <param name="path">The file to write.</param>
	/// <param name="content">The generated content.</param>
	private static void WriteWhenDifferent(string path, string content)
	{
		if (File.Exists(path) && string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
		{
			SetReadOnly(path);
			return;
		}

		string folder = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(folder))
		{
			Directory.CreateDirectory(folder);
		}

		if (File.Exists(path)) ClearReadOnly(path);
		File.WriteAllText(path, content);
		SetReadOnly(path);
	}

	private static void SetReadOnly(string path)
	{
		try
		{
			FileAttributes attributes = File.GetAttributes(path);
			if ((attributes & FileAttributes.ReadOnly) == 0) File.SetAttributes(path, attributes | FileAttributes.ReadOnly);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
		}
	}

	private static void ClearReadOnly(string path)
	{
		try
		{
			FileAttributes attributes = File.GetAttributes(path);
			if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
		}
	}

	private static void DeleteDirectory(string folder)
	{
		if (!Directory.Exists(folder))
		{
			return;
		}

		try
		{
			foreach (string filePath in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
			{
				MapReferenceResolver.ClearReadOnly(filePath);
			}

			Directory.Delete(folder, true);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
		}
	}
}

using System.Text;

namespace Realm.Shared.Distribution;

/// <summary>
///     What one save of a referencing map did to its folder, so an author is told which files were taken from another map
///     and which of their own edits that replaced.
/// </summary>
public sealed class MapReferenceResolutionResult
{
	public bool Success { get; init; }

	public string Message { get; init; } = string.Empty;

	public bool HasReferences { get; init; }

	public IReadOnlyList<string> CopiedFiles { get; init; } = Array.Empty<string>();

	public IReadOnlyList<string> ReplacedFiles { get; init; } = Array.Empty<string>();

	public IReadOnlyList<string> MetadataOverrides { get; init; } = Array.Empty<string>();

	public IReadOnlyList<string> ConflictingFiles { get; init; } = Array.Empty<string>();

	public static MapReferenceResolutionResult NoReferences()
	{
		return new MapReferenceResolutionResult { Success = true, Message = string.Empty, HasReferences = false };
	}

	public static MapReferenceResolutionResult Failure(string message)
	{
		return new MapReferenceResolutionResult { Success = false, Message = message, HasReferences = true };
	}
}

/// <summary>
///     Applies the maps a map folder references to that folder, which is what makes a referencing map an ordinary map
///     everywhere else: testing, exporting, publishing and playing all read the parent's own folder.
/// </summary>
/// <remarks>
///     The parent's <c>metadata.json</c> is merged by <see cref="MapMetadataMerger" />. Every other file is taken whole
///     from one map: a file only one map carries is simply that map's, and a file the parent and a referenced map both
///     carry is decided by the priority stated for it, or, when none is stated, by keeping the parent's. A collision
///     between two referenced maps that no priority settles is an error, because either choice would silently drop the
///     other map's file.
///     <para />
///     Files taken from a referenced map are made read-only, since an edit to one would be reverted by the next save. They
///     are only rewritten when their bytes differ, so a save of a map nothing changed in copies nothing and leaves every
///     modification time alone.
///     <para />
///     The generated code a workspace compiles with is never taken from a referenced map. <c>MapTemplates.g.cs</c> is
///     written from the map's own merged metadata and <c>WasmEntryPoint.g.cs</c> from the engine's current template, so a
///     referenced map's copy would describe another map's templates or another engine build.
/// </remarks>
public static class MapReferenceResolver
{
	private static readonly string[] SkippedDirectoryPrefixes =
	{
		"bin/",
		"obj/",
		"lib/",
		"wit/",
		".vscode/",
		".git/",
		MapScriptComposer.ComposedDirectoryName + "/"
	};

	private static readonly string[] SkippedFileNames =
	{
		"manifest.json",
		MapReferences.FileName,
		MapMetadataMerger.MetadataPartialFileName
	};

	private static readonly string[] GeneratedFileNames =
	{
		"MapTemplates.g.cs",
		"WasmEntryPoint.g.cs"
	};

	/// <summary>
	///     Applies the references a map folder keeps.
	/// </summary>
	/// <param name="mapFolder">The map folder being saved.</param>
	/// <param name="storeFolderLookup">
	///     Finds the folder a referenced map was imported into, used when the reference names no helper folder or that
	///     folder no longer exists.
	/// </param>
	/// <returns>What the save took from the referenced maps, or why it could not be applied.</returns>
	public static MapReferenceResolutionResult Apply(string mapFolder, Func<MapReference, string?> storeFolderLookup = null)
	{
		if (string.IsNullOrWhiteSpace(mapFolder) || !Directory.Exists(mapFolder))
		{
			return MapReferenceResolutionResult.Failure("The map folder being saved does not exist.");
		}

		MapReferences references = MapReferences.Load(mapFolder);
		if (references == null || references.IsEmpty)
		{
			DiscardComposedOutput(mapFolder);
			return MapReferenceResolutionResult.NoReferences();
		}

		var validation = references.Validate();
		if (!validation.IsValid)
		{
			return MapReferenceResolutionResult.Failure(validation.ErrorMessage);
		}

		var sources = new List<ReferenceSource>(references.References.Count);
		var unresolved = new List<string>();
		foreach (MapReference reference in references.References)
		{
			string folder = ResolveReferenceFolder(reference, storeFolderLookup);
			if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
			{
				unresolved.Add($"{reference.MapName} v{reference.Version}");
				continue;
			}

			sources.Add(new ReferenceSource(reference, folder));
		}

		if (unresolved.Count > 0)
		{
			return MapReferenceResolutionResult.Failure(
				$"The referenced map(s) {string.Join(", ", unresolved)} could not be found, in their helper folder or in the map store. " +
				$"Open the map store and download them, or point the reference at the folder holding the map.");
		}

		var copiedFiles = new List<string>();
		var replacedFiles = new List<string>();
		var conflictingFiles = new List<string>();

		Dictionary<string, List<string>> childFiles = CollectChildFiles(sources);
		List<string> parentFiles = EnumeratePackageFiles(mapFolder);

		var parentPaths = new HashSet<string>(parentFiles, StringComparer.OrdinalIgnoreCase);
		var parentOwnedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string path in parentPaths)
		{
			if (!IsCopiedFromReference(mapFolder, path))
			{
				parentOwnedPaths.Add(path);
			}
		}

		AdoptRootScripts(mapFolder, parentPaths, parentOwnedPaths);

		var everyPath = new HashSet<string>(parentPaths, StringComparer.OrdinalIgnoreCase);
		foreach (string path in childFiles.Keys)
		{
			everyPath.Add(path);
		}

		var orderedPaths = everyPath.ToList();
		orderedPaths.Sort(StringComparer.OrdinalIgnoreCase);

		var plan = new List<ResolvedFile>(orderedPaths.Count);
		foreach (string relativePath in orderedPaths)
		{
			if (string.Equals(relativePath, MapMetadataMerger.MetadataFileName, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			var candidates = new List<string>(2);
			if (IsComposedSource(relativePath))
			{
				continue;
			}

			if (childFiles.TryGetValue(relativePath, out List<string> contributingMaps))
			{
				candidates.AddRange(contributingMaps);
			}

			bool parentOwnsFile = parentOwnedPaths.Contains(relativePath);
			if (parentOwnsFile)
			{
				candidates.Add(string.Empty);
			}

			if (candidates.Count == 0)
			{
				if (parentPaths.Contains(relativePath))
				{
					plan.Add(new ResolvedFile(relativePath, null, null, parentOwned: false));
				}

				continue;
			}

			string chosenMap = ChooseSource(references, relativePath, candidates, out string choiceError);
			if (choiceError.Length > 0)
			{
				conflictingFiles.Add(choiceError);
				continue;
			}

			if (string.IsNullOrEmpty(chosenMap))
			{
				plan.Add(new ResolvedFile(relativePath, null, null, parentOwnsFile));
				continue;
			}

			ReferenceSource chosenSource = sources.First(source => string.Equals(source.Reference.MapName, chosenMap, StringComparison.OrdinalIgnoreCase));
			string sourcePath = Path.Combine(chosenSource.Folder, relativePath.Replace('/', Path.DirectorySeparatorChar));
			if (!File.Exists(sourcePath))
			{
				conflictingFiles.Add($"'{relativePath}' is stated to come from '{chosenMap}', which does not carry it.");
				continue;
			}

			plan.Add(new ResolvedFile(relativePath, chosenMap, sourcePath, parentOwnsFile));
		}

		if (conflictingFiles.Count > 0)
		{
			var conflicts = new StringBuilder();
			conflicts.Append(conflictingFiles.Count == 1
				? "One file is carried by more than one map and no priority decides it: "
				: $"{conflictingFiles.Count} files are carried by more than one map and no priority decides them: ");
			conflicts.Append(string.Join(" ", conflictingFiles.Take(6)));
			conflicts.Append($" State which map keeps each one in the Conflict Resolution dialog before saving.");
			return MapReferenceResolutionResult.Failure(conflicts.ToString());
		}

		foreach (ResolvedFile resolvedFile in plan)
		{
			string destinationPath = Path.Combine(mapFolder, resolvedFile.RelativePath.Replace('/', Path.DirectorySeparatorChar));
			if (resolvedFile.SourcePath == null)
			{
				ClearReadOnly(destinationPath);
				continue;
			}

			if (!CopyWhenDifferent(resolvedFile.SourcePath, destinationPath, out bool replacedExisting))
			{
				SetReadOnly(destinationPath);
				continue;
			}

			SetReadOnly(destinationPath);
			copiedFiles.Add(resolvedFile.RelativePath);
			if (replacedExisting && resolvedFile.ParentOwned)
			{
				replacedFiles.Add(resolvedFile.RelativePath);
			}
		}

		MapReferenceResolutionResult metadataResult = ApplyMetadata(mapFolder, sources);
		if (!metadataResult.Success)
		{
			return metadataResult;
		}

		var sourceFolders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (ReferenceSource source in sources)
		{
			sourceFolders[source.Reference.MapName] = source.Folder;
		}

		List<ComposedScriptMap>? composed = MapScriptComposer.Compose(
			mapFolder,
			sources.Select(source => source.Reference).ToList(),
			reference => sourceFolders.TryGetValue(reference.MapName, out string folder) ? folder : null);
		if (composed == null)
		{
			return MapReferenceResolutionResult.Failure(
				"A referenced map could not be read while its scripts were being composed, so the composed sources already in the map were kept. " +
				"Save again once the map is available.");
		}

		return new MapReferenceResolutionResult
		{
			Success = true,
			HasReferences = true,
			Message = BuildMessage(sources.Count, copiedFiles.Count, replacedFiles.Count, conflictingFiles.Count, composed.Count),
			CopiedFiles = copiedFiles,
			ReplacedFiles = replacedFiles,
			MetadataOverrides = metadataResult.MetadataOverrides,
			ConflictingFiles = conflictingFiles
		};
	}

	/// <summary>
	///     Removes what a composition left behind once a folder references nothing. Its generated sources would otherwise
	///     keep compiling the rules the author just removed, and the files an earlier save copied in would stay read-only
	///     even though no reference decides them any more, which is the same ownership a file a remaining reference stopped
	///     carrying gets.
	/// </summary>
	/// <param name="mapFolder">The map folder that no longer references anything.</param>
	private static void DiscardComposedOutput(string mapFolder)
	{
		string composedDirectory = Path.Combine(mapFolder, MapScriptComposer.ComposedDirectoryName);
		string registryPath = Path.Combine(mapFolder, MapScriptComposer.RegistryFileName);
		if (!Directory.Exists(composedDirectory) && !File.Exists(registryPath))
		{
			return;
		}

		MapScriptComposer.Compose(mapFolder, null, null);

		foreach (string relativePath in EnumeratePackageFiles(mapFolder))
		{
			ClearReadOnly(Path.Combine(mapFolder, relativePath.Replace('/', Path.DirectorySeparatorChar)));
		}
	}

	/// <summary>
	///     Answers whether a path is a C# source, which is composed rather than copied whole.
	/// </summary>
	/// <param name="relativePath">The path inside a map folder.</param>
	/// <returns>True for a <c>.cs</c> file.</returns>
	private static bool IsComposedSource(string relativePath)
	{
		return relativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	///     Takes back the root scripts an earlier save copied in from a referenced map. A copied script is read-only, so
	///     the author never owned it; it now lives under <c>composed/</c> in its own namespace instead, and leaving a copy
	///     at the root would compile the same map twice and could even become the entry script.
	/// </summary>
	/// <param name="mapFolder">The map folder being saved.</param>
	/// <param name="parentPaths">Every content path the folder holds.</param>
	/// <param name="parentOwnedPaths">The paths the parent owns, which this adds the adopted scripts to.</param>
	private static void AdoptRootScripts(string mapFolder, HashSet<string> parentPaths, HashSet<string> parentOwnedPaths)
	{
		foreach (string relativePath in parentPaths.ToList())
		{
			if (!IsComposedSource(relativePath)) continue;

			string path = Path.Combine(mapFolder, relativePath.Replace('/', Path.DirectorySeparatorChar));
			bool copiedFromReference = !parentOwnedPaths.Contains(relativePath);
			ClearReadOnly(path);
			parentOwnedPaths.Add(relativePath);

			if (!copiedFromReference) continue;
			if (string.Equals(relativePath, "MapScript.cs", StringComparison.OrdinalIgnoreCase)) continue;

			try
			{
				File.Delete(path);
				parentPaths.Remove(relativePath);
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
			}
		}
	}

	private static string BuildMessage(int referenceCount, int copiedCount, int replacedCount, int conflictCount, int composedCount)
	{
		var message = new StringBuilder();
		message.Append($"{referenceCount} referenced map(s) applied");
		if (copiedCount > 0)
		{
			message.Append($", {copiedCount} file(s) taken from them");
		}

		if (replacedCount > 0)
		{
			message.Append($", {replacedCount} locally edited file(s) replaced by the referenced copy");
		}

		if (conflictCount > 0)
		{
			message.Append($", {conflictCount} file(s) left undecided");
		}

		if (composedCount > 0)
		{
			message.Append($", {composedCount} script(s) composed");
		}

		message.Append('.');
		return message.ToString();
	}

	private static MapReferenceResolutionResult ApplyMetadata(string mapFolder, List<ReferenceSource> sources)
	{
		var ordered = new List<ReferenceSource>(sources);
		ordered.Sort((left, right) => string.Compare(left.Reference.MapName, right.Reference.MapName, StringComparison.OrdinalIgnoreCase));

		var documents = new List<MapMetadataDocument>(ordered.Count);
		foreach (ReferenceSource source in ordered)
		{
			string metadataPath = Path.Combine(source.Folder, MapMetadataMerger.MetadataFileName);
			if (!File.Exists(metadataPath))
			{
				return MapReferenceResolutionResult.Failure($"The referenced map '{source.Reference.MapName}' has no {MapMetadataMerger.MetadataFileName}.");
			}

			documents.Add(new MapMetadataDocument(source.Reference.MapName, File.ReadAllBytes(metadataPath)));
		}

		string parentMetadataPath = Path.Combine(mapFolder, MapMetadataMerger.MetadataFileName);
		byte[] parentMetadata = File.Exists(parentMetadataPath) ? File.ReadAllBytes(parentMetadataPath) : Array.Empty<byte>();

		MapMetadataMergeResult mergeResult = MapMetadataMerger.Merge(documents, parentMetadata);
		if (!mergeResult.Success)
		{
			return MapReferenceResolutionResult.Failure(mergeResult.ErrorMessage);
		}

		if (!MapMetadataMerger.VerifyIdempotent(documents, mergeResult.ParentIntentBytes, mergeResult.MergedBytes, out string idempotencyError))
		{
			return MapReferenceResolutionResult.Failure(idempotencyError);
		}

		string partialPath = Path.Combine(mapFolder, MapMetadataMerger.MetadataPartialFileName);
		WriteBytesWhenDifferent(partialPath, mergeResult.ParentIntentBytes);
		WriteBytesWhenDifferent(parentMetadataPath, mergeResult.MergedBytes);

		return new MapReferenceResolutionResult
		{
			Success = true,
			HasReferences = true,
			MetadataOverrides = mergeResult.OverriddenKeys
		};
	}

	private static string ChooseSource(MapReferences references, string relativePath, List<string> candidates, out string errorMessage)
	{
		errorMessage = string.Empty;

		if (candidates.Count == 1)
		{
			return candidates[0];
		}

		bool hasPriority = references.TryGetConflictPriority(relativePath, out string priorityMapName);
		if (hasPriority)
		{
			if (string.IsNullOrEmpty(priorityMapName))
			{
				if (candidates.Contains(string.Empty))
				{
					return string.Empty;
				}

				errorMessage = $"'{relativePath}' is stated to be kept by this map, which no longer carries it.";
				return string.Empty;
			}

			if (candidates.Contains(priorityMapName, StringComparer.OrdinalIgnoreCase))
			{
				return candidates.First(candidate => string.Equals(candidate, priorityMapName, StringComparison.OrdinalIgnoreCase));
			}

			errorMessage = $"'{relativePath}' is stated to come from '{priorityMapName}', which no longer carries it.";
			return string.Empty;
		}

		if (candidates.Contains(string.Empty))
		{
			return string.Empty;
		}

		errorMessage = $"'{relativePath}' is carried by {string.Join(" and ", candidates)} with no priority stated for it.";
		return string.Empty;
	}

	private static Dictionary<string, List<string>> CollectChildFiles(List<ReferenceSource> sources)
	{
		var files = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		foreach (ReferenceSource source in sources)
		{
			foreach (string relativePath in EnumeratePackageFiles(source.Folder))
			{
				if (!files.TryGetValue(relativePath, out List<string> contributors))
				{
					contributors = new List<string>(1);
					files[relativePath] = contributors;
				}

				contributors.Add(source.Reference.MapName);
			}
		}

		foreach (List<string> contributors in files.Values)
		{
			contributors.Sort(StringComparer.OrdinalIgnoreCase);
		}

		return files;
	}

	/// <summary>
	///     Lists the files a map folder carries as content, which is everything outside its build, its history and the
	///     files the reference mechanism itself owns.
	/// </summary>
	/// <param name="folder">The map folder.</param>
	/// <returns>Every relative path of the folder, in no particular order.</returns>
	public static List<string> EnumeratePackageFiles(string folder)
	{
		var found = new List<string>();
		if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
		{
			return found;
		}

		string[] filePaths;
		try
		{
			filePaths = Directory.GetFiles(folder, "*", SearchOption.AllDirectories);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return found;
		}

		foreach (string filePath in filePaths)
		{
			string relativePath = MapReferences.NormalizeRelativePath(Path.GetRelativePath(folder, filePath));
			if (string.IsNullOrEmpty(relativePath))
			{
				continue;
			}

			if (IsSkipped(relativePath))
			{
				continue;
			}

			found.Add(relativePath);
		}

		found.Sort(StringComparer.OrdinalIgnoreCase);
		return found;
	}

	public static bool IsSkipped(string relativePath)
	{
		foreach (string fileName in SkippedFileNames)
		{
			if (string.Equals(relativePath, fileName, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		foreach (string fileName in GeneratedFileNames)
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

	private static string ResolveReferenceFolder(MapReference reference, Func<MapReference, string?> storeFolderLookup)
	{
		if (!string.IsNullOrWhiteSpace(reference.FolderPath))
		{
			string helperFolder = reference.FolderPath.Replace('\\', Path.DirectorySeparatorChar);
			if (Directory.Exists(helperFolder) &&
			    (File.Exists(Path.Combine(helperFolder, MapMetadataMerger.MetadataFileName)) ||
			     File.Exists(Path.Combine(helperFolder, "manifest.json"))))
			{
				return helperFolder;
			}
		}

		return storeFolderLookup?.Invoke(reference);
	}

	/// <summary>
	///     Answers whether the file at one path was copied in by an earlier save, which a read-only attribute marks:
	///     the author cannot edit such a file, so it is the referenced map's content and not the parent's own.
	/// </summary>
	private static bool IsCopiedFromReference(string mapFolder, string relativePath)
	{
		string path = Path.Combine(mapFolder, relativePath.Replace('/', Path.DirectorySeparatorChar));
		try
		{
			return File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return false;
		}
	}

	private static bool CopyWhenDifferent(string sourcePath, string destinationPath, out bool replacedExisting)
	{
		replacedExisting = false;

		string destinationDirectory = Path.GetDirectoryName(destinationPath);
		if (!string.IsNullOrEmpty(destinationDirectory))
		{
			Directory.CreateDirectory(destinationDirectory);
		}

		byte[] sourceBytes = File.ReadAllBytes(sourcePath);
		if (File.Exists(destinationPath))
		{
			ClearReadOnly(destinationPath);
			if (File.ReadAllBytes(destinationPath).AsSpan().SequenceEqual(sourceBytes))
			{
				return false;
			}

			replacedExisting = true;
		}

		File.WriteAllBytes(destinationPath, sourceBytes);
		return true;
	}

	private static void WriteBytesWhenDifferent(string path, byte[] content)
	{
		if (File.Exists(path))
		{
			ClearReadOnly(path);
			if (File.ReadAllBytes(path).AsSpan().SequenceEqual(content))
			{
				return;
			}
		}

		string directory = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(directory))
		{
			Directory.CreateDirectory(directory);
		}

		File.WriteAllBytes(path, content);
	}

	public static void SetReadOnly(string path)
	{
		if (!File.Exists(path))
		{
			return;
		}

		FileAttributes attributes = File.GetAttributes(path);
		if ((attributes & FileAttributes.ReadOnly) == 0)
		{
			File.SetAttributes(path, attributes | FileAttributes.ReadOnly);
		}
	}

	public static void ClearReadOnly(string path)
	{
		if (!File.Exists(path))
		{
			return;
		}

		FileAttributes attributes = File.GetAttributes(path);
		if ((attributes & FileAttributes.ReadOnly) != 0)
		{
			File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
		}
	}

	public static void ClearReadOnlyRecursive(string folder)
	{
		if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
		{
			return;
		}

		foreach (string filePath in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
		{
			try
			{
				ClearReadOnly(filePath);
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
			}
		}
	}

	/// <summary>
	///     Lists the files a save copied in, which the read-only attribute marks. A step that has to make the whole folder
	///     writable, such as export, collects them first and marks exactly these again afterwards.
	/// </summary>
	/// <param name="folder">The map folder.</param>
	/// <returns>Every path under the folder that is marked read-only.</returns>
	public static List<string> CollectReadOnlyPaths(string folder)
	{
		var paths = new List<string>();
		if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
		{
			return paths;
		}

		foreach (string filePath in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
		{
			try
			{
				if ((File.GetAttributes(filePath) & FileAttributes.ReadOnly) != 0)
				{
					paths.Add(filePath);
				}
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
			}
		}

		return paths;
	}

	/// <summary>
	///     Marks the files <see cref="CollectReadOnlyPaths" /> listed as read-only again.
	/// </summary>
	/// <param name="paths">The paths to mark.</param>
	public static void RestoreReadOnly(IEnumerable<string> paths)
	{
		foreach (string path in paths)
		{
			SetReadOnly(path);
		}
	}

	public static void SetReadOnlyRecursive(string folder)
	{
		if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
		{
			return;
		}

		foreach (string filePath in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
		{
			try
			{
				SetReadOnly(filePath);
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
			}
		}
	}

	private sealed class ReferenceSource
	{
		public ReferenceSource(MapReference reference, string folder)
		{
			Reference = reference;
			Folder = folder;
		}

		public MapReference Reference { get; }

		public string Folder { get; }
	}

	private sealed class ResolvedFile
	{
		public ResolvedFile(string relativePath, string sourceMapName, string sourcePath, bool parentOwned)
		{
			RelativePath = relativePath;
			SourceMapName = sourceMapName;
			SourcePath = sourcePath;
			ParentOwned = parentOwned;
		}

		public string RelativePath { get; }

		public string SourceMapName { get; }

		public string SourcePath { get; }

		/// <summary>Whether the parent map authored this file, rather than a previous save copying it in.</summary>
		public bool ParentOwned { get; }
	}
}

using System;
using System.IO;
using Godot;
using Realm.Client.Core;
using Realm.Client.Utils;
using Realm.Shared.Services;

namespace Realm.Client.Services;

/// <summary>
///     Where the workspace is and where the repository's shared files are: the active map folder (editor or
///     game), the user's default save folder, and the repository root that holds MapTemplate and the MapAPI
///     binary. The root lookup walks up from the Godot project once and caches the answer, because every
///     scaffolding step asks for a template file.
/// </summary>
internal static class MapWorkspacePaths
{
	internal static string DefaultMapFolderGlobalPath()
	{
		string documentsDirectory = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
		if (!string.IsNullOrEmpty(documentsDirectory))
		{
			return documentsDirectory;
		}

		return ProjectSettings.GlobalizePath("user://");
	}

	private static string? _testActiveWorkspacePath;

	internal static void SetActiveWorkspacePathForTesting(string? path)
	{
		_testActiveWorkspacePath = path;
	}

	internal static string ActiveWorkspacePath()
	{
		if (!string.IsNullOrEmpty(_testActiveWorkspacePath))
			return _testActiveWorkspacePath;

		if (GameContext.SessionState.IsMapEditorMode)
			return EditorWorkspacePath();

		return ActiveMapDirectory();
	}

	internal static string ActiveMapDirectory()
	{
		string? mapDirectory = GameContext.MapStorageService?.CurrentMapDirectory;
		if (GameContext.Host != null && !string.IsNullOrEmpty(mapDirectory))
			return mapDirectory;

		return string.Empty;
	}

	internal static string EditorWorkspacePath()
	{
		if (!string.IsNullOrEmpty(GameContext.EditorService._editingDirectory))
			return GameContext.EditorService._editingDirectory;

		return string.Empty;
	}

	private static string? _cachedRepoRoot;
	private static bool _repoRootResolved;

	internal static string? RepoRoot()
	{
		if (_repoRootResolved) return _cachedRepoRoot;
		_repoRootResolved = true;
		string baseDir = PathUtils.GetProjectRoot();
		var current = new DirectoryInfo(baseDir);
		while (current != null)
		{
			if (File.Exists(Path.Combine(current.FullName, "Realm.sln")) || Directory.Exists(Path.Combine(current.FullName, "Realm.MapAPI")))
			{
				_cachedRepoRoot = current.FullName.Replace("\\", "/");
				return _cachedRepoRoot;
			}

			current = current.Parent;
		}

		GD.PushWarning("[MapWorkspacePaths] Could not locate the Realm repository (Realm.sln or Realm.MapAPI not found above res://). Template files and the MapAPI DLL will not be available.");
		return null;
	}

	internal static string? FindRootFile(string relativePath)
	{
		string found = PathUtils.FindPath(relativePath);
		if (File.Exists(found) || Directory.Exists(found))
		{
			return found;
		}

		string? repoRoot = RepoRoot();
		if (repoRoot == null) return null;
		return Path.Combine(repoRoot, relativePath).Replace("\\", "/");
	}

	/// <summary>
	///     The absolute path to a file in the map template. <see cref="string.Empty" /> when the template is not on this
	///     machine, which is what the <c>File.Exists</c>/<c>Directory.Exists</c> guards at every copy site test for.
	/// </summary>
	internal static string TemplatePath(string fileName)
	{
		return FindRootFile("MapTemplate/" + fileName) ?? string.Empty;
	}

	internal static string? TemplateRoot()
	{
		string? templateRoot = FindRootFile("MapTemplate");
		return Directory.Exists(templateRoot) ? templateRoot : null;
	}

	internal static string? WitPath()
	{
		return FindRootFile("Realm.MapAPI/wit/game.g.wit");
	}
}

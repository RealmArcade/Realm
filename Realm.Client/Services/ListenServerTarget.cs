using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Realm.Shared.Network;

namespace Realm.Client.Services;

/// <summary>
///     Which authority this build runs its matches on. The authority is a Godot process, so while developing it is the
///     project beside this one, run by the engine already running the client; in a build it is the exported authority
///     shipped in this game's own folder. Both shapes are named here rather than guessed at launch time, and they are
///     looked for from every folder this process can prove it knows, because the project folder the engine reports is
///     not always the folder the client was launched from.
/// </summary>
public static class ListenServerTarget
{
	/// <summary>The folder the authority is shipped in, and the project folder it lives in while developing.</summary>
	public const string FolderName = "Realm.ListenServerHost";

	/// <summary>The name the authority is exported as, without its platform extension.</summary>
	public const string ExecutableName = "Realm.ListenServerHost";

	/// <summary>The project file that states a folder is the authority's Godot project.</summary>
	private const string ProjectFileName = "project.godot";

	/// <summary>What an exported Godot project names the folder it keeps its assemblies in.</summary>
	private const string DataFolderPrefix = "data_";

	/// <summary>How far above each known folder the authority's own folder is looked for.</summary>
	private const int SearchedAncestorLevels = 6;

	/// <summary>
	///     Finds the authority this build would run a match on, either the exported one shipped beside the game or the
	///     project beside the client while developing.
	/// </summary>
	/// <param name="target">What to start, when something was found.</param>
	/// <param name="failure">Why nothing was found, naming every folder that was looked in.</param>
	/// <returns>Whether this build carries an authority it can start.</returns>
	public static bool TryResolve(out ServerLaunchTarget? target, out string failure)
	{
		target = null;
		failure = string.Empty;

		string extension = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
		var probed = new List<string>();

		foreach (string anchor in CandidateAnchors())
		{
			foreach (string directory in CandidateAuthorityFolders(anchor))
			{
				if (probed.Contains(directory)) continue;
				probed.Add(directory);

				string executablePath = Path.Combine(directory, ExecutableName + extension);
				if (File.Exists(executablePath))
				{
					target = new ServerLaunchTarget(executablePath, null);
					return true;
				}

				if (Directory.Exists(directory) && File.Exists(Path.Combine(directory, ProjectFileName)))
				{
					target = new ServerLaunchTarget(OS.GetExecutablePath(), directory);
					return true;
				}
			}
		}

		var searched = new List<string>();
		foreach (string directory in probed) searched.Add($"'{directory}'");
		failure = $"this build does not carry the authority that runs its matches: none of {string.Join(", ", searched)} is there";
		return false;
	}

	/// <summary>
	///     Every folder this process can prove it knows, most specific first. The engine's own answer comes first and the
	///     process facts after it, because a client run in a mode the engine does not globalize a resource path for is
	///     still running from a folder that holds the repository it was built in.
	/// </summary>
	private static List<string> CandidateAnchors()
	{
		var anchors = new List<string>();

		AddAnchor(anchors, TryGlobalizePath("res://"));
		AddAnchor(anchors, TryGlobalizePath("res://" + ProjectFileName) is { Length: > 0 } projectFile ? Path.GetDirectoryName(projectFile) : null);
		AddAnchor(anchors, TryExecutableDirectory());
		AddAnchor(anchors, AppContext.BaseDirectory);
		AddAnchor(anchors, TryCurrentDirectory());

		// A shipped game keeps its managed assemblies in a data folder named after this project, and the archive a player
		// opens carries the authority inside that folder rather than beside the executable they double-clicked.
		foreach (string anchor in anchors.ToArray()) AddDataFolderAnchors(anchors, anchor);

		return anchors;
	}

	/// <summary>Every data folder of one known folder, which a shipped build keeps its screen code and the authority in.</summary>
	private static void AddDataFolderAnchors(List<string> anchors, string anchor)
	{
		string[] dataFolders;
		try
		{
			dataFolders = Directory.GetDirectories(anchor, DataFolderPrefix + "*");
		}
		catch (Exception exception) when (exception is DirectoryNotFoundException or IOException or UnauthorizedAccessException)
		{
			return;
		}

		foreach (string dataFolder in dataFolders) AddAnchor(anchors, dataFolder);
	}

	/// <summary>
	///     Where the authority's folder sits relative to one known folder: inside it, or beside it, for as many levels
	///     above it as a build's output nesting is deep. A development client lives inside the repository, so the
	///     authority is at worst a sibling of one of the known folder's ancestors.
	/// </summary>
	private static IEnumerable<string> CandidateAuthorityFolders(string anchorDirectory)
	{
		DirectoryInfo? current;
		try
		{
			current = new DirectoryInfo(anchorDirectory);
		}
		catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
		{
			yield break;
		}

		for (int level = 0; current != null && level < SearchedAncestorLevels; level++, current = current.Parent)
		{
			yield return Path.Combine(current.FullName, FolderName);
			if (current.Parent != null) yield return Path.Combine(current.Parent.FullName, FolderName);
		}
	}

	/// <summary>Keeps a folder the engine named only if it is a folder this process can actually reach.</summary>
	private static void AddAnchor(List<string> anchors, string? folder)
	{
		if (string.IsNullOrWhiteSpace(folder)) return;

		string fullPath;
		try
		{
			fullPath = Path.GetFullPath(folder);
		}
		catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException or PathTooLongException)
		{
			return;
		}

		if (!Directory.Exists(fullPath)) return;
		if (anchors.Contains(fullPath)) return;

		anchors.Add(fullPath);
	}

	/// <summary>What the engine says a resource path resolves to, or nothing when it cannot say.</summary>
	private static string? TryGlobalizePath(string resourcePath)
	{
		try
		{
			return ProjectSettings.GlobalizePath(resourcePath);
		}
		catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
		{
			return null;
		}
	}

	/// <summary>The folder the running engine's executable sits in, which a shipped game shares with the authority.</summary>
	private static string? TryExecutableDirectory()
	{
		try
		{
			return Path.GetDirectoryName(OS.GetExecutablePath());
		}
		catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
		{
			return null;
		}
	}

	/// <summary>The folder the process was started from.</summary>
	private static string? TryCurrentDirectory()
	{
		try
		{
			return Directory.GetCurrentDirectory();
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}
}

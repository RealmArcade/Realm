using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Realm.Shared.Network;

namespace Realm.Client.Services;

public static class ListenServerTarget
{
	public const string FolderName = "Realm.ListenServerHost";

	public const string ExecutableName = "Realm.ListenServerHost";

	private const string ProjectFileName = "project.godot";

	private const string DataFolderPrefix = "data_";

	private const int SearchedAncestorLevels = 6;

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

	private static List<string> CandidateAnchors()
	{
		var anchors = new List<string>();

		AddAnchor(anchors, TryGlobalizePath("res://"));
		AddAnchor(anchors, TryGlobalizePath("res://" + ProjectFileName) is { Length: > 0 } projectFile ? Path.GetDirectoryName(projectFile) : null);
		AddAnchor(anchors, TryExecutableDirectory());
		AddAnchor(anchors, AppContext.BaseDirectory);
		AddAnchor(anchors, TryCurrentDirectory());

		foreach (string anchor in anchors.ToArray()) AddDataFolderAnchors(anchors, anchor);

		return anchors;
	}

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

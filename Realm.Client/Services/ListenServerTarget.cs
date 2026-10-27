using System;
using System.IO;
using Godot;
using Realm.Shared.Network;

namespace Realm.Client.Services;

/// <summary>
///     Which authority this build runs its matches on. The authority is a Godot process, so while developing it is the
///     project beside this one, run by the engine already running the client; in a build it is the exported authority
///     shipped in this game's own folder. Both shapes are named here rather than guessed at launch time.
/// </summary>
public static class ListenServerTarget
{
	/// <summary>The folder the authority is shipped in, and the project folder it lives in while developing.</summary>
	public const string FolderName = "Realm.ListenServerHost";

	/// <summary>The name the authority is exported as, without its platform extension.</summary>
	public const string ExecutableName = "Realm.ListenServerHost";

	public static bool TryResolve(out ServerLaunchTarget? target, out string failure)
	{
		target = null;
		failure = string.Empty;

		string gameDirectory = ProjectSettings.GlobalizePath("res://");
		string extension = OperatingSystem.IsWindows() ? ".exe" : string.Empty;

		string exportedPath = Path.Combine(gameDirectory, FolderName, ExecutableName + extension);
		if (File.Exists(exportedPath))
		{
			target = new ServerLaunchTarget(exportedPath, null);
			return true;
		}

		string projectDirectory = Path.GetFullPath(Path.Combine(gameDirectory, "..", FolderName));
		if (Directory.Exists(projectDirectory) && File.Exists(Path.Combine(projectDirectory, "project.godot")))
		{
			target = new ServerLaunchTarget(OS.GetExecutablePath(), projectDirectory);
			return true;
		}

		failure = $"this build does not carry the authority that runs its matches: neither '{exportedPath}' nor the project '{projectDirectory}' is there";
		return false;
	}
}

using System;
using Godot;

namespace Realm.Client.Services;

/// <summary>
/// The line sink the editor, the HUD and the gameplay services write to, and the console windows read from.
/// </summary>
public static class GameLog
{
	public static event Action<string>? OnLine;

	public static void LogToConsole(string message)
	{
		OnLine?.Invoke(message);
		GD.Print($"[REALM] {message}");
	}

	public static void LogError(string message)
	{
		OnLine?.Invoke(message);
		GD.PrintErr($"[REALM] {message}");
	}
}

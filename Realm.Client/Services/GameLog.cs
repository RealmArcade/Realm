using System;
using Godot;

namespace Realm.Client.Services;

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

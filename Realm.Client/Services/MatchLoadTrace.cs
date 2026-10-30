using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Godot;

namespace Realm.Client.Services;

public static class MatchLoadTrace
{
	private static readonly List<(string Name, double Milliseconds)> Phases = new();

	private static long _lastTimestamp;
	private static string _trigger = string.Empty;

	public static void Begin(string trigger)
	{
		Phases.Clear();
		_trigger = trigger;
		_lastTimestamp = Stopwatch.GetTimestamp();
	}

	public static void Mark(string phase)
	{
		long now = Stopwatch.GetTimestamp();
		if (_lastTimestamp == 0)
		{
			_lastTimestamp = now;
			_trigger = "untraced";
		}

		double milliseconds = (now - _lastTimestamp) * 1000.0 / Stopwatch.Frequency;
		_lastTimestamp = now;
		Phases.Add((phase, milliseconds));
	}

	public static bool IsTracing => _lastTimestamp != 0;

	public static void WriteSummary(string context)
	{
		if (Phases.Count == 0) return;

		var builder = new System.Text.StringBuilder(256);
		builder.Append("MATCH_LOAD trigger=").Append(_trigger).Append(" context=").Append(context);
		double total = 0;
		for (int index = 0; index < Phases.Count; index++)
		{
			(string name, double milliseconds) = Phases[index];
			total += milliseconds;
			builder.Append(' ').Append(name).Append('=').Append(milliseconds.ToString("F1", CultureInfo.InvariantCulture));
		}

		builder.Append(" total=").Append(total.ToString("F1", CultureInfo.InvariantCulture));
		string line = builder.ToString();
		GD.Print(line);
		AppendToFile(line);

		Phases.Clear();
		_lastTimestamp = 0;
		_trigger = string.Empty;
	}

	private static void AppendToFile(string line)
	{
		try
		{
			string path = ProjectSettings.GlobalizePath("user://logs/match_load_trace.log");
			string? directory = System.IO.Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(directory)) System.IO.Directory.CreateDirectory(directory);
			System.IO.File.AppendAllText(path, line + System.Environment.NewLine);
		}
		catch (Exception exception)
		{
			GD.PushWarning($"[MatchLoadTrace] Could not write the trace to disk: {exception.Message}");
		}
	}
}

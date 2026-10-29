using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Godot;

namespace Realm.Client.Services;

/// <summary>
///     Names and measures the phases of turning a lobby into a playable match. It exists so a claim about where the
///     wait goes is a number rather than an impression: every phase appends one figure, and the whole load is written as
///     a single line to the console and to <c>user://logs/match_load_trace.log</c>, which a run on a slow machine can be
///     read back from without a profiler attached.
/// </summary>
public static class MatchLoadTrace
{
	/// <summary>One figure per named phase, in the order the load reached them.</summary>
	private static readonly List<(string Name, double Milliseconds)> Phases = new();

	private static long _lastTimestamp;
	private static string _trigger = string.Empty;

	/// <summary>Starts a trace. The first phase is measured from this call, so the caller names what it is timing.</summary>
	public static void Begin(string trigger)
	{
		Phases.Clear();
		_trigger = trigger;
		_lastTimestamp = Stopwatch.GetTimestamp();
	}

	/// <summary>Closes the phase that began at the last mark (or at <see cref="Begin" />) and names it.</summary>
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

	/// <summary>Whether a trace is open, so a caller can mark a phase without beginning one it did not start.</summary>
	public static bool IsTracing => _lastTimestamp != 0;

	/// <summary>
	///     Writes every phase this trace collected as one line. It is called once the scene is ready, so the figures are
	///     about the load rather than about a process that is still starting.
	/// </summary>
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

		// The trace is closed once it is written. A scene that replaced the one it measured starts its own rather than
		// appending to this one, which would have counted the wait between the two scenes against whatever phase each
		// happened to mark first.
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

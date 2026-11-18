using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace Realm.Client.Services;

/// <summary>
///     The UGC licence version this machine's creator accepted, recorded once beside the player's other local state. The
///     editor asks before the first publish and never again until the licence version changes.
/// </summary>
public static class UgcLicenseAcceptance
{
	public static string AcceptancePath => ProjectSettings.GlobalizePath("user://ugc_license_acceptance.json");

	public static bool IsAccepted(string version)
	{
		return string.Equals(ReadAcceptance()?.Version, version, StringComparison.Ordinal);
	}

	public static void RecordAcceptance(string version)
	{
		try
		{
			string path = AcceptancePath;
			string? directory = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

			var acceptance = new AcceptanceRecord
			{
				Version = version,
				AcceptedAtUtc = DateTime.UtcNow.ToString("O")
			};

			File.WriteAllText(path, JsonSerializer.Serialize(acceptance, SerializerOptions));
			GameLog.LogToConsole($"[Licence] UGC licence {version} accepted and recorded at {path}");
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[Licence] the accepted licence version could not be recorded: {exception.Message}");
		}
	}

	private static AcceptanceRecord? ReadAcceptance()
	{
		try
		{
			string path = AcceptancePath;
			if (!File.Exists(path)) return null;

			return JsonSerializer.Deserialize<AcceptanceRecord>(File.ReadAllText(path), SerializerOptions);
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[Licence] the recorded licence acceptance could not be read: {exception.Message}");
			return null;
		}
	}

	private static readonly JsonSerializerOptions SerializerOptions = new()
	{
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	private sealed class AcceptanceRecord
	{
		[JsonPropertyName("version")]
		public string Version { get; set; } = string.Empty;

		[JsonPropertyName("acceptedAtUtc")]
		public string AcceptedAtUtc { get; set; } = string.Empty;
	}
}

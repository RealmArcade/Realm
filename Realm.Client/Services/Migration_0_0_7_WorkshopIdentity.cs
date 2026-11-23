using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Realm.Shared;

namespace Realm.Client.Services;

/// <summary>
///     Moves a map from the portal's idea of a dependency to Steam's idea of an identity. The unused
///     <c>Dependencies</c> list is removed, because reuse is vendored into the map when it is authored and nothing
///     resolves another map at load time, and the map states the engine generation its data belongs to so a lobby can
///     tell a newer game's map apart from one this build still upgrades.
/// </summary>
public class Migration_0_0_7_WorkshopIdentity : IMapMigration
{
	public string FromVersion => "v0.0.6";
	public string ToVersion => "v0.0.7";
	public string Description => "Drop the dead dependency list and state the map's engine generation";

	public MigrationResult Up(string mapDirectory, IProgress<MigrationProgressUpdate>? progress = null)
	{
		try
		{
			string metadataPath = Path.Combine(mapDirectory, "metadata.json");
			if (!File.Exists(metadataPath))
			{
				return new MigrationResult { Success = true, FromVersion = FromVersion, ToVersion = ToVersion };
			}

			progress?.Report(new MigrationProgressUpdate(Description, 1, 1, "Rewriting the map's identity..."));
			JsonObject metadataRoot = JsonNode.Parse(File.ReadAllText(metadataPath))?.AsObject() ?? new JsonObject();

			metadataRoot.Remove("Dependencies");
			metadataRoot["EngineGeneration"] = RealmVersion.EngineGeneration;
			metadataRoot["GameBuildNumber"] = ToVersion;
			MapJsonFormatter.SaveFormattedJson(metadataPath, metadataRoot);

			GameLog.LogToConsole($"[Migration_0_0_7] The map no longer states dependencies and now declares engine generation {RealmVersion.EngineGeneration}.");
			return new MigrationResult { Success = true, FromVersion = FromVersion, ToVersion = ToVersion };
		}
		catch (Exception exception)
		{
			return new MigrationResult
			{
				Success = false,
				ErrorMessage = $"Migration 0.0.7 failed: {exception.Message}",
				FromVersion = FromVersion,
				ToVersion = ToVersion
			};
		}
	}

	public async Task<MigrationResult> UpAsync(string mapDirectory, IProgress<MigrationProgressUpdate>? progress = null)
	{
		return await Task.Run(() => Up(mapDirectory, progress));
	}
}

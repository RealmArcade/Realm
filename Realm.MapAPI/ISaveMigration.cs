namespace Realm.MapAPI;

/// <summary>
/// Defines a contract for migrating save payload data from one schema version to the next.
/// </summary>
public interface ISaveMigration
{
	/// <summary>
	/// Gets the source schema version this migration upgrades from.
	/// </summary>
	int FromVersion { get; }

	/// <summary>
	/// Gets the target schema version this migration upgrades to.
	/// </summary>
	int ToVersion { get; }

	/// <summary>
	/// Transforms the raw JSON payload from the source schema version to the target schema version.
	/// </summary>
	/// <param name="jsonPayload">The JSON payload string at the source version.</param>
	/// <returns>The migrated JSON payload string at the target version.</returns>
	string Migrate(string jsonPayload);
}
namespace Realm.MapAPI;

/// <summary>
/// A save migration whose body is a supplied delegate, so a map can register a version step without declaring a class
/// of its own.
/// </summary>
public class DelegateSaveMigration : ISaveMigration
{
	private readonly Func<string, string> _migrationFunc;

	/// <summary>The schema version this migration upgrades from.</summary>
	public int FromVersion { get; }
	/// <summary>The schema version this migration upgrades to.</summary>
	public int ToVersion { get; }

	/// <summary>Binds a version step to the delegate that performs it.</summary>
	/// <param name="fromVersion">The schema version the payload arrives in.</param>
	/// <param name="toVersion">The schema version the payload is rewritten to.</param>
	/// <param name="migrationFunc">The transformation, taking the payload at the source version and returning it at the target version.</param>
	public DelegateSaveMigration(int fromVersion, int toVersion, Func<string, string> migrationFunc)
	{
		FromVersion = fromVersion;
		ToVersion = toVersion;
		_migrationFunc = migrationFunc;
	}

	/// <summary>Transforms the raw JSON payload from the source schema version to the target schema version.</summary>
	/// <param name="jsonPayload">The JSON payload string at the source version.</param>
	/// <returns>The migrated JSON payload string at the target version.</returns>
	public string Migrate(string jsonPayload)
	{
		return _migrationFunc(jsonPayload);
	}
}
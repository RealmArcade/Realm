namespace Realm.MapAPI;

public class DelegateSaveMigration : ISaveMigration
{
	private readonly Func<string, string> _migrationFunc;

	public int FromVersion { get; }
	public int ToVersion { get; }

	public DelegateSaveMigration(int fromVersion, int toVersion, Func<string, string> migrationFunc)
	{
		FromVersion = fromVersion;
		ToVersion = toVersion;
		_migrationFunc = migrationFunc;
	}

	public string Migrate(string jsonPayload)
	{
		return _migrationFunc(jsonPayload);
	}
}
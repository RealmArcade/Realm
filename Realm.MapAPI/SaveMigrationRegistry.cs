using System;
using System.Collections.Generic;

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

/// <summary>
/// Manages registration and sequential execution of save payload schema migrations for map scripts.
/// </summary>
public class SaveMigrationRegistry
{
    private readonly Dictionary<int, ISaveMigration> _migrationsByFromVersion = new();

    private class DelegateSaveMigration : ISaveMigration
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

    /// <summary>
    /// Registers a migration step between two sequential schema versions using a delegate transformer.
    /// </summary>
    /// <param name="fromVersion">The source schema version.</param>
    /// <param name="toVersion">The target schema version.</param>
    /// <param name="migrationFunc">A function that accepts the old JSON payload and returns the transformed JSON payload.</param>
    public void RegisterMigration(int fromVersion, int toVersion, Func<string, string> migrationFunc)
    {
        _migrationsByFromVersion[fromVersion] = new DelegateSaveMigration(fromVersion, toVersion, migrationFunc);
    }

    /// <summary>
    /// Registers an instance of <see cref="ISaveMigration"/>.
    /// </summary>
    /// <param name="migration">The save migration instance to register.</param>
    public void RegisterMigration(ISaveMigration migration)
    {
        _migrationsByFromVersion[migration.FromVersion] = migration;
    }

    /// <summary>
    /// Applies all registered migration steps sequentially starting from <paramref name="fromVersion"/> until <paramref name="targetVersion"/> is reached.
    /// </summary>
    /// <param name="jsonPayload">The initial raw JSON payload string.</param>
    /// <param name="fromVersion">The current schema version of the payload.</param>
    /// <param name="targetVersion">The desired target schema version.</param>
    /// <returns>The migrated JSON payload string.</returns>
    public string ApplyMigrations(string jsonPayload, int fromVersion, int targetVersion)
    {
        int currentVersion = fromVersion;
        string currentPayload = jsonPayload;

        while (currentVersion < targetVersion)
        {
            if (!_migrationsByFromVersion.TryGetValue(currentVersion, out var migration))
            {
                break;
            }

            currentPayload = migration.Migrate(currentPayload);
            currentVersion = migration.ToVersion;
        }

        return currentPayload;
    }
}

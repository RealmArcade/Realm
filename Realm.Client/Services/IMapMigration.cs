using System;
using System.Threading.Tasks;

namespace Realm.Client.Services;

public interface IMapMigration
{
	string FromVersion { get; }
	string ToVersion { get; }
	string Description { get; }
	MigrationResult Up(string mapDirectory, IProgress<MigrationProgressUpdate>? progress = null);
	Task<MigrationResult> UpAsync(string mapDirectory, IProgress<MigrationProgressUpdate>? progress = null);
}
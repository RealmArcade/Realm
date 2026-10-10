namespace Realm.Client.Services;

public record MigrationProgressUpdate(
	string CurrentMigration,
	int StepIndex,
	int TotalSteps,
	string Message
);
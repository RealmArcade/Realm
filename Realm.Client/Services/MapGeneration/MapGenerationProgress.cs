namespace Realm.Client.Services.MapGeneration;

public readonly record struct MapGenerationProgress(
	MapGenerationStage Stage,
	float Percent,
	bool IsRunning,
	bool IsComplete,
	bool HasFailed,
	string? FailureMessage);

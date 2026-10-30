namespace Realm.Benchmarks;

public sealed record DeltaComposition(
	double FrameBytes,
	double SerializedBytes,
	double UnitBytes,
	double UnitCount,
	double ComponentBytes,
	double ComponentCount,
	double AbilityBytes,
	double CatalogBytes,
	double ColdPayloadBytes);

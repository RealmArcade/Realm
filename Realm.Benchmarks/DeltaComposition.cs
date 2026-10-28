namespace Realm.Benchmarks;

/// <summary>
///     What one peer's delta frame is made of. A bandwidth figure alone cannot say which channel to work on, so this
///     measures the serialized size of each section of the same snapshot the frame reports its total for.
/// </summary>
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

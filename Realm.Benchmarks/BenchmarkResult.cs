namespace Realm.Benchmarks;

public sealed record BenchmarkResult(
	int Units,
	int Players,
	int Workers,
	int Ticks,
	double TickDeltaSeconds,
	double ReferenceDerate,
	double MeanTickMicros,
	double P50TickMicros,
	double P95TickMicros,
	double P99TickMicros,
	double MaxTickMicros,
	double CooldownMicros,
	double MovementMicros,
	double CombatMicros,
	double EconomyMicros,
	double StatusMicros,
	double SnapshotBuildMicros,
	double SnapshotSerializeMicros,
	double SnapshotCompressMicros,
	double SnapshotPhaseMicros,
	double DeltaBytesPerPeer,
	double BaselineBytesPerPeer,
	double AllVisibleDeltaBytesPerPeer,
	double VisibleUnitsPerPeer,
	DeltaComposition Composition)
{
	public double SnapshotMicrosPerPeer => SnapshotBuildMicros + SnapshotSerializeMicros + SnapshotCompressMicros;

	public double PerTickMicrosAllPeers => MeanTickMicros + SnapshotPhaseMicros;

	public double DeratedPerTickMicros => PerTickMicrosAllPeers / ReferenceDerate;

	public double DutyAt(double rateHz) => PerTickMicrosAllPeers * rateHz / 1_000_000.0;

	public double DeratedDutyAt(double rateHz) => DeratedPerTickMicros * rateHz / 1_000_000.0;

	public double UpstreamBytesPerSecond(double rateHz) => DeltaBytesPerPeer * Players * rateHz;

	public double DownstreamBytesPerSecond(double rateHz) => DeltaBytesPerPeer * rateHz;

	public double MaximumRateAtDuty(double duty)
	{
		return DeratedPerTickMicros <= 0 ? double.MaxValue : duty * 1_000_000.0 / DeratedPerTickMicros;
	}
}

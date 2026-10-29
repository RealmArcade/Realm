namespace Realm.Benchmarks;

public sealed record ZstdSweep(
	double UncompressedBytesPerFrame,
	double UncompressedMicrosPerFrame,
	double FrameCount,
	double SerializeMedianMicros,
	double SerializeBytesAllocated,
	double EncodeMedianMicros,
	double EncodeBytesAllocated,
	IReadOnlyList<ZstdLevelSample> Levels);

public sealed record ZstdLevelSample(
	int Level,
	double BytesPerFrame,
	double MedianMicrosPerFrame,
	double P95MicrosPerFrame,
	double Ratio)
{
	public double UpstreamMbps(int players, double rateHz) => BytesPerFrame * players * rateHz * 8.0 / 1_000_000.0;
}

namespace Realm.Benchmarks;

/// <summary>
///     What one delta stream of this room costs at each compression level, measured by framing the very frames a peer
///     would receive rather than a synthetic buffer. The byte count is the whole frame a peer is sent, and the time is
///     the encoder's alone, so a level is chosen on the CPU it adds against the bytes it removes.
/// </summary>
public sealed record ZstdSweep(
	double UncompressedBytesPerFrame,
	double UncompressedMicrosPerFrame,
	double FrameCount,
	double SerializeMedianMicros,
	double SerializeBytesAllocated,
	double EncodeMedianMicros,
	double EncodeBytesAllocated,
	IReadOnlyList<ZstdLevelSample> Levels);

/// <summary>
///     One compression level's share of the stream: how large the frame it produces is, and what producing it costs. A
///     level of zero means the frame carries the serialized snapshot unframed.
/// </summary>
public sealed record ZstdLevelSample(
	int Level,
	double BytesPerFrame,
	double MedianMicrosPerFrame,
	double P95MicrosPerFrame,
	double Ratio)
{
	/// <summary>
	///     The upstream a room of peers would need for this level, counting every one of them receiving its own frame at
	///     the rate a match sends at.
	/// </summary>
	public double UpstreamMbps(int players, double rateHz) => BytesPerFrame * players * rateHz * 8.0 / 1_000_000.0;
}

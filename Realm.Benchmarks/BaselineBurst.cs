namespace Realm.Benchmarks;

public sealed record BaselineBurst(
	double PhaseMicros,
	double BuildMicrosPerPeer,
	double SerializeMicrosPerPeer,
	double CompressMicrosPerPeer,
	double BytesPerPeer);

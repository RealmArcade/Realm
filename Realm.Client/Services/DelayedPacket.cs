using Godot;

namespace Realm.Client.Services;

public enum DelayedPacketKind : byte
{
	Snapshot = 0,
	Baseline = 1,
	SpellEffect = 2
}

public struct DelayedPacket
{
	public int PeerId;
	public DelayedPacketKind Kind;
	public byte[]? SnapshotPayload;
	public string? SpellId;
	public Vector3 Position;
	public double SendTime;
}

using MemoryPack;

namespace Realm.Shared.Network;

/// <summary>
///     The requests one screen is told about in one tick. A map can raise several in a single tick - a volley, a ground
///     ring, a hit sound and a scoreboard row - and each one used to be its own frame with its own header on the reliable
///     stream, queued behind whatever baseline that stream was already carrying. They travel together instead, split by
///     what a screen is allowed to miss.
/// </summary>
[MemoryPackable]
public partial struct PresentationBatch
{
	/// <summary>
	///     The requests in this batch, in the order the map raised them. A screen applies each one where its own
	///     description sequence says it belongs, so the order within a batch is the order they happened.
	/// </summary>
	public PresentationMessage[] Messages;

	/// <summary>
	///     Whether this kind of request is state the screen cannot be left without, rather than a moment it may miss. A
	///     match declares that it is drawn, what it calls its sides, what the camera may see and who won through these
	///     requests, and a screen that never hears one is a screen the match is no longer describing. An effect, a ping or
	///     a sound is the opposite. Anything this build does not name is treated as state, because an unrecognised
	///     request that is dropped is a screen quietly told less than the map meant it to be told.
	/// </summary>
	/// <param name="kind">The kind of request being carried.</param>
	/// <returns>True when the request must travel on the stream that cannot lose it.</returns>
	public static bool IsState(PresentationKind kind)
	{
		return kind switch
		{
			PresentationKind.FloatingText => false,
			PresentationKind.MinimapPing => false,
			PresentationKind.TargetIndicator => false,
			PresentationKind.VisualEffect => false,
			PresentationKind.BarrageVolley => false,
			PresentationKind.GroundRing => false,
			PresentationKind.BurstSphere => false,
			PresentationKind.TerrainLine => false,
			PresentationKind.PersistentAura => false,
			PresentationKind.Projectile => false,
			PresentationKind.CameraShaken => false,
			PresentationKind.WarningSound => false,
			PresentationKind.ClickSound => false,
			PresentationKind.CastSound => false,
			PresentationKind.UnitMeshImpulse => false,
			PresentationKind.ResourceMeshImpulse => false,
			_ => true
		};
	}
}

namespace Realm.Shared.Network;

/// <summary>
///     How a frame has to be carried. A reliable frame is delivered, in order, retransmitted until the peer has it: that
///     is what an order, an acknowledgement, a roster seat or a baseline needs, because the match cannot proceed without
///     it and the peer has no way to ask again. An unreliable-ordered frame is delivered if it arrives in time and
///     dropped if it does not: that is what a delta needs, because the next delta describes a newer world and a delta
///     whose delivery stalled every later delta behind it is the difference between a screen that is a little behind and
///     one that stops while the transport retransmits.
///     The choice is about what a lost frame costs, not about how important it is. A lost delta costs the change it
///     carried until the peer's acknowledgement reports the gap and the match describes the peer from a baseline again,
///     which is a state the protocol already has to handle; a lost baseline would cost the peer its whole world.
/// </summary>
public enum FrameDelivery : byte
{
	/// <summary>Delivered and ordered, retransmitted until the peer has it.</summary>
	Reliable = 1,

	/// <summary>Delivered only if it arrives in time, and dropped when a newer frame overtakes it.</summary>
	UnreliableOrdered = 2
}

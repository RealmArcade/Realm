using System.Numerics;
using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Movement;

/// <summary>
///     Where a locally issued order was aimed, held only until the authority says what it did with the order. It exists
///     so a click can draw its route on the next tick instead of waiting a round trip for the match, and it never moves
///     a unit: the planning pass that reads it produces a path and writes no position.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct PreviewMoveTo
{
	public Vector3 Target;

	/// <summary>Which order drew this route, so an answer about that order can take it back.</summary>
	public int CommandId;

	/// <summary>
	///     The sequence the order was sent on, so a route the match never answers can be taken back. An order can be
	///     accepted without any unit being told to go anywhere - it was already standing on the destination, or the order
	///     waits behind another - and a drawn route with no answer behind it would otherwise be planned again on every
	///     tick for the rest of the match.
	/// </summary>
	public int IssuedSequence;
}

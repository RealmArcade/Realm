using System;

namespace Realm.Shared.Network;

/// <summary>
///     How finely a unit's state is described on the wire. A position is stated to a sixty-fourth of a metre (about one
///     and a half centimetres), a facing to a sixty-fifthousandth of a turn (about a hundredth of a degree) and a hit
///     point to a sixty-fourth of a point, which is finer than any unit is rendered or any health bar is drawn.
///     The description is snapped to these steps on the authority as well as on the wire, so what a peer is told is the
///     same value the authority compares its own copy against: a separation nudge of a millimetre is a change no screen
///     can show, and it should not cost a record in every peer's frame. They are powers of two, so a snapped value is
///     exactly representable as a float and survives every round trip bit for bit.
/// </summary>
public static class UnitQuantization
{
	public const int PositionStepsPerMeter = 64;
	public const int HealthStepsPerPoint = 64;
	public const int RotationStepsPerTurn = 65536;

	private const float RadiansPerTurn = MathF.PI * 2f;
	private const float RadiansPerStep = RadiansPerTurn / RotationStepsPerTurn;

	public static int QuantizePosition(float meters) => (int)MathF.Round(meters * PositionStepsPerMeter);

	public static float DequantizePosition(int steps) => steps / (float)PositionStepsPerMeter;

	public static float SnapPosition(float meters) => DequantizePosition(QuantizePosition(meters));

	public static int QuantizeHealth(float points) => (int)MathF.Round(points * HealthStepsPerPoint);

	public static float DequantizeHealth(int steps) => steps / (float)HealthStepsPerPoint;

	public static float SnapHealth(float points) => DequantizeHealth(QuantizeHealth(points));

	/// <summary>
	///     A facing as a fraction of a full turn, so a rotation that has gone past several turns still states where the
	///     unit points. A negative rotation is a turn the other way, which is the same facing.
	/// </summary>
	public static ushort QuantizeRotation(float radians)
	{
		float turns = radians / RadiansPerTurn;
		turns -= MathF.Floor(turns);
		int steps = (int)MathF.Round(turns * RotationStepsPerTurn);
		return (ushort)(steps & (RotationStepsPerTurn - 1));
	}

	public static float DequantizeRotation(ushort steps) => steps * RadiansPerStep;

	public static float SnapRotation(float radians) => DequantizeRotation(QuantizeRotation(radians));
}

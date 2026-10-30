using System;

namespace Realm.Shared.Network;

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

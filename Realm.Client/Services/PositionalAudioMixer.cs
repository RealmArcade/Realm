using Godot;

namespace Realm.Client.Services;

public readonly struct AudioListener
{
	public Vector3 Position { get; }
	public Vector3 Forward { get; }
	public Vector3 Up { get; }

	public AudioListener(Vector3 position, Vector3 forward, Vector3 up)
	{
		Position = position;
		Forward = forward;
		Up = up;
	}

	public static AudioListener Default => new(Vector3.Zero, Vector3.Forward, Vector3.Up);
}

public readonly struct PositionalAudioMix
{
	public float GainLinear { get; }
	public float VolumeDb { get; }
	public float Pan { get; }

	public PositionalAudioMix(float gainLinear, float volumeDb, float pan)
	{
		GainLinear = gainLinear;
		VolumeDb = volumeDb;
		Pan = pan;
	}

	public static PositionalAudioMix Silent => new(0f, -80f, 0f);
}

public static class PositionalAudioMixer
{
	public const float RearAttenuation = 0.5f;
	public const float LateralPanStrength = 0.85f;
	public const float MinimumAudibleGain = 0.0001f;

	public static PositionalAudioMix Calculate(in AudioListener listener, Vector3 sourcePosition, float maxDistance, float baseVolumeDb)
	{
		if (maxDistance <= 0f) return PositionalAudioMix.Silent;

		Vector3 toSource = sourcePosition - listener.Position;
		float distance = toSource.Length();
		if (distance >= maxDistance) return PositionalAudioMix.Silent;

		Vector3 direction = distance > 0.0001f ? toSource / distance : listener.Forward;
		Vector3 listenerRight = listener.Forward.Cross(listener.Up);
		listenerRight = listenerRight.LengthSquared() > 0.0001f ? listenerRight.Normalized() : Vector3.Right;

		float distanceFalloff = 1f - (distance / maxDistance);
		distanceFalloff *= distanceFalloff;

		float frontality = Mathf.Clamp(direction.Dot(listener.Forward), -1f, 1f);
		float rearFactor = Mathf.Lerp(1f - RearAttenuation, 1f, (frontality + 1f) * 0.5f);
		float lateralPan = Mathf.Clamp(direction.Dot(listenerRight), -1f, 1f) * LateralPanStrength;

		float gain = distanceFalloff * rearFactor;
		if (gain <= MinimumAudibleGain) return new PositionalAudioMix(0f, -80f, lateralPan);

		return new PositionalAudioMix(gain, baseVolumeDb + Mathf.LinearToDb(gain), lateralPan);
	}
}

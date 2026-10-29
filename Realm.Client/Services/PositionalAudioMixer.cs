using Godot;

namespace Realm.Client.Services;

public enum AudioPriorityTier
{
	Critical,
	General
}

public enum AudioPlaybackChannel
{
	Sfx,
	Voice
}

public static class PositionalAudioProfile
{
	public const AudioStreamPlayer3D.AttenuationModelEnum AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance;
	public const float UnitSize = 10f;
	public const float DefaultMaxDistance = 120f;
	public const float VoiceMaxDistance = 160f;
	public const float MaxVolumeDb = 3f;
	public const float AttenuationFilterCutoffHz = 4500f;
	public const float DisabledAttenuationFilterCutoffHz = 20500f;
	public const float AttenuationFilterDb = -18f;
	public const float RearAttenuation = 0.5f;
	public const float MinimumAudibleGain = 0.0001f;
	private const float DistanceEpsilon = 0.00001f;

	public static float CalculateDistanceGain(float distance, float maxDistance)
	{
		if (maxDistance <= 0f) return 0f;
		if (distance >= maxDistance) return 0f;

		float normalizedDistance = Mathf.Max(distance, 0f) / UnitSize;
		float modelGain;

		switch (AttenuationModel)
		{
			case AudioStreamPlayer3D.AttenuationModelEnum.Disabled:
				modelGain = 1f;
				break;
			case AudioStreamPlayer3D.AttenuationModelEnum.InverseSquareDistance:
				modelGain = 1f / ((normalizedDistance * normalizedDistance) + DistanceEpsilon);
				break;
			case AudioStreamPlayer3D.AttenuationModelEnum.Logarithmic:
				modelGain = Mathf.Pow(10f, -Mathf.Log(normalizedDistance + DistanceEpsilon));
				break;
			default:
				modelGain = 1f / (normalizedDistance + DistanceEpsilon);
				break;
		}

		modelGain = Mathf.Min(modelGain, Mathf.DbToLinear(MaxVolumeDb));

		return modelGain * Mathf.Max(0f, 1f - (distance / maxDistance));
	}
}

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
	public float PerceivedGain { get; }
	public float VolumeDb { get; }

	public PositionalAudioMix(float perceivedGain, float volumeDb)
	{
		PerceivedGain = perceivedGain;
		VolumeDb = volumeDb;
	}

	public static PositionalAudioMix Silent => new(0f, -80f);
}

public static class PositionalAudioMixer
{
	public static PositionalAudioMix Calculate(in AudioListener listener, Vector3 sourcePosition, float maxDistance, float baseVolumeDb)
	{
		if (maxDistance <= 0f) return PositionalAudioMix.Silent;

		Vector3 toSource = sourcePosition - listener.Position;
		float distance = toSource.Length();
		if (distance >= maxDistance) return PositionalAudioMix.Silent;

		Vector3 forward = listener.Forward.LengthSquared() > 0.0001f ? listener.Forward.Normalized() : Vector3.Forward;
		Vector3 direction = distance > 0.0001f ? toSource / distance : forward;

		float distanceGain = PositionalAudioProfile.CalculateDistanceGain(distance, maxDistance);
		float frontality = Mathf.Clamp(direction.Dot(forward), -1f, 1f);
		float directionGain = Mathf.Lerp(1f - PositionalAudioProfile.RearAttenuation, 1f, (frontality + 1f) * 0.5f);

		float spatialGain = distanceGain * directionGain;
		if (spatialGain <= PositionalAudioProfile.MinimumAudibleGain) return PositionalAudioMix.Silent;

		float perceivedGain = spatialGain * Mathf.DbToLinear(baseVolumeDb);
		float volumeDb = baseVolumeDb + Mathf.LinearToDb(directionGain);

		return new PositionalAudioMix(perceivedGain, volumeDb);
	}
}

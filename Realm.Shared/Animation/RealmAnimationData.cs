using MemoryPack;
using System;
using System.IO;

namespace Realm.Shared.Animation;

public enum RealmAnimationLoopMode : byte
{
	None = 0,
	Linear = 1,
	PingPong = 2
}

[MemoryPackable]
public partial struct RealmKeyframeVector3
{
	public float Time { get; set; }
	public float X { get; set; }
	public float Y { get; set; }
	public float Z { get; set; }

	public RealmKeyframeVector3(float time, float x, float y, float z)
	{
		Time = time;
		X = x;
		Y = y;
		Z = z;
	}
}

[MemoryPackable]
public partial struct RealmKeyframeQuaternion
{
	public float Time { get; set; }
	public float X { get; set; }
	public float Y { get; set; }
	public float Z { get; set; }
	public float W { get; set; }

	public RealmKeyframeQuaternion(float time, float x, float y, float z, float w)
	{
		Time = time;
		X = x;
		Y = y;
		Z = z;
		W = w;
	}
}

[MemoryPackable]
public partial class RealmAnimationBoneTrack
{
	public string BoneName { get; set; } = string.Empty;
	public RealmKeyframeVector3[] PositionKeys { get; set; } = Array.Empty<RealmKeyframeVector3>();
	public RealmKeyframeQuaternion[] RotationKeys { get; set; } = Array.Empty<RealmKeyframeQuaternion>();
	public RealmKeyframeVector3[] ScaleKeys { get; set; } = Array.Empty<RealmKeyframeVector3>();
}

[MemoryPackable]
public partial class RealmAnimationData
{
	public int FormatVersion { get; set; } = 1;
	public string Name { get; set; } = string.Empty;
	public float Duration { get; set; }
	public float FrameRate { get; set; } = 30.0f;
	public RealmAnimationLoopMode LoopMode { get; set; } = RealmAnimationLoopMode.Linear;
	public RealmAnimationBoneTrack[] Tracks { get; set; } = Array.Empty<RealmAnimationBoneTrack>();
}

public static class RealmAnimationSerializer
{
	public static byte[] Serialize(RealmAnimationData animationData)
	{
		return RanimFile.Build(null, animationData, compressed: true);
	}

	public static RealmAnimationData Deserialize(ReadOnlySpan<byte> bytes)
	{
		return RanimFile.Parse(bytes).AnimationData;
	}

	public static RealmAnimationData LoadFromFile(string filePath)
	{
		byte[] rawBytes = File.ReadAllBytes(filePath);
		return Deserialize(rawBytes);
	}

	public static void SaveToFile(string filePath, RealmAnimationData animationData)
	{
		string? directory = Path.GetDirectoryName(filePath);
		if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
		{
			Directory.CreateDirectory(directory);
		}

		byte[] serialized = Serialize(animationData);
		File.WriteAllBytes(filePath, serialized);
	}
}

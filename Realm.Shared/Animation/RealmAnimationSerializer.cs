namespace Realm.Shared.Animation;

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
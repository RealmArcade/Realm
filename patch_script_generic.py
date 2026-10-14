import sys

filepath = "Realm.Shared/GlbPlayerColorProcessor.cs"
with open(filepath, "r") as f:
    content = f.read()

target = """	private static void AppendIndexedVec2Faces(List<(Vector2, Vector2, Vector2)> result, List<Vector2> data, List<int> indices)
	{
		for (int i = 0; i + 2 < indices.Count; i += 3)
		{
			int i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];
			if (i0 < data.Count && i1 < data.Count && i2 < data.Count)
			{
				result.Add((data[i0], data[i1], data[i2]));
			}
		}
	}

	private static void AppendSequentialVec2Faces(List<(Vector2, Vector2, Vector2)> result, List<Vector2> data)
	{
		for (int i = 0; i + 2 < data.Count; i += 3)
		{
			result.Add((data[i], data[i + 1], data[i + 2]));
		}
	}"""

replacement = """	private static void AppendIndexedFaces<T>(List<(T, T, T)> result, List<T> data, List<int> indices)
	{
		for (int i = 0; i + 2 < indices.Count; i += 3)
		{
			int i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];
			if (i0 < data.Count && i1 < data.Count && i2 < data.Count)
			{
				result.Add((data[i0], data[i1], data[i2]));
			}
		}
	}

	private static void AppendSequentialFaces<T>(List<(T, T, T)> result, List<T> data)
	{
		for (int i = 0; i + 2 < data.Count; i += 3)
		{
			result.Add((data[i], data[i + 1], data[i + 2]));
		}
	}"""

if target in content:
    content = content.replace(target, replacement)
else:
    print("Target 1 not found")
    sys.exit(1)

content = content.replace("AppendIndexedVec2Faces(result, uvData, indices);", "AppendIndexedFaces(result, uvData, indices);")
content = content.replace("AppendSequentialVec2Faces(result, uvData);", "AppendSequentialFaces(result, uvData);")

target2 = """	private static void AppendIndexedVec3Faces(List<(Vector3, Vector3, Vector3)> result, List<Vector3> data, List<int> indices)
	{
		for (int i = 0; i + 2 < indices.Count; i += 3)
		{
			int i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];
			if (i0 < data.Count && i1 < data.Count && i2 < data.Count)
			{
				result.Add((data[i0], data[i1], data[i2]));
			}
		}
	}

	private static void AppendSequentialVec3Faces(List<(Vector3, Vector3, Vector3)> result, List<Vector3> data)
	{
		for (int i = 0; i + 2 < data.Count; i += 3)
		{
			result.Add((data[i], data[i + 1], data[i + 2]));
		}
	}"""

if target2 in content:
    content = content.replace(target2, "")
else:
    print("Target 2 not found")
    sys.exit(1)

content = content.replace("AppendIndexedVec3Faces(result, posData, indices);", "AppendIndexedFaces(result, posData, indices);")
content = content.replace("AppendSequentialVec3Faces(result, posData);", "AppendSequentialFaces(result, posData);")

with open(filepath, "w") as f:
    f.write(content)
print("Patch successful")

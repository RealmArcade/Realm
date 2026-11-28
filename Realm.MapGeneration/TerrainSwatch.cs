namespace Realm.MapGeneration;

public readonly record struct TerrainSwatch(
	int Index,
	string Name,
	TerrainSurfaceKind Kind,
	int DefaultPathingCode,
	float Red,
	float Green,
	float Blue,
	bool HasColour)
{
	public static TerrainSwatch Empty(int index)
	{
		return new TerrainSwatch(index, string.Empty, TerrainSurfaceKind.Unknown, 44, 0.5f, 0.5f, 0.5f, false);
	}

	public float ColourDistance(float red, float green, float blue)
	{
		float deltaRed = Red - red;
		float deltaGreen = Green - green;
		float deltaBlue = Blue - blue;
		return deltaRed * deltaRed + deltaGreen * deltaGreen + deltaBlue * deltaBlue;
	}
}

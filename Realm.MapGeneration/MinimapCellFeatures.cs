namespace Realm.MapGeneration;

public readonly record struct MinimapCellFeatures(
	float Red,
	float Green,
	float Blue,
	float Hue,
	float Saturation,
	float Value,
	float LocalContrast,
	float TextureDetail);

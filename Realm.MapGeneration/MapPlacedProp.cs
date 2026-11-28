namespace Realm.MapGeneration;

public readonly record struct MapPlacedProp(string TemplateId, float X, float Y, float Z, float RotationY, float Scale)
{
	public PropCategory Category => PropCatalogEntry.Classify(TemplateId, TemplateId);
}

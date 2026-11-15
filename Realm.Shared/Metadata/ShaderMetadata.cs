using Realm.Shared.Serialization;

namespace Realm.Shared.Metadata;

[SchemaDescription("One shader configuration a weapon or effect can be drawn with.")]
public class ShaderMetadata
{
	[SchemaDescription("The shader's settings as a JSON document, written in the shape the shader itself expects.")]
	[SchemaControl(RealmSchemaControl.Json)]
	public string? ConfigJson { get; set; }

	[SchemaDescription("The map the shader was imported from, kept so attribution survives a round trip through the editor.")]
	public string? Attribution_ImportedFromMap { get; set; }
}

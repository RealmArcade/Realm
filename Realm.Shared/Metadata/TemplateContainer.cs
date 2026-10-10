using System.Text.Json;
using System.Text.Json.Serialization;

namespace Realm.Shared.Metadata;

public class TemplateContainer
{
	[JsonPropertyName("Units")]
	public List<UnitMetadata> Units { get; set; } = new();

	[JsonPropertyName("Buildings")]
	public List<UnitMetadata> Buildings { get; set; } = new();

	[JsonPropertyName("Resources")]
	public List<ResourceMetadata> Resources { get; set; } = new();

	[JsonPropertyName("Props")]
	public List<PropMetadata> Props { get; set; } = new();

	[JsonPropertyName("Abilities")]
	public List<AbilityMetadata> Abilities { get; set; } = new();

	[JsonPropertyName("Weapons")]
	public List<WeaponMetadata> Weapons { get; set; } = new();

	[JsonPropertyName("Upgrades")]
	public List<UpgradeMetadata> Upgrades { get; set; } = new();

	[JsonPropertyName("Items")]
	public List<ItemMetadata> Items { get; set; } = new();

	[JsonPropertyName("Attachments")]
	public List<AttachmentMetadata> Attachments { get; set; } = new();

	[JsonPropertyName("Vfx")]
	public List<VfxAttachmentConfig> Vfx { get; set; } = new();

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
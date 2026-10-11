using System;
using System.Collections.Generic;
using Godot;
using Realm.Shared.Metadata;

namespace Realm.Client.Services;

public class RegistryService
{
	public readonly Dictionary<StringName, UnitMetadata> UnitRegistry = new();
	public readonly Dictionary<StringName, UnitMetadata> BuildingRegistry = new();
	public readonly Dictionary<StringName, PropMetadata> PropRegistry = new();
	public readonly Dictionary<StringName, ResourceMetadata> ResourceRegistry = new();
	public readonly Dictionary<StringName, WeaponMetadata> WeaponRegistry = new();
	public readonly Dictionary<StringName, AttachmentMetadata> AttachmentRegistry = new();
	public readonly Dictionary<StringName, ItemMetadata> ItemRegistry = new();
	public Dictionary<string, Realm.Shared.Metadata.VfxAttachmentConfig> VfxRegistry { get; set; } = new(StringComparer.OrdinalIgnoreCase);
	public System.Collections.Generic.Dictionary<string, Realm.Client.Core.AbilityDefinition> AbilityDefinitions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
	public Dictionary<(int UnitUniqueId, string AbilityId), (bool Disabled, bool Hidden)> UnitAbilityStates { get; set; } = new();
	public Dictionary<(int UnitUniqueId, string AbilityId), float> UnitAbilityManaCosts { get; set; } = new();
	public Dictionary<string, string> ItemTooltips { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    
    public void ClearAll()
    {
        UnitRegistry.Clear();
        BuildingRegistry.Clear();
        PropRegistry.Clear();
        ResourceRegistry.Clear();
        WeaponRegistry.Clear();
        AttachmentRegistry.Clear();
        ItemRegistry.Clear();
        VfxRegistry.Clear();
        AbilityDefinitions.Clear();
        UnitAbilityStates.Clear();
        UnitAbilityManaCosts.Clear();
        ItemTooltips.Clear();
    }
}

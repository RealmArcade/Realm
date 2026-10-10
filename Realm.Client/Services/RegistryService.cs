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
    
    public void ClearAll()
    {
        UnitRegistry.Clear();
        BuildingRegistry.Clear();
        PropRegistry.Clear();
        ResourceRegistry.Clear();
        WeaponRegistry.Clear();
        AttachmentRegistry.Clear();
        ItemRegistry.Clear();
    }
}

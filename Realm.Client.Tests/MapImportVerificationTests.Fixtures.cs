using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using GdUnit4;
using Godot;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.UI.MapEditor;
using Realm.Shared.Distribution;
using Environment = System.Environment;

namespace Realm.Client.Tests;

/// <summary>metadata and code fixtures for the source map.</summary>
public partial class MapImportVerificationTests
	{
	private static JsonObject BuildTemplateMetadata()
	{
		var metadata = new JsonObject { ["GameBuildNumber"] = "v0.0.4" };
		var templates = new JsonObject
		{
			["Units"] = new JsonArray
			{
				new JsonObject
				{
					["TemplateID"] = "unit/source_knight",
					["Name"] = "Source Knight",
					["Weapons"] = new JsonArray { "weapon/source_blade" },
					["Abilities"] = new JsonArray { "ability/source_guard" },
					["VisualEffect"] = "vfx/source_glow"
				}
			},
			["Weapons"] = new JsonArray
			{
				new JsonObject
				{
					["TemplateID"] = "weapon/source_blade",
					["Name"] = "Source Blade",
					["Abilities"] = new JsonArray { "ability/source_cleave" }
				}
			},
			["Abilities"] = new JsonArray
			{
				new JsonObject
				{
					["TemplateID"] = "ability/source_cleave",
					["Name"] = "Source Cleave"
				},
				new JsonObject
				{
					["TemplateID"] = "ability/source_guard",
					["Name"] = "Source Guard"
				}
			},
			["Vfx"] = new JsonArray
			{
				new JsonObject
				{
					["VfxId"] = "vfx/source_glow",
					["Name"] = "Source Glow",
					["DecalId"] = "source_scorch"
				}
			},
			["Props"] = new JsonArray
			{
				new JsonObject
				{
					["TemplateID"] = "prop/source_rock",
					["Name"] = "Source Rock"
				}
			}
		};

		metadata["Templates"] = templates;
		metadata["decals"] = new JsonObject
		{
			["source_scorch"] = new JsonObject { ["Name"] = "Source Scorch" }
		};

		return metadata;
	}

	private static JsonObject BuildEmptyMetadata()
	{
		return new JsonObject
		{
			["GameBuildNumber"] = "v0.0.4",
			["Templates"] = new JsonObject()
		};
	}

	private static Dictionary<string, string> BuildCodeFiles()
	{
		return new Dictionary<string, string>
		{
			["MapScript.cs"] = """
			namespace Realm.Maps;

			using Realm.MapAPI;

			public class MapScript : IWasmModule
			{
				public void Initialize(IGameAPI api)
				{
				}

				public void Update(IGameAPI api, float delta)
				{
				}
			}
			""",
			["AncientUnitBase.cs"] = """
			namespace Ancient.Core;

			public abstract class AncientUnitBase
			{
				protected AncientStats Stats = new AncientStats();
			}
			""",
			["AncientStats.cs"] = """
			namespace Ancient.Core;

			public class AncientStats
			{
				public int Strength;
			}
			""",
			["AncientDamageType.cs"] = """
			namespace Ancient.Core;

			public enum AncientDamageType
			{
				Physical,
				Fire
			}
			""",
			["AncientAbility.cs"] = """
			namespace Ancient.Core;

			public class AncientAbility
			{
				public string Name = string.Empty;
			}
			""",
			["AncientWeapon.cs"] = """
			namespace Ancient.Core;

			using System.Collections.Generic;

			public class AncientWeapon
			{
				public AncientDamageType DamageType;
				public List<AncientAbility> Abilities = new List<AncientAbility>();
			}
			""",
			["AncientArchetype.cs"] = """
			namespace Ancient.Core;

			public record AncientArchetype(string Name);
			""",
			["AncientKnight.cs"] = """
			namespace Ancient.Core;

			using System.Collections.Generic;
			using Realm.Maps;

			/// <summary>An imported knight.</summary>
			[Attribution_SourceMap("Older Map")]
			public class AncientKnight : AncientUnitBase
			{
				public AncientWeapon PrimaryWeapon = new AncientWeapon();
				public List<AncientWeapon> SpareWeapons = new List<AncientWeapon>();
				public AncientArchetype Archetype = new AncientArchetype("Knight");
			}
			""",
			["AncientBanner.cs"] = """
			namespace Ancient.Core.Extra;

			public class AncientBanner : Ancient.Core.AncientUnitBase
			{
			}
			""",
			["AncientUnrelated.cs"] = """
			namespace Ancient.Other;

			public class AncientUnrelated
			{
			}
			"""
		};
	}
}

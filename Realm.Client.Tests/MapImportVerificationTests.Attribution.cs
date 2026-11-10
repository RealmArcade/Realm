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

/// <summary>collecting attribution into the destination manifest.</summary>
public partial class MapImportVerificationTests
	{
	[TestCase]
	public void TestAttributionCollectionWritesSortedManifestField()
	{
		string root = CreateTempRoot("attribution");
		try
		{
			string workspacePath = CreateDestinationWorkspace(root);
			string metadataPath = Path.Combine(workspacePath, "metadata.json");
			File.WriteAllText(metadataPath, """
			{
			  "GameBuildNumber": "v0.0.4",
			  "Templates": {
			    "Units": [
			      { "TemplateID": "unit/imported", "Name": "Imported", "Attribution_ImportedFromMap": "Zeta Map" },
			      { "TemplateID": "unit/also_imported", "Name": "Also Imported", "Attribution_ImportedFromMap": "Alpha Map" }
			    ]
			  }
			}
			""");

			WriteTextFile(workspacePath, "MapScript.cs", """
			namespace Realm.Maps;

			[Attribution_SourceMap("alpha map")]
			public class MapScript
			{
			}
			""");

			WriteTextFile(workspacePath, "imports/Source Map/Ancient/Core/AncientKnight.cs", """
			using Realm.Maps;

			namespace Ancient.Core;

			[Attribution_SourceMap("alpha map")]
			[Attribution_SourceMap("Beta Map")]
			public class AncientKnight
			{
			}
			""");

			WriteTextFile(workspacePath, "bin/Ignored.cs", """
			[Attribution_SourceMap("Ignored Map")]
			public class Ignored
			{
			}
			""");

			WriteTextFile(workspacePath, "obj/IgnoredToo.cs", """
			[Attribution_SourceMap("Also Ignored")]
			public class IgnoredToo
			{
			}
			""");

			GameContext.EnsureServices();
			MapAttributionService service = GameContext.MapAttributionService;
			IReadOnlyList<string> collected = service.CollectSortedSourceMapNames(workspacePath);

			Assertions.AssertThat(string.Join("|", collected)).IsEqual("Alpha Map|Beta Map|Zeta Map");

			var manifest = new MapManifest { MapName = "Destination" };
			manifest.SaveToFile(Path.Combine(workspacePath, "manifest.json"));

			service.UpdateManifestAttribution(workspacePath);
			MapManifest reloaded = MapManifest.LoadFromFile(Path.Combine(workspacePath, "manifest.json"));
			Assertions.AssertThat(string.Join("|", reloaded.Attribution_SourceMap)).IsEqual("Alpha Map|Beta Map|Zeta Map");

			var appliedManifest = new MapManifest();
			service.ApplyAttribution(appliedManifest, workspacePath);
			Assertions.AssertThat(string.Join("|", appliedManifest.Attribution_SourceMap)).IsEqual("Alpha Map|Beta Map|Zeta Map");

			string schema = MapManifest.GenerateJsonSchema();
			Assertions.AssertThat(schema.Contains("Attribution_SourceMap")).IsTrue();
		}
		finally
		{
			DeleteTempRoot(root);
		}
	}
}

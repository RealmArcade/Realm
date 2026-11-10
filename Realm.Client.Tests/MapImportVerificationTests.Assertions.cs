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

/// <summary>shared assertions for the imported manifest.</summary>
public partial class MapImportVerificationTests
	{
	private static void VerifyAttributionPopover(Control anchor)
	{
		var host = new Control { Name = "AttributionPopoverVerificationHost" };
		var sceneTree = Engine.GetMainLoop() as SceneTree;
		sceneTree?.Root.AddChild(host);

		var popover = new AttributionPopover();
		host.AddChild(popover);

		try
		{
			popover.ShowFor(anchor, new[] { "Zeta Map", "Alpha Map", "alpha map" });
			Assertions.AssertThat(popover.Visible).IsTrue();

			var sourceList = popover.FindChild("SourceList_VBoxContainer", true, false) as VBoxContainer;
			Assertions.AssertThat(sourceList != null).IsTrue();

			var labels = new List<string>();
			foreach (Node child in sourceList!.GetChildren())
			{
				if (child is PanelContainer row && row.GetChildCount() > 0 && row.GetChild(0) is Label label)
				{
					labels.Add(label.Text);
				}
			}

			Assertions.AssertThat(string.Join("|", labels)).OverrideFailureMessage($"Popover labels: {string.Join("|", labels)}").IsEqual("Alpha Map|Zeta Map");

			var emptyLabel = popover.FindChild("EmptyLabel_Label", true, false) as Label;
			Assertions.AssertThat(emptyLabel != null).IsTrue();
			Assertions.AssertThat(emptyLabel!.Visible).IsFalse();

			popover.ShowFor(anchor, Array.Empty<string>());
			Assertions.AssertThat(emptyLabel.Visible).IsTrue();

			popover.Close();
			Assertions.AssertThat(popover.Visible).IsFalse();
		}
		finally
		{
			host.Free();
		}
	}

	private static void AssertSingleImportedTemplate(JsonObject templates, string sectionKey, string templateIdentity)
	{
		JsonArray entries = templates[sectionKey]!.AsArray();
		JsonObject? entry = entries
			.Select(node => node!.AsObject())
			.FirstOrDefault(candidate => candidate["TemplateID"]?.GetValue<string>() == templateIdentity);
		if (entry == null && sectionKey == "Vfx")
		{
			entry = entries
				.Select(node => node!.AsObject())
				.FirstOrDefault(candidate => candidate["VfxId"]?.GetValue<string>() == templateIdentity);
		}

		Assertions.AssertThat(entry != null).OverrideFailureMessage($"{sectionKey} did not contain {templateIdentity}.").IsTrue();
		Assertions.AssertThat(entry!["Attribution_ImportedFromMap"]?.GetValue<string>()).IsEqual(SourceAttribution);
	}
}

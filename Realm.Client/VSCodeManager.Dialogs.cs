using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.UI.MapEditor;
using Realm.Ecs.Services;
using Realm.EditorAPI;
using Realm.Shared.Audio;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Services;
using Realm.Shared.Textures;
using Environment = System.Environment;
using FileAccess = Godot.FileAccess;
using HttpClient = System.Net.Http.HttpClient;
using Vector3 = System.Numerics.Vector3;

namespace Realm.Client;

[SupportedOSPlatform("windows")]
public partial class VSCodeManager
{
	private void HandleOpenVfxDialog(JsonNode? node, JsonObject responseObj)
	{
		string weaponId = node?["weaponId"]?.ToString() ?? "";
		var weaponDataNode = node?["weaponData"];

		Callable.From(() =>
		{
			WeaponMetadata? meta = null;
			if (!string.IsNullOrEmpty(weaponId) && GameHost.WeaponRegistry.TryGetValue(weaponId, out var existing))
			{
				meta = existing;
			}
			else if (weaponDataNode != null)
			{
				try
				{
					meta = JsonSerializer.Deserialize<WeaponMetadata>(
						weaponDataNode.ToJsonString(),
						new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
					);
				}
				catch (Exception ex)
				{
					GameLog.LogError($"[VSCodeManager] openVfxDialog deserialize error: {ex.Message}");
				}
			}

			if (GameContext.MapEditorHud != null)
			{
				GameContext.MapEditorHud.OpenWeaponVfxDialog(weaponId, meta, (updatedMeta) => { GameContext.MapEditorHud.SaveCustomWeaponToMetadata(weaponId, updatedMeta); });
			}
		}).CallDeferred();

		responseObj["action"] = "openVfxDialogResult";
		responseObj["success"] = true;
	}

	private void HandleOpenModelPicker(JsonNode? node, JsonObject responseObj)
	{
		string entityId = GetStringProperty(node, "unitId", "");
		if (string.IsNullOrEmpty(entityId)) entityId = GetStringProperty(node, "entityId", "");
		string fieldName = GetStringProperty(node, "field", "ModelPath");
		string domain = GetStringProperty(node, "domain", "units");
		string currentPath = GetStringProperty(node, "currentPath", "");

		Callable.From(() =>
		{
			if (GameContext.MapEditorHud == null) return;

			GameContext.MapEditorHud.OpenModelPickerDialog(entityId, fieldName, domain, currentPath, (updatedPath) => { GameContext.MapEditorHud.SaveEntityModelPathToMetadata(entityId, fieldName, domain, updatedPath); });
		}).CallDeferred();

		responseObj["action"] = "openModelPickerResult";
		responseObj["success"] = true;
	}


	private void HandleOpenAbilityVfxDialog(JsonNode? node, JsonObject responseObj)
	{
		string abilityId = GetStringProperty(node, "abilityId", "");
		var abilityDataNode = node?["abilityData"] as JsonObject;

		Callable.From(() =>
		{
			if (GameContext.MapEditorHud == null) return;

			GameContext.MapEditorHud.OpenAbilityVfxDialog(abilityId, abilityDataNode, (updatedData) =>
			{
				if (updatedData == null) return;

				string vfx = GetStringProperty(updatedData, "VisualEffect", "");
				string sound = GetStringProperty(updatedData, "CastSound", "");
				string icon = GetStringProperty(updatedData, "IconPath", "");
				float aoe = GetFloatProperty(updatedData, "AreaOfEffectRadius", 0f);

				GameContext.MapEditorHud.SaveCustomAbilityVfxToMetadata(abilityId, vfx, sound, icon, aoe);
			});
		}).CallDeferred();

		responseObj["action"] = "openAbilityVfxDialogResult";
		responseObj["success"] = true;
	}


	private void HandleOpenAnimationStudio(JsonNode? node, JsonObject responseObj)
	{
		string unitId = node?["unitId"]?.ToString() ?? node?["entityId"]?.ToString() ?? "";
		string modelPath = node?["modelPath"]?.ToString() ?? "";

		Callable.From(() =>
		{
			if (GameContext.MapEditorHud != null)
			{
				GameContext.MapEditorHud.OpenAnimationPreviewDialog(unitId, modelPath);
			}
		}).CallDeferred();

		responseObj["action"] = "openAnimationStudioResult";
		responseObj["success"] = true;
	}

}

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
	private void HandleFormatAndSaveJson(JsonNode? node, string action, JsonObject responseObj)
	{
		string filePath = GetSaveFilePath(node, action);
		string content = node?["content"]?.ToString() ?? node?["text"]?.ToString() ?? "";
		string requestId = node?["requestId"]?.ToString() ?? "";

		var (success, formattedContent, errorMsg) = TryFormatAndSaveJsonFile(filePath, content);

		if (success)
		{
			NotifyEditorOfFileUpdate(filePath);
		}

		PopulateFormatAndSaveJsonResponse(responseObj, requestId, success, filePath, formattedContent, errorMsg);
	}


	private string GetSaveFilePath(JsonNode? node, string action)
	{
		string filePath = node?["filePath"]?.ToString() ?? "";
		if (string.IsNullOrEmpty(filePath))
		{
			string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
			filePath = Path.Combine(wsPath, action == "saveTerrain" ? "terrain.json" : "metadata.json");
		}
		else if (!Path.IsPathRooted(filePath))
		{
			string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
			filePath = Path.Combine(wsPath, filePath);
		}

		return filePath;
	}


	private (bool success, string formattedContent, string errorMsg) TryFormatAndSaveJsonFile(string filePath, string content)
	{
		try
		{
			string fileName = Path.GetFileName(filePath).ToLowerInvariant();

			string formattedContent = FormatSpecialJsonFile(fileName, filePath, content);

			return (true, formattedContent, "");
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[VSCodeManager] formatAndSaveJson error: {ex.Message}");
			return (false, "", ex.Message);
		}
	}

	private string FormatSpecialJsonFile(string fileName, string filePath, string content)
	{
		string formattedContent = "";
		if (fileName == "metadata.json")
		{
			try
			{
				var metadata = MapFileService.LoadMetadataFromJson(content);
				MapFileService.SaveMetadata(filePath, metadata);
				formattedContent = MapFileService.SaveMetadataToJson(metadata);
			}
			catch
			{
				formattedContent = MapJsonFormatter.FormatJson(content);
				MapJsonFormatter.SaveFormattedJson(filePath, formattedContent);
			}
		}
		else if (fileName == "manifest.json")
		{
			try
			{
				var manifest = MapFileService.LoadManifestFromJson(content);
				MapFileService.SaveManifest(filePath, manifest);
				formattedContent = MapFileService.SaveManifestToJson(manifest);
			}
			catch
			{
				formattedContent = MapJsonFormatter.FormatJson(content);
				MapJsonFormatter.SaveFormattedJson(filePath, formattedContent);
			}
		}
		else if (fileName == "terrain.json")
		{
			try
			{
				var terrain = MapFileService.LoadTerrainFromJson(content);
				MapFileService.SaveTerrain(filePath, terrain);
				formattedContent = MapFileService.SaveTerrainToJson(terrain);
			}
			catch
			{
				formattedContent = MapJsonFormatter.FormatJson(content);
				MapJsonFormatter.SaveFormattedJson(filePath, formattedContent);
			}
		}
		else
		{
			formattedContent = MapJsonFormatter.FormatJson(content);
			MapJsonFormatter.SaveFormattedJson(filePath, formattedContent);
		}

		return formattedContent;
	}


	private void NotifyEditorOfFileUpdate(string filePath)
	{
		Callable.From(() =>
		{
			string fileName = Path.GetFileName(filePath).ToLowerInvariant();
			if (fileName == "metadata.json" || fileName == "manifest.json")
			{
				HandleMetadataUpdate(fileName);
			}
			else if (fileName == "terrain.json")
			{
				HandleTerrainUpdate(filePath);
			}
		}).CallDeferred();
	}

	private void HandleMetadataUpdate(string fileName)
	{
		string display = fileName == "manifest.json" ? "manifest.json" : (MapWorkspaceService.GetActiveWorkspacePath() != null ? "Workspace Metadata" : "metadata.json");
		Action reloadAction = () =>
		{
			if (GameContext.MapEditorHud != null)
			{
				GameContext.MapEditorHud.ReadMetadataAndRefreshTextures();
				GameContext.MapEditorHud.ShowFeedback(string.Format(TranslationServer.Translate("{0} updated externally â€” reloaded."), display));
			}
			else if (GameContext.Host != null && GameContext.Host.GroundTerrain != null)
			{
				GameContext.Host.GroundTerrain.ReloadTerrainTextures(true);
			}
		};

		if (FloatingDialogBase.HasAnyDialogOpen && GameContext.MapEditorHud != null)
		{
			GameContext.MapEditorHud.ShowConfirmationDialog(
				$"External edits detected in {display}. Reload external changes or keep current dialog changes?",
				onConfirm: reloadAction,
				confirmText: TranslationServer.Translate("RELOAD"),
				cancelText: TranslationServer.Translate("KEEP CHANGES")
			);
		}
		else
		{
			reloadAction();
		}
	}


	private void HandleTerrainUpdate(string filePath)
	{
		Action reloadAction = () =>
		{
			if (GameContext.Host != null && GameContext.SessionState.IsMapEditorMode)
			{
				GameContext.Host.LoadMapFromFile(filePath);
				GameContext.MapEditorHud?.ShowFeedback(TranslationServer.Translate("terrain.json updated externally â€” reloaded."));
			}
		};

		if (FloatingDialogBase.HasAnyDialogOpen && GameContext.MapEditorHud != null)
		{
			GameContext.MapEditorHud.ShowConfirmationDialog(
				TranslationServer.Translate("External edits detected in terrain.json. Reload external changes or keep current dialog changes?"),
				onConfirm: reloadAction,
				confirmText: TranslationServer.Translate("RELOAD"),
				cancelText: TranslationServer.Translate("KEEP CHANGES")
			);
		}
		else
		{
			reloadAction();
		}
	}

	private void PopulateFormatAndSaveJsonResponse(JsonObject responseObj, string requestId, bool success, string filePath, string formattedContent, string errorMsg)
	{
		responseObj["action"] = "formatAndSaveJsonResult";
		responseObj["type"] = "formatAndSaveJsonResult";
		responseObj["requestId"] = requestId;
		responseObj["success"] = success;
		responseObj["filePath"] = filePath;
		responseObj["formattedContent"] = formattedContent;
		responseObj["error"] = errorMsg;
	}

}

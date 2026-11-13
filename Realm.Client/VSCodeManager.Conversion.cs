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
	private void HandleConvertRtex(JsonNode? node, JsonObject responseObj)
	{
		string inputPath = node?["inputPath"]?.ToString() ?? node?["filePath"]?.ToString() ?? "";
		string outputPath = node?["outputPath"]?.ToString() ?? "";
		int layer = node?["layer"] is JsonValue layerNode ? layerNode.GetValue<int>() : 0;

		try
		{
			var res = TextureConverter.ExtractPngFromRtex(inputPath, outputPath, layer);
			PopulateRtexConversionResponse(responseObj, res, inputPath);
		}
		catch (Exception ex)
		{
			PopulateErrorResponse(responseObj, "convertRtexResult", ex.Message);
		}
	}

	private void PopulateRtexConversionResponse(JsonObject responseObj, TextureConversionResult res, string inputPath)
	{
		responseObj["action"] = "convertRtexResult";
		responseObj["type"] = "convertRtexResult";
		responseObj["success"] = res.Success;

		if (!res.Success)
		{
			responseObj["error"] = res.ErrorMessage;
			return;
		}

		responseObj["outputPath"] = res.OutputPath;
		AttachRtexMetadataIfExists(responseObj, inputPath);
	}

	private void AttachRtexMetadataIfExists(JsonObject responseObj, string inputPath)
	{
		try
		{
			if (File.Exists(inputPath))
			{
				byte[] rtexBytes = File.ReadAllBytes(inputPath);
				string? metaJson = RealmContainerFile.ExtractMetadataFromBytes(rtexBytes, RtexFile.MagicBytes);
				if (!string.IsNullOrEmpty(metaJson))
				{
					responseObj["metadata"] = JsonNode.Parse(metaJson);
				}
			}
		}
		catch
		{
		}
	}

	private void PopulateErrorResponse(JsonObject responseObj, string actionName, string errorMessage)
	{
		responseObj["action"] = actionName;
		responseObj["type"] = actionName;
		responseObj["success"] = false;
		responseObj["error"] = errorMessage;
	}


	private void HandleRenderRanim(JsonNode? node, JsonObject responseObj)
	{
		string inputPath = node?["inputPath"]?.ToString() ?? node?["filePath"]?.ToString() ?? "";
		string outputPath = node?["outputPath"]?.ToString() ?? "";
		try
		{
			var options = new RanimRenderOptions
			{
				Format = RanimOutputFormat.Webp,
				Width = 128,
				Height = 128,
				Fps = 12.0f
			};
			var res = RanimRenderer.ExportFile(inputPath, outputPath, options);
			responseObj["action"] = "renderRanimResult";
			responseObj["type"] = "renderRanimResult";
			responseObj["success"] = res.Success;
			if (!res.Success)
			{
				responseObj["error"] = res.ErrorMessage;
			}
			else
			{
				responseObj["outputPath"] = res.OutputPath;
			}
		}
		catch (Exception ex)
		{
			responseObj["action"] = "renderRanimResult";
			responseObj["type"] = "renderRanimResult";
			responseObj["success"] = false;
			responseObj["error"] = ex.Message;
		}
	}

	private void HandleConvertRmesh(JsonNode? node, JsonObject responseObj)
	{
		string inputPath = node?["inputPath"]?.ToString() ?? node?["filePath"]?.ToString() ?? "";
		string outputPath = node?["outputPath"]?.ToString() ?? "";

		try
		{
			if (!File.Exists(inputPath))
			{
				PopulateErrorResponse(responseObj, "convertRmeshResult", $"RMESH file not found: {inputPath}");
				return;
			}

			byte[] rmeshBytes = File.ReadAllBytes(inputPath);
			var (metaJson, glbBytes, _) = RmeshFile.Parse(rmeshBytes);

			if (glbBytes.Length == 0)
			{
				PopulateErrorResponse(responseObj, "convertRmeshResult", "No GLB payload found in RMESH file.");
				return;
			}

			SaveGlbBytes(outputPath, glbBytes);
			PopulateRmeshConversionResponse(responseObj, outputPath, metaJson);
		}
		catch (Exception ex)
		{
			PopulateErrorResponse(responseObj, "convertRmeshResult", ex.Message);
		}
	}

	private void SaveGlbBytes(string outputPath, byte[] glbBytes)
	{
		if (string.IsNullOrEmpty(outputPath)) return;

		string? dir = Path.GetDirectoryName(outputPath);
		if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
			Directory.CreateDirectory(dir);

		File.WriteAllBytes(outputPath, glbBytes);
	}

	private void PopulateRmeshConversionResponse(JsonObject responseObj, string outputPath, string? metaJson)
	{
		responseObj["action"] = "convertRmeshResult";
		responseObj["type"] = "convertRmeshResult";
		responseObj["success"] = true;
		responseObj["outputPath"] = outputPath;

		if (!string.IsNullOrEmpty(metaJson))
		{
			responseObj["metadata"] = JsonNode.Parse(metaJson);
		}
	}


	private string GetStringProperty(JsonNode? node, string key, string defaultValue = "")
	{
		if (node == null) return defaultValue;
		var val = node[key];
		if (val == null) return defaultValue;
		return val.ToString() ?? defaultValue;
	}

	private int GetIntProperty(JsonNode? node, string key, int defaultValue = 0)
	{
		if (node == null) return defaultValue;
		var val = node[key];
		if (val == null) return defaultValue;
		return (int)val;
	}

	private void HandleConvertRaud(JsonNode? node, JsonObject responseObj)
	{
		string inputPath = GetStringProperty(node, "inputPath", "");
		if (string.IsNullOrEmpty(inputPath)) inputPath = GetStringProperty(node, "filePath", "");
		string outputPath = GetStringProperty(node, "outputPath", "");
		int trackIndex = GetIntProperty(node, "trackIndex", 0);

		try
		{
			if (!File.Exists(inputPath))
			{
				PopulateErrorResponse(responseObj, "convertRaudResult", $"RAUD file not found: {inputPath}");
				return;
			}

			byte[] raudBytes = File.ReadAllBytes(inputPath);
			var (metaJson, entries, _) = RaudFile.Parse(raudBytes);

			if (entries.Count <= trackIndex)
			{
				PopulateErrorResponse(responseObj, "convertRaudResult", $"Audio track {trackIndex} not found in RAUD file.");
				return;
			}

			if (entries[trackIndex].Data.Length == 0)
			{
				PopulateErrorResponse(responseObj, "convertRaudResult", $"Audio track {trackIndex} not found in RAUD file.");
				return;
			}

			SaveAudioTrack(outputPath, entries[trackIndex].Data);
			PopulateRaudConversionResponse(responseObj, outputPath, entries.Count, metaJson);
		}
		catch (Exception ex)
		{
			PopulateErrorResponse(responseObj, "convertRaudResult", ex.Message);
		}
	}

	private void SaveAudioTrack(string outputPath, byte[] trackData)
	{
		if (string.IsNullOrEmpty(outputPath)) return;

		string? dir = Path.GetDirectoryName(outputPath);
		if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
			Directory.CreateDirectory(dir);

		File.WriteAllBytes(outputPath, trackData);
	}

	private void PopulateRaudConversionResponse(JsonObject responseObj, string outputPath, int trackCount, string? metaJson)
	{
		responseObj["action"] = "convertRaudResult";
		responseObj["type"] = "convertRaudResult";
		responseObj["success"] = true;
		responseObj["outputPath"] = outputPath;
		responseObj["trackCount"] = trackCount;

		if (!string.IsNullOrEmpty(metaJson))
		{
			responseObj["metadata"] = JsonNode.Parse(metaJson);
		}
	}


}

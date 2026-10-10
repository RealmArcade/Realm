using Realm.Client.Services;
using System;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Realm.Client.Utils;

public static class MapJsonFormatter
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true,
		IndentCharacter = '\t',
		IndentSize = 1,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
		NewLine = "\n"
	};

	private static readonly JsonDocumentOptions DocumentOptions = new()
	{
		AllowTrailingCommas = true,
		CommentHandling = JsonCommentHandling.Skip
	};

	public static JsonNode? SortKeysRecursively(JsonNode? node)
	{
		if (node is JsonArray arr)
		{
			return SortJsonArray(arr);
		}

		if (node is not JsonObject obj)
		{
			return node?.DeepClone();
		}

		return SortJsonObject(obj);
	}

	private static JsonArray SortJsonArray(JsonArray arr)
	{
		var newArr = new JsonArray();
		foreach (var item in arr)
		{
			newArr.Add(item != null ? SortKeysRecursively(item.DeepClone()) : null);
		}
		return newArr;
	}

	private static JsonObject SortJsonObject(JsonObject obj)
	{
		var sortedObj = new JsonObject();
		var keys = obj.Select(kvp => kvp.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();

		float topLeftX = -GetMapDimension(obj, "Width") / 2.0f;
		float topLeftZ = -GetMapDimension(obj, "Depth") / 2.0f;

		foreach (var key in keys)
		{
			ProcessObjectKey(obj, sortedObj, key, topLeftX, topLeftZ);
		}

		return sortedObj;
	}

	private static float GetMapDimension(JsonObject obj, string dimension)
	{
		return obj.TryGetPropertyValue(dimension, out var node) && float.TryParse(node?.ToString(), out float val) && val > 0 ? val : 128f;
	}

	private static void ProcessObjectKey(JsonObject obj, JsonObject sortedObj, string key, float topLeftX, float topLeftZ)
	{
		var value = obj[key];

		if (key == "Units" && TryGetJsonObjectArray(value, out var unitsArr))
		{
			sortedObj[key] = SortUnits(unitsArr, topLeftX, topLeftZ);
			return;
		}

		if (key == "Props" && TryGetJsonObjectArray(value, out var propsArr))
		{
			sortedObj[key] = SortProps(propsArr, topLeftX, topLeftZ);
			return;
		}

		if (key == "Decals" && TryGetJsonObjectArray(value, out var decalsArr))
		{
			sortedObj[key] = SortDecals(decalsArr, topLeftX, topLeftZ);
			return;
		}

		if (key == "Coordinates" && TryGetJsonObjectArray(value, out var coordsArr))
		{
			sortedObj[key] = SortCoordinates(coordsArr, topLeftX, topLeftZ);
			return;
		}

		sortedObj[key] = value != null ? SortKeysRecursively(value.DeepClone()) : null;
	}

	private static bool TryGetJsonObjectArray(JsonNode? node, out JsonArray array)
	{
		array = node as JsonArray;
		return array != null && array.All(item => item is JsonObject);
	}

	private static JsonArray SortUnits(JsonArray unitsArr, float topLeftX, float topLeftZ)
	{
		var sortedList = unitsArr.OfType<JsonObject>()
			.OrderBy(item => GetStringProperty(item, "TemplateId"), StringComparer.OrdinalIgnoreCase)
			.ThenBy(item => GetStringProperty(item, "TemplateId"), StringComparer.Ordinal)
			.ThenBy(item => CalculateDistance(item, "PosX", "PosZ", topLeftX, topLeftZ))
			.ThenBy(item => GetFloatProperty(item, "PosX"))
			.ThenBy(item => GetFloatProperty(item, "PosZ"))
			.ThenBy(item => GetFloatProperty(item, "PosY"))
			.ThenBy(item => GetFloatProperty(item, "RotationX"))
			.ThenBy(item => GetFloatProperty(item, "RotationY"))
			.ThenBy(item => GetFloatProperty(item, "RotationZ"))
			.ThenBy(item => GetFloatProperty(item, "Scale"))
			.ThenBy(item => GetFloatProperty(item, "Player"))
			.ThenBy(item => GetBoolProperty(item, "IsEnemy"))
			.ToList();

		return CreateSortedArray(sortedList);
	}

	private static JsonArray SortProps(JsonArray propsArr, float topLeftX, float topLeftZ)
	{
		var sortedList = propsArr.OfType<JsonObject>()
			.OrderBy(item => GetStringProperty(item, "TemplateId"), StringComparer.OrdinalIgnoreCase)
			.ThenBy(item => GetStringProperty(item, "TemplateId"), StringComparer.Ordinal)
			.ThenBy(item => CalculateDistance(item, "PosX", "PosZ", topLeftX, topLeftZ))
			.ThenBy(item => GetFloatProperty(item, "PosX"))
			.ThenBy(item => GetFloatProperty(item, "PosZ"))
			.ThenBy(item => GetFloatProperty(item, "PosY"))
			.ThenBy(item => GetFloatProperty(item, "RotationX"))
			.ThenBy(item => GetFloatProperty(item, "RotationY"))
			.ThenBy(item => GetFloatProperty(item, "RotationZ"))
			.ThenBy(item => GetFloatProperty(item, "Scale"))
			.ToList();

		return CreateSortedArray(sortedList);
	}

	private static JsonArray SortDecals(JsonArray decalsArr, float topLeftX, float topLeftZ)
	{
		var sortedList = decalsArr.OfType<JsonObject>()
			.OrderBy(item => GetStringProperty(item, "TemplateId"), StringComparer.OrdinalIgnoreCase)
			.ThenBy(item => GetStringProperty(item, "TemplateId"), StringComparer.Ordinal)
			.ThenBy(item => CalculateDistance(item, "PosX", "PosZ", topLeftX, topLeftZ))
			.ThenBy(item => GetFloatProperty(item, "PosX"))
			.ThenBy(item => GetFloatProperty(item, "PosZ"))
			.ThenBy(item => GetFloatProperty(item, "PosY"))
			.ThenBy(item => GetFloatProperty(item, "RotationY"))
			.ThenBy(item => GetFloatProperty(item, "Scale"))
			.ToList();

		return CreateSortedArray(sortedList);
	}

	private static JsonArray SortCoordinates(JsonArray coordsArr, float topLeftX, float topLeftZ)
	{
		var sortedList = coordsArr.OfType<JsonObject>()
			.OrderBy(item => GetStringProperty(item, "Name"), StringComparer.OrdinalIgnoreCase)
			.ThenBy(item => GetStringProperty(item, "Name"), StringComparer.Ordinal)
			.ThenBy(item =>
			{
				float midX = (GetFloatProperty(item, "MinX") + GetFloatProperty(item, "MaxX")) * 0.5f;
				float midZ = (GetFloatProperty(item, "MinZ") + GetFloatProperty(item, "MaxZ")) * 0.5f;
				return MathF.Sqrt(MathF.Pow(midX - topLeftX, 2) + MathF.Pow(midZ - topLeftZ, 2));
			})
			.ThenBy(item => GetFloatProperty(item, "MinX"))
			.ThenBy(item => GetFloatProperty(item, "MinZ"))
			.ThenBy(item => GetFloatProperty(item, "MaxX"))
			.ThenBy(item => GetFloatProperty(item, "MaxZ"))
			.ToList();

		return CreateSortedArray(sortedList);
	}

	private static float CalculateDistance(JsonObject item, string propX, string propZ, float topLeftX, float topLeftZ)
	{
		float x = GetFloatProperty(item, propX);
		float z = GetFloatProperty(item, propZ);
		return MathF.Sqrt(MathF.Pow(x - topLeftX, 2) + MathF.Pow(z - topLeftZ, 2));
	}

	private static JsonArray CreateSortedArray(System.Collections.Generic.List<JsonObject> sortedList)
	{
		var newArr = new JsonArray();
		foreach (var item in sortedList)
		{
			newArr.Add(SortKeysRecursively(item.DeepClone()));
		}
		return newArr;
	}

	private static float GetFloatProperty(JsonObject obj, string propertyName)
	{
		if (obj.TryGetPropertyValue(propertyName, out var prop) && prop != null && float.TryParse(prop.ToString(), out float val))
		{
			return val;
		}
		return 0f;
	}

	private static string GetStringProperty(JsonObject obj, string propertyName)
	{
		if (obj.TryGetPropertyValue(propertyName, out var prop) && prop != null)
		{
			return prop.ToString();
		}
		return string.Empty;
	}

	private static bool GetBoolProperty(JsonObject obj, string propertyName)
	{
		if (obj.TryGetPropertyValue(propertyName, out var prop) && prop != null && bool.TryParse(prop.ToString(), out bool val))
		{
			return val;
		}
		return false;
	}

	public static string DetectLineEnding(string? filePath, string? existingContent = null)
	{
		if (!string.IsNullOrEmpty(existingContent))
		{
			if (existingContent.Contains("\r\n")) return "\r\n";
			if (existingContent.Contains("\n")) return "\n";
		}
		if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
		{
			using var reader = new StreamReader(filePath);
			int prev = -1;
			int curr;
			while ((curr = reader.Read()) != -1)
			{
				if (curr == '\n')
				{
					return prev == '\r' ? "\r\n" : "\n";
				}
				prev = curr;
			}
		}
		return "\n";
	}

	public static string FormatJson(string jsonText)
	{
		if (string.IsNullOrWhiteSpace(jsonText))
		{
			return "{\n}\n";
		}

		var parsed = JsonNode.Parse(jsonText, documentOptions: DocumentOptions);
		if (parsed == null)
		{
			return "{\n}\n";
		}

		var sorted = SortKeysRecursively(parsed);
		string result = sorted != null ? sorted.ToJsonString(JsonOptions) : "{\n}";
		string formatted = result.TrimEnd() + "\n";
		if (DetectLineEnding(null, jsonText) == "\r\n")
		{
			formatted = formatted.Replace("\r\n", "\n").Replace("\n", "\r\n");
		}
		return formatted;
	}

	public static string FormatNode(JsonNode node)
	{
		if (node == null)
		{
			return "{\n}\n";
		}

		var sorted = SortKeysRecursively(node);
		string result = sorted != null ? sorted.ToJsonString(JsonOptions) : "{\n}";
		return result.TrimEnd() + "\n";
	}

	public static void SaveFormattedJson(string filePath, string jsonText)
	{
		string formatted = FormatJson(jsonText);
		string lineEnding = DetectLineEnding(filePath, jsonText);
		if (lineEnding == "\r\n")
		{
			formatted = formatted.Replace("\r\n", "\n").Replace("\n", "\r\n");
		}
		string directory = Path.GetDirectoryName(filePath);
		if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
		{
			Directory.CreateDirectory(directory);
		}
		if (File.Exists(filePath) && File.ReadAllText(filePath) == formatted)
		{
			return;
		}
		EditorService.LastInternalSaveTimeUtc = DateTime.UtcNow;
		File.WriteAllText(filePath, formatted);
	}

	public static void SaveFormattedJson(string filePath, JsonNode node)
	{
		string formatted = FormatNode(node);
		string lineEnding = DetectLineEnding(filePath);
		if (lineEnding == "\r\n")
		{
			formatted = formatted.Replace("\r\n", "\n").Replace("\n", "\r\n");
		}
		string directory = Path.GetDirectoryName(filePath);
		if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
		{
			Directory.CreateDirectory(directory);
		}
		if (File.Exists(filePath) && File.ReadAllText(filePath) == formatted)
		{
			return;
		}
		EditorService.LastInternalSaveTimeUtc = DateTime.UtcNow;
		File.WriteAllText(filePath, formatted);
	}
}
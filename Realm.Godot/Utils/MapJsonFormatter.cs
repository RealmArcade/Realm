using System;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

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
		if (node is JsonObject obj)
		{
			var sortedObj = new JsonObject();
			var keys = obj.Select(kvp => kvp.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();

			float width = obj.TryGetPropertyValue("Width", out var wNode) && float.TryParse(wNode?.ToString(), out float w) && w > 0 ? w : 128f;
			float depth = obj.TryGetPropertyValue("Depth", out var dNode) && float.TryParse(dNode?.ToString(), out float d) && d > 0 ? d : 128f;
			float topLeftX = -width / 2.0f;
			float topLeftZ = -depth / 2.0f;

			foreach (var key in keys)
			{
				var value = obj[key];

				if (key == "Units" && value is JsonArray unitsArr && unitsArr.All(item => item is JsonObject))
				{
					var sortedList = unitsArr.OfType<JsonObject>()
						.OrderBy(item => GetStringProperty(item, "TemplateId"), StringComparer.OrdinalIgnoreCase)
						.ThenBy(item => GetStringProperty(item, "TemplateId"), StringComparer.Ordinal)
						.ThenBy(item =>
						{
							float x = GetFloatProperty(item, "PosX");
							float z = GetFloatProperty(item, "PosZ");
							return MathF.Sqrt(MathF.Pow(x - topLeftX, 2) + MathF.Pow(z - topLeftZ, 2));
						})
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

					var newUnitsArr = new JsonArray();
					foreach (var item in sortedList)
					{
						newUnitsArr.Add(SortKeysRecursively(item.DeepClone()));
					}
					sortedObj[key] = newUnitsArr;
					continue;
				}

				if (key == "Props" && value is JsonArray propsArr && propsArr.All(item => item is JsonObject))
				{
					var sortedList = propsArr.OfType<JsonObject>()
						.OrderBy(item => GetStringProperty(item, "TemplateId"), StringComparer.OrdinalIgnoreCase)
						.ThenBy(item => GetStringProperty(item, "TemplateId"), StringComparer.Ordinal)
						.ThenBy(item =>
						{
							float x = GetFloatProperty(item, "PosX");
							float z = GetFloatProperty(item, "PosZ");
							return MathF.Sqrt(MathF.Pow(x - topLeftX, 2) + MathF.Pow(z - topLeftZ, 2));
						})
						.ThenBy(item => GetFloatProperty(item, "PosX"))
						.ThenBy(item => GetFloatProperty(item, "PosZ"))
						.ThenBy(item => GetFloatProperty(item, "PosY"))
						.ThenBy(item => GetFloatProperty(item, "RotationX"))
						.ThenBy(item => GetFloatProperty(item, "RotationY"))
						.ThenBy(item => GetFloatProperty(item, "RotationZ"))
						.ThenBy(item => GetFloatProperty(item, "Scale"))
						.ToList();

					var newPropsArr = new JsonArray();
					foreach (var item in sortedList)
					{
						newPropsArr.Add(SortKeysRecursively(item.DeepClone()));
					}
					sortedObj[key] = newPropsArr;
					continue;
				}

				if (key == "Decals" && value is JsonArray decalsArr && decalsArr.All(item => item is JsonObject))
				{
					var sortedList = decalsArr.OfType<JsonObject>()
						.OrderBy(item => GetStringProperty(item, "TemplateId"), StringComparer.OrdinalIgnoreCase)
						.ThenBy(item => GetStringProperty(item, "TemplateId"), StringComparer.Ordinal)
						.ThenBy(item =>
						{
							float x = GetFloatProperty(item, "PosX");
							float z = GetFloatProperty(item, "PosZ");
							return MathF.Sqrt(MathF.Pow(x - topLeftX, 2) + MathF.Pow(z - topLeftZ, 2));
						})
						.ThenBy(item => GetFloatProperty(item, "PosX"))
						.ThenBy(item => GetFloatProperty(item, "PosZ"))
						.ThenBy(item => GetFloatProperty(item, "PosY"))
						.ThenBy(item => GetFloatProperty(item, "RotationY"))
						.ThenBy(item => GetFloatProperty(item, "Scale"))
						.ToList();

					var newDecalsArr = new JsonArray();
					foreach (var item in sortedList)
					{
						newDecalsArr.Add(SortKeysRecursively(item.DeepClone()));
					}
					sortedObj[key] = newDecalsArr;
					continue;
				}

				if (key == "Coordinates" && value is JsonArray coordsArr && coordsArr.All(item => item is JsonObject))
				{
					var sortedList = coordsArr.OfType<JsonObject>()
						.OrderBy(item => GetStringProperty(item, "Name"), StringComparer.OrdinalIgnoreCase)
						.ThenBy(item => GetStringProperty(item, "Name"), StringComparer.Ordinal)
						.ThenBy(item =>
						{
							float minX = GetFloatProperty(item, "MinX");
							float minZ = GetFloatProperty(item, "MinZ");
							float maxX = GetFloatProperty(item, "MaxX");
							float maxZ = GetFloatProperty(item, "MaxZ");
							float midX = (minX + maxX) * 0.5f;
							float midZ = (minZ + maxZ) * 0.5f;
							return MathF.Sqrt(MathF.Pow(midX - topLeftX, 2) + MathF.Pow(midZ - topLeftZ, 2));
						})
						.ThenBy(item => GetFloatProperty(item, "MinX"))
						.ThenBy(item => GetFloatProperty(item, "MinZ"))
						.ThenBy(item => GetFloatProperty(item, "MaxX"))
						.ThenBy(item => GetFloatProperty(item, "MaxZ"))
						.ToList();

					var newCoordsArr = new JsonArray();
					foreach (var item in sortedList)
					{
						newCoordsArr.Add(SortKeysRecursively(item.DeepClone()));
					}
					sortedObj[key] = newCoordsArr;
					continue;
				}

				sortedObj[key] = value != null ? SortKeysRecursively(value.DeepClone()) : null;
			}
			return sortedObj;
		}
		else if (node is JsonArray arr)
		{
			var newArr = new JsonArray();
			foreach (var item in arr)
			{
				newArr.Add(item != null ? SortKeysRecursively(item.DeepClone()) : null);
			}
			return newArr;
		}

		return node?.DeepClone();
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

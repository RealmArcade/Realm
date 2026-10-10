using System.Text.Json;
using System.Text.Json.Serialization;

namespace Realm.Shared.Metadata;

public class UnitAnimationEntryJsonConverter : JsonConverter<UnitAnimationEntry>
{
	public override UnitAnimationEntry Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.String)
		{
			return new UnitAnimationEntry
			{
				Animation = reader.GetString() ?? string.Empty
			};
		}

		if (reader.TokenType == JsonTokenType.StartObject)
		{
			using var doc = JsonDocument.ParseValue(ref reader);
			return ParseAnimationObject(doc.RootElement);
		}

		return default;
	}

	private UnitAnimationEntry ParseAnimationObject(JsonElement root)
	{
		string anim = string.Empty;
		string? right = null;
		string? left = null;

		foreach (var prop in root.EnumerateObject())
		{
			if (IsAnimationProperty(prop.Name))
			{
				anim = prop.Value.GetString() ?? string.Empty;
			}
			else if (IsRightHandProperty(prop.Name))
			{
				right = prop.Value.ValueKind == JsonValueKind.Null ? null : prop.Value.GetString();
			}
			else if (IsLeftHandProperty(prop.Name))
			{
				left = prop.Value.ValueKind == JsonValueKind.Null ? null : prop.Value.GetString();
			}
		}

		return new UnitAnimationEntry
		{
			Animation = anim,
			RightHandAttachment = right,
			LeftHandAttachment = left
		};
	}

	private bool IsAnimationProperty(string name) => 
		name.Equals("Animation", StringComparison.OrdinalIgnoreCase) ||
		name.Equals("Name", StringComparison.OrdinalIgnoreCase) ||
		name.Equals("Path", StringComparison.OrdinalIgnoreCase);

	private bool IsRightHandProperty(string name) =>
		name.Equals("RightHandAttachment", StringComparison.OrdinalIgnoreCase) ||
		name.Equals("RightHand", StringComparison.OrdinalIgnoreCase) ||
		name.Equals("AttachmentRight", StringComparison.OrdinalIgnoreCase);

	private bool IsLeftHandProperty(string name) =>
		name.Equals("LeftHandAttachment", StringComparison.OrdinalIgnoreCase) ||
		name.Equals("LeftHand", StringComparison.OrdinalIgnoreCase) ||
		name.Equals("AttachmentLeft", StringComparison.OrdinalIgnoreCase);

	public override void Write(Utf8JsonWriter writer, UnitAnimationEntry value, JsonSerializerOptions options)
	{
		if (string.IsNullOrEmpty(value.RightHandAttachment) && string.IsNullOrEmpty(value.LeftHandAttachment))
		{
			writer.WriteStringValue(value.Animation ?? string.Empty);
		}
		else
		{
			writer.WriteStartObject();
			writer.WriteString("Animation", value.Animation ?? string.Empty);
			if (value.RightHandAttachment != null)
			{
				writer.WriteString("RightHandAttachment", value.RightHandAttachment);
			}
			else
			{
				writer.WriteNull("RightHandAttachment");
			}
			if (value.LeftHandAttachment != null)
			{
				writer.WriteString("LeftHandAttachment", value.LeftHandAttachment);
			}
			else
			{
				writer.WriteNull("LeftHandAttachment");
			}
			writer.WriteEndObject();
		}
	}
}
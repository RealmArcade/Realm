using System;
using System.Text.Json;

namespace Realm.Shared.Network.Relay;

/// <summary>
///     The answer a lobby server gives a volunteer that offered its bandwidth: whether the offer was pooled, and the
///     sentence the volunteer's own screen shows when it was not. It is read here rather than in the screen that registered,
///     because it is a contract with every lobby server in the field and a contract belongs where it can be read without
///     one. A server writes its JSON in whatever casing its host states - the node this project ships writes camelCase,
///     because that is the default its web host serializes with - and a volunteer that read only the casing it expected
///     would treat a pooled offer as a refusal and stop offering.
/// </summary>
/// <param name="Advertised">Whether the volunteer is now in the node's pool.</param>
/// <param name="Reason">The sentence the node gave for refusing it, or empty when it was pooled.</param>
public readonly record struct RelayRegistrationAnswer(bool Advertised, string Reason)
{
	/// <summary>The sentence stated when a node answered with something this build cannot read.</summary>
	public const string UnreadableReason = "The lobby server answered this machine's offer with something this build could not read, so this machine is carrying nothing.";

	/// <summary>
	///     Reads the answer a node gave to an offer.
	/// </summary>
	/// <param name="json">The body the node answered with.</param>
	/// <param name="answer">The answer, or the stated reason nothing could be read.</param>
	/// <returns>Whether the body was an answer this build understands.</returns>
	public static bool TryParse(string? json, out RelayRegistrationAnswer answer)
	{
		answer = new RelayRegistrationAnswer(false, UnreadableReason);
		if (string.IsNullOrWhiteSpace(json)) return false;

		try
		{
			using JsonDocument document = JsonDocument.Parse(json);
			JsonElement root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object) return false;

			answer = new RelayRegistrationAnswer(ReadFlag(root, "Advertised"), ReadText(root, "Reason"));
			return true;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	private static bool ReadFlag(JsonElement root, string name)
	{
		return TryGet(root, name, out JsonElement value) && value.ValueKind == JsonValueKind.True;
	}

	private static string ReadText(JsonElement root, string name)
	{
		if (!TryGet(root, name, out JsonElement value)) return string.Empty;
		if (value.ValueKind != JsonValueKind.String) return string.Empty;

		return value.GetString() ?? string.Empty;
	}

	private static bool TryGet(JsonElement root, string name, out JsonElement value)
	{
		if (root.TryGetProperty(name, out value)) return true;

		if (char.IsUpper(name[0]))
		{
			Span<char> lowercased = stackalloc char[name.Length];
			name.AsSpan().CopyTo(lowercased);
			lowercased[0] = char.ToLowerInvariant(lowercased[0]);

			if (root.TryGetProperty(new string(lowercased), out value)) return true;
		}

		value = default;
		return false;
	}
}

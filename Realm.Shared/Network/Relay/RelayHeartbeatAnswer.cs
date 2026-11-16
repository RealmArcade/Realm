using System;
using System.Text.Json;

namespace Realm.Shared.Network.Relay;

/// <summary>
///     The answer a lobby server gives a volunteer's heartbeat: whether the volunteer is still in its pool. It is read here
///     rather than in the screen that sends the heartbeat, because a node restarts, is redeployed or forgets a volunteer
///     that went quiet, and a volunteer that could not read that answer would keep believing it is pooled while no lobby
///     server offers it to anybody. The status is read without assuming a casing, for the same reason a registration answer
///     is: the node this project ships writes its JSON with its web host's defaults.
/// </summary>
/// <param name="Known">Whether the node still pools the volunteer that sent the heartbeat.</param>
public readonly record struct RelayHeartbeatAnswer(bool Known)
{
	/// <summary>The status a node states for a volunteer it still pools.</summary>
	public const string RecordedStatus = "Recorded";

	/// <summary>The status a node states for a volunteer it does not know.</summary>
	public const string UnknownStatus = "Unknown";

	/// <summary>
	///     Reads the answer a node gave to a heartbeat.
	/// </summary>
	/// <param name="json">The body the node answered with.</param>
	/// <param name="answer">The answer, which states nothing is known when the body cannot be read.</param>
	/// <returns>Whether the body was an answer this build understands.</returns>
	public static bool TryParse(string? json, out RelayHeartbeatAnswer answer)
	{
		answer = new RelayHeartbeatAnswer(false);
		if (string.IsNullOrWhiteSpace(json)) return false;

		try
		{
			using JsonDocument document = JsonDocument.Parse(json);
			JsonElement root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object) return false;

			if (!TryGetStatus(root, out string status)) return false;

			answer = new RelayHeartbeatAnswer(string.Equals(status, RecordedStatus, StringComparison.OrdinalIgnoreCase));
			return true;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	private static bool TryGetStatus(JsonElement root, out string status)
	{
		status = string.Empty;
		if (!root.TryGetProperty("Status", out JsonElement value) && !root.TryGetProperty("status", out value)) return false;
		if (value.ValueKind != JsonValueKind.String) return false;

		status = value.GetString() ?? string.Empty;
		return true;
	}
}

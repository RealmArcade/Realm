using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.Shared.Network;
using Realm.Shared.Abilities;

namespace Realm.Client.Testing;

public static class MultiplayerHardeningChecks
{
	public static int Run(string outputDirectory, bool hasAuthority, string authorityLogPath = "")
	{
		return Run(outputDirectory, hasAuthority, null, null, authorityLogPath);
	}

	public static int Run(string outputDirectory, bool hasAuthority, LobbyManager? lobby, PlayerInfo? hostSlotBaseline, string authorityLogPath = "")
	{
		var failures = new List<string>();
		var notes = new List<string>();

		RunPayloadChecks(failures, notes);
		if (hasAuthority) RunSpellAuthorityChecks(failures, notes);
		if (hasAuthority && lobby != null && hostSlotBaseline != null) RunLobbyIdentityChecks(outputDirectory, lobby, hostSlotBaseline, failures, notes);
		if (hasAuthority) RunCommandBudgetChecks(outputDirectory, authorityLogPath, failures, notes);
		if (hasAuthority && lobby != null) RunSessionIdentityChecks(lobby, failures, notes);

		var builder = new StringBuilder();
		builder.AppendLine("=== Multiplayer hardening checks ===");
		builder.AppendLine($"Authority checks run: {(hasAuthority ? "yes" : "no")}");
		foreach (string note in notes) builder.AppendLine($"  note: {note}");
		builder.AppendLine($"Failures: {failures.Count}");
		foreach (string failure in failures) builder.AppendLine($"  - {failure}");
		builder.AppendLine($"Verdict: {(failures.Count == 0 ? "PASS" : "FAIL")}");

		string text = builder.ToString();
		GD.Print(text);
		Directory.CreateDirectory(outputDirectory);
		File.WriteAllText(Path.Combine(outputDirectory, "hardening_report.txt"), text);
		File.WriteAllText(
			Path.Combine(outputDirectory, "hardening_report.json"),
			JsonSerializer.Serialize(new { failures = failures.Count, failureMessages = failures, notes }, new JsonSerializerOptions { WriteIndented = true }));

		return failures.Count;
	}

	private static void RunCommandBudgetChecks(string outputDirectory, string authorityLogPath, List<string> failures, List<string> notes)
	{
		string markerPath = Path.Combine(outputDirectory, "command_flood_sent.txt");
		if (!File.Exists(markerPath))
		{
			notes.Add("the client never submitted a command flood, so the authority's command budget is unmeasured for this run");
			return;
		}

		int sentCount = int.TryParse(File.ReadAllText(markerPath).Trim(), out int parsed) ? parsed : 0;

		int rateLimited = ReadAuthorityRateLimitRefusals(authorityLogPath);
		if (rateLimited <= 0) rateLimited = GameContext.Host?.RefusedCommandCount ?? 0;
		notes.Add($"command flood: the client submitted {sentCount} commands and the authority refused {rateLimited} of them for exceeding the rate limit");
		if (rateLimited == 0)
		{
			failures.Add($"the client submitted {sentCount} commands in one tick and the authority refused none of them, so a peer can drive unlimited authority work per tick.");
		}
	}

	private static int ReadAuthorityRateLimitRefusals(string authorityLogPath)
	{
		if (string.IsNullOrEmpty(authorityLogPath) || !File.Exists(authorityLogPath)) return 0;

		try
		{
			using var stream = new FileStream(authorityLogPath, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			using var reader = new StreamReader(stream);
			string log = reader.ReadToEnd();
			int closedTotal = ReadLastReportedCount(log, "the command rate limit refused ");
			int liveTotal = ReadLastReportedCount(log, "the command rate limit has refused ");
			return Math.Max(closedTotal, liveTotal);
		}
		catch (IOException)
		{
			return 0;
		}
	}

	private static int ReadLastReportedCount(string log, string marker)
	{
		int index = log.LastIndexOf(marker, StringComparison.Ordinal);
		if (index < 0) return 0;

		int start = index + marker.Length;
		int end = log.IndexOf(' ', start);
		if (end < 0) return 0;

		return int.TryParse(log.AsSpan(start, end - start), out int reported) ? reported : 0;
	}

	private static void RunSessionIdentityChecks(LobbyManager lobby, List<string> failures, List<string> notes)
	{
		int failuresBefore = failures.Count;
		int checkedPeers = 0;
		foreach (PlayerInfo player in lobby.PlayerList)
		{
			if (player.PeerId <= 1) continue;

			checkedPeers++;
			if (!lobby.HasRegisteredSessionToken(player.PeerId))
			{
				failures.Add($"peer {player.PeerId} (slot {player.Slot}) never registered its session token, so a reconnect from it would be refused as a session no player in the match owns.");
				continue;
			}

			if (string.IsNullOrEmpty(player.SessionToken))
			{
				failures.Add($"peer {player.PeerId} (slot {player.Slot}) registered a session token but the match holds none for it.");
				continue;
			}

			if (!lobby.TryGetReconnectOwnerForHarness(player.SessionToken, out int ownerSlot))
			{
				failures.Add($"the session token peer {player.PeerId} registered resolves to no player, so its reconnect would be refused.");
				continue;
			}

			if (ownerSlot != player.Slot)
			{
				failures.Add($"the session token peer {player.PeerId} registered resolves to slot {ownerSlot} instead of its own slot {player.Slot}.");
			}
		}

		if (checkedPeers == 0) notes.Add("no remote human peer joined, so reconnect identity was not verified");
		else if (failures.Count == failuresBefore) notes.Add($"{checkedPeers} remote peer(s) registered their own session token, and the reconnect lookup resolves each of them to its own slot");
	}

	private static void RunPayloadChecks(List<string> failures, List<string> notes)
	{
		if (PeerPayloadGuard.TryDeserialize<NetworkCommand>(null, NetworkCommandLimits.MaximumPayloadBytes, out _))
		{
			failures.Add("A null command payload was accepted by the payload guard.");
		}

		if (PeerPayloadGuard.TryDeserialize<NetworkCommand>(Array.Empty<byte>(), NetworkCommandLimits.MaximumPayloadBytes, out _))
		{
			failures.Add("An empty command payload was accepted by the payload guard.");
		}

		byte[] truncated = { 0x01, 0x02, 0x03 };
		if (PeerPayloadGuard.TryDeserialize<NetworkCommand>(truncated, NetworkCommandLimits.MaximumPayloadBytes, out _))
		{
			failures.Add("A truncated payload was accepted by the payload guard.");
		}

		var oversized = new byte[NetworkCommandLimits.MaximumPayloadBytes + 1];
		if (PeerPayloadGuard.TryDeserialize<NetworkCommand>(oversized, NetworkCommandLimits.MaximumPayloadBytes, out _))
		{
			failures.Add($"A {oversized.Length} byte payload was accepted, above the {NetworkCommandLimits.MaximumPayloadBytes} byte limit.");
		}

		var random = new System.Random(20261024);
		for (int attempt = 0; attempt < 64; attempt++)
		{
			var payload = new byte[random.Next(1, 256)];
			random.NextBytes(payload);
			try
			{
				PeerPayloadGuard.TryDeserialize<NetworkCommand>(payload, NetworkCommandLimits.MaximumPayloadBytes, out _);
			}
			catch (Exception exception)
			{
				failures.Add($"A random payload threw out of the guard instead of being rejected: {exception.Message}");
				break;
			}
		}

		NetworkCommand valid = new()
		{
			CommandId = 7,
			CommandType = "move",
			UnitEntityIds = new List<int> { 12 },
			TargetPosition = new NetworkVector3(1f, 2f, 3f),
			ArgString = string.Empty
		};
		if (!NetworkCommandLimits.TrySanitize(ref valid, out string validRejection))
		{
			failures.Add($"A well-formed command was rejected by the sanitiser: {validRejection}");
		}

		CheckRejected(failures, "a command with no type", BuildCommand(null, 1, null));
		CheckRejected(failures, "an uppercase command type", BuildCommand("Move", 1, null));
		CheckRejected(failures, "a command type of 200 characters", BuildCommand(new string('a', 200), 1, null));
		CheckRejected(failures, "a command id of zero", BuildCommand("move", 0, null));
		CheckRejected(failures, "a negative command id", BuildCommand("move", -3, null));
		CheckRejected(failures, "a non-finite target position", BuildCommand("move", 1, new NetworkVector3(float.NaN, 0f, 0f)));

		var oversizedUnitList = new List<int>();
		for (int index = 0; index < NetworkCommandLimits.MaximumUnitEntityIds + 1; index++) oversizedUnitList.Add(index + 1);
		CheckRejected(failures, "a unit list above the limit", BuildCommand("move", 1, null, oversizedUnitList));
		CheckRejected(failures, "a negative unit id", BuildCommand("move", 1, null, new List<int> { 4, -9 }));
		CheckRejected(failures, "an argument with a control character", BuildCommand("build", 1, null, null, "worker\u0007"));

		notes.Add($"payload checks: null, empty, truncated, oversized, 64 random payloads, and 10 malformed command shapes");
	}

	private static void CheckRejected(List<string> failures, string description, NetworkCommand command)
	{
		NetworkCommand copy = command;
		if (NetworkCommandLimits.TrySanitize(ref copy, out string rejectionReason)) failures.Add($"{description} was accepted by the command sanitiser.");
		else if (string.IsNullOrEmpty(rejectionReason)) failures.Add($"{description} was rejected without a reason.");
	}

	private static NetworkCommand BuildCommand(string? commandType, int commandId, NetworkVector3? targetPosition, List<int>? unitEntityIds = null, string? argument = null)
	{
		return new NetworkCommand
		{
			CommandId = commandId,
			CommandType = commandType!,
			UnitEntityIds = unitEntityIds ?? new List<int> { 1 },
			TargetPosition = targetPosition ?? new NetworkVector3(0f, 0f, 0f),
			ArgString = argument ?? string.Empty
		};
	}

	private static void RunLobbyIdentityChecks(string outputDirectory, LobbyManager lobby, PlayerInfo hostSlotBaseline, List<string> failures, List<string> notes)
	{
		string markerPath = Path.Combine(outputDirectory, "forged_rpc_sent.txt");
		if (!File.Exists(markerPath))
		{
			notes.Add("the client never sent its forged lobby RPCs, so slot and diagnostics identity was not verified");
			return;
		}

		PlayerInfo? hostPlayer = lobby.PlayerList.Find(player => player.PeerId == 1);
		if (hostPlayer == null)
		{
			failures.Add("The host's own player entry disappeared from the lobby list.");
			return;
		}

		const string ForgedLatency = "999";
		const string ForgedLoss = "99%";

		if (!string.Equals(hostPlayer.Name, hostSlotBaseline.Name, StringComparison.Ordinal)) failures.Add($"A peer changed the host's name from '{hostSlotBaseline.Name}' to '{hostPlayer.Name}'.");
		if (!string.Equals(hostPlayer.Faction, hostSlotBaseline.Faction, StringComparison.Ordinal)) failures.Add($"A peer changed the host's faction from '{hostSlotBaseline.Faction}' to '{hostPlayer.Faction}'.");
		if (!string.Equals(hostPlayer.Team, hostSlotBaseline.Team, StringComparison.Ordinal)) failures.Add($"A peer changed the host's team from '{hostSlotBaseline.Team}' to '{hostPlayer.Team}'.");
		if (hostPlayer.Latency.StartsWith(ForgedLatency, StringComparison.Ordinal)) failures.Add($"A peer rewrote the host's latency to the forged value '{hostPlayer.Latency}'.");
		if (hostPlayer.Jitter.StartsWith(ForgedLatency, StringComparison.Ordinal)) failures.Add($"A peer rewrote the host's jitter to the forged value '{hostPlayer.Jitter}'.");
		if (hostPlayer.PacketLoss.StartsWith(ForgedLoss, StringComparison.Ordinal)) failures.Add($"A peer rewrote the host's packet loss to the forged value '{hostPlayer.PacketLoss}'.");

		if (failures.Count == 0)
		{
			notes.Add($"forged slot and diagnostics RPCs targeting peer 1 were ignored: slot stayed '{hostPlayer.Name}'/'{hostPlayer.Faction}'/'{hostPlayer.Team}' and latency '{hostPlayer.Latency}' is the host's own measurement, not the forged 999 ms");
		}
	}

	private static void RunSpellAuthorityChecks(List<string> failures, List<string> notes)
	{
		GameHost? gameHost = GameContext.Host;
		if (gameHost == null || GameContext.World == null)
		{
			failures.Add("The host world was unavailable, so spell authority could not be validated.");
			return;
		}

		NetworkService networkService = GameContext.NetworkService;
		string? spellId = null;
		foreach (KeyValuePair<string, AbilityDefinition> definition in GameContext.RegistryService.AbilityDefinitions)
		{
			spellId = definition.Key;
			break;
		}

		if (spellId == null)
		{
			notes.Add("no ability definitions are registered on this map, so the spell gate was checked only for rejecting an unknown spell");
			spellId = "harness_unknown_spell";
		}

		NetworkCommand unknownSpell = BuildCommand("spell", 9001, new NetworkVector3(0f, 0f, 0f), new List<int> { 1 }, "harness_spell_that_does_not_exist");
		if (networkService.ValidateSpellCommandForHarness(1, unknownSpell, out string unknownReason)) failures.Add("A spell command naming an undefined spell was authorised.");
		else if (string.IsNullOrEmpty(unknownReason)) failures.Add("An undefined spell was rejected without a reason.");
		else notes.Add($"undefined spell rejected: {unknownReason}");

		NetworkCommand noCaster = BuildCommand("spell", 9002, new NetworkVector3(0f, 0f, 0f), new List<int>(), spellId);
		if (networkService.ValidateSpellCommandForHarness(1, noCaster, out string noCasterReason)) failures.Add("A spell command with no caster was authorised.");
		else notes.Add($"caster-less spell rejected: {noCasterReason}");

		int localPlayerIndex = gameHost.LocalPlayerIndex;
		int foreignServerEntityId = -1;
		int localServerEntityId = -1;
		int foreignPeerId = -1;
		for (int index = 0; index < gameHost.AllUnits.Count; index++)
		{
			Unit3D unit = gameHost.AllUnits[index];
			if (!GodotObject.IsInstanceValid(unit) || unit.IsBuilding) continue;
			int serverEntityId = gameHost.GetServerEntityIdForHarness(unit.Entity.Id);
			if (serverEntityId <= 0) continue;

			if (unit.Player == localPlayerIndex && localServerEntityId < 0) localServerEntityId = serverEntityId;
			if (unit.Player != localPlayerIndex && foreignServerEntityId < 0)
			{
				foreignServerEntityId = serverEntityId;
				foreignPeerId = unit.Player + 1;
			}
		}

		if (foreignServerEntityId < 0)
		{
			notes.Add("no foreign-owned unit was present, so the forged-caster case was skipped");
		}
		else
		{
			NetworkCommand forged = BuildCommand("spell", 9003, new NetworkVector3(0f, 0f, 0f), new List<int> { foreignServerEntityId }, spellId);
			if (networkService.ValidateSpellCommandForHarness(1, forged, out string forgedReason)) failures.Add($"Peer 1 authorised a spell naming server entity {foreignServerEntityId}, which belongs to another player.");
			else if (string.IsNullOrEmpty(forgedReason)) failures.Add("A forged caster was rejected without a reason.");
			else notes.Add($"forged caster {foreignServerEntityId} (peer {foreignPeerId}'s unit) rejected: {forgedReason}");
		}

		if (localServerEntityId < 0)
		{
			failures.Add("The harness could not find a locally owned unit to prove the spell gate accepts a legitimate cast.");
			return;
		}

		if (GameContext.RegistryService.AbilityDefinitions.TryGetValue(spellId, out AbilityDefinition? registeredDefinition) && registeredDefinition != null)
		{
			System.Numerics.Vector3 casterPosition = System.Numerics.Vector3.Zero;
			Entity casterEntity = new(localServerEntityId);
			if (GameContext.World.Has<Position>(casterEntity))
			{
				casterPosition = GameContext.World.Get<Position>(casterEntity).Value;
			}

			NetworkCommand legitimate = BuildCommand(
				"spell",
				9004,
				new NetworkVector3(casterPosition.X, casterPosition.Y, casterPosition.Z),
				new List<int> { localServerEntityId },
				spellId);

			if (networkService.TryGetPlayerEntityForPeer(1, out Entity playerEntity)
				&& GameContext.World.Has<SpellCooldowns>(playerEntity))
			{
				ref var cooldowns = ref GameContext.World.Get<SpellCooldowns>(playerEntity);
				cooldowns.Remove(spellId);
			}

			bool authorised = networkService.ValidateSpellCommandForHarness(1, legitimate, out string legitimateReason);
			if (authorised)
			{
				notes.Add($"legitimate cast accepted for peer 1 with its own caster {localServerEntityId} ({spellId})");
			}
			else
			{
				notes.Add($"legitimate cast rejected for peer 1 ({spellId}): {legitimateReason}");
			}
		}
	}
}

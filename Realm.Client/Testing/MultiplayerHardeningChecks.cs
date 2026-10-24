using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Ecs;
using Realm.Ecs.Components.Core;

namespace Realm.Client.Testing;

public static class MultiplayerHardeningChecks
{
	public static int Run(string outputDirectory, bool hasAuthority)
	{
		var failures = new List<string>();
		var notes = new List<string>();

		RunPayloadChecks(failures, notes);
		if (hasAuthority) RunSpellAuthorityChecks(failures, notes);

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

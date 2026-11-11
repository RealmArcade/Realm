using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.Shared;
using Realm.Shared.Abilities;
using Realm.Shared.Maps;

namespace Realm.Client.Testing;

public partial class MultiplayerSimulationHarness : Node
{
	private async Task<int> RunUxSmokeAsync()
	{
		var report = new StringBuilder();
		bool passed = true;

		void Check(string name, bool condition, string detail = "")
		{
			if (!condition) passed = false;
			string line = $"{(condition ? "PASS" : "FAIL")} {name}{(string.IsNullOrEmpty(detail) ? string.Empty : $": {detail}")}";
			report.AppendLine(line);
			GameLog.LogToConsole($"[UX_SMOKE] {line}");
		}

		void Note(string text)
		{
			report.AppendLine(text);
			GameLog.LogToConsole($"[UX_SMOKE] {text}");
		}

		LobbyManager lobby = GameContext.Lobby ?? throw new InvalidOperationException("LobbyManager autoload is unavailable.");
		GameContext.SessionState.IsTestMode = true;
		lobby.IsSinglePlayer = true;
		lobby.IsHost = true;
		lobby.IsGameStarted = true;
		lobby.ActiveMapName = _mapFolder;

		string? mapDirectory = GameHost.ResolveMapDirectory(_mapFolder);
		Check("map resolved", !string.IsNullOrEmpty(mapDirectory), mapDirectory ?? _mapFolder);
		if (string.IsNullOrEmpty(mapDirectory)) return FinishUxSmoke(report, passed);

		ClientNetworkChannel channel = GetNodeOrNull<ClientNetworkChannel>("/root/RealmNetwork") ?? throw new InvalidOperationException("This build has no channel to an authority.");
		GameContext.ServerSession?.Dispose();
		var session = new ClientServerSession(channel);
		var start = new ServerMatchStart(mapDirectory, "UX Smoke", HarnessMatchSeed, Path.Combine(_outputDirectory, "listen_server"), _outputDirectory, LocalizationManager.GetCurrentLanguageCode(), AuthorityRecordDirectory: _outputDirectory);
		MatchStartResult started = await session.TryStartAsync(start, _port);
		Check("authority started", started.Started, started.Started ? $"port {session.Port}" : started.Failure);
		if (!started.Started) return FinishUxSmoke(report, passed);
		GameContext.AttachServerSession(session);
		_session = session;

		PackedScene scene = GD.Load<PackedScene>("res://GameHost.tscn");
		Node gameHost = scene.Instantiate();
		gameHost.Name = "GameHost";
		AddChild(gameHost);

		bool ready = await WaitUntilAsync(() =>
		{
			GameHost? host = GameContext.Host;
			return host != null && GameContext.World != null && host.AllUnits.Count > 0;
		}, 60f);
		Check("replicated world ready", ready);

		GameContext.UserInterface?.TransitionTo(GameScreen.InGameHUD);
		await ToSignal(GetTree().CreateTimer(3.0f), SceneTreeTimer.SignalName.Timeout);

		InGameHUD? hud = GameContext.InGameHud;
		GameHost? currentHost = GameContext.Host;
		Check("HUD built", hud != null);
		Check("game host present", currentHost != null);
		if (hud == null || currentHost == null) return FinishUxSmoke(report, passed);

		Unit3D? hero = FindUnit(currentHost, "hero/", 1);
		Check("Vale hero replicated", hero != null);
		if (hero == null) return FinishUxSmoke(report, passed);

		currentHost.SelectUnitInternal(new UnitId(hero.Entity.Id));
		await ToSignal(GetTree().CreateTimer(2.0f), SceneTreeTimer.SignalName.Timeout);

		Node? readings = hud.FindChild("UnitStatsReadings_VBoxContainer", true, false);
		Check("readings section visible", readings is Control { Visible: true });
		CheckReading(readings, "Innate", "Unbroken", Check);
		CheckReading(readings, "Relics", "0 / 6", Check);
		string? bulwark = ReadReadingValue(readings, "Bulwark");
		Check("bulwark row present", bulwark != null, bulwark ?? "missing");
		Check("bulwark uses one building", bulwark != null && bulwark.Contains("1 building", StringComparison.Ordinal), bulwark ?? "missing");

		List<Button> commandButtons = FindCommandButtons(hud);
		Note($"hero command buttons={commandButtons.Count}");
		for (int index = 0; index < commandButtons.Count; index++)
		{
			Note($"hero command[{index}] tooltip='{commandButtons[index].TooltipText}' disabled={commandButtons[index].Disabled}");
		}

		Check("hero card offers an active", FindCommandButton(hud, "Grounding Shout") != null);

		Button? beginButton = FindCommandButton(hud, "Begin Construction");
		Check("begin construction card present", beginButton != null);
		if (beginButton != null)
		{
			Check("begin construction card enabled", !beginButton.Disabled);
			float woodBefore = hud.Wood;
			beginButton.EmitSignal(Button.SignalName.Pressed);
			await ToSignal(GetTree().CreateTimer(0.3f), SceneTreeTimer.SignalName.Timeout);
			Check("begin construction asks for a site", currentHost.ActiveSpellTargeting == "ability/begin_construction", currentHost.ActiveSpellTargeting ?? "none");

			currentHost.CastSpellAt("ability/begin_construction", hero.GlobalPosition + new Vector3(4f, 0f, 4f));
			await ToSignal(GetTree().CreateTimer(1.0f), SceneTreeTimer.SignalName.Timeout);
			Check("begin construction reserves 100 wood", hud.Wood <= woodBefore - 90f, $"{woodBefore:0.##} -> {hud.Wood:0.##}");

			string draftFeedback = ReadFeedback(hud);
			Check("the draw announces its offers", draftFeedback.Contains("offered", StringComparison.OrdinalIgnoreCase), draftFeedback);
			Check("begin construction is withdrawn while its draw is open", FindCommandButton(hud, "Begin Construction") == null);

			List<Button> draftOptions = FindCommandButtonsContaining(hud, "for 100 wood");
			Check("all three drawn packages are on the first page", draftOptions.Count == 3, $"cards={draftOptions.Count}");

			List<Button> draftCard = FindCommandButtons(hud);
			List<string> draftKeys = ReadCommandHotkeys(draftCard);
			Note($"draft card buttons={draftCard.Count} keys=[{string.Join(", ", draftKeys)}]");
			Check("the drawn card names a key for every command", draftKeys.Count == draftCard.Count, $"keys={draftKeys.Count} buttons={draftCard.Count}");
			Check("the drawn card has no duplicate keys", HasDuplicateHotkeys(draftKeys) == false, string.Join(", ", draftKeys));
			if (draftOptions.Count > 0)
			{
				int buildingsBefore = CountPlayerBuildings(currentHost, 1);
				string draftedCard = draftOptions[0].TooltipText.Split('\n')[0];
				draftOptions[0].EmitSignal(Button.SignalName.Pressed);
				await ToSignal(GetTree().CreateTimer(1.5f), SceneTreeTimer.SignalName.Timeout);

				int buildingsAfter = CountPlayerBuildings(currentHost, 1);
				Check("locking a draft starts its site", buildingsAfter == buildingsBefore + 1, $"buildings {buildingsBefore} -> {buildingsAfter}");
				string siteFeedback = ReadFeedback(hud);
				Check("the site announces construction", siteFeedback.Contains("under construction", StringComparison.OrdinalIgnoreCase), siteFeedback);
				Note($"drafted card='{draftedCard}'");
			}
		}

		Button? shoutButton = FindCommandButton(hud, "Grounding Shout");
		Check("hero active card present after the draw", shoutButton != null);
		if (shoutButton != null)
		{
			Check("hero active card enabled", !shoutButton.Disabled);
			float manaBefore = ReadMana(hero);
			shoutButton.EmitSignal(Button.SignalName.Pressed);
			await ToSignal(GetTree().CreateTimer(0.3f), SceneTreeTimer.SignalName.Timeout);
			Check("the active asks for a target", currentHost.ActiveSpellTargeting == "ability/grounding_shout", currentHost.ActiveSpellTargeting ?? "none");

			currentHost.CastSpellAt("ability/grounding_shout", hero.GlobalPosition + new Vector3(2f, 0f, 2f));
			await ToSignal(GetTree().CreateTimer(1.0f), SceneTreeTimer.SignalName.Timeout);
			float manaAfter = ReadMana(hero);
			Check("the active spends its mana", manaAfter <= manaBefore - 50f, $"{manaBefore:0.#} -> {manaAfter:0.#}");
		}

		Unit3D? nexus = FindUnit(currentHost, "building/nexus", 1);
		Check("player Nexus replicated", nexus != null);
		if (nexus == null) return FinishUxSmoke(report, passed);

		currentHost.SelectUnitInternal(new UnitId(nexus.Entity.Id));
		await ToSignal(GetTree().CreateTimer(2.0f), SceneTreeTimer.SignalName.Timeout);

		Button? relicButton = FindCommandButton(hud, "Sigil of the Wall");
		Check("relic vendor button present", relicButton != null);
		if (relicButton == null) return FinishUxSmoke(report, passed);
		Check("relic vendor button enabled", !relicButton.Disabled);

		AbilityDefinition? relicDefinition = currentHost.GetAbilityDefinition("item/sigil_of_the_wall");
		Check("relic card is an instant cast", relicDefinition?.IsInstant == true, $"present={relicDefinition != null} instant={relicDefinition?.IsInstant}");
		Note($"relic tooltip='{relicButton.TooltipText}'");

		float goldBefore = hud.Gold;
		relicButton.EmitSignal(Button.SignalName.Pressed);
		await ToSignal(GetTree().CreateTimer(2.0f), SceneTreeTimer.SignalName.Timeout);
		float goldAfter = hud.Gold;
		Check("buying a relic spends its gold", goldAfter <= goldBefore - 90f, $"{goldBefore:0.##} -> {goldAfter:0.##}");

		float goldBeforeHotkeyRelic = hud.Gold;
		bool hotkeyHandled = hud.HandleCommandCardHotkey(Key.Key2, (KeyModifierMask)0);
		Check("the second relic answers its numbered hotkey", hotkeyHandled);
		await ToSignal(GetTree().CreateTimer(1.5f), SceneTreeTimer.SignalName.Timeout);
		Check("the hotkeyed relic spends its gold", hud.Gold <= goldBeforeHotkeyRelic - 90f, $"{goldBeforeHotkeyRelic:0.##} -> {hud.Gold:0.##}");

		currentHost.SelectUnitInternal(new UnitId(hero.Entity.Id));
		await ToSignal(GetTree().CreateTimer(2.0f), SceneTreeTimer.SignalName.Timeout);
		Node? heroReadings = hud.FindChild("UnitStatsReadings_VBoxContainer", true, false);
		CheckReading(heroReadings, "Relics", "2 / 6", Check);

		if (DisplayServer.GetName() == "headless")
		{
			Note("screenshot skipped: headless display has no viewport texture");
		}
		else
		{
			Image image = GetViewport().GetTexture().GetImage();
			string screenshotPath = Path.Combine(_outputDirectory, "ux_smoke.png");
			image.SavePng(screenshotPath);
			Note($"screenshot={screenshotPath} size={image.GetWidth()}x{image.GetHeight()}");
		}

		return FinishUxSmoke(report, passed);
	}

	private static Unit3D? FindUnit(GameHost host, string templatePrefix, int player)
	{
		foreach (Unit3D unit in host.AllUnits)
		{
			if (!GodotObject.IsInstanceValid(unit)) continue;
			if (unit.Player != player) continue;
			if (!unit.TemplateID.StartsWith(templatePrefix, StringComparison.OrdinalIgnoreCase)) continue;

			return unit;
		}

		return null;
	}

	private static List<Button> FindCommandButtons(InGameHUD hud)
	{
		var buttons = new List<Button>();
		GridContainer? grid = hud.FindChild("GridContainer", true, false) as GridContainer;
		if (grid == null) return buttons;

		foreach (Node child in grid.GetChildren())
		{
			if (child is Button button) buttons.Add(button);
		}

		return buttons;
	}

	private static Button? FindCommandButton(InGameHUD hud, string tooltipFragment)
	{
		List<Button> buttons = FindCommandButtons(hud);
		for (int index = 0; index < buttons.Count; index++)
		{
			Button button = buttons[index];
			if (button.TooltipText.Contains(tooltipFragment, StringComparison.OrdinalIgnoreCase)) return button;
		}

		return null;
	}

	private static List<Button> FindCommandButtonsContaining(InGameHUD hud, string tooltipFragment)
	{
		var matches = new List<Button>();
		List<Button> buttons = FindCommandButtons(hud);
		for (int index = 0; index < buttons.Count; index++)
		{
			Button button = buttons[index];
			if (button.TooltipText.Contains(tooltipFragment, StringComparison.OrdinalIgnoreCase)) matches.Add(button);
		}

		return matches;
	}

	private static string ReadFeedback(InGameHUD hud)
	{
		return hud.FindChild("FeedbackLabel", true, false) is Label label ? label.Text : string.Empty;
	}

	private static List<string> ReadCommandHotkeys(List<Button> buttons)
	{
		var hotkeys = new List<string>();
		foreach (Button button in buttons)
		{
			string tooltip = button.TooltipText;
			int closingBracket = tooltip.IndexOf(']');
			if (tooltip.StartsWith('[') && closingBracket > 1) hotkeys.Add(tooltip[1..closingBracket]);
		}

		return hotkeys;
	}

	private static bool HasDuplicateHotkeys(List<string> hotkeys)
	{
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string hotkey in hotkeys)
		{
			if (!seen.Add(hotkey)) return true;
		}

		return false;
	}

	private static int CountPlayerBuildings(GameHost host, int player)
	{
		int count = 0;
		foreach (Unit3D unit in host.AllUnits)
		{
			if (!GodotObject.IsInstanceValid(unit)) continue;
			if (unit.Player != player || !unit.IsBuilding) continue;

			count++;
		}

		return count;
	}

	private static float ReadMana(Unit3D unit)
	{
		EcsWorld? world = GameContext.World;
		if (world == null || !world.IsAlive(unit.Entity) || !world.Has<Mana>(unit.Entity)) return 0f;

		return world.Get<Mana>(unit.Entity).Current;
	}

	private static string? ReadReadingValue(Node? readings, string name)
	{
		if (readings == null) return null;

		foreach (Node child in readings.GetChildren())
		{
			if (child is not HBoxContainer row) continue;

			string rowName = string.Empty;
			string rowValue = string.Empty;
			foreach (Node labelNode in row.GetChildren())
			{
				if (labelNode is not Label label) continue;
				if (label.Name == "UnitStatsReadingRowName_Label") rowName = label.Text;
				if (label.Name == "UnitStatsReadingRowValue_Label") rowValue = label.Text;
			}

			if (string.Equals(rowName, name, StringComparison.Ordinal)) return rowValue;
		}

		return null;
	}

	private void CheckReading(Node? readings, string name, string expected, Action<string, bool, string> check)
	{
		string? actual = ReadReadingValue(readings, name);
		check($"{name} reading", string.Equals(actual, expected, StringComparison.Ordinal), $"'{actual}' expected '{expected}'");
	}

	private int FinishUxSmoke(StringBuilder report, bool passed)
	{
		report.AppendLine($"result={(passed ? "PASS" : "FAIL")}");
		string reportPath = Path.Combine(_outputDirectory, "ux_smoke.txt");
		File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(false));
		GameLog.LogToConsole($"[UX_SMOKE] result={(passed ? "PASS" : "FAIL")} report={reportPath}");
		return passed ? 0 : 1;
	}
}

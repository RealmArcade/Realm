using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.UI.InGame;
using Realm.Client.VFX;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Tags;
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
		string opponentFaction = IsSable ? "Vale" : "Sable";
		MatchRosterSeat[] roster =
		{
			new(1, 0, HostPlayerName, 1, "Team 1", false, Faction: _faction),
			new(2, 0, "UX Opponent", 2, "Team 2", true, Faction: opponentFaction)
		};
		var start = new ServerMatchStart(mapDirectory, "UX Smoke", HarnessMatchSeed, Path.Combine(_outputDirectory, "listen_server"), _outputDirectory, LocalizationManager.GetCurrentLanguageCode(), Cheats: true, Roster: roster, AuthorityRecordDirectory: _outputDirectory);
		MatchStartResult started = await session.TryStartAsync(start, _port);
		Check("authority started", started.Started, started.Started ? $"port {session.Port}" : started.Failure);
		if (!started.Started) return FinishUxSmoke(report, passed);
		GameContext.AttachServerSession(session);
		_session = session;

		PackedScene scene = GD.Load<PackedScene>("res://GameHost.tscn");
		Node gameHost = scene.Instantiate();
		gameHost.Name = "Main";
		GetTree().Root.AddChild(gameHost);

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

		// A headless window keeps the pointer at the top-left corner, which is inside the edge-pan margin and drags the
		// camera every frame. Edge panning is a player's pointer rule, not part of what a focus is being measured for, so
		// it is switched off for the run: otherwise every centring check is answered by the pan that follows it.
		if (GameContext.InputService.TryGetCameraState(out CameraState cameraState))
		{
			cameraState.EnableEdgePanning = false;
			GameContext.World.Set(GameContext.Simulation.WorldEntity, cameraState);
			Note("edge panning disabled for the automated camera checks");
		}

		void FocusCamera(Vector3 position)
		{
			ActiveCamera()?.FocusOnPosition(position);
		}

		CameraControl? ActiveCamera()
		{
			return currentHost.MainCamera ?? currentHost.GetViewport().GetCamera3D() as CameraControl;
		}

		bool IsOnScreen(Vector3 position)
		{
			CameraControl? camera = ActiveCamera();
			return camera != null && camera.IsPositionInFrustum(position);
		}

		List<Button> FindDraftOffers()
		{
			var offers = new List<Button>();
			foreach (Button button in FindCommandButtons(hud))
			{
				// A package card names its building and nothing else. The hero's own cards are tagged the same way, so
				// the mana line and the construction card are what keep them out of the draw's list.
				if (!button.TooltipText.Contains("[/b]", StringComparison.Ordinal)) continue;
				if (button.TooltipText.Contains("Abandon Construction", StringComparison.OrdinalIgnoreCase)) continue;
				if (button.TooltipText.Contains("Begin Construction", StringComparison.OrdinalIgnoreCase)) continue;
				if (button.TooltipText.Contains("mana", StringComparison.OrdinalIgnoreCase)) continue;
				offers.Add(button);
			}
			return offers;
		}

		Unit3D? FindRaisedSoldier()
		{
			foreach (Unit3D unit in currentHost.AllUnits)
			{
				if (!GodotObject.IsInstanceValid(unit)) continue;
				if (unit.Player != 1) continue;
				if (!unit.TemplateID.StartsWith("unit/", StringComparison.OrdinalIgnoreCase)) continue;
				if (unit.TemplateID.StartsWith("unit/blueprint", StringComparison.OrdinalIgnoreCase)) continue;
				if (unit.TemplateID.StartsWith("unit/building_worker", StringComparison.OrdinalIgnoreCase)) continue;
				return unit;
			}
			return null;
		}

		void CheckReadingsFold(string label, Node? readingsNode)
		{
			if (readingsNode is not Control readingsControl || hud.FindChild("StatsScroll", true, false) is not ScrollContainer scroll)
			{
				Check($"{label} readings are measurable", false);
				return;
			}

			float viewportTop = scroll.GlobalPosition.Y;
			float viewportBottom = viewportTop + scroll.Size.Y;
			float readingsTop = readingsControl.GlobalPosition.Y;
			float readingsBottom = readingsTop + readingsControl.Size.Y;
			Check($"{label} readings lead the inspector", MathF.Abs(readingsTop - viewportTop) <= 1f,
				$"readings start {readingsTop:0.#} viewport start {viewportTop:0.#}");
			Check($"{label} readings stay above the fold", readingsBottom <= viewportBottom + 1f,
				$"readings {readingsTop:0.#}..{readingsBottom:0.#} viewport {viewportTop:0.#}..{viewportBottom:0.#}");
		}

		static bool ReadOverheadCharge(Unit3D? building, UnitHealthBarOverlay? overlay, out int badgeValue, out int chargeValue)
		{
			badgeValue = 0;
			chargeValue = 0;
			if (building == null || !GodotObject.IsInstanceValid(building)) return false;
			if (overlay == null || !overlay.TryReadBadge(building.Entity, out badgeValue)) return false;
			if (GameContext.World == null || !GameContext.World.TryGet(building.Entity, out UnitCharges unitCharges)) return false;

			chargeValue = unitCharges.Charges;
			return badgeValue == chargeValue;
		}

		static string MinimapRingDetail(MinimapOverlay? overlay, Unit3D? target)
		{
			if (overlay == null) return "no minimap overlay";
			if (target == null || !GodotObject.IsInstanceValid(target)) return $"camp gone, rings={overlay.AttackTargetRingsDrawn}";
			return $"rings={overlay.AttackTargetRingsDrawn} campRinged={overlay.IsAttackTargetRinged(target)} campHealth={ReadHealth(target):0.#}";
		}

		async Task<string> SacrificeHeroAsync(Unit3D targetHero)
		{
			currentHost.SelectUnitInternal(new UnitId(targetHero.Entity.Id));
			await ToSignal(GetTree().CreateTimer(0.5f), SceneTreeTimer.SignalName.Timeout);
			Check("the dev kill cheat is accepted", hud.TryTriggerCheat("cooked"));
			bool fell = await WaitUntilAsync(() => FindUnit(currentHost, "hero/", 1) == null, 10f);
			Check("the hero leaves the field when it dies", fell);
			return ReadFeedback(hud);
		}

		async Task<Unit3D?> WaitForRevivalAsync()
		{
			ulong deadline = Time.GetTicksMsec() + 25000;
			while (Time.GetTicksMsec() < deadline && FindUnit(currentHost, "hero/", 1) == null)
			{
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			}

			Unit3D? revived = FindUnit(currentHost, "hero/", 1);
			Check("the hero returns when its revival timer expires", revived != null);
			if (revived == null) return null;

			// The feedback label is one line, and the idle hint can claim it the same moment the hero stands up, so the
			// announcement is watched frame by frame instead of read as whichever notice happened to land last.
			for (int frame = 0; frame < 20; frame++)
			{
				string line = ReadFeedback(hud);
				if (line.Contains("returns to the field", StringComparison.OrdinalIgnoreCase))
				{
					Check("the revival announces the hero is back", true, line);
					return revived;
				}

				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			}

			Check("the revival announces the hero is back", false, ReadFeedback(hud));
			return revived;
		}

		Unit3D? hero = FindUnit(currentHost, "hero/", 1);
		Check($"{_faction} hero replicated", hero != null);
		if (hero == null) return FinishUxSmoke(report, passed);

		currentHost.SelectUnitInternal(new UnitId(hero.Entity.Id));
		FocusCamera(hero.GlobalPosition);
		await ToSignal(GetTree().CreateTimer(2.0f), SceneTreeTimer.SignalName.Timeout);
		// The presentation places a replicated node on its first drawn frame, so the first focus can land on the
		// placeholder the node was built at. Focusing again once the world has been drawn measures the camera, not
		// the frame the node spent at the origin.
		FocusCamera(hero.GlobalPosition);
		await ToSignal(GetTree().CreateTimer(0.4f), SceneTreeTimer.SignalName.Timeout);
		Check("the hero stands on screen", IsOnScreen(hero.GlobalPosition), $"hero {hero.GlobalPosition} camera {ActiveCamera()?.Position}");

		// The ruleset's own food cap has to be the one the readout states, and a building has to spend from it rather
		// than raising the cap. The label is read as the player sees it, not from the player component behind it.
		Node? supplyReadout = hud.FindChild("SupplyLabel", true, false);
		Check("the food readout states the ruleset cap", supplyReadout is Label supplyLabel && supplyLabel.Text.EndsWith("/ 40", StringComparison.Ordinal), (supplyReadout as Label)?.Text ?? "missing");

		// Focusing a unit has to bring it to the middle of the screen, not the top of the frustum. The camera is moved
		// off the hero first so centering cannot pass by being where the camera already was. The control-group recall
		// and the focus key both route through CenterCameraAt, so this measures the rule they share.
		FocusCamera(hero.GlobalPosition + new Vector3(30f, 0f, 30f));
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		currentHost.CenterCameraOnSelectedOrBase();
		await ToSignal(GetTree().CreateTimer(0.25f), SceneTreeTimer.SignalName.Timeout);
		if (ActiveCamera() is { } centeringCamera)
		{
			Vector2 unitOnScreen = centeringCamera.UnprojectPosition(hero.GlobalPosition);
			Vector2 screenCenter = GetViewport().GetVisibleRect().Size * 0.5f;
			Check("double-tapping a control group centers it on screen", unitOnScreen.DistanceTo(screenCenter) < 60f, $"unit at {unitOnScreen}, center {screenCenter}");
		}

		Node? readings = hud.FindChild("UnitStatsReadings_VBoxContainer", true, false);
		Check("readings section visible", readings is Control { Visible: true });
		CheckReading(readings, "Innate", FactionInnate, Check);
		CheckReading(readings, "Relics", "0 / 6", Check);
		if (!IsSable)
		{
			string? bulwark = ReadReadingValue(readings, "Bulwark");
			Check("bulwark row present", bulwark != null, bulwark ?? "missing");
			Check("bulwark uses one building", bulwark != null && bulwark.Contains("1 building", StringComparison.Ordinal), bulwark ?? "missing");
		}

		CheckReadingsFold("hero", readings);

		// The stat sheet is read through category icons: the frame stays a short strip, and hovering a category shows the
		// rows that belong to it. The pointer is driven by emitting the same signals Godot sends, so the popup path is
		// exercised without depending on where the operating system's cursor happens to be during an automated run.
		Node? categoryStrip = hud.FindChild("UnitStatsCategories_HBoxContainer", true, false);
		Check("the stat sheet folds into category icons", categoryStrip is HBoxContainer, categoryStrip?.Name ?? "missing");
		if (categoryStrip is HBoxContainer strip)
		{
			int visibleIcons = 0;
			foreach (Node child in strip.GetChildren())
			{
				if (child is Control { Visible: true }) visibleIcons++;
			}

			Check("every stat category a unit has shows an icon", visibleIcons >= 5, $"icons={visibleIcons}");
			int foldedRows = hud.FindChildren("UnitStatsRow*", string.Empty, true, false).Count;
			Check("no stat row is drawn until a category is hovered", foldedRows == 0, $"rows={foldedRows}");
			SaveUxScreenshot("ux_smoke_stats_folded.png", "folded stats", Note);

			Node? offenseIcon = hud.FindChild("UnitStatsCategoryOffense_Label", true, false);
			Check("the offense category has an icon to hover", offenseIcon is Label);
			if (offenseIcon is Label offenseLabel)
			{
				offenseLabel.EmitSignal(Control.SignalName.MouseEntered);
				await ToSignal(GetTree().CreateTimer(0.35f), SceneTreeTimer.SignalName.Timeout);

				Node? popup = hud.FindChild("UnitStatsCategoryPopup_PopupPanel", true, false);
				Check("hovering a category opens its popup", popup is PopupPanel { Visible: true });
				int popupRows = 0;
				if (hud.FindChild("UnitStatsCategoryPopupRows_VBoxContainer", true, false) is VBoxContainer popupRowList)
				{
					foreach (Node child in popupRowList.GetChildren())
					{
						if (child.Name.ToString().StartsWith("UnitStatsRow", StringComparison.Ordinal)) popupRows++;
					}
				}

				Check("the popup lists the category's own rows", popupRows >= 4, $"rows={popupRows}");
				Note($"offense popup position={((PopupPanel?)popup)?.Position} size={((PopupPanel?)popup)?.Size}");
				SaveUxScreenshot("ux_smoke_stats_popup.png", "stat category popup", Note);

				offenseLabel.EmitSignal(Control.SignalName.MouseExited);
				await ToSignal(GetTree().CreateTimer(0.6f), SceneTreeTimer.SignalName.Timeout);
				Check("leaving the icon closes its popup", popup is not PopupPanel { Visible: true });
			}
		}

		List<Button> commandButtons = FindCommandButtons(hud);
		Note($"hero command buttons={commandButtons.Count}");
		for (int index = 0; index < commandButtons.Count; index++)
		{
			Note($"hero command[{index}] tooltip='{commandButtons[index].TooltipText}' disabled={commandButtons[index].Disabled}");
		}

		Check("hero card offers an active", FindCommandButton(hud, FactionActiveName) != null);
		if (IsSable)
		{
			Check("Sable's card offers Ember Lance", FindCommandButton(hud, "Ember Lance") != null);
			Check("Sable's card offers Backdraft", FindCommandButton(hud, "Backdraft") != null);
		}

		Button? beginButton = FindCommandButton(hud, "Begin Construction");
		Check("begin construction card present", beginButton != null);
		if (beginButton != null)
		{
			Check("begin construction card enabled", !beginButton.Disabled);

			// A card's key is the slot it is drawn in, so the build command may not be wired to B regardless of where the
			// grid puts it: the tooltip has to name a grid key, and that key has to be the one the card answers.
			string beginTooltip = beginButton.TooltipText;
			char beginKey = beginTooltip.StartsWith('[') && beginTooltip.Length > 2 ? beginTooltip[1] : '\0';
			Check("begin construction names a grid key", "QWERASDFZXCV".Contains(beginKey), beginTooltip);
			Check("begin construction is not hard-wired to B", beginKey != 'B', beginTooltip);

			float woodBefore = hud.Wood;
			bool answeredByKey = beginKey != '\0' && hud.HandleCommandCardHotkey(ParseHotkey(beginKey.ToString()), (KeyModifierMask)0);
			Check("begin construction answers the key its slot names", answeredByKey, beginTooltip);
			await ToSignal(GetTree().CreateTimer(0.3f), SceneTreeTimer.SignalName.Timeout);
			Check("begin construction asks for a site", currentHost.ActiveSpellTargeting == "ability/begin_construction", currentHost.ActiveSpellTargeting ?? "none");

			currentHost.CastSpellAt("ability/begin_construction", hero.GlobalPosition + new Vector3(2f, 0f, 2f));
			await ToSignal(GetTree().CreateTimer(1.2f), SceneTreeTimer.SignalName.Timeout);
			Check("begin construction reserves 100 wood", hud.Wood <= woodBefore - 90f, $"{woodBefore:0.##} -> {hud.Wood:0.##}");

			string draftFeedback = ReadFeedback(hud);
			Check("the draw announces its offers", draftFeedback.Contains("offered", StringComparison.OrdinalIgnoreCase), draftFeedback);

			// The offers belong to the blueprint that was marked on the ground, not to the hero that cast the draw. A
			// player who never selects the site sees no packages, which is what separates the two grids.
			Unit3D? blueprint = FindUnit(currentHost, "unit/blueprint", 1);
			Check("the draw stands on the board as a blueprint", blueprint != null);
			Check("the hero does not list the drawn packages", FindDraftOffers().Count == 0, $"hero cards={FindDraftOffers().Count}");
			if (blueprint != null)
			{
				currentHost.SelectUnitInternal(new UnitId(blueprint.Entity.Id));
				await ToSignal(GetTree().CreateTimer(0.6f), SceneTreeTimer.SignalName.Timeout);

				List<Button> draftOptions = FindDraftOffers();
				Check("all three drawn packages are on the blueprint's grid", draftOptions.Count == 3, $"cards={draftOptions.Count}");

				List<Button> draftCard = FindCommandButtons(hud);
				List<string> draftKeys = ReadCommandHotkeys(draftCard);
				Note($"draft card buttons={draftCard.Count} keys=[{string.Join(", ", draftKeys)}]");
				Check("the drawn card names a key for every command", draftKeys.Count == draftCard.Count, $"keys={draftKeys.Count} buttons={draftCard.Count}");
				Check("the drawn card has no duplicate keys", HasDuplicateHotkeys(draftKeys) == false, string.Join(", ", draftKeys));
				SaveUxScreenshot("ux_smoke_draft.png", "draft", Note);

				// What the draw offers is read from the hero's sheet while the site holds the cards, so both halves of
				// the draw are exercised: the packages where they are picked, the offer list where it is read.
				currentHost.SelectUnitInternal(new UnitId(hero.Entity.Id));
				await ToSignal(GetTree().CreateTimer(0.4f), SceneTreeTimer.SignalName.Timeout);
				Check("begin construction is withdrawn while its draw is open", FindCommandButton(hud, "Begin Construction") == null);
				Node? draftReadings = hud.FindChild("UnitStatsReadings_VBoxContainer", true, false);
				int offeredReadings = CountReadingRowsNamed(draftReadings, "Offered");
				Check("the draw publishes an offered reading per package", offeredReadings == 3, $"rows={offeredReadings}");
				CheckReadingsFold("hero with the draw open", draftReadings);

				// Abandoning is the only way back out of a draw, so it is played before anything is locked: the reserved
				// wood has to return, the begin card has to come back and the offers have to leave both grids.
				currentHost.SelectUnitInternal(new UnitId(blueprint.Entity.Id));
				await ToSignal(GetTree().CreateTimer(0.4f), SceneTreeTimer.SignalName.Timeout);
				Button? abandonButton = FindCommandButton(hud, "Abandon Construction");
				Check("the drawn card offers the abandon command", abandonButton != null);
				if (abandonButton != null)
				{
					List<Button> abandonCard = FindCommandButtons(hud);
					List<string> abandonKeys = ReadCommandHotkeys(abandonCard);
					int abandonIndex = abandonCard.IndexOf(abandonButton);
					string abandonKey = abandonIndex >= 0 && abandonIndex < abandonKeys.Count ? abandonKeys[abandonIndex] : string.Empty;
					Check("the abandon command names its grid key", abandonKey.Length > 0, abandonKey);
					bool abandonedByHotkey = abandonKey.Length > 0 && hud.HandleCommandCardHotkey(ParseHotkey(abandonKey), (KeyModifierMask)0);
					Check("the draw answers the abandon hotkey", abandonedByHotkey, abandonKey);
				}

				await ToSignal(GetTree().CreateTimer(1.0f), SceneTreeTimer.SignalName.Timeout);
				Check("abandoning the draw returns its wood", hud.Wood >= woodBefore - 1f, $"wood {hud.Wood:0.##} expected {woodBefore:0.##}");
				string abandonFeedback = ReadFeedback(hud);
				Check("abandoning the draw announces the refund", abandonFeedback.Contains("returned", StringComparison.OrdinalIgnoreCase), abandonFeedback);
				Check("abandoning the draw clears its blueprint", FindUnit(currentHost, "unit/blueprint", 1) == null);

				currentHost.SelectUnitInternal(new UnitId(hero.Entity.Id));
				await ToSignal(GetTree().CreateTimer(0.4f), SceneTreeTimer.SignalName.Timeout);
				Check("abandoning the draw restores the begin card", FindCommandButton(hud, "Begin Construction") != null);
				Check("abandoning the draw withdraws the offers", FindDraftOffers().Count == 0);
				Node? abandonedReadings = hud.FindChild("UnitStatsReadings_VBoxContainer", true, false);
				Check("abandoning the draw clears the offered readings", CountReadingRowsNamed(abandonedReadings, "Offered") == 0);
				SaveUxScreenshot("ux_smoke_abandoned.png", "abandoned", Note);
			}
			else
			{
				Check("the draw can be abandoned", FindCommandButton(hud, "Abandon Construction") != null);
				if (FindCommandButton(hud, "Abandon Construction") is { } fallbackAbandon)
				{
					fallbackAbandon.EmitSignal(Button.SignalName.Pressed);
					await ToSignal(GetTree().CreateTimer(1.0f), SceneTreeTimer.SignalName.Timeout);
				}
			}

			Button? reopenButton = FindCommandButton(hud, "Begin Construction");
			Check("the begin card answers again after an abandon", reopenButton != null && !reopenButton.Disabled);
			if (reopenButton != null)
			{
				reopenButton.EmitSignal(Button.SignalName.Pressed);
				await ToSignal(GetTree().CreateTimer(0.3f), SceneTreeTimer.SignalName.Timeout);
				Check("the second draw asks for a site", currentHost.ActiveSpellTargeting == "ability/begin_construction", currentHost.ActiveSpellTargeting ?? "none");

				currentHost.CastSpellAt("ability/begin_construction", hero.GlobalPosition + new Vector3(2f, 0f, 2f));
				await ToSignal(GetTree().CreateTimer(1.2f), SceneTreeTimer.SignalName.Timeout);
				Check("the second draw reserves 100 wood", hud.Wood <= woodBefore - 90f, $"{woodBefore:0.##} -> {hud.Wood:0.##}");
				Unit3D? secondBlueprint = FindUnit(currentHost, "unit/blueprint", 1);
				Check("the second draw stands on the board", secondBlueprint != null);
				if (secondBlueprint != null)
				{
					currentHost.SelectUnitInternal(new UnitId(secondBlueprint.Entity.Id));
					await ToSignal(GetTree().CreateTimer(0.5f), SceneTreeTimer.SignalName.Timeout);
					Check("the second draw offers three packages", FindDraftOffers().Count == 3, $"cards={FindDraftOffers().Count}");
				}

				// A hero can die while its draw is open, and the draw has to close with it: the reserved wood comes back
				// and the card returns to the state a player who never began a construction sees.
				Note("dying with the second draw open");
				string midDrawDeath = await SacrificeHeroAsync(hero);
				Check("the death states when the hero returns", midDrawDeath.Contains("returns in", StringComparison.OrdinalIgnoreCase), midDrawDeath);
				await ToSignal(GetTree().CreateTimer(1.0f), SceneTreeTimer.SignalName.Timeout);

				Check("dying with a draw open returns its wood", hud.Wood >= woodBefore - 1f, $"wood {hud.Wood:0.##} expected {woodBefore:0.##}");
				Check("dying with a draw open withdraws the offers", FindUnit(currentHost, "unit/blueprint", 1) == null);
				Check("a fallen hero offers no construction draw", FindCommandButton(hud, "Begin Construction") == null);
				Node? midDrawReadings = hud.FindChild("UnitStatsReadings_VBoxContainer", true, false);
				Check("dying with a draw open clears the offered readings", CountReadingRowsNamed(midDrawReadings, "Offered") == 0);

				hero = await WaitForRevivalAsync();
				Check("the hero revives after dying inside a draw", hero != null);
				if (hero != null)
				{
					currentHost.SelectUnitInternal(new UnitId(hero.Entity.Id));
					await ToSignal(GetTree().CreateTimer(1.0f), SceneTreeTimer.SignalName.Timeout);
					Check("dying with a draw open restores the begin card", FindCommandButton(hud, "Begin Construction") is { Disabled: false });
				}
			}

			if (hero == null) return FinishUxSmoke(report, passed);
			currentHost.SelectUnitInternal(new UnitId(hero.Entity.Id));
			await ToSignal(GetTree().CreateTimer(1.0f), SceneTreeTimer.SignalName.Timeout);

			Button? finalDrawButton = FindCommandButton(hud, "Begin Construction");
			Check("the begin card opens a draw after a revival", finalDrawButton != null && !finalDrawButton.Disabled);
			if (finalDrawButton != null)
			{
				finalDrawButton.EmitSignal(Button.SignalName.Pressed);
				await ToSignal(GetTree().CreateTimer(0.3f), SceneTreeTimer.SignalName.Timeout);
				Check("the final draw asks for a site", currentHost.ActiveSpellTargeting == "ability/begin_construction", currentHost.ActiveSpellTargeting ?? "none");

				currentHost.CastSpellAt("ability/begin_construction", hero.GlobalPosition + new Vector3(2f, 0f, 2f));
				await ToSignal(GetTree().CreateTimer(1.2f), SceneTreeTimer.SignalName.Timeout);
				Check("the final draw reserves 100 wood", hud.Wood <= woodBefore - 90f, $"{woodBefore:0.##} -> {hud.Wood:0.##}");
			}

			Unit3D? finalBlueprint = FindUnit(currentHost, "unit/blueprint", 1);
			Check("the final draw stands on the board", finalBlueprint != null);
			if (finalBlueprint != null)
			{
				currentHost.SelectUnitInternal(new UnitId(finalBlueprint.Entity.Id));
				await ToSignal(GetTree().CreateTimer(0.5f), SceneTreeTimer.SignalName.Timeout);
				List<Button> draftOptions = FindDraftOffers();
				Check("the final draw offers three packages", draftOptions.Count == 3, $"cards={draftOptions.Count}");
				List<Button> draftCard = FindCommandButtons(hud);
				List<string> draftKeys = ReadCommandHotkeys(draftCard);

				if (draftOptions.Count > 0)
				{
					int buildingsBefore = CountPlayerBuildings(currentHost, 1);

					// The Mender Shrine is preferred when the draw offers it, because its soldier carries an ability card:
					// that is the only way this run can see a locked soldier's card stay readable and unpressable.
					Button drafted = draftOptions.Find(option => option.TooltipText.Contains("Mender Shrine", StringComparison.OrdinalIgnoreCase)) ?? draftOptions[0];
					string draftedCard = drafted.TooltipText.Split('\n')[0];
					int offerIndex = draftCard.IndexOf(drafted);
					string offerKey = offerIndex >= 0 && offerIndex < draftKeys.Count ? draftKeys[offerIndex] : string.Empty;
					Key offerHotkey = ParseHotkey(offerKey);
					Check("the drafted offer names its hotkey", offerHotkey != Key.None, offerKey);
					bool lockedByHotkey = offerHotkey != Key.None && hud.HandleCommandCardHotkey(offerHotkey, (KeyModifierMask)0);
					Check("the drafted offer answers its hotkey", lockedByHotkey, offerKey);

					// The announcement is a transient line the HUD replaces when it next speaks, such as the idle-worker
					// notice that follows a finished unit. It is watched for as it appears instead of read once later.
					bool siteAnnounced = false;
					string siteFeedback = string.Empty;
					for (int attempt = 0; attempt < 15; attempt++)
					{
						siteFeedback = ReadFeedback(hud);
						if (siteFeedback.Contains("under construction", StringComparison.OrdinalIgnoreCase))
						{
							siteAnnounced = true;
							break;
						}

						await ToSignal(GetTree().CreateTimer(0.1f), SceneTreeTimer.SignalName.Timeout);
					}

					int buildingsAfter = CountPlayerBuildings(currentHost, 1);
					Check("locking a draft starts its site", buildingsAfter == buildingsBefore + 1, $"buildings {buildingsBefore} -> {buildingsAfter}");
					Check("the site announces construction", siteAnnounced, siteFeedback);
					Check("locking the draw answers its blueprint", FindUnit(currentHost, "unit/blueprint", 1) == null);
					Note($"drafted card='{draftedCard}'");
				}
			}
		}

		currentHost.SelectUnitInternal(new UnitId(hero.Entity.Id));
		await ToSignal(GetTree().CreateTimer(0.5f), SceneTreeTimer.SignalName.Timeout);

		string activeAbility = IsSable ? "ability/cinderfield" : "ability/grounding_shout";
		float activeManaCost = IsSable ? 100f : 60f;
		Button? activeButton = FindCommandButton(hud, FactionActiveName);
		Check("the hero active card is present after the draw", activeButton != null);
		if (activeButton != null)
		{
			Check("the hero active card is enabled", !activeButton.Disabled);
			if (IsSable)
			{
				Check("Cinderfield's tooltip states its level scaling", activeButton.TooltipText.Contains("rising to 5 seconds with level", StringComparison.OrdinalIgnoreCase), activeButton.TooltipText);
			}

			float manaBefore = ReadMana(hero);
			activeButton.EmitSignal(Button.SignalName.Pressed);
			await ToSignal(GetTree().CreateTimer(0.3f), SceneTreeTimer.SignalName.Timeout);
			Check("the active asks for a target", currentHost.ActiveSpellTargeting == activeAbility, currentHost.ActiveSpellTargeting ?? "none");

			Vector3 activeTarget = IsSable
				? hero.GlobalPosition + new Vector3(6f, 0f, 0f)
				: hero.GlobalPosition + new Vector3(2f, 0f, 2f);
			FocusCamera(activeTarget);
			await ToSignal(GetTree().CreateTimer(0.3f), SceneTreeTimer.SignalName.Timeout);
			currentHost.CastSpellAt(activeAbility, activeTarget);
			await ToSignal(GetTree().CreateTimer(0.5f), SceneTreeTimer.SignalName.Timeout);

			if (IsSable)
			{
				string fieldFeedback = ReadFeedback(hud);
				Check("Cinderfield announces the burning ground", fieldFeedback.Contains("burns the ground", StringComparison.OrdinalIgnoreCase), fieldFeedback);
				GroundShockwave3D? groundRing = currentHost.FindChild("GroundShockwave3D", true, false) as GroundShockwave3D;
				Check("Cinderfield draws its ground ring in the world", groundRing != null);
				Check("Cinderfield's ground ring covers its stated radius", groundRing != null && Mathf.Abs(groundRing.MaxRadius - 5f) < 0.01f, $"radius {groundRing?.MaxRadius}");
				Check("Cinderfield's ground ring stands on screen", IsOnScreen(activeTarget), $"cast at {activeTarget} camera {ActiveCamera()?.Position}");
				SaveUxScreenshot("ux_smoke_cinderfield.png", "cinderfield", Note);
			}

			await ToSignal(GetTree().CreateTimer(0.4f), SceneTreeTimer.SignalName.Timeout);
			float manaAfter = ReadMana(hero);
			Check("the active spends its mana", manaAfter <= manaBefore - (activeManaCost - 10f), $"{manaBefore:0.#} -> {manaAfter:0.#}");
		}

		Unit3D? nexus = FindUnit(currentHost, "building/nexus", 1);
		Check("player Nexus replicated", nexus != null);
		if (nexus == null) return FinishUxSmoke(report, passed);

		Note("ordering the hero to gather gold from the nearest mine");
		Unit3D? gatheringHero = FindUnit(currentHost, "hero/", 1);
		Vector3? minePosition = gatheringHero == null ? null : FindNearestResourceNode(gatheringHero.GlobalPosition);
		Check("a gold mine stands on the board", minePosition.HasValue);
		if (gatheringHero != null && minePosition.HasValue)
		{
			currentHost.SelectUnitInternal(new UnitId(gatheringHero.Entity.Id));
			await ToSignal(GetTree().CreateTimer(0.5f), SceneTreeTimer.SignalName.Timeout);
			float goldBeforeGather = hud.Gold;
			Check("the gather order is issued", currentHost.TryIssueGatherCommandAt(minePosition.Value));
			string gatherFeedback = ReadFeedback(hud);
			Check("the gather order names its resource", gatherFeedback.Contains("Gathering", StringComparison.OrdinalIgnoreCase), gatherFeedback);

			bool gathered = await WaitUntilAsync(() => hud.Gold >= goldBeforeGather + 5f, 30f);
			Check("the hero gathers gold and the player receives it", gathered, $"{goldBeforeGather:0.##} -> {hud.Gold:0.##}");

			Note("ordering the hero to gather wood from the nearest tree");
			Vector3? treePosition = FindNearestResourceNode(gatheringHero.GlobalPosition, "resource/melee_woodland");
			Check("a woodland tree stands on the board", treePosition.HasValue);
			if (treePosition.HasValue)
			{
				currentHost.SelectUnitInternal(new UnitId(gatheringHero.Entity.Id));
				await ToSignal(GetTree().CreateTimer(0.5f), SceneTreeTimer.SignalName.Timeout);
				float woodBeforeGather = hud.Wood;
				Check("the wood gather order is issued", currentHost.TryIssueGatherCommandAt(treePosition.Value));
				string woodFeedback = ReadFeedback(hud);
				Check("the wood gather order names its resource", woodFeedback.Contains("WOOD", StringComparison.OrdinalIgnoreCase), woodFeedback);

				bool woodGathered = await WaitUntilAsync(() => hud.Wood >= woodBeforeGather + 3f, 30f);
				Check("the hero gathers wood and the player receives it", woodGathered, $"{woodBeforeGather:0.##} -> {hud.Wood:0.##}");
			}

			currentHost.IssueMoveCommand(nexus.GlobalPosition);
			await ToSignal(GetTree().CreateTimer(3.0f), SceneTreeTimer.SignalName.Timeout);
		}

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

		// The design floats a soldier-granting building's charge count over it for every player, without a selection. The
		// badge is read from the plate the screen actually draws, so a badge that stopped following the charges fails here
		// even while the inspector still reports them.
		Unit3D? chargeBuilding = FindPlayerProductionBuilding(currentHost);
		UnitHealthBarOverlay? healthBars = hud.FindChild("UnitHealthBarOverlay", true, false) as UnitHealthBarOverlay;
		bool badgeAppeared = false;
		if (chargeBuilding != null)
		{
			FocusCamera(chargeBuilding.GlobalPosition);
			badgeAppeared = await WaitUntilAsync(
				() => healthBars != null && healthBars.TryReadBadge(chargeBuilding.Entity, out int value) && value > 0, 60f);
		}
		Check("a soldier-granting building floats its charge count without a selection", badgeAppeared,
			$"building={chargeBuilding?.UnitId ?? "missing"}");
		Check("the floating charge count is the charge count the building holds",
			ReadOverheadCharge(chargeBuilding, healthBars, out int badgeNow, out int chargesNow), $"badge={badgeNow} charges={chargesNow}");

		Note("waiting for the drafted building to finish construction and raise its first soldier");
		bool soldierArrived = await WaitUntilAsync(() => FindRaisedSoldier() != null, 55f);
		Check("the drafted building raises its first soldier", soldierArrived);

		if (chargeBuilding != null && GodotObject.IsInstanceValid(chargeBuilding))
		{
			FocusCamera(chargeBuilding.GlobalPosition);
			await ToSignal(GetTree().CreateTimer(0.4f), SceneTreeTimer.SignalName.Timeout);
			Check("the floating charge count follows the charge the soldier spent",
				ReadOverheadCharge(chargeBuilding, healthBars, out int badgeAfterSpend, out int chargesAfterSpend),
				$"badge={badgeAfterSpend} charges={chargesAfterSpend}");
		}

		Unit3D? soldier = FindRaisedSoldier();
		if (soldier != null)
		{
			currentHost.SelectUnitInternal(new UnitId(soldier.Entity.Id));
			await ToSignal(GetTree().CreateTimer(1.0f), SceneTreeTimer.SignalName.Timeout);

			Node? soldierReadings = hud.FindChild("UnitStatsReadings_VBoxContainer", true, false);
			Check("soldier readings visible", soldierReadings is Control { Visible: true });
			Check("soldier reports its formation", ReadReadingValue(soldierReadings, "Formation") is { Length: > 0 }, ReadReadingValue(soldierReadings, "Formation") ?? "missing");
			Check("soldier reports its targeting rule", ReadReadingValue(soldierReadings, "Targeting") is { Length: > 0 }, ReadReadingValue(soldierReadings, "Targeting") ?? "missing");
			Check("soldier reports its source building", ReadReadingValue(soldierReadings, "Produced by") is { Length: > 0 }, ReadReadingValue(soldierReadings, "Produced by") ?? "missing");
			Check("soldier reports the charges of its source building", ReadReadingValue(soldierReadings, "Charges") is { Length: > 0 }, ReadReadingValue(soldierReadings, "Charges") ?? "missing");
			CheckReadingsFold("soldier", soldierReadings);

			// A soldier is the map's to command: its cards stay on the grid so a player can read what the army does, but
			// nothing on that grid may be pressed. The order cards are left off entirely rather than offered and refused.
			// The lock is a replicated component, so it may land a description after the unit's spawn record does. Waiting
			// for it is part of the check: reading the grid the instant the soldier appears measured the frame before the
			// map's own tag had been applied and called a correct lock a missing one.
			bool soldierOrdersLocked = await WaitUntilAsync(
				() => GameContext.World != null && GameContext.World.Has<PlayerOrdersLocked>(soldier.Entity), 5f);
			Note($"selected unit type={soldier.UnitId} entity={soldier.Entity.Id} player={soldier.Player} ordersLocked={soldierOrdersLocked}");
			await ToSignal(GetTree().CreateTimer(0.4f), SceneTreeTimer.SignalName.Timeout);
			List<Button> soldierCards = FindCommandButtons(hud);
			for (int cardIndex = 0; cardIndex < soldierCards.Count; cardIndex++)
			{
				Note($"soldier card[{cardIndex}] disabled={soldierCards[cardIndex].Disabled} tooltip='{soldierCards[cardIndex].TooltipText.Split('\n')[0]}'");
			}
			Check("the map locks the soldier it raised", soldierOrdersLocked);
			if (string.Equals(soldier.UnitId, "unit/mender", StringComparison.Ordinal))
			{
				Check("a soldier still shows the ability card its tooltip describes", soldierCards.Count > 0, $"cards={soldierCards.Count}");
				Check("a soldier's ability card names its tooltip", soldierCards.Exists(card => card.TooltipText.Contains("Mend", StringComparison.OrdinalIgnoreCase)), string.Join(" | ", soldierCards.Select(card => card.TooltipText.Split('\n')[0])));
			}

			Check("a soldier's cards are disabled to the player", soldierCards.TrueForAll(card => card.Disabled), $"enabled={soldierCards.Count(card => !card.Disabled)}");
			Check("a soldier offers no move card", FindCommandButton(hud, "Move / Right-Click Ground") == null);
			Check("a soldier offers no attack card", FindCommandButton(hud, "Attack / Attack-Move") == null);
			FocusCamera(soldier.GlobalPosition);
			await ToSignal(GetTree().CreateTimer(0.4f), SceneTreeTimer.SignalName.Timeout);
			Check("the soldier stands on screen", IsOnScreen(soldier.GlobalPosition), $"soldier {soldier.GlobalPosition} camera {ActiveCamera()?.Position}");
			SaveUxScreenshot("ux_smoke_soldier.png", "soldier", Note);
		}

		Unit3D? productionBuilding = FindPlayerProductionBuilding(currentHost);
		Check("the drafted building is replicated", productionBuilding != null);
		if (productionBuilding != null)
		{
			currentHost.SelectUnitInternal(new UnitId(productionBuilding.Entity.Id));
			await ToSignal(GetTree().CreateTimer(1.0f), SceneTreeTimer.SignalName.Timeout);

			Node? buildingReadings = hud.FindChild("UnitStatsReadings_VBoxContainer", true, false);
			Check("building readings visible", buildingReadings is Control { Visible: true });
			Check("building reports what it unlocks", ReadReadingValue(buildingReadings, "Unlocks") is { Length: > 0 }, ReadReadingValue(buildingReadings, "Unlocks") ?? "missing");
			Check("building reports its capacity", ReadReadingValue(buildingReadings, "Capacity") is { Length: > 0 }, ReadReadingValue(buildingReadings, "Capacity") ?? "missing");
			Check("building reports its income", ReadReadingValue(buildingReadings, "Income") is { Length: > 0 }, ReadReadingValue(buildingReadings, "Income") ?? "missing");
			Check("building reports its production", ReadReadingValue(buildingReadings, "Production") is { Length: > 0 }, ReadReadingValue(buildingReadings, "Production") ?? "missing");
			CheckReadingsFold("building", buildingReadings);
			FocusCamera(productionBuilding.GlobalPosition);
			await ToSignal(GetTree().CreateTimer(0.4f), SceneTreeTimer.SignalName.Timeout);
			Check("the drafted building stands on screen", IsOnScreen(productionBuilding.GlobalPosition), $"building {productionBuilding.GlobalPosition} camera {ActiveCamera()?.Position}");
			SaveUxScreenshot("ux_smoke_building.png", "building", Note);
		}

		// The board guards its middle with a neutral camp, and the soldier that was just raised is the army sent to
		// clear it. This is the only fight the screen can show before the enemy base is described: the hero marches
		// there, the camp fights back, the kill pays the bounty its template states, and the camp returns on its timer.
		Check("the shroud cheat is accepted", hud.TryTriggerCheat("aura"));
		Unit3D? boar = await WaitForUnitAsync(currentHost, "unit/crest_boar", 0, 15f);
		if (boar != null) boar = FindNearestUnit(currentHost, "unit/crest_boar", 0, hero.GlobalPosition) ?? boar;
		Check("the neutral camp is described once the board is revealed", boar != null, $"camp {boar?.GlobalPosition.ToString() ?? "missing"}");
		if (boar != null)
		{
			Vector3 campPosition = boar.GlobalPosition;
			int campsBefore = CountUnits(currentHost, "unit/crest_boar", 0);
			Note($"marching the army onto the camp at {campPosition} with {campsBefore} camp(s) on the board");
			float goldBeforeCamp = hud.Gold;
			currentHost.SelectUnitInternal(new UnitId(hero.Entity.Id));
			await ToSignal(GetTree().CreateTimer(0.4f), SceneTreeTimer.SignalName.Timeout);
			currentHost.IssueMoveCommand(campPosition);
			bool arrived = await WaitUntilAsync(() => GodotObject.IsInstanceValid(hero) && hero.GlobalPosition.DistanceTo(campPosition) < 12f, 75f);
			Check("the army marches to the neutral camp", arrived, $"{hero.GlobalPosition} -> camp {campPosition}");

			// The soldier walks slower than the hero and follows its slot, so the army is gathered at the camp rather
			// than beside the hero at every moment of the march.
			bool soldierJoined = await WaitUntilAsync(() =>
			{
				Unit3D? follower = FindRaisedSoldier();
				return follower != null && follower.GlobalPosition.DistanceTo(campPosition) < 25f;
			}, 25f);
			Unit3D? follower = FindRaisedSoldier();
			Check("the soldier marches with the hero to the camp", soldierJoined,
				follower == null ? "no soldier" : $"soldier at {follower.GlobalPosition}, camp {campPosition}");

			FocusCamera(campPosition);
			await ToSignal(GetTree().CreateTimer(0.6f), SceneTreeTimer.SignalName.Timeout);
			Check("the camp stands on screen", IsOnScreen(boar.GlobalPosition), $"camp {boar.GlobalPosition} camera {ActiveCamera()?.Position}");

			float boarHealthBefore = ReadHealth(boar);
			currentHost.SelectUnitInternal(new UnitId(hero.Entity.Id));
			await ToSignal(GetTree().CreateTimer(0.4f), SceneTreeTimer.SignalName.Timeout);
			currentHost.IssueAttackCommand(boar);
			bool struck = await WaitUntilAsync(() => !GodotObject.IsInstanceValid(boar) || ReadHealth(boar) < boarHealthBefore - 0.5f, 20f);
			float boarHealthAfterFirstBlow = GodotObject.IsInstanceValid(boar) ? ReadHealth(boar) : 0f;
			Note($"camp health {boarHealthBefore:0.#} -> {boarHealthAfterFirstBlow:0.#}");
			Check("the camp takes the army's blows", struck);

			MeshInstance3D? targetMarker = currentHost.FindChild("AttackTargetMarker_0", true, false) as MeshInstance3D;
			Check("the camp being attacked carries a target marker", targetMarker is { Visible: true });
			Check("the target marker stands over the camp",
				targetMarker != null && targetMarker.GlobalPosition.DistanceTo(campPosition) < 8f,
				targetMarker == null ? "no marker" : $"marker {targetMarker.GlobalPosition}, camp {campPosition}");

			MinimapOverlay? minimapOverlay = hud.FindChild("MinimapOverlay", true, false) as MinimapOverlay;
			bool minimapRinged = await WaitUntilAsync(() => minimapOverlay != null && minimapOverlay.IsAttackTargetRinged(boar), 10f);
			Check("the minimap rings the camp being attacked", minimapRinged, MinimapRingDetail(minimapOverlay, boar));
			SaveUxScreenshot("ux_smoke_battle.png", "battle", Note);

			bool felled = await WaitUntilAsync(() => !GodotObject.IsInstanceValid(boar) || ReadHealth(boar) <= 0f, 30f);
			Check("the army fells the camp", felled);

			bool ringCleared = await WaitUntilAsync(() => minimapOverlay == null || !minimapOverlay.IsAttackTargetRinged(boar), 10f);
			Check("the minimap ring clears from the felled camp", ringCleared, MinimapRingDetail(minimapOverlay, boar));
			Check("the kill pays the bounty the camp states", hud.Gold >= goldBeforeCamp + 40f, $"gold {goldBeforeCamp:0.#} -> {hud.Gold:0.#}");
			int campsAfterKill = CountUnits(currentHost, "unit/crest_boar", 0);
			bool campGone = await WaitUntilAsync(() => !GodotObject.IsInstanceValid(boar), 10f);
			Check("the felled camp leaves the board", campGone, $"camps {campsBefore} -> {campsAfterKill}");

			Vector3 campWatchPost = campPosition + new Vector3(24f, 0f, 24f);
			currentHost.IssueMoveCommand(campWatchPost);
			await WaitUntilAsync(() => GodotObject.IsInstanceValid(hero) && hero.GlobalPosition.DistanceTo(campPosition) > 16f, 30f);

			Note("waiting for the camp to return on its authored timer");
			bool campReturned = await WaitUntilAsync(() => FindNeutralNear(currentHost, campPosition, 4f) != null, 60f);
			Check("the camp returns where it was placed on the timer its template states", campReturned,
				$"camps {campsAfterKill} -> {CountUnits(currentHost, "unit/crest_boar", 0)}");
		}

		Note("cooking the hero to play the death and revival flow");
		string deathFeedback = await SacrificeHeroAsync(hero);
		Check("the death states when the hero returns", deathFeedback.Contains("returns in", StringComparison.OrdinalIgnoreCase), deathFeedback);
		bool armyDispersed = await WaitUntilAsync(() => FindRaisedSoldier() == null, 5f);
		Check("the hero's army disperses with it", armyDispersed);

		Unit3D? revivedHero = await WaitForRevivalAsync();
		Check("the revived hero is replicated", revivedHero != null);
		if (revivedHero != null)
		{
			currentHost.SelectUnitInternal(new UnitId(revivedHero.Entity.Id));
			await ToSignal(GetTree().CreateTimer(1.5f), SceneTreeTimer.SignalName.Timeout);
			Node? revivedReadings = hud.FindChild("UnitStatsReadings_VBoxContainer", true, false);
			CheckReading(revivedReadings, "Relics", "2 / 6", Check);
			CheckReadingsFold("revived hero", revivedReadings);
			FocusCamera(revivedHero.GlobalPosition);
			await ToSignal(GetTree().CreateTimer(0.4f), SceneTreeTimer.SignalName.Timeout);
			Check("the revived hero stands on screen", IsOnScreen(revivedHero.GlobalPosition), $"hero {revivedHero.GlobalPosition} camera {ActiveCamera()?.Position}");
			SaveUxScreenshot("ux_smoke_revived.png", "revived", Note);
		}

		// A match only ends when the enemy Nexus falls, so the hero is sent at it through the same attack order a right
		// click issues and given the damage a developer needs to reach the end in one blow. What the screen has to show is
		// the fight and then the victory it causes. The board is already revealed by the shroud cheat, so the enemy base
		// is described and can be clicked without marching there to look first.
		Unit3D? enemyNexus = await WaitForUnitAsync(currentHost, "building/nexus", 2, 10f);
		Check("the enemy Nexus replicates once the board is revealed", enemyNexus != null);
		Unit3D? attacker = FindUnit(currentHost, "hero/", 1);
		Check("the hero is standing to fight", attacker != null);
		if (enemyNexus != null && attacker != null)
		{
			currentHost.SelectUnitInternal(new UnitId(attacker.Entity.Id));
			await ToSignal(GetTree().CreateTimer(0.5f), SceneTreeTimer.SignalName.Timeout);
			Check("the hero is the screen's selection", currentHost.SelectedUnits.Count == 1 && currentHost.SelectedUnits[0].Entity == attacker.Entity, $"selected={currentHost.SelectedUnits.Count}");
			Note($"attack ids: hero player={attacker.Player} controllable={GameContext.PerspectiveFilter.CanControl(attacker.Player)} server={currentHost.GetServerEntityIdForHarness(attacker.Entity.Id)} nexus server={currentHost.GetServerEntityIdForHarness(enemyNexus.Entity.Id)}");

			// A player reaches the enemy base by walking there, so the hero does the same and the crossing is checked
			// before the fight: an attack across ground the hero cannot path over would otherwise be read as a refusal.
			Vector3 farAttackStart = attacker.GlobalPosition;
			currentHost.IssueAttackCommand(enemyNexus);
			bool chaseStarted = await WaitUntilAsync(() => GodotObject.IsInstanceValid(attacker) && attacker.GlobalPosition.DistanceTo(farAttackStart) > 3f, 8f);
			float chaseDistance = GodotObject.IsInstanceValid(attacker) ? farAttackStart.DistanceTo(attacker.GlobalPosition) : 0f;
			Note($"far attack: hero moved {chaseDistance:0.##} to {attacker.GlobalPosition}");
			Check("the hero chases the enemy it was ordered onto", chaseStarted, $"moved {chaseDistance:0.##} towards {enemyNexus.GlobalPosition}");

			float distanceBefore = attacker.GlobalPosition.DistanceTo(enemyNexus.GlobalPosition);
			currentHost.IssueMoveCommand(enemyNexus.GlobalPosition);
			bool approached = await WaitUntilAsync(() => GodotObject.IsInstanceValid(attacker) && attacker.GlobalPosition.DistanceTo(enemyNexus.GlobalPosition) < 12f, 45f);
			float distanceAfter = GodotObject.IsInstanceValid(attacker) ? attacker.GlobalPosition.DistanceTo(enemyNexus.GlobalPosition) : distanceBefore;
			Check("the hero crosses the board to the enemy base", approached, $"{distanceBefore:0} -> {distanceAfter:0} at {attacker.GlobalPosition}");

			if (approached)
			{
				float nexusHealthBefore = ReadHealth(enemyNexus);
				currentHost.IssueAttackCommand(enemyNexus);
				Check("the attack order names its target", ReadFeedback(hud).Contains("Attack", StringComparison.OrdinalIgnoreCase), ReadFeedback(hud));
				bool accepted = await WaitUntilAsync(() =>
				{
					EcsWorld? world = GameContext.World;
					return world != null && world.IsAlive(attacker.Entity) && world.Has<AttackTarget>(attacker.Entity);
				}, 3f);
				Note($"the authority states the attack target: {accepted}");
				Check("the dev damage cheat is accepted", hud.TryTriggerCheat("gigachad"));

				FocusCamera(enemyNexus.GlobalPosition);
				await ToSignal(GetTree().CreateTimer(0.6f), SceneTreeTimer.SignalName.Timeout);
				Check("the enemy Nexus stands on screen while it is attacked", IsOnScreen(enemyNexus.GlobalPosition), $"nexus {enemyNexus.GlobalPosition} camera {ActiveCamera()?.Position}");

				bool struck = false;
				for (int second = 1; second <= 45; second++)
				{
					await ToSignal(GetTree().CreateTimer(1f), SceneTreeTimer.SignalName.Timeout);
					struck = !GodotObject.IsInstanceValid(enemyNexus) || ReadHealth(enemyNexus) < nexusHealthBefore - 0.5f;
					if (struck) break;
					if (second % 5 == 0) Note($"attack watch {second}s hero={attacker.GlobalPosition} nexus health={ReadHealth(enemyNexus):0.#}");
				}

				float nexusHealthAfter = GodotObject.IsInstanceValid(enemyNexus) ? ReadHealth(enemyNexus) : 0f;
				Check("the hero's attack takes the enemy Nexus's health", struck, $"nexus {nexusHealthBefore:0.#} -> {nexusHealthAfter:0.#} hero={attacker.GlobalPosition}");
				SaveUxScreenshot("ux_smoke_combat.png", "combat", Note);

				// Breaking a 6000 point Nexus with one hero takes minutes of real fighting, so the ending is reached the way
				// a developer reaches it: the end-match cheat, after which the screen has to be the victory card and not the
				// match still running behind it.
				Check("the end-match cheat is accepted", hud.TryTriggerCheat("ezclap"));
				bool victoryShown = await WaitUntilAsync(() =>
				{
					GameOver? over = GameContext.UserInterface?.GetNodeOrNull<GameOver>("GameOver");
					return over != null && string.Equals(over.GetNodeOrNull<Label>("TitleLabel")?.Text, "VICTORY", StringComparison.Ordinal);
				}, 40f);
				GameOver? over = GameContext.UserInterface?.GetNodeOrNull<GameOver>("GameOver");
				string title = over?.GetNodeOrNull<Label>("TitleLabel")?.Text ?? "no game over screen";
				Check("the ended match shows the victory card", victoryShown, $"title '{title}'");
				SaveUxScreenshot("ux_smoke_victory.png", "victory", Note);
			}
		}

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

	private static Vector3? FindNearestResourceNode(Vector3 near, string propPrefix = "resource/melee_gold_mine")
	{
		if (GameContext.World == null) return null;

		Vector3? closest = null;
		float closestDistance = float.MaxValue;
		var query = QueryCache.AllPropIdentityAndPositionQuery;
		GameContext.World.Query(in query, (Entity entity, ref Position position, ref PropIdentity identity) =>
		{
			if (!identity.PropId.StartsWith(propPrefix, StringComparison.OrdinalIgnoreCase)) return;

			var nodePosition = new Vector3(position.Value.X, position.Value.Y, position.Value.Z);
			float distance = near.DistanceTo(nodePosition);
			if (distance >= closestDistance) return;

			closestDistance = distance;
			closest = nodePosition;
		});

		return closest;
	}

	private static Unit3D? FindPlayerProductionBuilding(GameHost host)
	{
		foreach (Unit3D unit in host.AllUnits)
		{
			if (!GodotObject.IsInstanceValid(unit)) continue;
			if (unit.Player != 1) continue;
			if (!unit.TemplateID.StartsWith("building/", StringComparison.OrdinalIgnoreCase)) continue;
			if (unit.TemplateID == "building/nexus") continue;

			return unit;
		}

		return null;
	}

	private async Task<Unit3D?> WaitForUnitAsync(GameHost host, string templatePrefix, int player, float seconds)
	{
		Unit3D? found = FindUnit(host, templatePrefix, player);
		if (found != null) return found;

		await WaitUntilAsync(() => FindUnit(host, templatePrefix, player) != null, seconds);
		return FindUnit(host, templatePrefix, player);
	}

	private static Unit3D? FindNearestUnit(GameHost host, string templatePrefix, int player, Vector3 from)
	{
		Unit3D? nearest = null;
		float nearestDistance = float.MaxValue;
		foreach (Unit3D unit in host.AllUnits)
		{
			if (!GodotObject.IsInstanceValid(unit)) continue;
			if (unit.Player != player) continue;
			if (!unit.TemplateID.StartsWith(templatePrefix, StringComparison.OrdinalIgnoreCase)) continue;

			float distance = unit.GlobalPosition.DistanceTo(from);
			if (distance >= nearestDistance) continue;

			nearest = unit;
			nearestDistance = distance;
		}

		return nearest;
	}

	private static int CountUnits(GameHost host, string templatePrefix, int player)
	{
		int count = 0;
		foreach (Unit3D unit in host.AllUnits)
		{
			if (!GodotObject.IsInstanceValid(unit)) continue;
			if (unit.Player != player) continue;
			if (!unit.TemplateID.StartsWith(templatePrefix, StringComparison.OrdinalIgnoreCase)) continue;

			count++;
		}

		return count;
	}

	private static Unit3D? FindNeutralNear(GameHost host, Vector3 position, float radius)
	{
		foreach (Unit3D unit in host.AllUnits)
		{
			if (!GodotObject.IsInstanceValid(unit)) continue;
			if (unit.Player != 0) continue;
			if (!unit.TemplateID.StartsWith("unit/crest_boar", StringComparison.OrdinalIgnoreCase)) continue;
			if (unit.GlobalPosition.DistanceTo(position) > radius) continue;

			return unit;
		}

		return null;
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

	private static Key ParseHotkey(string hotkey)
	{
		return Enum.TryParse(hotkey, true, out Key key) ? key : Key.None;
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

	private void SaveUxScreenshot(string fileName, string label, Action<string> note)
	{
		if (DisplayServer.GetName() == "headless")
		{
			note($"{label} screenshot skipped: headless display has no viewport texture");
			return;
		}

		Image image = GetViewport().GetTexture().GetImage();
		string screenshotPath = Path.Combine(_outputDirectory, fileName);
		image.SavePng(screenshotPath);
		note($"{label} screenshot={screenshotPath} size={image.GetWidth()}x{image.GetHeight()}");
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

	private static float ReadHealth(Unit3D unit)
	{
		EcsWorld? world = GameContext.World;
		if (world == null || !world.IsAlive(unit.Entity) || !world.Has<Health>(unit.Entity)) return 0f;

		return world.Get<Health>(unit.Entity).Current;
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
			if (child is HBoxContainer { Visible: true } row)
			{
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

			string? nested = ReadReadingValue(child, name);
			if (nested != null) return nested;
		}

		return null;
	}

	private static int CountReadingRowsNamed(Node? readings, string name)
	{
		if (readings == null) return 0;

		int count = 0;
		foreach (Node child in readings.GetChildren())
		{
			if (child is HBoxContainer { Visible: true } row)
			{
				foreach (Node labelNode in row.GetChildren())
				{
					if (labelNode is Label label && label.Name == "UnitStatsReadingRowName_Label" && string.Equals(label.Text, name, StringComparison.Ordinal))
					{
						count++;
						break;
					}
				}
			}

			count += CountReadingRowsNamed(child, name);
		}

		return count;
	}

	private void CheckReading(Node? readings, string name, string expected, Action<string, bool, string> check)
	{
		string? actual = ReadReadingValue(readings, name);
		check($"{name} reading", string.Equals(actual, expected, StringComparison.Ordinal), $"'{actual}' expected '{expected}'");
	}

	private bool IsSable => string.Equals(_faction, "Sable", StringComparison.OrdinalIgnoreCase);

	private string FactionInnate => IsSable ? "Cinder Mark" : "Unbroken";

	private string FactionActiveName => IsSable ? "Cinderfield" : "Grounding Shout";

	private int FinishUxSmoke(StringBuilder report, bool passed)
	{
		report.AppendLine($"result={(passed ? "PASS" : "FAIL")}");
		string reportPath = Path.Combine(_outputDirectory, "ux_smoke.txt");
		File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(false));
		GameLog.LogToConsole($"[UX_SMOKE] result={(passed ? "PASS" : "FAIL")} report={reportPath}");
		return passed ? 0 : 1;
	}
}

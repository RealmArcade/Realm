using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Realm.Ecs;
using Godot;
using Godot.Collections;
using Realm.Client.Network;
using Realm.Client.ReplaySystem;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.UI.MapEditor;
using Realm.Client.VFX;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Array = System.Array;
using Realm.Shared.Abilities;

namespace Realm.Client.Core;
public partial class GameHost
{
	private void HandleGameplayInput(InputEvent @event)
	{
		if (@event is InputEventKey settingsKeyEvent && settingsKeyEvent.Pressed && !settingsKeyEvent.Echo && settingsKeyEvent.Keycode == Key.F12)
		{
			GetViewport().SetInputAsHandled();
			GameContext.UserInterface?.OpenSettingsOverlay();
			return;
		}

		if (@event is InputEventKey confirmationEscapeEvent && confirmationEscapeEvent.Pressed && !confirmationEscapeEvent.Echo && confirmationEscapeEvent.Keycode == Key.Escape
			&& TryCancelActiveConfirmationOverlay())
		{
			GetViewport().SetInputAsHandled();
			return;
		}

		if (GameContext.ReplayPlayback.IsPlayingReplay)
		{
			return;
		}

		bool isSpectator = GameContext.PerspectiveFilter.IsSpectating;
		if (isSpectator)
		{
			if (@event is InputEventKey specKeyEvent && specKeyEvent.Pressed && !specKeyEvent.Echo)
			{
				if (specKeyEvent.Keycode != Key.Escape && specKeyEvent.Keycode != Key.Space && specKeyEvent.Keycode != Key.Z && specKeyEvent.Keycode != Key.Tab)
				{
					GetViewport().SetInputAsHandled();
					return;
				}
			}
			else if (@event is InputEventMouseButton specMouseBtn)
			{
				if (specMouseBtn.ButtonIndex == MouseButton.Right)
				{
					GetViewport().SetInputAsHandled();
					return;
				}

				if (specMouseBtn.ButtonIndex == MouseButton.Left)
				{
					if (ActiveSpellTargeting != null || ActiveCommandTargeting != null || ActiveBuildingPlacementType != null)
					{
						ActiveSpellTargeting = null;
						ActiveCommandTargeting = null;
						CancelBuildingPlacement();
						Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
						GetViewport().SetInputAsHandled();
						return;
					}
				}
			}
		}

		if (@event is InputEventKey escapeEvent && escapeEvent.Pressed && escapeEvent.Keycode == Key.Escape)
		{
			if (ActiveSpellTargeting != null || ActiveCommandTargeting != null)
			{
				ActiveSpellTargeting = null;
				ActiveCommandTargeting = null;
				Input.SetCustomMouseCursor(null);
				Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
				if (GameContext.InGameHud != null)
					Hud.ShowFeedbackText("Targeting Cancelled", new Color(0.8f, 0.8f, 0.8f));
				GetViewport().SetInputAsHandled();
				return;
			}

			if (ActiveBuildingPlacementType != null)
			{
				CancelBuildingPlacement();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (ActivePingMode)
			{
				ActivePingMode = false;
				if (GameContext.InGameHud != null)
					Hud.ShowFeedbackText("Ping Mode Cancelled", new Color(0.8f, 0.8f, 0.8f));
				GetViewport().SetInputAsHandled();
				return;
			}

			if (GameContext.InGameHud != null && Hud.IsBuildSubMenuOpen)
			{
				Hud.ExitBuildSubMenu();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (SelectedUnits.Count > 0)
			{
				if (SelectedUnits.Count == 1 && GameContext.PerspectiveFilter.CanControl(SelectedUnits[0].Player))
				{
					var unit = SelectedUnits[0];
					if (World.Has<ProductionQueue>(unit.Entity) && World.Get<ProductionQueue>(unit.Entity).Count > 0)
					{
						CancelLastQueuedUnit(unit.Entity);
						GetViewport().SetInputAsHandled();
						return;
					}
				}

				ClearSelection();
				GameContext.InGameHud?.RefreshUI(SelectedUnits);
				GetViewport().SetInputAsHandled();
				return;
			}
		}

		if (@event is InputEventKey keyReleaseEvent && !keyReleaseEvent.Pressed && keyReleaseEvent.Keycode == Key.Shift)
		{
			if (ActiveBuildingPlacementType != null)
			{
				CancelBuildingPlacement();
				GameContext.InGameHud?.ExitBuildSubMenu();
				GetViewport().SetInputAsHandled();
				return;
			}
		}

		if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
		{
			if (keyEvent.Keycode >= Key.Key0 && keyEvent.Keycode <= Key.Key9)
			{
				int groupIdx = (int)(keyEvent.Keycode - Key.Key0);
				bool ctrlPressed = Input.IsKeyPressed(Key.Ctrl);
				if (ctrlPressed)
				{
					AssignControlGroup(groupIdx);
					GetViewport().SetInputAsHandled();
					return;
				}
				else
				{
					RecallControlGroup(groupIdx);
					GetViewport().SetInputAsHandled();
					return;
				}
			}

			if (keyEvent.Keycode == Key.F1)
			{
				SelectAllIdleUnits();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (keyEvent.Keycode == Key.F2)
			{
				SelectAllMilitaryUnits();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (keyEvent.Keycode == Key.F4)
			{
				if (GameContext.InGameHud != null)
				{
					Hud.ToggleMinimapTerrain();
				}

				GetViewport().SetInputAsHandled();
				return;
			}

			if (keyEvent.Keycode == Key.G && Input.IsKeyPressed(Key.Alt))
			{
				ActivePingMode = !ActivePingMode;
				if (GameContext.InGameHud != null)
				{
					if (ActivePingMode)
						Hud.ShowFeedbackText("Ping Mode: Click Minimap or Ground to ping", new Color(1.0f, 0.1f, 0.2f));
					else
						Hud.ShowFeedbackText("Ping Mode Cancelled", new Color(0.8f, 0.8f, 0.8f));
				}

				GetViewport().SetInputAsHandled();
				return;
			}

			if (keyEvent.Keycode == Key.A && Input.IsKeyPressed(Key.Ctrl))
			{
				SelectAllMilitaryUnits();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (keyEvent.Keycode == Key.Space)
			{
				CenterCameraOnPrimaryBuilding();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (GameContext.InGameHud != null && Hud.HandleCommandCardHotkey(keyEvent.Keycode, keyEvent.GetModifiersMask()))
			{
				GetViewport().SetInputAsHandled();
				return;
			}


			if (keyEvent.Keycode == Key.Tab)
			{
				bool reverse = keyEvent.ShiftPressed || Input.IsKeyPressed(Key.Shift);
				CycleSelectionFocus(reverse);
				GetViewport().SetInputAsHandled();
				return;
			}

			if (keyEvent.Keycode == Key.F3)
			{
				SelectAllBuildings();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (keyEvent.Keycode >= Key.F5 && keyEvent.Keycode <= Key.F8)
			{
				int slot = (int)(keyEvent.Keycode - Key.F5) + 1;
				bool ctrlPressed = Input.IsKeyPressed(Key.Ctrl);
				if (ctrlPressed)
				{
					SaveCameraLocation(slot);
					GetViewport().SetInputAsHandled();
					return;
				}
				else
				{
					RecallCameraLocation(slot);
					GetViewport().SetInputAsHandled();
					return;
				}
			}

			if (keyEvent.Keycode == Key.Quoteleft)
			{
				if (WasmConsoleWindow.IsSinglePlayerOrTestMode())
				{
					GameContext.WasmConsole.ToggleVisibility();
					GetViewport().SetInputAsHandled();
					return;
				}

				CycleThroughBuildings();
				GetViewport().SetInputAsHandled();
				return;
			}
		}

		if (@event is InputEventMouseButton rightBtn && rightBtn.ButtonIndex == MouseButton.Right && rightBtn.Pressed && !IsMouseOverUI())
		{
			if (ActiveSpellTargeting != null || ActiveCommandTargeting != null || ActiveBuildingPlacementType != null)
			{
				ActiveSpellTargeting = null;
				ActiveCommandTargeting = null;
				CancelBuildingPlacement();
				Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
				GetViewport().SetInputAsHandled();
				return;
			}

			if (SelectedUnits.Count == 0) goto SkipRightClick;

			bool anyFriendlySelected = false;
			foreach (var su in SelectedUnits)
			{
				if (GameContext.PerspectiveFilter.CanControl(su.Player))
				{
					anyFriendlySelected = true;
					break;
				}
			}

			if (!anyFriendlySelected) goto SkipRightClick;

			if (SelectedUnits.Count == 1 && GameContext.PerspectiveFilter.CanControl(SelectedUnits[0].Player) && CanProduceUnits(SelectedUnits[0]))
			{
				var hit = RaycastFromMouse(rightBtn.Position);
				if (hit != null && hit.ContainsKey("position"))
				{
					SetRallyPoint(SelectedUnits[0], hit["position"].AsVector3());
				}

				GetViewport().SetInputAsHandled();
				return;
			}

			{
				var hit = RaycastFromMouse(rightBtn.Position);
				if (hit != null && hit.ContainsKey("position"))
				{
					var hitPos = hit["position"].AsVector3();
					var collider = hit["collider"].As<Node>();
					var clickedUnit = FindUnit3DInParentChain(collider);
					var clickedProp = FindProp3DInParentChain(collider);
					bool shiftHeld = Input.IsKeyPressed(Key.Shift);
					bool isClickedEnemy = clickedUnit != null && GameContext.PerspectiveFilter.IsHostileToObserved(clickedUnit.Player);

					if (clickedUnit != null && isClickedEnemy && clickedUnit.Visible)
					{
						IssueAttackCommand(clickedUnit, shiftHeld);
					}
					else if (clickedUnit != null && !isClickedEnemy && clickedUnit != SelectedUnits.Find(u => GameContext.PerspectiveFilter.CanControl(u.Player)))
					{
						if (clickedUnit.IsBuilding && World.Has<UnderConstruction>(clickedUnit.Entity))
						{
							bool workerSelected = false;
							foreach (var u in SelectedUnits)
							{
								if (u.UnitId == "worker")
								{
									workerSelected = true;
									break;
								}
							}

							if (workerSelected)
							{
								IssueResumeConstructionCommand(clickedUnit, shiftHeld);
							}
							else
							{
								IssueFollowCommand(clickedUnit, shiftHeld);
							}
						}
						else
						{
							IssueFollowCommand(clickedUnit, shiftHeld);
						}
					}
					else if (clickedProp != null && clickedProp.Visible && clickedProp.IsResource)
					{
						IssueGatherCommand(clickedProp, shiftHeld);
					}
					else
					{
						IssueMoveCommand(hitPos, shiftHeld);
					}

					GetViewport().SetInputAsHandled();
					return;
				}
			}
		}

		SkipRightClick:

		if (@event is InputEventMouseButton mouseBtn)
		{
			if (mouseBtn.ButtonIndex == MouseButton.Left)
			{
				if (mouseBtn.Pressed)
				{
					GameLog.LogToConsole($"[GameHost] Unhandled left-click press at position: {mouseBtn.Position}");

					if (mouseBtn.DoubleClick)
					{
						PerformDoubleClickSelection(mouseBtn.Position);
						GetViewport().SetInputAsHandled();
						return;
					}

					if (ActivePingMode)
					{
						var hit = RaycastFromMouse(mouseBtn.Position);
						if (hit != null && hit.ContainsKey("position"))
						{
							AddMinimapPing(hit["position"].AsVector3());
						}

						ActivePingMode = false;
						GetViewport().SetInputAsHandled();
						return;
					}
					else if (ActiveBuildingPlacementType != null)
					{
						var hit = RaycastTerrainFromMouse(mouseBtn.Position) ?? RaycastFromMouse(mouseBtn.Position);
						if (hit != null && hit.ContainsKey("position"))
						{
							var hitPos = hit["position"].AsVector3();
							hitPos.Y = GetTerrainHeightAt(hitPos);
							ExecuteBuildingPlacement(ActiveBuildingPlacementType, hitPos);
						}

						GetViewport().SetInputAsHandled();
						return;
					}
					else if (ActiveSpellTargeting != null)
					{
						var hit = RaycastFromMouse(mouseBtn.Position);
						if (hit != null && hit.ContainsKey("position"))
						{
							ExecuteSpellCast(ActiveSpellTargeting, hit["position"].AsVector3());
						}

						ActiveSpellTargeting = null;
						Input.SetCustomMouseCursor(null);
						Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
					}
					else if (ActiveCommandTargeting != null)
					{
						var hit = RaycastFromMouse(mouseBtn.Position);
						if (hit != null && hit.ContainsKey("position"))
						{
							var hitPos = hit["position"].AsVector3();
							var collider = hit["collider"].As<Node>();
							var clickedUnit = FindUnit3DInParentChain(collider);

							bool shiftHeld = Input.IsKeyPressed(Key.Shift);
							if (ActiveCommandTargeting == "attack")
							{
								if (clickedUnit != null && clickedUnit.Entity != Entity.Null)
								{
									IssueAttackCommand(clickedUnit, shiftHeld);
								}
								else
								{
									IssueAttackMoveCommand(hitPos, shiftHeld);
								}
							}
							else if (ActiveCommandTargeting == "move")
							{
								IssueMoveCommand(hitPos, shiftHeld);
							}
							else if (ActiveCommandTargeting == "patrol")
							{
								IssuePatrolCommand(hitPos, shiftHeld);
							}
							else if (ActiveCommandTargeting == "rally")
							{
								if (SelectedUnits.Count == 1 && GameContext.PerspectiveFilter.CanControl(SelectedUnits[0].Player) && CanProduceUnits(SelectedUnits[0]))
								{
									SetRallyPoint(SelectedUnits[0], hitPos);
								}
							}
						}

						ActiveCommandTargeting = null;
						Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
					}
					else
					{
						if (IsMouseOverUI()) return;

						_isDragging = true;
						_dragStart = mouseBtn.Position;
						_dragEnd = mouseBtn.Position;
					}
				}
				else if (_isDragging)
				{
					GameLog.LogToConsole($"[GameHost] Unhandled left-click release at position: {mouseBtn.Position}");

					_isDragging = false;
					if (GameContext.InGameHud != null)
					{
						Hud.UpdateDragBox(Vector2.Zero, Vector2.Zero, false);
					}

					ClearTemporarySelection();

					float dragDist = _dragStart.DistanceTo(_dragEnd);
					if (dragDist > DragThreshold)
					{
						PerformBoxSelection(_dragStart, _dragEnd);
					}
					else
					{
						PerformSingleClickSelection(_dragStart);
					}
				}
			}
			else if (mouseBtn.ButtonIndex == MouseButton.Right && mouseBtn.Pressed)
			{
				GameLog.LogToConsole($"[GameHost] Unhandled right-click press at position: {mouseBtn.Position}");

				if (ActiveBuildingPlacementType != null)
				{
					CancelBuildingPlacement();
					GetViewport().SetInputAsHandled();
					return;
				}

				if (ActivePingMode)
				{
					ActivePingMode = false;
					if (GameContext.InGameHud != null)
						Hud.ShowFeedbackText("Ping Mode Cancelled", new Color(0.8f, 0.8f, 0.8f));
					GetViewport().SetInputAsHandled();
					return;
				}

				if (ActiveSpellTargeting != null || ActiveCommandTargeting != null)
				{
					ActiveSpellTargeting = null;
					ActiveCommandTargeting = null;
					Input.SetCustomMouseCursor(null);
					Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
					if (GameContext.InGameHud != null)
						Hud.ShowFeedbackText("Targeting Cancelled", new Color(0.8f, 0.8f, 0.8f));
					return;
				}

				var hit = RaycastFromMouse(mouseBtn.Position);
				if (hit != null && hit.ContainsKey("position"))
				{
					var hitPos = hit["position"].AsVector3();
					var collider = hit["collider"].As<Node>();
					var clickedUnit = FindUnit3DInParentChain(collider);
					var clickedProp = FindProp3DInParentChain(collider);

					if (SelectedUnits.Count == 1 && GameContext.PerspectiveFilter.CanControl(SelectedUnits[0].Player) && CanProduceUnits(SelectedUnits[0]))
					{
						SetRallyPoint(SelectedUnits[0], hitPos);
						GetViewport().SetInputAsHandled();
						return;
					}

					if (clickedUnit != null && clickedUnit.Entity != Entity.Null)
					{
						if (GameContext.PerspectiveFilter.IsHostileToObserved(clickedUnit.Player))
						{
							IssueAttackCommand(clickedUnit);
						}
						else
						{
							IssueFollowCommand(clickedUnit);
						}
					}
					else if (clickedProp != null && clickedProp.Visible && clickedProp.IsResource)
					{
						IssueGatherCommand(clickedProp);
					}
					else
					{
						IssueMoveCommand(hitPos);
					}
				}
			}
		}
		else if (@event is InputEventMouseMotion mouseMotion && _isDragging)
		{
			_dragEnd = mouseMotion.Position;
			if (GameContext.InGameHud != null)
			{
				Hud.UpdateDragBox(_dragStart, _dragEnd, true);
			}

			UpdateTemporarySelection(_dragStart, _dragEnd);
		}
	}
}

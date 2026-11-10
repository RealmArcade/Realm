using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Godot;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.VFX;
using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Replication;
using Realm.Ecs.Services;
using Realm.Shared.Metadata;
using Realm.Shared.Network;

namespace Realm.Client.Core;


public partial class GameHost
{
	private readonly List<PresentationMessage> _heldHudPresentation = new();

	// A map states everything it declares about itself once, and a HUD that arrives after it holds all of it until the
	// screen that draws it exists, so the bound has to clear a large map's whole set of declarations rather than a burst.
	private const int MaximumHeldHudPresentation = 1024;

	private void PresentSummaryTable(PresentationMessage message)
	{
		HoldHudPresentation(message);
	}

	private void PresentInstructionsSection(PresentationMessage message)
	{
		if (string.IsNullOrWhiteSpace(message.Key)) return;

		HoldHudPresentation(message);
	}

	/// <summary>
	///     What a map declares about a match - its rules page and the card its standings share - is stated once, to the
	///     peer that has just been seated. A screen that starts its match on an authority already running, which is what
	///     testing a map from the editor does, is seated before its HUD is built, so those declarations are held here and
	///     replayed into the HUD rather than dropped for the want of a screen to draw them on.
	/// </summary>
	private void HoldHudPresentation(PresentationMessage message)
	{
		InGameHUD? hud = GameContext.InGameHud;
		if (hud == null || !GodotObject.IsInstanceValid(hud))
		{
			if (_heldHudPresentation.Count >= MaximumHeldHudPresentation) _heldHudPresentation.RemoveAt(0);
			_heldHudPresentation.Add(message);
			return;
		}

		ApplyHudPresentation(hud, message);
	}

	private static void ApplyHudPresentation(InGameHUD hud, PresentationMessage message)
	{
		string[] values = message.Values ?? Array.Empty<string>();

		switch ((PresentationKind)message.Kind)
		{
			case PresentationKind.SummaryTableVisible:
				hud.ShowSummaryTable(message.Key, message.Flag);
				break;
			case PresentationKind.SummaryTableHeaders:
				hud.ClearSummaryTable();
				hud.SetSummaryTableHeaders(values);
				break;
			case PresentationKind.SummaryTableRow:
				hud.SetSummaryTableRow(message.Key, values);
				break;
			case PresentationKind.SummaryTableCleared:
				hud.ClearSummaryTable();
				break;
			case PresentationKind.InstructionsSectionAdded:
				hud.AddInstructionsSection(message.Key, message.Text);
				break;
			case PresentationKind.TooltipNumberSet:
				GameContext.RegistryService?.TooltipValues.SetNumber(
					TooltipValueStore.OwnerKey(message.Key, message.Text), message.Text2, message.Value);
				break;
			case PresentationKind.TooltipTextSet:
				GameContext.RegistryService?.TooltipValues.SetText(
					TooltipValueStore.OwnerKey(message.Key, message.Text), message.Text2, message.Text3);
				break;
		}
	}

	/// <summary>
	///     Replays onto a HUD that has just been built every declaration this client was handed before it existed.
	/// </summary>
	public void OnInGameHudReady()
	{
		InGameHUD? hud = GameContext.InGameHud;
		if (hud == null || !GodotObject.IsInstanceValid(hud)) return;

		for (int index = 0; index < _heldHudPresentation.Count; index++)
		{
			ApplyHudPresentation(hud, _heldHudPresentation[index]);
		}

		_heldHudPresentation.Clear();
		FlushLeaderboardUpdates();
	}

}

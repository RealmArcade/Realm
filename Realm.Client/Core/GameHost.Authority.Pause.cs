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
	private void ApplyMatchPause(MatchPauseState state)
	{
		var network = GameContext.NetworkService;
		if (network == null) return;

		network.IsPaused = state.Paused;
		network.PausedByPlayerIndex = state.Paused ? state.RequestedByPlayer : -1;
		network.PauseResumeCountdownSeconds = state.Paused ? state.ResumeCountdownSeconds : 0;
		network.NoteAuthorityAlive();
		UpdatePauseUI();
	}

	private void ApplyPauseAllowance(MatchPauseAllowance allowance)
	{
		var network = GameContext.NetworkService;
		if (network == null) return;

		network.RemainingPauses = allowance.PausesRemaining;
		network.PauseCooldownSecondsRemaining = allowance.CooldownSecondsRemaining;
		UpdatePauseUI();

		if (string.IsNullOrEmpty(allowance.Rejection)) return;

		string message = allowance.Rejection == MatchPauseAllowance.RefusalExhausted
			? TranslationServer.Translate("Every pause this player is allowed this match has been used")
			: string.Format(TranslationServer.Translate("A pause is only available again in {0} second(s)"), allowance.CooldownSecondsRemaining);

		GameContext.InGameHud?.ShowFeedbackText(message, new Color(0.9f, 0.7f, 0.2f));
	}

}

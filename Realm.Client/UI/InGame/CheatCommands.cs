using Godot;
using Realm.Client.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Realm.Client.UI.InGame;

/// <summary>
///     Cheat commands typed into chat. The cheat service decides whether a line is a cheat and what it did; this is the
///     part that decides what the player sees afterwards, which is why it is in the UI and not in the service.
/// </summary>
public sealed class CheatCommands
{
	/// <summary>
	///     What a cheat can call for on screen: a fresh selection refresh, a notice, and the minimap's two kinds of redraw
	///     (the terrain capture is a frame-yielding job, the overlay a repaint).
	/// </summary>
	public sealed record Hooks(
		Action<List<Unit3D>?> RefreshSelection,
		Action<string, Color> Feedback,
		Func<Task> RegenerateMinimap,
		Action RedrawMinimap);

	private readonly Hooks _hooks;

	public CheatCommands(Hooks hooks) => _hooks = hooks;

	/// <summary>
	///     Offers a chat line to the cheat service. Returns true when the line was a cheat, so the caller knows the line
	///     was consumed and must not also be sent as chat.
	/// </summary>
	public bool TryTrigger(string text)
	{
		if (GameContext.CheatService == null) return false;
		if (GameContext.PerspectiveFilter.IsSpectating) return false;

		List<Entity> selectedEntities = GameContext.Host?.SelectedUnits is { } selectedUnits
			? new List<Entity>(Enumerable.Select(selectedUnits, u => u.Entity))
			: new List<Entity>();

		// The peer lives on the tree, which a plain class reaches through the main loop rather than through a node.
		MultiplayerApi? multiplayer = (Engine.GetMainLoop() as SceneTree)?.Root?.Multiplayer;
		bool isMultiplayer = GameContext.Lobby != null && !GameContext.Lobby.IsSinglePlayer
		                                                && multiplayer?.MultiplayerPeer != null && multiplayer.MultiplayerPeer is not OfflineMultiplayerPeer;

		(CheatResult result, _) = GameContext.CheatService.TryTriggerCheat(text, isMultiplayer, selectedEntities);
		if (result == CheatResult.None) return false;

		_hooks.Feedback(TranslationServer.Translate("Cheat Activated!"), new Color(0.1f, 0.9f, 0.2f));
		HandleResult(result);
		return true;
	}

	private void HandleResult(CheatResult result)
	{
		switch (result)
		{
			case CheatResult.Gigachad:
				_hooks.RefreshSelection(GameContext.Host?.SelectedUnits);
				break;
			case CheatResult.AbsoluteUnit:
			{
				if (GameContext.Host is not { } host) break;

				foreach (Unit3D unit in host.SelectedUnits)
				{
					if (GodotObject.IsInstanceValid(unit)) unit.Scale = new Vector3(3f, 3f, 3f);
				}

				_hooks.RefreshSelection(host.SelectedUnits);
				break;
			}
			case CheatResult.EzClap:
				GameContext.UserInterface?.PlayClickSound();
				GameContext.UserInterface?.TransitionTo(GameScreen.GameOver, true);
				break;
			case CheatResult.NoCap:
				_ = _hooks.RegenerateMinimap();
				_hooks.RedrawMinimap();
				break;
		}
	}
}

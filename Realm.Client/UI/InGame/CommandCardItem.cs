using System;
using Realm.Client.Core;
using Realm.Client.UI;
using Realm.Ecs;

namespace Realm.Client.UI.InGame;

public class CommandCardItem
{
	public string Id { get; set; }
	public string IconPath { get; set; }
	/// <summary>May contain <c>{{Scope.Key}}</c> tokens; resolved when the tooltip is shown.</summary>
	public string Tooltip { get; set; }
	public TooltipContext TooltipContext { get; set; }
	public Action Callback { get; set; }
	public HotkeySpec Hotkey { get; set; }
	public Func<bool> IsDisabled { get; set; }
	public Func<string> GetButtonText { get; set; }
	public string AbilityId { get; set; }
	public Entity CasterEntity { get; set; } = Entity.Null;
	public float ManaCost { get; set; } = 0f;
	public int GridX { get; set; } = -1;
	public int GridY { get; set; } = -1;
}
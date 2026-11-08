using System;
using Realm.Client.Core;
using Realm.Client.UI;
using Realm.Ecs;

namespace Realm.Client.UI.InGame;

public class CommandCardItem
{
	public string Id { get; set; } = null!;
	public string IconPath { get; set; } = null!;
	public string Tooltip { get; set; } = null!;
	public TooltipContext TooltipContext { get; set; }
	public Action Callback { get; set; } = null!;
	public HotkeySpec Hotkey { get; set; }

	/// <summary>
	///     A core command with no hotkey of its own: its key comes from the grid slot it is drawn in, and its tooltip
	///     carries the letter of that slot rather than one written into the text.
	/// </summary>
	public bool HotkeyFromGrid { get; set; }
	public Func<bool> IsDisabled { get; set; } = null!;
	public Func<string> GetButtonText { get; set; } = null!;
	public string AbilityId { get; set; } = null!;
	public bool IgnoresCooldownGate { get; set; }
	public Entity CasterEntity { get; set; } = Entity.Null;
	public float ManaCost { get; set; } = 0f;
	public int GridX { get; set; } = -1;
	public int GridY { get; set; } = -1;
}
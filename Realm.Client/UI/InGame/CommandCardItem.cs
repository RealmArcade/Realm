using System;
using Arch.Core;
using Godot;

namespace Realm.Client.UI.InGame;

public class CommandCardItem
{
	public string Id { get; set; }
	public string IconPath { get; set; }
	public string Tooltip { get; set; }
	public Action Callback { get; set; }
	public Key Hotkey { get; set; }
	public Func<bool> IsDisabled { get; set; }
	public Func<string> GetButtonText { get; set; }
	public string AbilityId { get; set; }
	public Entity CasterEntity { get; set; } = Entity.Null;
	public float ManaCost { get; set; } = 0f;
}